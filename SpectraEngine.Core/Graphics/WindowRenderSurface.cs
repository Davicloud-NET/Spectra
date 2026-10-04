using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using System;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// The standalone path's <see cref="IRenderSurface"/>: a Silk.NET window
/// exposed as a handle, a context and a size.
/// </summary>
public sealed class WindowRenderSurface : IRenderSurface
{
    private readonly IWindow _window;

    /// <summary>Wraps a window the engine created.</summary>
    public WindowRenderSurface(IWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        _window.FramebufferResize += OnFramebufferResize;
    }

    /// <inheritdoc/>
    public event Action<Vector2D<int>>? Resized;

    /// <inheritdoc/>
    // None is a normal answer: an OpenGL window may expose no native handle.
    public RenderSurfaceKind Kind
    {
        get
        {
            if (_window.Native is not { } native)
                return RenderSurfaceKind.None;

            if (native.Win32 is not null) return RenderSurfaceKind.Win32;
            if (native.X11 is not null) return RenderSurfaceKind.X11;
            if (native.Wayland is not null) return RenderSurfaceKind.Wayland;
            return RenderSurfaceKind.None;
        }
    }

    /// <inheritdoc/>
    public nint NativeHandle
    {
        get
        {
            if (_window.Native is not { } native)
                return 0;

            if (native.Win32 is { } win32) return win32.Hwnd;
            if (native.X11 is { } x11) return (nint)x11.Window;
            if (native.Wayland is { } wayland) return wayland.Surface;
            return 0;
        }
    }

    /// <inheritdoc/>
    public IGLContext? GLContext => _window.GLContext;

    /// <inheritdoc/>
    public Vector2D<int> PixelSize => _window.FramebufferSize;

    /// <summary>The window behind this surface, for the engine code that owns its title, cursor and event pump.</summary>
    public IWindow Window => _window;

    private void OnFramebufferResize(Vector2D<int> size) => Resized?.Invoke(size);
}
