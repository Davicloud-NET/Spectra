using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

// CPU-only mesh with real positions and bounds. The resize tool and the gizmo's
// selection box read HasLocalBounds and LocalBounds; without both a test would
// hit the no-bounds fallback by accident.
internal sealed class BoxMesh : Mesh
{
    private BoxMesh(Vector3[] corners, Aabb bounds)
    {
        Positions = corners;
        LocalBounds = bounds;
        HasLocalBounds = true;
        IndexCount = 0;
    }

    public static BoxMesh Centred(Vector3 halfExtents)
    {
        var min = -halfExtents;
        var max = halfExtents;
        Vector3[] corners =
        [
            new(min.X, min.Y, min.Z),
            new(max.X, min.Y, min.Z),
            new(min.X, max.Y, min.Z),
            new(max.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z),
            new(max.X, min.Y, max.Z),
            new(min.X, max.Y, max.Z),
            new(max.X, max.Y, max.Z),
        ];
        return new BoxMesh(corners, new Aabb(min, max));
    }

    public static BoxMesh Spanning(Vector3 min, Vector3 max)
    {
        Vector3[] corners =
        [
            new(min.X, min.Y, min.Z),
            new(max.X, min.Y, min.Z),
            new(min.X, max.Y, min.Z),
            new(max.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z),
            new(max.X, min.Y, max.Z),
            new(min.X, max.Y, max.Z),
            new(max.X, max.Y, max.Z),
        ];
        return new BoxMesh(corners, new Aabb(min, max));
    }

    public override void Draw()
    {
    }

    public override void DrawInstanced(InstanceBuffer instances, int instanceCount, int firstInstance = 0)
    {
    }

    public override void Dispose()
    {
    }
}
