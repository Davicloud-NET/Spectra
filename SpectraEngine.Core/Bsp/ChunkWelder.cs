using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Bsp;

// Per-cell snap + weld stage of a static-world compile. Output must be
// bit-identical to the global VertexSnapper.Snap + TJunctionWelder.Weld over
// the whole surface list.
//
// A brush's candidates are every brush resident in any cell of its footprint.
// That is enough: a snapped edge point lies within GridSize/2 of its brush's
// AABB, and a vertex within Epsilon of it lies within Epsilon + GridSize/2 of
// its own brush's AABB, which is inside ChunkGrid.WeldBand. So the other brush
// is resident in a footprint cell.
//
// Reuse is decided by carve-array identity, not by dirty cells: a brush far
// from an edit still re-welds when a large neighbour spanning both re-carves.
internal static class ChunkWelder
{
    // Returns welded surfaces index-aligned with the placements. Pure CPU,
    // safe off the render thread.
    internal static Polygon[][] Weld(
        IReadOnlyList<BrushPlacement> placements,
        Polygon[][] perBrushSurfaces,
        ChunkGrid chunks,
        CsgWeldCache? previousCache,
        bool produceCache,
        out CsgWeldCache? nextCache,
        out CsgWeldStats stats)
        => Weld(placements, perBrushSurfaces, chunks, previousCache, produceCache, out nextCache, out stats, out _);

    // Also returns the candidate sets, which the incremental compile patches.
    internal static Polygon[][] Weld(
        IReadOnlyList<BrushPlacement> placements,
        Polygon[][] perBrushSurfaces,
        ChunkGrid chunks,
        CsgWeldCache? previousCache,
        bool produceCache,
        out CsgWeldCache? nextCache,
        out CsgWeldStats stats,
        out int[][] candidatesOut,
        Polygon[]?[]? retainedSnaps = null)
    {
        int n = placements.Count;
        var welded = new Polygon[n][];
        CsgWeldCache.Entry[]? entries = produceCache ? new CsgWeldCache.Entry[n] : null;

        int[][] candidates = ComputeCandidateSets(placements, chunks);
        candidatesOut = candidates;

        // Validation is memoized per (stored carves, candidate set) reference
        // pair, so a dense cell validates in linear time.
        var ownedByCell = new Dictionary<ChunkCoord, List<int>>();
        var cellOrder = new List<ChunkCoord>();
        var validationMemo = new Dictionary<(Polygon[][] Stored, int[] Current), bool>();
        int reused = 0;
        for (int i = 0; i < n; i++)
        {
            if (previousCache is not null &&
                previousCache.TryGet(perBrushSurfaces[i], out CsgWeldCache.Entry? cached))
            {
                (Polygon[][], int[]) memoKey = (cached.CandidateCarves, candidates[i]);
                if (!validationMemo.TryGetValue(memoKey, out bool candidatesValid))
                {
                    candidatesValid = CsgWeldCache.CandidatesMatch(cached, candidates[i], perBrushSurfaces);
                    validationMemo[memoKey] = candidatesValid;
                }

                if (candidatesValid)
                {
                    welded[i] = cached.Welded;
                    if (entries is not null)
                        entries[i] = cached;
                    reused++;
                    continue;
                }
            }

            BrushPlacement placement = placements[i];
            ChunkCoord owner = ChunkGrid.OwnerCell(in placement);
            if (!ownedByCell.TryGetValue(owner, out List<int>? needing))
            {
                ownedByCell[owner] = needing = [];
                cellOrder.Add(owner);
            }
            needing.Add(i);
        }

        // Snapped surfaces are computed lazily, only for welded brushes and
        // their candidates.
        var snapped = retainedSnaps ?? new Polygon[]?[n];
        var distinctSets = new HashSet<int[]>();
        var unionScratch = new HashSet<int>();
        var candidateCarvesMemo = entries is not null ? new Dictionary<int[], Polygon[][]>() : null;
        var candidateSurfaces = new List<Polygon>();
        var input = new List<Polygon>();
        foreach (ChunkCoord cell in cellOrder)
        {
            List<int> needing = ownedByCell[cell];

            // Candidate arrays are shared per footprint box, so union the
            // distinct arrays by reference.
            distinctSets.Clear();
            foreach (int i in needing)
                distinctSets.Add(candidates[i]);
            int[] cellCandidates = UnionDistinctSets(distinctSets, unionScratch);

            WeldCell(
                needing, cellCandidates, SnappedOf,
                i => perBrushSurfaces[i].Length,
                candidateSurfaces, input,
                (i, slice) =>
                {
                    welded[i] = slice;
                    if (entries is not null)
                        entries[i] = new CsgWeldCache.Entry(CandidateCarvesFor(candidates[i]), slice);
                });
        }

        stats = new CsgWeldStats(reused, n - reused);
        nextCache = entries is not null ? CsgWeldCache.FromEntries(perBrushSurfaces, entries) : null;
        return welded;

        // One list per distinct candidate set, shared by every entry using it.
        Polygon[][] CandidateCarvesFor(int[] candidateIndices)
        {
            if (!candidateCarvesMemo!.TryGetValue(candidateIndices, out Polygon[][]? carves))
            {
                carves = new Polygon[candidateIndices.Length][];
                for (int k = 0; k < carves.Length; k++)
                    carves[k] = perBrushSurfaces[candidateIndices[k]];
                candidateCarvesMemo[candidateIndices] = carves;
            }
            return carves;
        }

        // Snap is per vertex, so snapping one brush alone matches the global snap.
        Polygon[] SnappedOf(int index) => snapped[index] ??= VertexSnapper.Snap(perBrushSurfaces[index]);
    }

    // Ascending union. Shared with the incremental compile so both build the
    // same candidate grid.
    internal static int[] UnionDistinctSets(HashSet<int[]> distinctSets, HashSet<int> unionScratch)
    {
        if (distinctSets.Count == 1)
        {
            using HashSet<int[]>.Enumerator single = distinctSets.GetEnumerator();
            single.MoveNext();
            return single.Current;
        }

        unionScratch.Clear();
        foreach (int[] set in distinctSets)
        {
            foreach (int j in set)
                unionScratch.Add(j);
        }
        var union = new int[unionScratch.Count];
        unionScratch.CopyTo(union);
        Array.Sort(union);
        return union;
    }

    // Welds one owner cell. Shared with the incremental compile so both produce
    // identical output. Welding is per polygon, so surfaceCountOf (the carve
    // count) is also the welded slice width.
    internal static void WeldCell(
        List<int> needing,
        int[] cellCandidates,
        Func<int, Polygon[]> snappedOf,
        Func<int, int> surfaceCountOf,
        List<Polygon> candidateScratch,
        List<Polygon> inputScratch,
        Action<int, Polygon[]> emit)
    {
        candidateScratch.Clear();
        foreach (int j in cellCandidates)
            candidateScratch.AddRange(snappedOf(j));

        inputScratch.Clear();
        foreach (int i in needing)
            inputScratch.AddRange(snappedOf(i));

        Polygon[] output = TJunctionWelder.Weld(inputScratch, candidateScratch);

        // Slices are fresh arrays: they become results and cache entries.
        int offset = 0;
        foreach (int i in needing)
        {
            var slice = new Polygon[surfaceCountOf(i)];
            Array.Copy(output, offset, slice, 0, slice.Length);
            offset += slice.Length;
            emit(i, slice);
        }
    }

    // Sets are ascending so CsgWeldCache can validate by position. Placements
    // with the same footprint box share one array; otherwise a dense cell of
    // n brushes stores n copies of an n-element set.
    private static int[][] ComputeCandidateSets(IReadOnlyList<BrushPlacement> placements, ChunkGrid chunks)
    {
        var candidates = new int[placements.Count][];
        var byFootprint = new Dictionary<(ChunkCoord Min, ChunkCoord Max), int[]>();
        var scratch = new HashSet<int>();
        for (int i = 0; i < candidates.Length; i++)
        {
            BrushPlacement placement = placements[i];
            // Same box as ChunkGrid.ComputeFootprint.
            Aabb inflated = ChunkGrid.InflatedBounds(in placement);
            (ChunkCoord Min, ChunkCoord Max) footprint =
                (ChunkCoord.FromPosition(inflated.Min), ChunkCoord.FromPosition(inflated.Max));
            if (byFootprint.TryGetValue(footprint, out int[]? shared))
            {
                candidates[i] = shared;
                continue;
            }

            scratch.Clear();
            for (int x = footprint.Min.X; x <= footprint.Max.X; x++)
            {
                for (int y = footprint.Min.Y; y <= footprint.Max.Y; y++)
                {
                    for (int z = footprint.Min.Z; z <= footprint.Max.Z; z++)
                    {
                        if (chunks.TryGet(new ChunkCoord(x, y, z), out WorldChunk chunk))
                        {
                            foreach (int j in chunk.ResidentBrushIndices)
                                scratch.Add(j);
                        }
                    }
                }
            }

            var set = new int[scratch.Count];
            scratch.CopyTo(set);
            Array.Sort(set);
            byFootprint[footprint] = candidates[i] = set;
        }
        return candidates;
    }
}
