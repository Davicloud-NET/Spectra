using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// Snaps polygon vertices to a fine grid, so a corner shared by two faces
/// is bit-identical on both and the rasterizer leaves no cracks.
/// </summary>
public static class VertexSnapper
{
    /// <summary>Snap grid in world units: above float noise, below anything visible.</summary>
    public const float GridSize = 1e-4f;

    /// <summary>Returns new polygons with every vertex snapped. Inputs are not modified.</summary>
    public static Polygon[] Snap(IReadOnlyList<Polygon> polygons)
    {
        var result = new Polygon[polygons.Count];
        for (int i = 0; i < polygons.Count; i++)
        {
            ReadOnlySpan<Vector3> oldVerts = polygons[i].VertexSpan;
            var newVerts = new Vector3[oldVerts.Length];
            for (int j = 0; j < oldVerts.Length; j++)
                newVerts[j] = SnapVertex(oldVerts[j]);
            result[i] = new Polygon(newVerts, polygons[i].Surface, polygons[i].Face);
        }
        return result;
    }

    private static Vector3 SnapVertex(Vector3 v) => new(
        MathF.Round(v.X / GridSize) * GridSize,
        MathF.Round(v.Y / GridSize) * GridSize,
        MathF.Round(v.Z / GridSize) * GridSize);
}
