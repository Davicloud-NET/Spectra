using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using SpectraEngine.Core.Graphics;
using System;

namespace SpectraEngine.Executable;

// A windowless composited surface, shared by the probes that measure that route.
internal sealed class CompositedProbeSurface(int width, int height) : IRenderSurface
{
    public RenderSurfaceKind Kind => RenderSurfaceKind.Composited;

    public nint NativeHandle => 0;

    public IGLContext? GLContext => null;

    public Vector2D<int> PixelSize => new(width, height);

    // Never fires, but Engine.AttachSurface subscribes on every run.
    public event Action<Vector2D<int>>? Resized
    {
        add { }
        remove { }
    }
}
