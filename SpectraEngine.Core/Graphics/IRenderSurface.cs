using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using System;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// Which platform's handle <see cref="IRenderSurface.NativeHandle"/> carries.
/// </summary>
// Values are compared and stored. Append new kinds, never renumber.
public enum RenderSurfaceKind
{
    /// <summary>No native handle: an OpenGL surface, or a headless one.</summary>
    None,

    /// <summary>A Win32 <c>HWND</c>. The only kind the D3D backends accept.</summary>
    Win32,

    /// <summary>An X11 window id.</summary>
    X11,

    /// <summary>A Wayland surface pointer.</summary>
    Wayland,

    /// <summary>
    /// No window and no swap chain: the renderer draws into a shared target
    /// somebody else presents. <see cref="IRenderSurface.NativeHandle"/> is zero.
    /// </summary>
    Composited,
}

/// <summary>
/// What a renderer draws into and presents to. The engine need not own it,
/// which is what lets a shell embed the engine. D3D backends need a
/// <see cref="RenderSurfaceKind.Win32"/> handle, OpenGL needs <see cref="GLContext"/>.
/// </summary>
// PixelSize and Resized belong to the surface's own thread. The render thread
// reads Renderer.GetFramebufferSize instead.
public interface IRenderSurface
{
    /// <summary>Which platform's handle <see cref="NativeHandle"/> is, if any.</summary>
    RenderSurfaceKind Kind { get; }

    /// <summary>The native surface handle, or zero when there is none.</summary>
    nint NativeHandle { get; }

    /// <summary>The OpenGL context bound to this surface, or null. Only the GL backend uses it.</summary>
    IGLContext? GLContext { get; }

    /// <summary>Current size in pixels. Read on the surface's own thread.</summary>
    Vector2D<int> PixelSize { get; }

    /// <summary>Raised on the surface's own thread when its pixel size changes.</summary>
    event Action<Vector2D<int>>? Resized;
}
