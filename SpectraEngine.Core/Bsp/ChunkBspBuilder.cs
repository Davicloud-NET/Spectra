using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpectraEngine.Core.Bsp;

// Per-cell BSP stage of a static-world compile. Each occupied cell gets a
// tree built from the welded surfaces of every brush resident in it. Routed
// queries must answer the same as one tree over the whole world; tests pin that.
//
// Why a cell's subset is enough, within the cell plus ChunkGrid.WeldBand:
// - each resident contributes its complete surface set, not one clipped to the cell
// - a brush containing a point of the cell is resident, so only residents make it solid
// - a carver reaching within the band is resident too, so its faces close the union there
// Holes can exist beyond the band, where no routed query asks this cell.
//
// A cell rebuilds when a resident's welded array is not reference-identical to
// the previous compile's (CsgBspCache). The dirty-cell set is not consulted.
internal static class ChunkBspBuilder
{
    // Background-thread safe. Fresh cells build in parallel; each tree depends
    // only on its own cell's input.
    internal static void Build(
        ChunkGrid chunks,
        Polygon[][] weldedPerBrush,
        CsgBspCache? previousCache,
        bool produceCache,
        out CsgBspCache? nextCache,
        out CsgBspStats stats)
    {
        IReadOnlyList<WorldChunk> cells = chunks.OrderedChunks;
        CsgBspCache.Entry[]? entries = produceCache ? new CsgBspCache.Entry[cells.Count] : null;

        var needsBuild = new List<int>();
        int reused = 0;
        for (int c = 0; c < cells.Count; c++)
        {
            WorldChunk chunk = cells[c];
            if (previousCache is not null &&
                previousCache.TryGet(chunk.Coord, out CsgBspCache.Entry? cached) &&
                CsgBspCache.ResidentsMatch(cached, chunk, weldedPerBrush))
            {
                chunk.AttachBsp(cached.Tree);
                if (entries is not null)
                    entries[c] = cached;
                reused++;
                continue;
            }
            needsBuild.Add(c);
        }

        // Each cell writes only its own chunk and entry slot.
        if (needsBuild.Count == 1)
        {
            BuildCell(needsBuild[0]);
        }
        else if (needsBuild.Count > 1)
        {
            Parallel.For(0, needsBuild.Count, i => BuildCell(needsBuild[i]));
        }

        stats = new CsgBspStats(reused, needsBuild.Count);
        nextCache = entries is not null ? CsgBspCache.FromEntries(cells, entries) : null;
        return;

        void BuildCell(int c)
        {
            WorldChunk chunk = cells[c];
            BspTree tree = BuildCellTree(chunk, i => weldedPerBrush[i], out Polygon[][] residentWelded);
            chunk.AttachBsp(tree);
            if (entries is not null)
                entries[c] = new CsgBspCache.Entry(residentWelded, tree);
        }
    }

    // Shared with the incremental compile so both build identical trees.
    // Input is every resident's welded set in ascending placement order, the
    // same order as CsgWorld.Surfaces. residentWelded is what a cache entry stores.
    internal static BspTree BuildCellTree(
        WorldChunk chunk, Func<int, Polygon[]> weldedOf, out Polygon[][] residentWelded)
    {
        IReadOnlyList<int> residents = chunk.ResidentBrushIndices;

        residentWelded = new Polygon[residents.Count][];
        int total = 0;
        for (int k = 0; k < residentWelded.Length; k++)
        {
            residentWelded[k] = weldedOf(residents[k]);
            total += residentWelded[k].Length;
        }

        var input = new Polygon[total];
        int offset = 0;
        foreach (Polygon[] surfaces in residentWelded)
        {
            surfaces.CopyTo(input, offset);
            offset += surfaces.Length;
        }

        return BspTree.BuildFromSurfaces(input);
    }
}
