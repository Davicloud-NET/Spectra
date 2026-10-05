using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Input;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Viewport.Windows;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpectraEngine.Editor.Viewport;

// The composited viewport: the engine resolves into a shared keyed-mutex
// texture and the compositor draws it on an ordinary composition visual, so
// the pane is a normal control with no airspace limits.
// Windows only (D3D11 shared NT handle, ClipCursor). UI thread only.
// Input goes through ViewportInputRouter, same as the Win32 viewport.
internal sealed class CompositionEngineViewport : Control, IEngineViewport, IViewportCursor
{
    private readonly ILogger _logger;
    private readonly Action<string>? _onUnavailable;
    private readonly Action<ViewportChoiceReason>? _onFailure;
    private readonly ViewportInputRouter _router;
    private readonly CompositedRenderSurface _surface = new();
    private readonly Dictionary<StandardCursorType, Cursor> _cursors = [];

    private CompositionDrawingSurface? _drawingSurface;
    private CompositionSurfaceVisual? _visual;
    private CompositedFramePump? _pump;
    private TopLevel? _topLevel;
    private Window? _window;

    private EngineHost? _host;
    private IPointer? _pointer;
    private bool _releasingCapture;

    // Last reported drag state, so AssetDragChanged fires only on a change.
    private AssetDragState? _dragState;

    // In dips. A drag delivers no PointerMoved, so this feeds the editor's
    // hover while one is in flight.
    private Point? _lastDragPoint;

    // Tracks published vs shut down, which is what tells a re-parent from a
    // teardown.
    private readonly ViewportSurfaceLifetime _lifetime = new();

    // Geometry as of the last layout pass.
    private PixelPoint _originOnScreen;
    private Size _sizeInDips;
    private double _scaling = 1.0;

    private StandardCursorType? _shownCursor;
    private bool _cursorHidden;

    internal CompositionEngineViewport(
        ILogger logger, Action<string>? onUnavailable, Action<ViewportChoiceReason>? onFailure = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _onUnavailable = onUnavailable;
        _onFailure = onFailure;

        Focusable = true;

        _router = new ViewportInputRouter(this);
        _router.ShellChord += chord => ShellChord?.Invoke(chord);
        _router.ContextMenuRequested += (x, y) => ContextMenuRequested?.Invoke(x, y);

        // Tunnel: Alt, Tab and the arrows are eaten by handlers that run
        // before a bubbling one would see them.
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnTunnelKeyUp, RoutingStrategies.Tunnel);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnAssetDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnAssetDragLeave);
        AddHandler(DragDrop.DropEvent, OnAssetDrop);

        LostFocus += OnViewportLostFocus;
    }

    /// <inheritdoc/>
    public event Action<IRenderSurface>? SurfaceCreated;

    /// <inheritdoc/>
    public event Action? SurfaceDestroying;

    /// <inheritdoc/>
    public event Action<ShellChord>? ShellChord;

    /// <inheritdoc/>
    public event Action<int, int>? ContextMenuRequested;

    /// <inheritdoc/>
    public event Action<ContentDragPayload, int, int, MaterialDropScope>? AssetDropped;

    /// <inheritdoc/>
    public event Action<AssetDragState?>? AssetDragChanged;

    /// <inheritdoc/>
    public bool AcceptsAssetDrops => true;

    /// <inheritdoc/>
    public Control Control => this;

    /// <inheritdoc/>
    public EngineHost? Host
    {
        get => _host;
        set
        {
            _host = value;

            _router.Sink = value is null ? null : new EngineHostSink(value);

            // The shell clears the host before it stops the session. The pump
            // must stop first: an in-flight update waits on a key only the
            // producer can release.
            if (value is null)
                _pump?.Stop();
        }
    }

    /// <inheritdoc/>
    public bool IsAwaitingEngine => _pump is { HasHandOverInFlight: true };

    /// <inheritdoc/>
    public void FocusEngine() => Focus();

    /// <inheritdoc/>
    // Also feeds the pump the current shared target and checks for a stalled
    // hand-over.
    public void PumpCursorMode()
    {
        if (_host is not { } host)
            return;

        _router.ApplyCursorMode(host.RequestedCursorMode);
        host.ApplyPendingCursorMode();
        ApplyCursorShape(host);

        if (host.LastSnapshot.SharedTarget is { } shared)
            _pump?.Observe(shared);

        _pump?.CheckForStall();
    }

    /// <inheritdoc/>
    // Runs again on every re-dock or float. The window and the compositor half
    // are rebuilt (a float is another window); the surface is not, the engine
    // is still rendering into it.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _topLevel = TopLevel.GetTopLevel(this);

        // Three things layout never reports: deactivation (alt-tab leaves
        // Avalonia focus where it was), minimise (bounds are kept), and a
        // window move (screen origin changes with no layout pass).
        _window = _topLevel as Window;
        if (_window is { } window)
        {
            window.Deactivated += OnWindowDeactivated;
            window.PropertyChanged += OnWindowPropertyChanged;
            window.PositionChanged += OnWindowPositionChanged;
        }

        LayoutUpdated += OnLayoutUpdated;
        ReadGeometry();

        _ = InitializeAsync();

        // Re-parent: take the keyboard back, or the tool keys are dead until
        // somebody clicks in the scene.
        if (_lifetime.IsPublished)
            Focus();
    }

    /// <inheritdoc/>
    // A detach is a re-parent unless Shutdown was called. Treating it as a
    // teardown would stop the engine and build a second session on re-attach.
    // Only the compositor half goes; the pump owns the drawing surface and
    // releases it after the last hand-over settles.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdated;

        // First, while the top level is still known: the unlock restores the
        // cursor to a client position that needs it to map to the screen.
        _router.ApplyCursorMode(CursorMode.Normal);

        if (_window is { } window)
        {
            window.Deactivated -= OnWindowDeactivated;
            window.PropertyChanged -= OnWindowPropertyChanged;
            window.PositionChanged -= OnWindowPositionChanged;
        }

        // True only after Shutdown. The engine must be off the surface when
        // this returns.
        if (_lifetime.Detached())
            SurfaceDestroying?.Invoke();

        // Stopped on a re-parent too; the re-attach builds a fresh pump.
        _pump?.Stop();
        _pump = null;

        ElementComposition.SetElementChildVisual(this, null);
        _visual = null;

        // Not disposed here: the pump owns it after Stop.
        _drawingSurface = null;

        _window = null;
        _topLevel = null;

        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc/>
    public void Shutdown()
    {
        if (_lifetime.Shutdown())
            SurfaceDestroying?.Invoke();

        _pump?.Stop();

        // A session can be closed mid-drag from the keyboard.
        ReportDrag(null);
    }

    // Negotiates the compositor's GPU interop and publishes the surface.
    // The interop query is async, so failure is reported through a callback.
    private async Task InitializeAsync()
    {
        try
        {
            CompositionVisual? element = ElementComposition.GetElementVisual(this);
            if (element?.Compositor is not { } compositor)
            {
                Unavailable("this window has no compositor, so the engine's frame has nowhere to go.");
                return;
            }

            ICompositionGpuInterop? interop = await compositor.TryGetCompositionGpuInterop();
            if (interop is null)
            {
                Unavailable(
                    "this compositor exposes no GPU interop, so an engine frame cannot be imported.");
                return;
            }

            if (!interop.SupportedImageHandleTypes.Contains(
                    KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureNtHandle))
            {
                Unavailable(
                    "this compositor does not import D3D11 NT handles, which is the only kind the engine " +
                    "produces.");
                return;
            }

            // Detached while the interop was being negotiated.
            if (_topLevel is null)
                return;

            _drawingSurface = compositor.CreateDrawingSurface();
            _visual = compositor.CreateSurfaceVisual();
            _visual.Surface = _drawingSurface;
            _visual.Size = new Vector(Bounds.Width, Bounds.Height);
            ElementComposition.SetElementChildVisual(this, _visual);

            _pump = new CompositedFramePump(
                new CompositorImageSource(interop, _drawingSurface),
                AcknowledgeRelease,
                _logger,
                onFault: OnPumpFaulted);

            // The surface is published once per session. After a re-parent the
            // fresh pump just imports the generation the next frame names.
            switch (_lifetime.Attached())
            {
                case ViewportAttach.Publish:
                    _logger.LogInformation(
                        "Composited viewport ready on the compositor's own adapter; the native child is " +
                        "not in use.");
                    SurfaceCreated?.Invoke(_surface);
                    break;

                case ViewportAttach.Resume:
                    _logger.LogInformation(
                        "Composited viewport re-attached; the session's shared target will be re-imported.");
                    break;

                default:
                    _logger.LogDebug(
                        "Composited viewport attached after shutdown; publishing nothing.");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The composited viewport could not be set up");
            Unavailable($"the composited viewport could not be set up: {ex.Message}");
        }
    }

    private void Unavailable(string reason)
    {
        _logger.LogError("Composited viewport unavailable: {Reason}", reason);
        _onUnavailable?.Invoke(
            $"The composited viewport is not available here: {reason} Relaunch with --viewport=native.");

        // A running session that lost its picture is not a green session.
        if (_lifetime.IsPublished)
            _onFailure?.Invoke(ViewportChoiceReason.FirstUpdateFaulted);
    }

    // A window move triggers no layout pass.
    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e) => ReadGeometry();

    // Report only. Swapping to the native child here would tear down a live
    // engine.
    private void OnPumpFaulted() => _onFailure?.Invoke(ViewportChoiceReason.FirstUpdateFaulted);

    // Tells the engine a retired shared-target generation may be freed. Goes
    // through the host's latch; the resource belongs to the render thread.
    private void AcknowledgeRelease(int generation) =>
        _host?.NotifySharedTargetReleased(generation);

    /// <inheritdoc/>
    // Avalonia hit-tests what a visual drew, so paint a transparent fill or
    // the pane never gets a click.
    public override void Render(DrawingContext context) =>
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

    private void OnLayoutUpdated(object? sender, EventArgs e) => ReadGeometry();

    // Picks up a move, a resize or a scaling change. The move matters because
    // the cursor lock's anchor is a screen point.
    private void ReadGeometry()
    {
        if (_topLevel is null)
            return;

        double scaling = _topLevel.RenderScaling;
        Size size = Bounds.Size;

        PixelPoint origin;
        try
        {
            origin = this.PointToScreen(default);
        }
        catch (InvalidOperationException)
        {
            // No root yet between attach and the first layout.
            return;
        }

        bool resized = size != _sizeInDips || scaling != _scaling;
        bool moved = origin != _originOnScreen;

        _sizeInDips = size;
        _scaling = scaling;
        _originOnScreen = origin;

        if (resized)
        {
            if (_visual is { } visual)
                visual.Size = new Vector(size.Width, size.Height);

            _surface.SetPixelSize(
                (int)Math.Round(size.Width * scaling), (int)Math.Round(size.Height * scaling));
        }

        if (moved || resized)
            _router.OnViewportMoved();

        UpdateVisibility();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
            UpdateVisibility();
    }

    // Bounds alone are not enough: a minimised window's controls keep theirs,
    // and the pump would go on copying frames nobody sees.
    private void UpdateVisibility()
    {
        bool onScreen = Bounds.Width > 0
            && Bounds.Height > 0
            && _window is not { WindowState: WindowState.Minimized };

        _pump?.SetVisible(onScreen);
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        _pointer = e.Pointer;

        // An OLE drag delivers no pointer moves, so getting one means any drag
        // is over. DragLeave is not guaranteed; this clears a stuck overlay.
        ReportDrag(null);

        // A fast move is one event carrying the whole path. Reading only the
        // last position loses the travel in between.
        var path = e.GetIntermediatePoints(this);
        if (path.Count > 0)
        {
            foreach (var point in path)
                SubmitMove(point.Position);
        }
        else
        {
            SubmitMove(e.GetPosition(this));
        }

        e.Handled = true;
        base.OnPointerMoved(e);
    }

    private void SubmitMove(Point positionInDips)
    {
        (int x, int y) = ToPixels(positionInDips);
        _router.OnPointerMove(x, y);
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _pointer = e.Pointer;

        PointerButtons button = AvaloniaKeys.ToPointerButton(
            e.GetCurrentPoint(this).Properties.PointerUpdateKind);
        if (button is PointerButtons.None)
        {
            base.OnPointerPressed(e);
            return;
        }

        Focus();

        // Position first: the router measures right-click travel from where
        // the press happened.
        SubmitMove(e.GetPosition(this));
        _router.OnPointerDown(button);

        e.Handled = true;
        base.OnPointerPressed(e);
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _pointer = e.Pointer;

        PointerButtons button = AvaloniaKeys.ToPointerButton(
            e.GetCurrentPoint(this).Properties.PointerUpdateKind);
        if (button is PointerButtons.None)
        {
            base.OnPointerReleased(e);
            return;
        }

        _router.OnPointerUp(button);

        e.Handled = true;
        base.OnPointerReleased(e);
    }

    /// <inheritdoc/>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _pointer = e.Pointer;
        _router.OnScroll((float)e.Delta.X, (float)e.Delta.Y);

        e.Handled = true;
        base.OnPointerWheelChanged(e);
    }

    /// <inheritdoc/>
    // The keyboard is still here, so the router gets balanced button releases
    // and not the release-everything focus event.
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Releasing the capture ourselves raises this event too.
        if (!_releasingCapture)
            _router.OnPointerCaptureLost();

        base.OnPointerCaptureLost(e);
    }

    // Claimed for every asset payload, droppable or not: the shell refuses in
    // words through AssetDropPolicy, which a "no entry" cursor cannot.
    private void OnAssetDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(ContentDrag.Format) is not { } payload)
            return;

        // A drag can start inside this pane, so there may be no DragEnter.
        ReportDrag(new AssetDragState(payload, ScopeOf(e.KeyModifiers)));

        // No PointerMoved arrives during an OLE drag, so feed the position to
        // the router here. Not through OnPointerMoved, which clears the overlay.
        Point position = e.GetPosition(this);
        if (_lastDragPoint != position)
        {
            _lastDragPoint = position;
            SubmitMove(position);
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    // Not marked Handled: the pane the pointer moved onto needs the event too.
    private void OnAssetDragLeave(object? sender, RoutedEventArgs e)
    {
        _lastDragPoint = null;
        ReportDrag(null);
    }

    // Ctrl widens a material drop from one face to the whole block. Read per
    // event: the modifier can change mid-drag.
    private static MaterialDropScope ScopeOf(Avalonia.Input.KeyModifiers modifiers) =>
        modifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) ? MaterialDropScope.Brush : MaterialDropScope.Face;

    private void OnAssetDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(ContentDrag.Format) is not { } payload)
            return;

        e.Handled = true;

        // Clear the overlay before the shell handles the drop.
        ReportDrag(null);

        // Framebuffer pixels, same conversion as a press.
        (int x, int y) = ToPixels(e.GetPosition(this));
        AssetDropped?.Invoke(payload, x, y, ScopeOf(e.KeyModifiers));
    }

    // DragOver fires per pointer move with the same answer, so only a change
    // is raised.
    private void ReportDrag(AssetDragState? state)
    {
        // Record value equality: DragOver builds a fresh state each time.
        if (_dragState == state)
            return;

        _dragState = state;
        AssetDragChanged?.Invoke(state);
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        _router.OnKeyDown(AvaloniaKeys.ToInputKey(e.Key), AvaloniaKeys.ToModifiers(e.KeyModifiers));

        // Always handled: while the viewport has focus the engine owns the
        // keyboard, as with the native child.
        e.Handled = true;
    }

    private void OnTunnelKeyUp(object? sender, KeyEventArgs e)
    {
        _router.OnKeyUp(AvaloniaKeys.ToInputKey(e.Key));
        e.Handled = true;
    }

    private void OnViewportLostFocus(object? sender, RoutedEventArgs e) => _router.OnFocusLost();

    // Alt-tab leaves Avalonia's focus where it was, so LostFocus alone would
    // keep the cursor locked and buttons held.
    private void OnWindowDeactivated(object? sender, EventArgs e) => _router.OnFocusLost();

    private void ApplyCursorShape(EngineHost host)
    {
        if (_cursorHidden)
            return;

        StandardCursorType wanted = AvaloniaKeys.ToStandardCursor(host.RequestedCursorShape);
        if (_shownCursor == wanted)
            return;

        _shownCursor = wanted;
        Cursor = CursorFor(wanted);
    }

    private Cursor CursorFor(StandardCursorType type)
    {
        if (_cursors.TryGetValue(type, out Cursor? cursor))
            return cursor;

        cursor = new Cursor(type);
        _cursors[type] = cursor;
        return cursor;
    }

    /// <inheritdoc/>
    ViewportSize IViewportCursor.ClientSize
    {
        get
        {
            Silk.NET.Maths.Vector2D<int> size = _surface.PixelSize;
            return new ViewportSize(size.X, size.Y);
        }
    }

    /// <inheritdoc/>
    int IViewportCursor.DragSlack
    {
        get
        {
            // Half of SM_CXDRAG: the metric is the rectangle's full width and
            // travel is measured from its centre.
            const int fallback = 4;

            try
            {
                int width = Win32Interop.GetSystemMetricsForDpi(
                    Win32Interop.SM_CXDRAG, (uint)Math.Round(96.0 * _scaling));
                return width > 1 ? width / 2 : fallback;
            }
            catch (EntryPointNotFoundException)
            {
                // Pre-1607 Windows has no per-DPI metrics.
                return fallback;
            }
        }
    }

    /// <inheritdoc/>
    ViewportPoint IViewportCursor.ClientToScreen(ViewportPoint client)
    {
        if (_topLevel is null)
            return client;

        try
        {
            // Router pixels to dips on the way in. PointToScreen already
            // returns physical screen pixels.
            PixelPoint screen = this.PointToScreen(
                new Point(client.X / _scaling, client.Y / _scaling));
            return new ViewportPoint(screen.X, screen.Y);
        }
        catch (InvalidOperationException)
        {
            return client;
        }
    }

    /// <inheritdoc/>
    void IViewportCursor.MoveCursor(int screenX, int screenY) =>
        Win32Interop.SetCursorPos(screenX, screenY);

    /// <inheritdoc/>
    void IViewportCursor.ClipToClient(bool clip)
    {
        if (!clip)
        {
            Win32Interop.ClipCursorRelease(0);
            return;
        }

        if (_topLevel is null)
            return;

        try
        {
            PixelPoint topLeft = this.PointToScreen(default);
            PixelPoint bottomRight = this.PointToScreen(new Point(Bounds.Width, Bounds.Height));

            Win32Interop.ClipCursor(new Win32Interop.RECT
            {
                Left = topLeft.X,
                Top = topLeft.Y,
                Right = bottomRight.X,
                Bottom = bottomRight.Y,
            });
        }
        catch (InvalidOperationException)
        {
            // No visual root, nothing to fence.
        }
    }

    /// <inheritdoc/>
    void IViewportCursor.SetCursorHidden(bool hidden)
    {
        _cursorHidden = hidden;

        if (hidden)
        {
            _shownCursor = null;
            Cursor = CursorFor(StandardCursorType.None);
        }
        else
        {
            _shownCursor = StandardCursorType.Arrow;
            Cursor = CursorFor(StandardCursorType.Arrow);
        }
    }

    /// <inheritdoc/>
    // Avalonia's capture, not Win32's: the HWND belongs to the framework.
    // Releasing raises PointerCaptureLost, hence _releasingCapture.
    void IViewportCursor.SetPointerCapture(bool captured)
    {
        if (_pointer is not { } pointer)
            return;

        if (captured)
        {
            pointer.Capture(this);
            return;
        }

        _releasingCapture = true;
        try
        {
            pointer.Capture(null);
        }
        finally
        {
            _releasingCapture = false;
        }
    }

    private (int X, int Y) ToPixels(Point dips) =>
        ((int)Math.Round(dips.X * _scaling), (int)Math.Round(dips.Y * _scaling));

    // Adapter so the router never names EngineHost; tests give it a recorder.
    private sealed class EngineHostSink(EngineHost host) : IInputSink
    {
        public void Submit(in InputEvent input) => host.SubmitInput(in input);
    }

    private sealed class CompositorImageSource(
        ICompositionGpuInterop interop, CompositionDrawingSurface surface) : ICompositedImageSource
    {
        public ICompositedImage Import(nint ntHandle, int width, int height)
        {
            ICompositionImportedGpuImage image = interop.ImportImage(
                new PlatformHandle(
                    ntHandle, KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureNtHandle),
                new PlatformGraphicsExternalImageProperties
                {
                    Width = width,
                    Height = height,

                    // The shared resource is UNORM under an sRGB view, so the
                    // bytes are already sRGB-encoded.
                    Format = PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm,

                    // D3D render targets are top-left origin. Wrong here flips
                    // the picture without failing anything.
                    TopLeftOrigin = true,
                });

            return new CompositorImage(image, surface);
        }

        // Called by the pump once its last import has settled. Disposing the
        // surface earlier faults a hand-over still in flight.
        public ValueTask DisposeAsync()
        {
            surface.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CompositorImage(
        ICompositionImportedGpuImage image, CompositionDrawingSurface surface) : ICompositedImage
    {
        public Task ImportCompleted => image.ImportCompleted;

        public Task UpdateAsync(uint acquireKey, uint releaseKey) =>
            surface.UpdateWithKeyedMutexAsync(image, acquireKey, releaseKey);

        public ValueTask DisposeAsync() => image.DisposeAsync();
    }
}
