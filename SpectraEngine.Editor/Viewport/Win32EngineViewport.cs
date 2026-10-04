using Avalonia.Controls;
using Avalonia.Platform;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Viewport.Windows;
using System;

namespace SpectraEngine.Editor.Viewport;

/// <summary>
/// The pane the engine renders into: a native child window embedded in the
/// visual tree, with the engine's own swap chain behind it.
/// </summary>
// The engine renders with its own thread, device and present. Windows only: on
// other platforms the default child appears but no surface is published.
public sealed class Win32EngineViewport : NativeControlHost, IEngineViewport
{
    private Win32ViewportWindow? _window;
    private EngineHost? _host;

    /// <summary>
    /// Raised on the UI thread once the native surface exists. A host starts
    /// the engine here.
    /// </summary>
    public event Action<IRenderSurface>? SurfaceCreated;

    /// <summary>
    /// Raised on the UI thread before the native surface is destroyed. A host
    /// must have stopped the engine by the time this returns.
    /// </summary>
    public event Action? SurfaceDestroying;

    /// <summary>Raised for a Ctrl chord the shell owns rather than the engine.</summary>
    // The OS delivers the keyboard to the child window while it has focus, so
    // Avalonia's menu accelerators never fire.
    public event Action<ShellChord>? ShellChord;

    /// <summary>
    /// Raised on the UI thread for a right-click that never became a freelook
    /// drag, in framebuffer pixels.
    /// </summary>
    public event Action<int, int>? ContextMenuRequested;

    /// <inheritdoc/>
    // Never raised: see AcceptsAssetDrops.
#pragma warning disable CS0067
    public event Action<ContentDragPayload, int, int, MaterialDropScope>? AssetDropped;
#pragma warning restore CS0067

    /// <inheritdoc/>
    // Never raised: this window sees no drag, and the overlay it would drive
    // is drawn under the child HWND.
#pragma warning disable CS0067
    public event Action<AssetDragState?>? AssetDragChanged;
#pragma warning restore CS0067

    /// <summary>
    /// Always false: the OS delivers input to the child HWND, and that window
    /// is not a registered OLE drop target.
    /// </summary>
    public bool AcceptsAssetDrops => false;

    /// <summary>Whether this platform can host the engine at all.</summary>
    public static bool IsSupported => EngineViewports.IsSupported;

    /// <inheritdoc/>
    public Control Control => this;

    /// <summary>The running engine's host. Setting it turns the viewport's input on.</summary>
    public EngineHost? Host
    {
        get => _host;
        set
        {
            _host = value;
            if (_window is not null)
                _window.Host = value;
        }
    }

    /// <summary>
    /// Applies the cursor mode the engine asked for. UI thread only, once per
    /// pass of the shell's pump.
    /// </summary>
    public void PumpCursorMode() => _window?.PumpCursorMode();

    /// <inheritdoc/>
    // Nothing to do: the surface is the HWND, and DestroyNativeControlCore
    // raises SurfaceDestroying when the control goes.
    public void Shutdown()
    {
    }

    /// <summary>Hands the keyboard to the engine's native child window.</summary>
    // Win32 SetFocus, not Avalonia's Focus(): a NativeControlHost is not
    // focusable, so that call does nothing.
    public void FocusEngine() => _window?.FocusKeyboard();

    /// <inheritdoc/>
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!IsSupported)
            return base.CreateNativeControlCore(parent);

        _window = new Win32ViewportWindow(parent.Handle) { Host = _host };
        _window.ShellChord += chord => ShellChord?.Invoke(chord);
        _window.ContextMenuRequested += (x, y) => ContextMenuRequested?.Invoke(x, y);

        SurfaceCreated?.Invoke(_window);

        return new PlatformHandle(_window.NativeHandle, "HWND");
    }

    /// <inheritdoc/>
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_window is null)
        {
            base.DestroyNativeControlCore(control);
            return;
        }

        // Before the window goes: presenting into a destroyed HWND fails in
        // the driver, not as an exception.
        SurfaceDestroying?.Invoke();

        _window.Dispose();
        _window = null;
    }
}
