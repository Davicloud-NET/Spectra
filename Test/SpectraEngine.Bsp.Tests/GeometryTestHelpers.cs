using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Bsp.Tests;

// Vertices are keyed on the VertexSnapper.GridSize lattice, so points from
// different floating-point paths compare equal.
internal static class GeometryTestHelpers
{
    public static (long X, long Y, long Z) LatticeKey(Vector3 v) => (
        (long)MathF.Round(v.X / VertexSnapper.GridSize),
        (long)MathF.Round(v.Y / VertexSnapper.GridSize),
        (long)MathF.Round(v.Z / VertexSnapper.GridSize));

    // Every undirected edge must be shared by two polygons. One means a crack,
    // three or more means doubled surfaces.
    public static void ShouldBeClosedTwoManifold(IReadOnlyList<Polygon> polygons)
    {
        var edgeCounts = new Dictionary<((long, long, long) A, (long, long, long) B), int>();

        foreach (Polygon poly in polygons)
        {
            IReadOnlyList<Vector3> verts = poly.Vertices;
            for (int i = 0; i < verts.Count; i++)
            {
                var a = LatticeKey(verts[i]);
                var b = LatticeKey(verts[(i + 1) % verts.Count]);

                // Sub-grid sliver edge.
                if (a.Equals(b))
                    continue;

                var key = Comparer<(long, long, long)>.Default.Compare(a, b) <= 0 ? (a, b) : (b, a);
                edgeCounts[key] = edgeCounts.TryGetValue(key, out int count) ? count + 1 : 1;
            }
        }

        edgeCounts.ShouldNotBeEmpty();
        foreach (var (edge, count) in edgeCounts)
            count.ShouldBe(2, $"undirected edge {edge.A} -- {edge.B} is used by {count} polygon(s), expected exactly 2");
    }

    // Planar convex polygons only.
    public static float Area(Polygon poly)
    {
        IReadOnlyList<Vector3> v = poly.Vertices;
        Vector3 crossSum = Vector3.Zero;
        for (int i = 1; i + 1 < v.Count; i++)
            crossSum += Vector3.Cross(v[i] - v[0], v[i + 1] - v[0]);
        return crossSum.Length() * 0.5f;
    }

    public static float TotalArea(IEnumerable<Polygon> polygons)
    {
        float total = 0f;
        foreach (Polygon poly in polygons)
            total += Area(poly);
        return total;
    }

    // Inclusive.
    public static bool Contains(this Aabb bounds, Vector3 point) =>
        point.X >= bounds.Min.X && point.X <= bounds.Max.X &&
        point.Y >= bounds.Min.Y && point.Y <= bounds.Max.Y &&
        point.Z >= bounds.Min.Z && point.Z <= bounds.Max.Z;
}
