using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// Broadphase overlap detection over brush bounding boxes: a sort-and-sweep
/// on the X axis.
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

        var order = new int[n];
        for (int i = 0; i < n; i++)
            order[i] = i;
        Array.Sort(order, (a, b) => bounds[a].Min.X.CompareTo(bounds[b].Min.X));

        var active = new List<int>();

        // Sweeping brush in the high 32 bits, its partner in the low 32.
        var pairs = new List<long>();

        for (int s = 0; s < n; s++)
        {
            int i = order[s];
            float minX = bounds[i].Min.X;

            for (int a = active.Count - 1; a >= 0; a--)
            {
                if (bounds[active[a]].Max.X < minX)
                {
                    active[a] = active[^1];
                    active.RemoveAt(active.Count - 1);
                }
            }

            // Active brushes overlap on X. Check Y and Z.
            foreach (int j in active)
            {
                if (bounds[i].Intersects(bounds[j]))
                    pairs.Add(((long)i << 32) | (uint)j);
            }

            active.Add(i);
        }

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

        // Clipping order must be placement order, not sweep discovery order.
        foreach (int[] neighbors in result) Array.Sort(neighbors);
        return result;
    }
}
