using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// Broadphase overlap detection over brush bounding boxes. A sort-and-sweep on
/// the X axis cuts brush-pair testing from O(n²) toward O(n log n), feeding the
/// CSG carve only genuine candidates instead of every other brush.
/// </summary>
public static class BrushBroadphase
{
    /// <summary>
    /// Returns, for each brush, the indices of every other brush whose bounding
    /// box overlaps it.
    /// </summary>
    public static int[][] FindOverlaps(IReadOnlyList<Aabb> bounds)
    {
        int n = bounds.Count;
        var result = new int[n][];
        if (n == 0)
            return result;

        // Sweep by min-X to discover pairs, then order each result by authored
        // placement index. Tied sweep keys no longer affect emitted geometry.
        var order = new int[n];
        for (int i = 0; i < n; i++)
            order[i] = i;
        Array.Sort(order, (a, b) => bounds[a].Min.X.CompareTo(bounds[b].Min.X));

        // Brushes whose X span is still open at the current sweep position.
        var active = new List<int>();

        // Overlapping pairs in discovery order, the sweeping brush packed into
        // the high 32 bits and its active partner into the low 32. One shared
        // buffer replaces n growing per-brush lists; the counted fill below
        // turns it into exact-size arrays with zero intermediate copies.
        var pairs = new List<long>();

        for (int s = 0; s < n; s++)
        {
            int i = order[s];
            float minX = bounds[i].Min.X;

            // Retire brushes the sweep line has passed (swap-remove; order is irrelevant).
            for (int a = active.Count - 1; a >= 0; a--)
            {
                if (bounds[active[a]].Max.X < minX)
                {
                    active[a] = active[^1];
                    active.RemoveAt(active.Count - 1);
                }
            }

            // Anything still active overlaps on X; confirm Y and Z.
            foreach (int j in active)
            {
                if (bounds[i].Intersects(bounds[j]))
                    pairs.Add(((long)i << 32) | (uint)j);
            }

            active.Add(i);
        }

        // Allocate exact neighbor storage before filling and canonical sorting.
        var counts = new int[n];
        foreach (long pair in pairs)
        {
            counts[(int)(pair >> 32)]++;
            counts[(int)pair]++;
        }

        for (int i = 0; i < n; i++)
            result[i] = counts[i] == 0 ? [] : new int[counts[i]];

        var cursors = new int[n];
        foreach (long pair in pairs)
        {
            int i = (int)(pair >> 32);
            int j = (int)pair;
            result[i][cursors[i]++] = j;
            result[j][cursors[j]++] = i;
        }

        // Geometry v2: clipping order is authored placement order, independent
        // of sweep discovery and of unrelated brushes leaving the active set.
        foreach (int[] neighbors in result) Array.Sort(neighbors);
        return result;
    }
}
