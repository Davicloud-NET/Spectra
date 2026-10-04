using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using SpectraEngine.Core.Graphics;
using System;

namespace SpectraEngine.Editor.Viewport;

// A surface with no window: the engine renders into a shared target and the
// compositor puts it on screen. All it carries is a size, written on the UI thread.
internal sealed class CompositedRenderSurface : IRenderSurface
{
    // Never zero: a collapsed pane would hand the renderer a degenerate size.
    private Vector2D<int> _size = new(1, 1);

    /// <inheritdoc/>
    public RenderSurfaceKind Kind => RenderSurfaceKind.Composited;

    /// <inheritdoc/>
    public nint NativeHandle => 0;

    /// <inheritdoc/>
    public IGLContext? GLContext => null;

    /// <inheritdoc/>
    public Vector2D<int> PixelSize => _size;

    /// <inheritdoc/>
    public event Action<Vector2D<int>>? Resized;

    // Size in real pixels. Raise only on a change: the renderer rebuilds its
    // shared target for every Resized it gets.
    internal void SetPixelSize(int width, int height)
    {
        var size = new Vector2D<int>(Math.Max(1, width), Math.Max(1, height));
        if (size == _size)
            return;

        _size = size;
        Resized?.Invoke(size);
    }
}
