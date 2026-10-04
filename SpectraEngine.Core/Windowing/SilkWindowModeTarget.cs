using Silk.NET.Maths;
using Silk.NET.Windowing;
using System;

namespace SpectraEngine.Core.Windowing;

// Silk.NET IWindow as an IWindowModeTarget. Window thread only: everything
// forwards to GLFW.
internal sealed class SilkWindowModeTarget : IWindowModeTarget
{
    private readonly IWindow _window;

    internal SilkWindowModeTarget(IWindow window)
    {
        _window = window;
    }

    /// <inheritdoc/>
    public WindowRect Bounds
    {
        get
        {
            Vector2D<int> position = _window.Position;
            Vector2D<int> size = _window.Size;
            return new WindowRect(position.X, position.Y, size.X, size.Y);
        }
        set
        {
            _window.Position = new Vector2D<int>(value.X, value.Y);
            _window.Size = new Vector2D<int>(value.Width, value.Height);
        }
    }

    /// <inheritdoc/>
    public bool Decorated
    {
        // WindowBorder has no plain "decorated". Resizable is the windowed default.
        get => _window.WindowBorder != WindowBorder.Hidden;
        set => _window.WindowBorder = value ? WindowBorder.Resizable : WindowBorder.Hidden;
    }

    /// <inheritdoc/>
    public bool IsMaximized
    {
        get => _window.WindowState == WindowState.Maximized;
        set => _window.WindowState = value ? WindowState.Maximized : WindowState.Normal;
    }

    /// <inheritdoc/>
    public bool TryGetDisplayBounds(out WindowRect bounds)
    {
        if (_window.Monitor is { } monitor)
        {
            // Silk's GLFW monitor.Bounds is the work area (desktop minus
            // taskbar). The video mode has the real resolution, so take the
            // larger per axis. No video mode falls back to the work area.
            Rectangle<int> area = monitor.Bounds;
            Vector2D<int> resolution = monitor.VideoMode.Resolution ?? area.Size;
            bounds = new WindowRect(
                area.Origin.X,
                area.Origin.Y,
                Math.Max(area.Size.X, resolution.X),
                Math.Max(area.Size.Y, resolution.Y));
            return true;
        }

        bounds = default;
        return false;
    }
}
