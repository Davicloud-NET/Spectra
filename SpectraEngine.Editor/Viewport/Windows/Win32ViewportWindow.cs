using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Input;
using System;
using System.Runtime.InteropServices;

namespace SpectraEngine.Editor.Viewport.Windows;

// The Win32 child window the engine renders into. The shell owns the window
// class because mouse messages over a child go to its own window procedure and
// never bubble to the parent. Input decisions live in ViewportInputRouter.
// Sits above the XAML: Avalonia cannot draw over it. UI thread only.
internal sealed class Win32ViewportWindow : IRenderSurface, IViewportCursor, IDisposable
{
    private const string ClassName = "SpectraViewportWindow";

    private static ushort _classAtom;
    private static nint _arrowCursor;

    // Kept alive in a field: the class registration holds a raw function
    // pointer to it, and a collected delegate is a call into freed memory.
    private static Win32Interop.WndProc? _sharedProc;

    private readonly ViewportInputRouter _router;

    private nint _hwnd;
    private Vector2D<int> _size;
    private EngineHost? _host;

    internal Win32ViewportWindow(nint parent)
    {
        EnsureClassRegistered();

        _hwnd = Win32Interop.CreateWindowEx(
            exStyle: 0,
            ClassName,
            windowName: null,
            Win32Interop.WS_CHILD | Win32Interop.WS_VISIBLE |
            Win32Interop.WS_CLIPSIBLINGS | Win32Interop.WS_CLIPCHILDREN,
            x: 0, y: 0, width: 1, height: 1,
            parent,
            menu: 0,
            Win32Interop.GetModuleHandle(null),
            param: 0);

        if (_hwnd == 0)
            throw new InvalidOperationException(
                $"Could not create the viewport window (Win32 error {Marshal.GetLastWin32Error()}).");

        _router = new ViewportInputRouter(this);
        _router.ShellChord += chord => ShellChord?.Invoke(chord);
        _router.ContextMenuRequested += (x, y) => ContextMenuRequested?.Invoke(x, y);

        _windows[_hwnd] = this;
        _size = ReadClientSize();
    }

    // Not GWLP_USERDATA: the first messages arrive during CreateWindowEx,
    // before there is an instance to store.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<nint, Win32ViewportWindow> _windows = new();

    /// <inheritdoc/>
    public RenderSurfaceKind Kind => RenderSurfaceKind.Win32;

    /// <inheritdoc/>
    public nint NativeHandle => _hwnd;

    /// <inheritdoc/>
    // No embedded WGL context yet, so the shell runs a D3D backend.
    public IGLContext? GLContext => null;

    /// <inheritdoc/>
    public Vector2D<int> PixelSize => _size;

    /// <inheritdoc/>
    public event Action<Vector2D<int>>? Resized;

    public event Action<ShellChord>? ShellChord;

    // A right press that never became a drag, in client pixels.
    public event Action<int, int>? ContextMenuRequested;

    // Input arriving while this is null is dropped, not queued.
    internal EngineHost? Host
    {
        get => _host;
        set
        {
            _host = value;
            _router.Sink = value is null ? null : new EngineHostSink(value);
        }
    }

    // UI thread, once per pass of the shell's pump. The engine has no device
    // to hide, so it publishes a request and the router applies it here.
    internal void PumpCursorMode()
    {
        if (_host is not { } host)
            return;

        _router.ApplyCursorMode(host.RequestedCursorMode);
        host.ApplyPendingCursorMode();
    }

    internal void FocusKeyboard()
    {
        if (_hwnd != 0)
            Win32Interop.SetFocus(_hwnd);
    }

    public void Dispose()
    {
        if (_hwnd == 0)
            return;

        // Release the cursor lock while there is still a window to release it against.
        _router.ApplyCursorMode(CursorMode.Normal);

        _windows.TryRemove(_hwnd, out _);
        Win32Interop.DestroyWindow(_hwnd);
        _hwnd = 0;
    }

    private static void EnsureClassRegistered()
    {
        if (_classAtom != 0)
            return;

        _sharedProc = StaticWndProc;
        _arrowCursor = Win32Interop.LoadCursor(0, Win32Interop.IDC_ARROW);

        var windowClass = new Win32Interop.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32Interop.WNDCLASSEX>(),
            style = Win32Interop.CS_OWNDC | Win32Interop.CS_HREDRAW | Win32Interop.CS_VREDRAW,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_sharedProc),
            hInstance = Win32Interop.GetModuleHandle(null),
            hCursor = _arrowCursor,

            // No background brush: an OS erase flashes on every resize.
            hbrBackground = 0,
            lpszClassName = Marshal.StringToHGlobalUni(ClassName),
        };

        _classAtom = Win32Interop.RegisterClassEx(ref windowClass);
        if (_classAtom == 0)
            throw new InvalidOperationException(
                $"Could not register the viewport window class (Win32 error {Marshal.GetLastWin32Error()}).");
    }

    private static nint StaticWndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (_windows.TryGetValue(hwnd, out Win32ViewportWindow? window) &&
            window.HandleMessage(message, wParam, lParam, out nint result))
        {
            return result;
        }

        return Win32Interop.DefWindowProc(hwnd, message, wParam, lParam);
    }

    // Cached: this runs on every mouse move. Stock cursors are never destroyed.
    private nint ResolveCursor()
    {
        CursorShape shape = _host?.RequestedCursorShape ?? CursorShape.Arrow;

        int id = shape switch
        {
            CursorShape.Crosshair => Win32Interop.IDC_CROSS,
            CursorShape.Grab or CursorShape.Grabbing => Win32Interop.IDC_HAND,
            CursorShape.SizeWestEast => Win32Interop.IDC_SIZEWE,
            CursorShape.SizeNorthSouth => Win32Interop.IDC_SIZENS,
            CursorShape.SizeNorthWestSouthEast => Win32Interop.IDC_SIZENWSE,
            CursorShape.SizeNorthEastSouthWest => Win32Interop.IDC_SIZENESW,
            CursorShape.SizeAll or CursorShape.Rotate => Win32Interop.IDC_SIZEALL,
            CursorShape.No => Win32Interop.IDC_NO,
            _ => Win32Interop.IDC_ARROW,
        };

        if (_cursors.TryGetValue(id, out nint handle))
            return handle;

        handle = Win32Interop.LoadCursor(0, id);
        if (handle == 0)
            handle = _arrowCursor;

        _cursors[id] = handle;
        return handle;
    }

    private readonly Dictionary<int, nint> _cursors = [];

    private bool HandleMessage(uint message, nint wParam, nint lParam, out nint result)
    {
        result = 0;

        switch (message)
        {
            case Win32Interop.WM_ERASEBKGND:
                // Claimed and ignored: an OS erase before the next present flashes.
                result = 1;
                return true;

            case Win32Interop.WM_SETCURSOR:
                if ((lParam & 0xFFFF) != Win32Interop.HTCLIENT)
                    return false;

                // Must be set in this message: Windows re-asserts the class
                // cursor on every mouse move, so a SetCursor from anywhere
                // else is reverted within a frame.
                Win32Interop.SetCursor(_router.IsCursorLocked ? 0 : ResolveCursor());
                result = 1;
                return true;

            case Win32Interop.WM_SIZE:
                OnResized();
                return false;

            case Win32Interop.WM_KILLFOCUS:
                _router.OnFocusLost();
                return false;

            case Win32Interop.WM_MOUSEMOVE:
                _router.OnPointerMove(Win32Interop.LowInt16(lParam), Win32Interop.HighInt16(lParam));
                return false;

            case Win32Interop.WM_LBUTTONDOWN: OnButtonDown(PointerButtons.Left); return false;
            case Win32Interop.WM_RBUTTONDOWN: OnButtonDown(PointerButtons.Right); return false;
            case Win32Interop.WM_MBUTTONDOWN: OnButtonDown(PointerButtons.Middle); return false;

            case Win32Interop.WM_LBUTTONUP: _router.OnPointerUp(PointerButtons.Left); return false;
            case Win32Interop.WM_RBUTTONUP: _router.OnPointerUp(PointerButtons.Right); return false;
            case Win32Interop.WM_MBUTTONUP: _router.OnPointerUp(PointerButtons.Middle); return false;

            case Win32Interop.WM_MOUSEWHEEL:
                _router.OnScroll(0f, Win32Interop.HighInt16(wParam) / (float)Win32Interop.WHEEL_DELTA);
                return false;

            case Win32Interop.WM_MOUSEHWHEEL:
                _router.OnScroll(Win32Interop.HighInt16(wParam) / (float)Win32Interop.WHEEL_DELTA, 0f);
                return false;

            case Win32Interop.WM_KEYDOWN:
            case Win32Interop.WM_SYSKEYDOWN:
                // Every system key is claimed (F10 alone is one), or the OS
                // treats it as a menu accelerator and eats the next keystroke.
                if (_router.OnKeyDown(Win32Keys.ToInputKey((int)wParam, lParam), ReadModifiers()))
                {
                    result = 0;
                    return true;
                }

                result = 0;
                return message == Win32Interop.WM_SYSKEYDOWN;

            case Win32Interop.WM_KEYUP:
            case Win32Interop.WM_SYSKEYUP:
                _router.OnKeyUp(Win32Keys.ToInputKey((int)wParam, lParam));
                result = 0;
                return message == Win32Interop.WM_SYSKEYUP;

            case Win32Interop.WM_DESTROY:
                _windows.TryRemove(_hwnd, out _);
                return false;

            default:
                return false;
        }
    }

    // Super is not read: nothing tests for it.
    private static KeyModifiers ReadModifiers()
    {
        KeyModifiers modifiers = KeyModifiers.None;

        if (Win32Interop.IsKeyDown(Win32Interop.VK_CONTROL))
            modifiers |= KeyModifiers.Control;
        if (Win32Interop.IsKeyDown(Win32Interop.VK_SHIFT))
            modifiers |= KeyModifiers.Shift;
        if (Win32Interop.IsKeyDown(Win32Interop.VK_MENU))
            modifiers |= KeyModifiers.Alt;

        return modifiers;
    }

    private void OnButtonDown(PointerButtons button)
    {
        // Focus follows the click, or shortcuts keep going to another panel.
        // Before the router, so its capture is taken by a focused window.
        Win32Interop.SetFocus(_hwnd);
        _router.OnPointerDown(button);
    }

    private void OnResized()
    {
        Vector2D<int> size = ReadClientSize();
        if (size == _size)
            return;

        _size = size;
        Resized?.Invoke(size);
    }

    /// <inheritdoc/>
    ViewportSize IViewportCursor.ClientSize
    {
        get
        {
            Vector2D<int> size = ReadClientSize();
            return new ViewportSize(size.X, size.Y);
        }
    }

    /// <inheritdoc/>
    int IViewportCursor.DragSlack
    {
        get
        {
            // Half of SM_CXDRAG: the metric is a full width, travel is measured
            // from the centre. Fallback is the metric's default at 100%.
            const int fallback = 4;

            if (_hwnd == 0)
                return fallback;

            try
            {
                uint dpi = Win32Interop.GetDpiForWindow(_hwnd);
                if (dpi == 0)
                    return fallback;

                int width = Win32Interop.GetSystemMetricsForDpi(Win32Interop.SM_CXDRAG, dpi);
                return width > 1 ? width / 2 : fallback;
            }
            catch (EntryPointNotFoundException)
            {
                // Pre-1607 Windows has neither entry point.
                return fallback;
            }
        }
    }

    /// <inheritdoc/>
    ViewportPoint IViewportCursor.ClientToScreen(ViewportPoint client)
    {
        if (_hwnd == 0)
            return client;

        var point = new Win32Interop.POINT { X = client.X, Y = client.Y };
        Win32Interop.ClientToScreen(_hwnd, ref point);
        return new ViewportPoint(point.X, point.Y);
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

        if (_hwnd == 0 || !Win32Interop.GetClientRect(_hwnd, out Win32Interop.RECT client))
            return;

        var topLeft = new Win32Interop.POINT { X = client.Left, Y = client.Top };
        var bottomRight = new Win32Interop.POINT { X = client.Right, Y = client.Bottom };
        Win32Interop.ClientToScreen(_hwnd, ref topLeft);
        Win32Interop.ClientToScreen(_hwnd, ref bottomRight);

        Win32Interop.ClipCursor(new Win32Interop.RECT
        {
            Left = topLeft.X,
            Top = topLeft.Y,
            Right = bottomRight.X,
            Bottom = bottomRight.Y,
        });
    }

    /// <inheritdoc/>
    void IViewportCursor.SetCursorHidden(bool hidden) =>
        Win32Interop.SetCursor(hidden ? 0 : _arrowCursor);

    /// <inheritdoc/>
    void IViewportCursor.SetPointerCapture(bool captured)
    {
        if (captured)
        {
            if (_hwnd != 0)
                Win32Interop.SetCapture(_hwnd);
        }
        else
        {
            Win32Interop.ReleaseCapture();
        }
    }

    // The router must not name EngineHost: tests construct it against a recorder.
    private sealed class EngineHostSink(EngineHost host) : IInputSink
    {
        public void Submit(in InputEvent input) => host.SubmitInput(in input);
    }

    private Vector2D<int> ReadClientSize()
    {
        if (_hwnd == 0 || !Win32Interop.GetClientRect(_hwnd, out Win32Interop.RECT client))
            return new Vector2D<int>(1, 1);

        // Never zero: a collapsed pane would hand the swap chain a degenerate size.
        return new Vector2D<int>(
            Math.Max(1, client.Right - client.Left),
            Math.Max(1, client.Bottom - client.Top));
    }
}
