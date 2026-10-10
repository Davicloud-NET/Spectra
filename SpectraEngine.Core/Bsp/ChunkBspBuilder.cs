using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpectraEngine.Core.Bsp;

// Per-cell BSP stage of a static-world compile. Each occupied cell gets a
// tree built from the welded surfaces of every brush resident in it, cut to
// the cell grown by ChunkGrid.WeldBand. Routed queries must answer the same
// as one tree over the whole world; tests pin that.
//
// Why a cell's subset is enough inside the grown cell:
// - a brush containing a point of it is resident, so only residents make it solid
// - a carver reaching into it is resident too, so its faces close the union there
//
// Why the cut: a leaf is solid or empty by the side of the surface that split
// it off last, which holds only where the surfaces are closed. Past the band
// a resident's face can be buried in a brush that is not resident. A leaf
// that reaches the cell through that gap takes its answer from out there.
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
    // same order as CsgWorld.Surfaces, less what lies outside the grown cell.
    // residentWelded is what a cache entry stores.
    internal static BspTree BuildCellTree(
        WorldChunk chunk, Func<int, Polygon[]> weldedOf, out Polygon[][] residentWelded)
    {
        IReadOnlyList<int> residents = chunk.ResidentBrushIndices;

        residentWelded = new Polygon[residents.Count][];
        for (int k = 0; k < residentWelded.Length; k++)
            residentWelded[k] = weldedOf(residents[k]);

        Aabb reach = chunk.Coord.Bounds.Expanded(ChunkGrid.WeldBand);
        var input = new List<Polygon>();
        foreach (Polygon[] surfaces in residentWelded)
        {
            foreach (Polygon surface in surfaces)
            {
                if (surface.ClippedTo(reach) is { } inside)
                    input.Add(inside);
            }
        }

        return BspTree.BuildFromSurfaces(input);
    }
}
