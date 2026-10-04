using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// Removes T-junctions by inserting any vertex that lies on another polygon's
/// edge into that edge. Without it the rasterizer leaves one-pixel cracks
/// where two surfaces meet along a line with different vertex counts.
/// </summary>
public static class TJunctionWelder
{
    private const float Epsilon = Polygon.Epsilon;
    private const float EpsilonSq = Epsilon * Epsilon;

    // Spatial hash cell, world units. Affects speed only. A power of two, so
    // the coordinate-to-cell mapping is exact in floating point.
    private const float CellSize = 0.5f;
    private const float InvCellSize = 1f / CellSize;

    // A whole extra cell of padding, against rounding in the slab arithmetic
    // at large world coordinates. The candidate tests themselves stay exact.
    private const float QueryInflation = Epsilon + CellSize;

    /// <summary>
    /// Welds the polygons against their own vertices. Inputs are not modified;
    /// a polygon with no T-junctions is returned as the same instance.
    /// </summary>
    public static Polygon[] Weld(IReadOnlyList<Polygon> polygons)
        => Weld(polygons, polygons);

    /// <summary>
    /// Welds the polygons against the vertices of <paramref name="candidateSurfaces"/>.
    /// The result depends only on the set of candidates near each edge, not on
    /// their order, duplicates or extras.
    /// </summary>
    public static Polygon[] Weld(IReadOnlyList<Polygon> polygons, IReadOnlyList<Polygon> candidateSurfaces)
    {
        var grid = new Dictionary<(int X, int Y, int Z), List<Vector3>>();
        foreach (Polygon poly in candidateSurfaces)
        {
            foreach (Vector3 v in poly.VertexSpan)
            {
                var key = CellOf(v);
                if (!grid.TryGetValue(key, out List<Vector3>? cell))
                    grid[key] = cell = [];
                cell.Add(v);
            }
        }

        // The grid is read-only from here, so the parallel loop is deterministic.
        var result = new Polygon[polygons.Count];
        Parallel.For(0, polygons.Count,
            static () => new WeldScratch(),
            (i, _, scratch) =>
            {
                result[i] = WeldOne(polygons[i], grid, scratch);
                return scratch;
            },
            static _ => { });
        return result;
    }

    // One per worker. Contents must never escape into a result.
    private sealed class WeldScratch
    {
        public readonly List<Vector3> NewVerts = [];
        public readonly List<(float T, Vector3 V)> Insertions = [];
    }

    private static Polygon WeldOne(Polygon poly, Dictionary<(int X, int Y, int Z), List<Vector3>> grid, WeldScratch scratch)
    {
        ReadOnlySpan<Vector3> oldVerts = poly.VertexSpan;
        List<Vector3> newVerts = scratch.NewVerts;
        List<(float T, Vector3 V)> insertions = scratch.Insertions;
        newVerts.Clear();

        for (int i = 0; i < oldVerts.Length; i++)
        {
            Vector3 a = oldVerts[i];
            Vector3 b = oldVerts[(i + 1) % oldVerts.Length];
            newVerts.Add(a);

            Vector3 ab = b - a;
            float abLengthSq = ab.LengthSquared();
            if (abLengthSq < EpsilonSq)
                continue;

            insertions.Clear();
            CollectInsertions(a, b, ab, abLengthSq, grid, insertions);

            if (insertions.Count == 0)
                continue;

            // Ties on t break on position. List.Sort is unstable, and the
            // per-cell weld passes candidates in a different order than the
            // global one, so the output must not depend on candidate order.
            insertions.Sort(static (x, y) =>
            {
                int c = x.T.CompareTo(y.T);
                if (c != 0) return c;
                c = x.V.X.CompareTo(y.V.X);
                if (c != 0) return c;
                c = x.V.Y.CompareTo(y.V.Y);
                return c != 0 ? c : x.V.Z.CompareTo(y.V.Z);
            });

            // Two polygons can contribute the same T-vertex.
            Vector3 last = a;
            foreach (var (_, v) in insertions)
            {
                if (Vector3.DistanceSquared(v, last) < EpsilonSq)
                    continue;
                newVerts.Add(v);
                last = v;
            }
        }

        // No insertions: return the same instance. The weld, BSP and mesh
        // caches validate reuse by reference identity.
        if (newVerts.Count == oldVerts.Length)
            return poly;

        var welded = new Vector3[newVerts.Count];
        newVerts.CopyTo(welded);
        return new Polygon(welded, poly.Surface, poly.Face);
    }

    // Candidates on the open segment a..b. Walks one-cell slabs along the
    // dominant axis, so a long diagonal does not visit its whole bounding box.
    private static void CollectInsertions(
        Vector3 a, Vector3 b, Vector3 ab, float abLengthSq,
        Dictionary<(int X, int Y, int Z), List<Vector3>> grid,
        List<(float T, Vector3 V)> insertions)
    {
        int axis = DominantAxis(ab);
        int u = axis == 0 ? 1 : 0;
        int w = axis == 2 ? 1 : 2;

        // abD is non-zero: dominant axis of a non-zero edge.
        float aD = Component(a, axis);
        float abD = Component(ab, axis);

        int slabMin = FloorToCell(MathF.Min(aD, aD + abD) - QueryInflation);
        int slabMax = FloorToCell(MathF.Max(aD, aD + abD) + QueryInflation);

        var pad = new Vector3(QueryInflation);

        for (int c = slabMin; c <= slabMax; c++)
        {
            // The part of the segment that can reach slab c.
            float lo = c * CellSize - QueryInflation;
            float hi = (c + 1) * CellSize + QueryInflation;
            float t0 = (lo - aD) / abD;
            float t1 = (hi - aD) / abD;
            if (t0 > t1)
                (t0, t1) = (t1, t0);
            t0 = MathF.Max(t0, 0f);
            t1 = MathF.Min(t1, 1f);
            if (t0 > t1)
                continue;

            Vector3 p0 = a + ab * t0;
            Vector3 p1 = a + ab * t1;
            Vector3 boxMin = Vector3.Min(p0, p1) - pad;
            Vector3 boxMax = Vector3.Max(p0, p1) + pad;

            int uMin = FloorToCell(Component(boxMin, u));
            int uMax = FloorToCell(Component(boxMax, u));
            int wMin = FloorToCell(Component(boxMin, w));
            int wMax = FloorToCell(Component(boxMax, w));

            for (int cu = uMin; cu <= uMax; cu++)
            {
                for (int cw = wMin; cw <= wMax; cw++)
                {
                    if (!grid.TryGetValue(MakeKey(axis, c, cu, cw), out List<Vector3>? cell))
                        continue;

                    foreach (Vector3 v in cell)
                    {
                        // The edge's own endpoints.
                        if (Vector3.DistanceSquared(v, a) < EpsilonSq) continue;
                        if (Vector3.DistanceSquared(v, b) < EpsilonSq) continue;

                        float t = Vector3.Dot(v - a, ab) / abLengthSq;
                        if (t <= 0f || t >= 1f) continue;

                        Vector3 closest = a + ab * t;
                        if (Vector3.DistanceSquared(closest, v) > EpsilonSq) continue;

                        insertions.Add((t, v));
                    }
                }
            }
        }
    }

    private static (int X, int Y, int Z) CellOf(Vector3 v) =>
        (FloorToCell(v.X), FloorToCell(v.Y), FloorToCell(v.Z));

    private static int FloorToCell(float value) => (int)MathF.Floor(value * InvCellSize);

    private static int DominantAxis(Vector3 v)
    {
        float x = MathF.Abs(v.X), y = MathF.Abs(v.Y), z = MathF.Abs(v.Z);
        return x >= y ? (x >= z ? 0 : 2) : (y >= z ? 1 : 2);
    }

    private static float Component(in Vector3 v, int axis) =>
        axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    // cu/cw follow the u/w axis mapping in CollectInsertions.
    private static (int X, int Y, int Z) MakeKey(int axis, int d, int cu, int cw) =>
        axis switch { 0 => (d, cu, cw), 1 => (cu, d, cw), _ => (cu, cw, d) };
}
