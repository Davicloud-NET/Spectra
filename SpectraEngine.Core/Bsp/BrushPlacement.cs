using System.Numerics;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// A brush and the world transform that places it, captured on the render
/// thread. The background CSG compile reads only these, never live scene state.
/// </summary>
/// <param name="Transform">
/// World-from-local. Must be rigid (no scale): the CSG epsilons assume unit
/// plane normals. Callers validate it.
/// </param>
public readonly record struct BrushPlacement(Brush Brush, Matrix4x4 Transform)
{
    /// <summary>World-space bounds of the brush under this placement.</summary>
    public Aabb WorldBounds => Brush.LocalBounds.Transform(Transform);
}
