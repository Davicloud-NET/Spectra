using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace SpectraEngine.Core.Bsp;

// Derives a new CsgWorld from the previous one by re-running each stage over
// the edit's neighbourhood only. Everything else is carried by reference, so
// the cost does not depend on world size. The result must be bit-identical to
// a from-scratch compile, which is why it reuses the full path's per-brush and
// per-cell functions.
//
// Sets, each matching what the corresponding cache would miss:
//   C  changed: dirty-cell residents whose (brush, matrix) differs.
//   R  re-carve: C and its carve neighbours.
//   W  re-weld: residents of R's old and new footprints.
//   rebuilt cells: W's footprints plus the old footprints of C. A cell keeps
//   its mesh when its owned set and arrays are untouched.
//
// Safe off the render thread. The previous world is still live there and is
// never mutated.
internal static class CsgIncrementalCompiler
{
    // False means this edit cannot be patched exactly. Not an error; the
    // caller falls back to the validated path.
    internal static bool TryBuild(
        IReadOnlyList<BrushPlacement> placements,
        IReadOnlyList<ChunkCoord> dirtyCells,
        CsgWorld previous,
        [NotNullWhen(true)] out CsgWorld? world)
    {
        world = null;
        int n = placements.Count;
        IReadOnlyList<BrushPlacement> prevPlacements = previous.StoragePlacements;
        PlacementSnapshot? snapshot = (placements as PlacementSlotView)?.Snapshot;
        if (n == 0 || (snapshot is null && (previous.SourceSnapshot is not null || n != prevPlacements.Count)))
            return false;
        if (snapshot is not null && (previous.SourceSnapshot is not { } prior ||
            (snapshot.ParentId != prior.Id && snapshot.Id != prior.Id))) return false;

        CsgWorldCarry carry = previous.Carry.Expand(n);
        ChunkGrid prevGrid = previous.StorageChunks;
        IComparer<int> order = placements as PlacementSlotView ?? (IComparer<int>)Comparer<int>.Default;

        using var workspace = CompileWorkspace.Rent();
        var changed = workspace.List<int>();
        var candidateSeen = workspace.Set<int>();
        if (snapshot is not null)
        {
            if (snapshot.Id != previous.SourceSnapshot!.Id)
                foreach (var change in snapshot.Changes) changed.Add(change.Slot);
        }
        else foreach (ChunkCoord cell in dirtyCells)
        {
            if (!prevGrid.TryGet(cell, out WorldChunk cellChunk))
                continue;
            foreach (int i in cellChunk.ResidentBrushIndices)
            {
                if (candidateSeen.Add(i) && !SamePlacement(placements[i], prevPlacements[i]))
                    changed.Add(i);
            }
        }
        changed.Sort(order);

        if (snapshot is null) VerifyTrustedDiff(placements, prevPlacements, changed);

        if (changed.Count == 0)
        {
            // Nothing changed. The empty mesh delta tells the GPU swap so.
            world = CsgWorld.CreatePatched(
                placements, previous.SurfaceCount, prevGrid, previous.ChunkMeshes, dirtyCells, carry,
                previous, chunkMeshDelta: [],
                new CsgCacheStats(snapshot?.Count ?? n, 0), new CsgWeldStats(snapshot?.Count ?? n, 0),
                new CsgBspStats(prevGrid.Count, 0), new CsgMeshStats(previous.ChunkMeshes.Count, 0));
            return true;
        }

        var oldBounds = workspace.Map<int, Aabb>();
        var newBounds = workspace.Map<int, Aabb>();
        var oldFootprints = workspace.Map<int, ChunkCoord[]>();
        var newFootprints = workspace.Map<int, ChunkCoord[]>();
        foreach (int c in changed)
        {
            BrushPlacement prevPlacement = c < prevPlacements.Count ? prevPlacements[c] : default;
            BrushPlacement placement = placements[c];
            if (prevPlacement.Brush is not null) oldBounds[c] = prevPlacement.WorldBounds;
            if (placement.Brush is not null) newBounds[c] = placement.WorldBounds;
            oldFootprints[c] = prevPlacement.Brush is null ? [] : ChunkGrid.ComputeFootprint(in prevPlacement);
            newFootprints[c] = placement.Brush is null ? [] : ChunkGrid.ComputeFootprint(in placement);
        }

        // Overlap candidates: previous residents of every cell the old or new
        // bounds cover, plus the changed brushes, whose previous residency
        // does not cover where they moved to.
        var overlapCandidates = workspace.Set<int>(changed);
        foreach (int c in changed)
        {
            if (oldBounds.TryGetValue(c, out var old)) AddResidentsOfBoundsCells(prevGrid, old, overlapCandidates);
            if (newBounds.TryGetValue(c, out var current)) AddResidentsOfBoundsCells(prevGrid, current, overlapCandidates);
        }

        var recarve = workspace.Set<int>(changed);
        var removedPartners = workspace.Map<int, HashSet<int>>();
        var addedPartners = workspace.Map<int, HashSet<int>>();
        var newNeighborSet = workspace.Set<int>();
        foreach (int c in changed)
        {
            newNeighborSet.Clear();
            if (newBounds.TryGetValue(c, out Aabb boundsC)) foreach (int j in overlapCandidates)
            {
                if (j == c || placements[j].Brush is null)
                    continue;
                Aabb boundsJ = newBounds.TryGetValue(j, out Aabb movedBounds)
                    ? movedBounds
                    : placements[j].WorldBounds;
                if (boundsC.Intersects(boundsJ))
                    newNeighborSet.Add(j);
            }

            int[] prevNeighbors = carry.CarveNeighbors[c];
            foreach (int j in prevNeighbors)
                recarve.Add(j);

            foreach (int j in prevNeighbors)
            {
                if (!newNeighborSet.Contains(j))
                {
                    GetOrAdd(removedPartners, c).Add(j);
                    GetOrAdd(removedPartners, j).Add(c);
                }
            }
            foreach (int j in newNeighborSet)
            {
                recarve.Add(j);
                if (Array.IndexOf(prevNeighbors, j) < 0)
                {
                    GetOrAdd(addedPartners, c).Add(j);
                    GetOrAdd(addedPartners, j).Add(c);
                }
            }
        }

        // Clipping is in authored order, so new overlaps can be patched
        // locally without the broadphase sweep.
        var neighborChanges = workspace.Set<int>(removedPartners.Keys);
        neighborChanges.UnionWith(addedPartners.Keys);
        // A reorder changes clip order even with the same overlap set, so
        // every recarved brush gets its neighbours re-sorted.
        neighborChanges.UnionWith(recarve);
        var neighborReplacements = workspace.List<(int, int[])>();
        var partners = workspace.Set<int>();
        foreach (int i in neighborChanges)
        {
            partners.Clear();
            partners.UnionWith(carry.CarveNeighbors[i]);
            if (removedPartners.TryGetValue(i, out var removed)) partners.ExceptWith(removed);
            if (addedPartners.TryGetValue(i, out var added)) partners.UnionWith(added);
            var ordered = new int[partners.Count];
            partners.CopyTo(ordered);
            Array.Sort(ordered, order);
            neighborReplacements.Add((i, ordered));
        }
        PagedArray<int[]> neighbors = carry.CarveNeighbors.WithReplacements(neighborReplacements);
        var recarveList = workspace.List<int>(recarve);
        recarveList.Sort(order);
        var carveReplacements = new (int Index, Polygon[] Value)[recarveList.Count];
        PagedArray<int[]> neighborsFinal = neighbors;
        if (recarveList.Count <= 4)
        {
            using var scratch = Csg.CarveScratch.Rent();
            for (int k = 0; k < recarveList.Count; k++)
            {
                int b = recarveList[k];
                carveReplacements[k] = (b, Csg.CarveSingle(placements, b, neighborsFinal[b], scratch));
            }
        }
        else
        {
            Parallel.For(0, recarveList.Count,
                static () => Csg.CarveScratch.Rent(),
                (k, _, scratch) =>
                {
                    int b = recarveList[k];
                    carveReplacements[k] = (b, Csg.CarveSingle(placements, b, neighborsFinal[b], scratch));
                    return scratch;
                },
                static scratch => scratch.Dispose());
        }
        PagedArray<Polygon[]> carved = carry.CarvedPerBrush.WithReplacements(carveReplacements);

        // Only changed brushes move between cells.
        var cellDeltas = workspace.Map<ChunkCoord, CellDelta>();
        foreach (int c in changed)
        {
            ChunkCoord[] oldFootprint = oldFootprints[c];
            ChunkCoord[] newFootprint = newFootprints[c];
            foreach (ChunkCoord cell in oldFootprint)
            {
                if (Array.IndexOf(newFootprint, cell) < 0)
                    GetOrAdd(cellDeltas, cell).Removed.Add(c);
            }
            foreach (ChunkCoord cell in newFootprint)
            {
                if (Array.IndexOf(oldFootprint, cell) < 0)
                    GetOrAdd(cellDeltas, cell).Added.Add(c);
            }
        }

        // W: current residents of R's footprints, old and new for moved brushes.
        var weldCells = workspace.Set<ChunkCoord>();
        var footprintOf = workspace.Map<int, ChunkCoord[]>();
        foreach (int r in recarveList)
        {
            ChunkCoord[] footprint;
            if (newFootprints.TryGetValue(r, out ChunkCoord[]? moved))
            {
                footprint = moved;
                foreach (ChunkCoord cell in oldFootprints[r])
                    weldCells.Add(cell);
            }
            else
            {
                BrushPlacement placement = placements[r];
                footprint = ChunkGrid.ComputeFootprint(in placement);
            }
            footprintOf[r] = footprint;
            foreach (ChunkCoord cell in footprint)
                weldCells.Add(cell);
        }

        var reweld = workspace.Set<int>();
        var residentsScratch = workspace.List<int>();
        foreach (ChunkCoord cell in weldCells)
        {
            ResidentsAfter(prevGrid, cellDeltas, cell, residentsScratch, order);
            foreach (int i in residentsScratch)
                reweld.Add(i);
        }
        var weldList = workspace.List<int>(reweld);
        weldList.Sort(order);

        // Rebuilt cells: W's footprints plus the old footprints of C.
        var affectedCells = workspace.Set<ChunkCoord>(weldCells);
        foreach (int w in weldList)
        {
            if (footprintOf.TryGetValue(w, out ChunkCoord[]? known))
            {
                foreach (ChunkCoord cell in known)
                    affectedCells.Add(cell);
            }
            else
            {
                BrushPlacement placement = placements[w];
                foreach (ChunkCoord cell in ChunkGrid.ComputeFootprint(in placement))
                    affectedCells.Add(cell);
            }
        }
        var affectedList = workspace.List<ChunkCoord>(affectedCells);
        affectedList.Sort();

        // Fresh WorldChunk per affected cell. The previous ones are still live
        // in the previous world.
        var gridChanges = workspace.List<(ChunkCoord Coord, WorldChunk? Chunk)>();
        var ownerCellOf = workspace.Map<int, ChunkCoord>();
        foreach (ChunkCoord cell in affectedList)
        {
            ResidentsAfter(prevGrid, cellDeltas, cell, residentsScratch, order);
            bool existed = prevGrid.TryGet(cell, out _);
            if (residentsScratch.Count == 0)
            {
                if (existed)
                    gridChanges.Add((cell, null));
                continue;
            }

            var chunk = new WorldChunk(cell);
            foreach (int i in residentsScratch)
            {
                chunk.AddResident(i);
                if (!ownerCellOf.TryGetValue(i, out ChunkCoord owner))
                {
                    BrushPlacement placement = placements[i];
                    ownerCellOf[i] = owner = ChunkGrid.OwnerCell(in placement);
                }
                if (owner == cell)
                    chunk.AddOwned(i, carved[i]);
            }
            gridChanges.Add((cell, chunk));
        }

        ChunkGrid grid = ChunkGrid.Patch(prevGrid, gridChanges);

        // Candidate sets are recomputed over the patched grid, same math as
        // ChunkWelder.ComputeCandidateSets but only for W.
        var candidateReplacements = workspace.List<(int, int[])>();
        foreach (int c in changed)
            if (placements[c].Brush is null) candidateReplacements.Add((c, []));
        var candidateOf = workspace.Map<int, int[]>();
        var byFootprintBox = workspace.Map<(ChunkCoord Min, ChunkCoord Max), int[]>();
        var candidateScratchSet = workspace.Set<int>();
        foreach (int w in weldList)
        {
            BrushPlacement placement = placements[w];
            Aabb inflated = ChunkGrid.InflatedBounds(in placement);
            (ChunkCoord Min, ChunkCoord Max) box =
                (ChunkCoord.FromPosition(inflated.Min), ChunkCoord.FromPosition(inflated.Max));
            if (!byFootprintBox.TryGetValue(box, out int[]? set))
            {
                candidateScratchSet.Clear();
                for (int x = box.Min.X; x <= box.Max.X; x++)
                {
                    for (int y = box.Min.Y; y <= box.Max.Y; y++)
                    {
                        for (int z = box.Min.Z; z <= box.Max.Z; z++)
                        {
                            if (grid.TryGet(new ChunkCoord(x, y, z), out WorldChunk cellChunk))
                            {
                                foreach (int j in cellChunk.ResidentBrushIndices)
                                    candidateScratchSet.Add(j);
                            }
                        }
                    }
                }
                set = new int[candidateScratchSet.Count];
                candidateScratchSet.CopyTo(set);
                Array.Sort(set, order);
                byFootprintBox[box] = set;
            }
            candidateOf[w] = set;
            candidateReplacements.Add((w, set));
        }

        var ownedByCell = workspace.Map<ChunkCoord, List<int>>();
        var weldCellOrder = workspace.List<ChunkCoord>();
        foreach (int w in weldList)
        {
            if (!ownerCellOf.TryGetValue(w, out ChunkCoord owner))
            {
                BrushPlacement placement = placements[w];
                ownerCellOf[w] = owner = ChunkGrid.OwnerCell(in placement);
            }
            if (!ownedByCell.TryGetValue(owner, out List<int>? needing))
            {
                ownedByCell[owner] = needing = [];
                weldCellOrder.Add(owner);
            }
            needing.Add(w);
        }

        var snappedMemo = workspace.Map<int, Polygon[]>();
        var snapReplacements = workspace.List<(int Index, Polygon[]? Value)>();
        foreach (int index in recarveList) snapReplacements.Add((index, null));
        var distinctSets = workspace.Set<int[]>();
        var unionScratch = workspace.Set<int>();
        var candidateSurfaceScratch = workspace.List<Polygon>();
        var inputScratch = workspace.List<Polygon>();
        var weldReplacements = workspace.List<(int Index, Polygon[] Value)>();
        foreach (int c in changed)
            if (placements[c].Brush is null) weldReplacements.Add((c, []));
        foreach (ChunkCoord cell in weldCellOrder)
        {
            List<int> needing = ownedByCell[cell];
            distinctSets.Clear();
            foreach (int i in needing)
                distinctSets.Add(candidateOf[i]);
            int[] cellCandidates = ChunkWelder.UnionDistinctSets(distinctSets, unionScratch);
            Array.Sort(cellCandidates, order);

            ChunkWelder.WeldCell(
                needing, cellCandidates, SnappedOf,
                i => carved[i].Length,
                candidateSurfaceScratch, inputScratch,
                (i, slice) => weldReplacements.Add((i, slice)));
        }

        PagedArray<Polygon[]> welded = carry.WeldedPerBrush.WithReplacements(weldReplacements);
        PagedArray<int[]> candidates = carry.WeldCandidates.WithReplacements(candidateReplacements);

        int surfaceCount = previous.SurfaceCount;
        foreach ((int i, Polygon[] slice) in weldReplacements)
            surfaceCount += slice.Length - carry.WeldedPerBrush[i].Length;

        var freshChunks = workspace.List<WorldChunk>();
        foreach ((_, WorldChunk? chunk) in gridChanges)
        {
            if (chunk is null)
                continue;
            foreach (int o in chunk.OwnedBrushIndices)
                chunk.AddWeldedSurfaces(welded[o]);
            freshChunks.Add(chunk);
        }

        // A few small trees are cheaper than thread-pool dispatch.
        if (freshChunks.Count <= 4)
        {
            foreach (WorldChunk chunk in freshChunks)
                AttachTree(chunk);
        }
        else
        {
            Parallel.For(0, freshChunks.Count, k => AttachTree(freshChunks[k]));
        }

        // Rebuild a cell's mesh only when its owned welded input changed.
        // Keeping the instance tells the GPU swap the cell is unchanged.
        PagedArray<ChunkMesh> prevMeshes = previous.ChunkMeshesPaged;
        var meshChanges = new List<(ChunkCoord Coord, ChunkMesh? Mesh)>();
        var toBuild = workspace.List<WorldChunk>();
        var placeholderSlots = workspace.List<int>(); // meshChanges indices, aligned with toBuild
        foreach ((ChunkCoord coord, WorldChunk? chunk) in gridChanges)
        {
            ChunkMesh? prevMesh = FindMesh(prevMeshes, coord);
            if (chunk is null || chunk.WeldedSurfaces.Count == 0)
            {
                if (prevMesh is not null)
                    meshChanges.Add((coord, null));
                continue;
            }

            if (prevMesh is not null && prevGrid.TryGet(coord, out WorldChunk prevChunk) &&
                OwnedInputUnchanged(prevChunk, chunk, carry, welded))
            {
                continue; // mesh carried forward, not a change
            }

            placeholderSlots.Add(meshChanges.Count);
            meshChanges.Add((coord, null));
            toBuild.Add(chunk);
        }

        if (toBuild.Count > 0)
        {
            var built = new ChunkMesh[toBuild.Count];
            if (toBuild.Count <= 4)
            {
                for (int k = 0; k < toBuild.Count; k++)
                    built[k] = ChunkMeshBuilder.BuildArtifact(toBuild[k]);
            }
            else
            {
                Parallel.For(0, toBuild.Count, k => built[k] = ChunkMeshBuilder.BuildArtifact(toBuild[k]));
            }
            for (int k = 0; k < placeholderSlots.Count; k++)
                meshChanges[placeholderSlots[k]] = (meshChanges[placeholderSlots[k]].Coord, built[k]);
        }

        PagedArray<ChunkMesh> chunkMeshes = SpliceMeshes(prevMeshes, meshChanges);

        var carryNext = new CsgWorldCarry(carved, welded, neighbors, candidates,
            carry.SnappedPerBrush.WithReplacements(snapReplacements));
        int active = snapshot?.Count ?? n;
        int carvedActive = 0;
        foreach (int i in recarveList) if (placements[i].Brush is not null) carvedActive++;
        world = CsgWorld.CreatePatched(
            placements, surfaceCount, grid, chunkMeshes, dirtyCells, carryNext,
            previous, meshChanges,
            new CsgCacheStats(active - carvedActive, carvedActive),
            new CsgWeldStats(active - weldList.Count, weldList.Count),
            new CsgBspStats(grid.Count - freshChunks.Count, freshChunks.Count),
            new CsgMeshStats(chunkMeshes.Count - toBuild.Count, toBuild.Count));
        return true;

        Polygon[] SnappedOf(int index)
        {
            if (ReferenceEquals(carved[index], carry.CarvedPerBrush[index]) &&
                carry.SnappedPerBrush[index] is { } retained) return retained;
            if (!snappedMemo.TryGetValue(index, out Polygon[]? snapped))
            {
                snappedMemo[index] = snapped = VertexSnapper.Snap(carved[index]);
                snapReplacements.Add((index, snapped));
            }
            return snapped;
        }

        void AttachTree(WorldChunk chunk) =>
            chunk.AttachBsp(ChunkBspBuilder.BuildCellTree(chunk, i => welded[i], out _));
    }

    // Same comparison as CsgCompileCache's validation.
    private static bool SamePlacement(in BrushPlacement a, in BrushPlacement b) =>
        ReferenceEquals(a.Brush, b.Brush) && a.Transform == b.Transform;

    // Every placement outside the changed set must equal the previous one at
    // the same index. Debug only: this is the O(world) sweep the trusted diff
    // exists to avoid.
    [Conditional("DEBUG")]
    private static void VerifyTrustedDiff(
        IReadOnlyList<BrushPlacement> placements, IReadOnlyList<BrushPlacement> prevPlacements, List<int> changed)
    {
        var changedSet = new HashSet<int>(changed);
        for (int i = 0; i < placements.Count; i++)
        {
            if (!changedSet.Contains(i) && !SamePlacement(placements[i], prevPlacements[i]))
            {
                throw new InvalidOperationException(
                    $"Incremental compile contract violated: placement {i} changed but is not covered by the " +
                    "dirty-cell set (or the placement list was reordered). The caller must dirty every changed " +
                    "placement's old and new footprint, or compile through the validated path.");
            }
        }
    }

    // Any brush intersecting the bounds is resident in one of the cells they
    // cover, so this finds every overlap candidate.
    private static void AddResidentsOfBoundsCells(ChunkGrid grid, in Aabb bounds, HashSet<int> into)
    {
        ChunkCoord min = ChunkCoord.FromPosition(bounds.Min);
        ChunkCoord max = ChunkCoord.FromPosition(bounds.Max);
        for (int x = min.X; x <= max.X; x++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                {
                    if (grid.TryGet(new ChunkCoord(x, y, z), out WorldChunk chunk))
                    {
                        foreach (int i in chunk.ResidentBrushIndices)
                            into.Add(i);
                    }
                }
            }
        }
    }

    // Fills into with the cell's residents after the edit, ascending.
    private static void ResidentsAfter(
        ChunkGrid prevGrid, Dictionary<ChunkCoord, CellDelta> deltas, ChunkCoord cell, List<int> into, IComparer<int> order)
    {
        into.Clear();
        bool hasDelta = deltas.TryGetValue(cell, out CellDelta? delta);
        if (prevGrid.TryGet(cell, out WorldChunk chunk))
        {
            foreach (int i in chunk.ResidentBrushIndices)
            {
                if (!hasDelta || !delta!.Removed.Contains(i))
                    into.Add(i);
            }
        }
        if (hasDelta && delta!.Added.Count > 0)
        {
            into.AddRange(delta.Added);
        }
        into.Sort(order);
    }

    private static TValue GetOrAdd<TKey, TValue>(Dictionary<TKey, TValue> map, TKey key)
        where TKey : notnull
        where TValue : new()
    {
        if (!map.TryGetValue(key, out TValue? value))
            map[key] = value = new TValue();
        return value;
    }

    private static ChunkMesh? FindMesh(IReadOnlyList<ChunkMesh> meshes, ChunkCoord coord)
    {
        int lo = 0, hi = meshes.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int cmp = meshes[mid].Coord.CompareTo(coord);
            if (cmp == 0)
                return meshes[mid];
            if (cmp < 0)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return null;
    }

    // Same rule as CsgMeshCache.OwnersMatch, checked against the carry.
    private static bool OwnedInputUnchanged(
        WorldChunk prevChunk, WorldChunk newChunk, CsgWorldCarry carry, PagedArray<Polygon[]> welded)
    {
        IReadOnlyList<int> prevOwned = prevChunk.OwnedBrushIndices;
        IReadOnlyList<int> newOwned = newChunk.OwnedBrushIndices;
        if (prevOwned.Count != newOwned.Count)
            return false;
        for (int k = 0; k < newOwned.Count; k++)
        {
            int o = newOwned[k];
            if (prevOwned[k] != o || !ReferenceEquals(welded[o], carry.WeldedPerBrush[o]))
                return false;
        }
        return true;
    }

    // Both lists ascending. A null mesh removes; otherwise replace or insert.
    // Replace-only edits go through paged copy-on-write.
    private static PagedArray<ChunkMesh> SpliceMeshes(
        PagedArray<ChunkMesh> previous, List<(ChunkCoord Coord, ChunkMesh? Mesh)> changes)
    {
        if (changes.Count == 0)
            return previous;

        int sizeDelta = 0;
        bool replaceOnly = true;
        var replacements = new List<(int Index, ChunkMesh Value)>(changes.Count);
        foreach ((ChunkCoord coord, ChunkMesh? mesh) in changes)
        {
            int pos = LowerBound(previous, coord, 0);
            bool existed = pos < previous.Count && previous[pos].Coord == coord;
            sizeDelta += (mesh is not null ? 1 : 0) - (existed ? 1 : 0);
            if (existed && mesh is not null)
                replacements.Add((pos, mesh));
            else
                replaceOnly = false;
        }

        if (replaceOnly)
            return previous.WithReplacements(replacements);

        var merged = new ChunkMesh[previous.Count + sizeDelta];
        int src = 0, dst = 0;
        foreach ((ChunkCoord coord, ChunkMesh? mesh) in changes)
        {
            int pos = LowerBound(previous, coord, src);
            previous.CopyTo(src, merged, dst, pos - src);
            dst += pos - src;
            src = pos < previous.Count && previous[pos].Coord == coord ? pos + 1 : pos;
            if (mesh is not null)
                merged[dst++] = mesh;
        }
        previous.CopyTo(src, merged, dst, previous.Count - src);
        return PagedArray<ChunkMesh>.From(merged);
    }

    // First index in [from, count) whose coordinate is >= coord.
    private static int LowerBound(PagedArray<ChunkMesh> ordered, ChunkCoord coord, int from)
    {
        int lo = from, hi = ordered.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (ordered[mid].Coord.CompareTo(coord) < 0)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    private sealed class CellDelta
    {
        public readonly List<int> Removed = [];
        public readonly List<int> Added = [];
    }
}
