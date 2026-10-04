using System.Collections.Generic;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Silk.NET.Input;
using SilkCursorMode = Silk.NET.Input.CursorMode;

namespace SpectraEngine.Core.Input;

/// <summary>
/// Tracks keyboard and mouse state from submitted input events and exposes a
/// pollable query surface. Events arrive on the OS-event thread and queries
/// come from the render thread, so all state sits under one lock.
/// </summary>
// Unqualified CursorMode here is the engine's; Silk's is SilkCursorMode.
public sealed class InputManager : ICursorLock, ICursorShape, IInputSink
{
    private readonly ILogger<InputManager> _logger;
    private readonly object _stateLock = new();
    private readonly HashSet<InputKey> _keysDown = [];
    private readonly HashSet<InputKey> _pendingPressed = [];
    private readonly HashSet<InputKey> _pressedThisFrame = [];

    private PointerButtons _buttonsDown;
    private PointerButtons _pendingPressedButtons;
    private PointerButtons _pressedButtonsThisFrame;
    private PointerButtons _pendingReleasedButtons;
    private PointerButtons _releasedButtonsThisFrame;

    private IInputContext? _inputContext;
    private Vector2 _accumulatedMouseDelta;
    private Vector2 _mouseDelta;
    private Vector2? _lastMousePosition;
    private Vector2 _accumulatedScrollDelta;
    private Vector2 _scrollDelta;

    // Requested from any thread, applied by the main thread in ApplyPendingCursorMode.
    private CursorMode _requestedCursorMode = CursorMode.Normal;
    private CursorMode _appliedCursorMode = CursorMode.Normal;

    // Where the cursor was when the lock was taken. Also what MousePosition
    // reports while locked, since GLFW's virtual position is unbounded then.
    private Vector2 _cursorRestorePosition;
    private bool _hasCursorRestorePosition;

    public InputManager(ILogger<InputManager> logger)
    {
        _logger = logger;
    }

    /// <summary>The mouse movement accumulated during the last <see cref="Update"/> interval.</summary>
    public Vector2 MouseDelta
    {
        // Vector2 is two floats: an unlocked read could be torn.
        get { lock (_stateLock) return _mouseDelta; }
    }

    /// <summary>
    /// The cursor's latest position in window client coordinates: pixels,
    /// origin at the top-left, y growing downward. Not latched per frame.
    /// Zero until the OS reports a first position. While the cursor is locked
    /// this stays at the position the lock was taken at.
    /// </summary>
    public Vector2 MousePosition
    {
        get
        {
            lock (_stateLock)
            {
                if (_appliedCursorMode == CursorMode.Locked && _hasCursorRestorePosition)
                    return _cursorRestorePosition;

                return _lastMousePosition ?? Vector2.Zero;
            }
        }
    }

    /// <summary>
    /// Wheel movement accumulated during the last <see cref="Update"/> interval,
    /// in notches: <c>Y</c> is the vertical wheel (positive = away from the
    /// user), <c>X</c> the horizontal one. Latched per frame like
    /// <see cref="MouseDelta"/>.
    /// </summary>
    public Vector2 ScrollDelta
    {
        get { lock (_stateLock) return _scrollDelta; }
    }

    /// <summary>The mouse buttons currently held.</summary>
    public PointerButtons PointerButtonsDown
    {
        get { lock (_stateLock) return _buttonsDown; }
    }

    /// <summary>
    /// The mouse buttons that went from up to down on this tick. Latched by
    /// <see cref="Update"/>.
    /// </summary>
    public PointerButtons PointerButtonsPressed
    {
        get { lock (_stateLock) return _pressedButtonsThisFrame; }
    }

    /// <summary>
    /// The mouse buttons that went from down to up on this tick. Latched by
    /// <see cref="Update"/>. A button pressed and released inside a single
    /// tick reports on both edge sets.
    /// </summary>
    public PointerButtons PointerButtonsReleased
    {
        get { lock (_stateLock) return _releasedButtonsThisFrame; }
    }

    /// <summary>
    /// The modifier keys currently held. Left and right physical keys collapse
    /// into one flag each.
    /// </summary>
    public KeyModifiers Modifiers
    {
        get
        {
            lock (_stateLock)
            {
                KeyModifiers modifiers = KeyModifiers.None;
                if (_keysDown.Contains(InputKey.ShiftLeft) || _keysDown.Contains(InputKey.ShiftRight))
                    modifiers |= KeyModifiers.Shift;
                if (_keysDown.Contains(InputKey.ControlLeft) || _keysDown.Contains(InputKey.ControlRight))
                    modifiers |= KeyModifiers.Control;
                if (_keysDown.Contains(InputKey.AltLeft) || _keysDown.Contains(InputKey.AltRight))
                    modifiers |= KeyModifiers.Alt;
                if (_keysDown.Contains(InputKey.SuperLeft) || _keysDown.Contains(InputKey.SuperRight))
                    modifiers |= KeyModifiers.Super;
                return modifiers;
            }
        }
    }

    private CursorShape _requestedCursorShape = CursorShape.Arrow;

    /// <inheritdoc/>
    public void RequestCursorShape(CursorShape shape)
    {
        lock (_stateLock)
            _requestedCursorShape = shape;
    }

    /// <inheritdoc/>
    public CursorShape CursorShape
    {
        get { lock (_stateLock) return _requestedCursorShape; }
    }

    /// <inheritdoc/>
    public void RequestCursorMode(CursorMode mode)
    {
        lock (_stateLock)
            _requestedCursorMode = mode;
    }

    /// <inheritdoc/>
    public CursorMode CursorMode
    {
        get { lock (_stateLock) return _appliedCursorMode; }
    }

    /// <inheritdoc/>
    public bool IsCursorLocked
    {
        get { lock (_stateLock) return _appliedCursorMode == CursorMode.Locked; }
    }

    /// <summary>
    /// The cursor mode last asked for, whether or not it has been applied. An
    /// embedded host polls this, captures the pointer itself, then calls
    /// <see cref="ApplyPendingCursorMode"/>.
    /// </summary>
    public CursorMode RequestedCursorMode
    {
        get { lock (_stateLock) return _requestedCursorMode; }
    }

    /// <summary>
    /// Applies the cursor mode last requested. Main thread only, once per pass
    /// of the OS-event pump: GLFW cursor calls belong to the thread that
    /// created the window. Leaving the lock puts the cursor back where it was.
    /// </summary>
    public void ApplyPendingCursorMode()
    {
        CursorMode requested;
        Vector2 restore;
        bool hasRestore;
        lock (_stateLock)
        {
            if (_requestedCursorMode == _appliedCursorMode)
                return;

            requested = _requestedCursorMode;
            restore = _cursorRestorePosition;
            hasRestore = _hasCursorRestorePosition;

            // Capture the restore point before the backend starts reporting
            // virtual coordinates.
            if (requested == CursorMode.Locked && _lastMousePosition is { } live)
            {
                restore = live;
                hasRestore = true;
            }
        }

        // With no device (headless, or an embedded host) the bookkeeping below
        // still runs, so the request does not stay pending.
        if (PrimaryMouse() is { } mouse)
        {
            mouse.Cursor.CursorMode = requested switch
            {
                CursorMode.Locked => SilkCursorMode.Disabled,
                CursorMode.Hidden => SilkCursorMode.Hidden,
                _ => SilkCursorMode.Normal,
            };

            // After the mode change: a position written while the cursor is
            // still disabled goes to the virtual cursor.
            if (requested != CursorMode.Locked && hasRestore)
                mouse.Position = restore;
        }

        lock (_stateLock)
        {
            _appliedCursorMode = requested;
            _cursorRestorePosition = restore;
            _hasCursorRestorePosition = hasRestore;

            // The backend's jump on capture or release is not user motion.
            _accumulatedMouseDelta = Vector2.Zero;
            if (requested != CursorMode.Locked && hasRestore)
                _lastMousePosition = restore;
        }
    }

    /// <summary>
    /// Applies the cursor shape last requested. Main thread only, standalone
    /// window only; an embedded host reads <see cref="CursorShape"/> instead.
    /// </summary>
    public void ApplyPendingCursorShape()
    {
        CursorShape requested;
        lock (_stateLock)
        {
            if (_requestedCursorShape == _appliedCursorShape)
                return;

            requested = _requestedCursorShape;
        }

        if (PrimaryMouse() is { } mouse)
        {
            // Silk has no Grab, Grabbing or Rotate cursor.
            mouse.Cursor.StandardCursor = requested switch
            {
                CursorShape.Crosshair => StandardCursor.Crosshair,
                CursorShape.Grab or CursorShape.Grabbing => StandardCursor.Hand,
                CursorShape.SizeWestEast => StandardCursor.HResize,
                CursorShape.SizeNorthSouth => StandardCursor.VResize,
                CursorShape.SizeNorthWestSouthEast => StandardCursor.NwseResize,
                CursorShape.SizeNorthEastSouthWest => StandardCursor.NeswResize,
                CursorShape.SizeAll or CursorShape.Rotate => StandardCursor.ResizeAll,
                CursorShape.No => StandardCursor.NotAllowed,
                _ => StandardCursor.Default,
            };
        }

        lock (_stateLock)
            _appliedCursorShape = requested;
    }

    private CursorShape _appliedCursorShape = CursorShape.Arrow;

    /// <summary>
    /// Reacts to the window gaining or losing focus. Main thread only. Losing
    /// focus releases the cursor lock and every held key and button.
    /// </summary>
    public void OnWindowFocusChanged(bool focused)
    {
        if (!focused)
            Submit(InputEvent.FocusLost());
    }

    // Caller must hold _stateLock.
    private void ReleaseEverything()
    {
        // Overwrite the request too, or the render thread's stale "locked"
        // re-takes the cursor on the next pump.
        _requestedCursorMode = CursorMode.Normal;

        _keysDown.Clear();
        _pendingPressed.Clear();

        // Held buttons become release edges, so a gesture waiting for its
        // release still gets one.
        _pendingReleasedButtons |= _buttonsDown;
        _buttonsDown = PointerButtons.None;
        _pendingPressedButtons = PointerButtons.None;

        _accumulatedMouseDelta = Vector2.Zero;
        _accumulatedScrollDelta = Vector2.Zero;
    }

    // Silk exposes one cursor per mouse; the window's is the first mouse's.
    private IMouse? PrimaryMouse()
    {
        IInputContext? context = _inputContext;
        return context is not null && context.Mice.Count > 0 ? context.Mice[0] : null;
    }

    public void Initialize(IInputContext inputContext)
    {
        _inputContext = inputContext;

        for (int i = 0; i < _inputContext.Keyboards.Count; i++)
        {
            var keyboard = _inputContext.Keyboards[i];
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
        }

        for (int i = 0; i < _inputContext.Mice.Count; i++)
        {
            var mouse = _inputContext.Mice[i];
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += OnScroll;
        }

        // Seed the position so a click before any move event does not pick at (0,0).
        if (_inputContext.Mice.Count > 0)
        {
            Vector2 position = _inputContext.Mice[0].Position;
            lock (_stateLock)
                _lastMousePosition ??= position;
        }

        _logger.LogInformation("Input manager initialized ({KeyboardCount} keyboards, {MouseCount} mice)",
            _inputContext.Keyboards.Count, _inputContext.Mice.Count);
    }

    /// <summary>Latches per-frame deltas; call once per game tick before querying.</summary>
    public void Update(double deltaTime)
    {
        lock (_stateLock)
        {
            _mouseDelta = _accumulatedMouseDelta;
            _accumulatedMouseDelta = Vector2.Zero;

            _scrollDelta = _accumulatedScrollDelta;
            _accumulatedScrollDelta = Vector2.Zero;

            _pressedThisFrame.Clear();
            foreach (InputKey k in _pendingPressed)
                _pressedThisFrame.Add(k);
            _pendingPressed.Clear();

            _pressedButtonsThisFrame = _pendingPressedButtons;
            _pendingPressedButtons = PointerButtons.None;

            _releasedButtonsThisFrame = _pendingReleasedButtons;
            _pendingReleasedButtons = PointerButtons.None;
        }
    }

    public bool IsKeyDown(InputKey key)
    {
        lock (_stateLock)
            return _keysDown.Contains(key);
    }

    /// <summary>True for the single tick on which <paramref name="key"/> went from up to down.</summary>
    public bool WasKeyPressed(InputKey key)
    {
        lock (_stateLock)
            return _pressedThisFrame.Contains(key);
    }

    /// <summary>
    /// True while every button in <paramref name="button"/> is held. A single
    /// flag is the ordinary case; a combination asks for all of them.
    /// </summary>
    public bool IsMouseButtonDown(PointerButtons button)
    {
        lock (_stateLock)
            return (_buttonsDown & button) == button;
    }

    /// <summary>True for the single tick on which <paramref name="button"/> went from up to down.</summary>
    public bool WasMouseButtonPressed(PointerButtons button)
    {
        lock (_stateLock)
            return (_pressedButtonsThisFrame & button) == button;
    }

    /// <summary>
    /// True for the single tick on which <paramref name="button"/> went from
    /// down to up.
    /// </summary>
    public bool WasMouseButtonReleased(PointerButtons button)
    {
        lock (_stateLock)
            return (_releasedButtonsThisFrame & button) == button;
    }

    public void Shutdown()
    {
        if (_inputContext is not null)
        {
            // Release a captured cursor before the devices go away.
            RequestCursorMode(CursorMode.Normal);
            ApplyPendingCursorMode();


            for (int i = 0; i < _inputContext.Keyboards.Count; i++)
            {
                var keyboard = _inputContext.Keyboards[i];
                keyboard.KeyDown -= OnKeyDown;
                keyboard.KeyUp -= OnKeyUp;
            }

            for (int i = 0; i < _inputContext.Mice.Count; i++)
            {
                var mouse = _inputContext.Mice[i];
                mouse.MouseDown -= OnMouseDown;
                mouse.MouseUp -= OnMouseUp;
                mouse.MouseMove -= OnMouseMove;
                mouse.Scroll -= OnScroll;
            }

            _inputContext.Dispose();
            _inputContext = null;
        }

        _logger.LogInformation("Input manager shut down");
    }

    // OS-event thread. Internal so tests can drive them without a device;
    // the device parameter is unused and may be null.

    internal void OnKeyDown(IKeyboard keyboard, Key key, int keyCode) =>
        Submit(InputEvent.KeyDown(SilkInputKeys.ToInputKey(key)));

    internal void OnKeyUp(IKeyboard keyboard, Key key, int keyCode) =>
        Submit(InputEvent.KeyUp(SilkInputKeys.ToInputKey(key)));

    internal void OnMouseDown(IMouse mouse, MouseButton button) =>
        Submit(InputEvent.PointerDown(SilkInputKeys.ToPointerButton(button)));

    internal void OnMouseUp(IMouse mouse, MouseButton button) =>
        Submit(InputEvent.PointerUp(SilkInputKeys.ToPointerButton(button)));

    internal void OnScroll(IMouse mouse, ScrollWheel wheel) =>
        Submit(InputEvent.Scroll(new Vector2(wheel.X, wheel.Y)));

    internal void OnMouseMove(IMouse mouse, Vector2 position) =>
        Submit(InputEvent.PointerMove(position));

    /// <summary>
    /// Applies one event to the input state. Called by the standalone window's
    /// device handlers and by an embedded host through
    /// <c>EngineHost.SubmitInput</c>.
    /// </summary>
    public void Submit(in InputEvent input)
    {
        bool focusLost = false;

        lock (_stateLock)
        {
            switch (input.Kind)
            {
                case InputEventKind.KeyDown:
                    // Unknown stays out of the held set: all unnamed keys share
                    // that value, so one release would lift them all.
                    if (input.Key is not InputKey.Unknown && _keysDown.Add(input.Key))
                        _pendingPressed.Add(input.Key);
                    break;

                case InputEventKind.KeyUp:
                    _keysDown.Remove(input.Key);
                    break;

                case InputEventKind.PointerDown:
                    PointerButtons pressed = input.Button & ~_buttonsDown;
                    _buttonsDown |= input.Button;
                    _pendingPressedButtons |= pressed;
                    break;

                case InputEventKind.PointerUp:
                    // A release for a button never seen down reports nothing.
                    PointerButtons released = input.Button & _buttonsDown;
                    _buttonsDown &= ~input.Button;
                    _pendingReleasedButtons |= released;
                    break;

                case InputEventKind.PointerMove:
                    if (_lastMousePosition.HasValue)
                        _accumulatedMouseDelta += input.Value - _lastMousePosition.Value;
                    _lastMousePosition = input.Value;
                    break;

                case InputEventKind.PointerDelta:
                    // No position update: a captured cursor has none.
                    _accumulatedMouseDelta += input.Value;
                    break;

                case InputEventKind.Scroll:
                    _accumulatedScrollDelta += input.Value;
                    break;

                case InputEventKind.FocusLost:
                    ReleaseEverything();
                    focusLost = true;
                    break;
            }
        }

        // Outside the lock. Give the cursor back now, not a frame later.
        if (focusLost)
            ApplyPendingCursorMode();
    }
}
