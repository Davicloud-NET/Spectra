using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// The incremental static-world compile: derives a new <see cref="CsgWorld"/>
/// from the previous one by re-running each pipeline stage over exactly the
/// edit's neighbourhood — changed brushes, their carve neighbours, the brushes
/// welded against those, and the cells any of them touch — while every other
/// artifact (carved and welded arrays, chunk instances with their BSP trees,
/// chunk mesh artifacts) is carried forward by reference. No step reads,
/// copies, or validates per-brush or per-cell state outside that
/// neighbourhood (the carry is paged copy-on-write, the chunk map layered),
/// which is what makes a one-part edit cost the same in a 50k-part world as
/// in a 1k-part world — the open-world pillar's requirement.
/// </summary>
/// <remarks>
/// <b>Scoping rules (mirroring the validation-driven full path exactly):</b>
/// <list type="bullet">
/// <item><description><b>Changed set C:</b> placements at dirty-cell residents
/// whose (brush, matrix) differs from the previous compile's — complete by the
/// trusted-diff contract (every changed placement's old footprint is dirty,
/// and its old residency makes it a dirty-cell resident of the previous
/// grid).</description></item>
/// <item><description><b>Re-carve set R = C ∪ neighbours(C):</b> a brush's
/// carve is a function of its placement and its ordered carver sequence, so
/// exactly the changed brushes and the brushes whose carver sequence contains
/// one re-carve — the same set the carve cache's validation would
/// miss.</description></item>
/// <item><description><b>Re-weld set W = residents of R's (old and new)
/// footprints:</b> b re-welds iff some candidate's carve array changed, and
/// r ∈ candidates(b) ⇔ footprint(r) ∩ footprint(b) ≠ ∅ ⇔ b is resident in a
/// footprint cell of r — the same set the weld cache's validation would
/// miss.</description></item>
/// <item><description><b>Rebuilt cells = footprint cells of W plus the old
/// footprints of C:</b> precisely the cells with a resident whose welded
/// array changed or whose membership changed — the BSP cache's miss set. Mesh
/// artifacts additionally survive a cell rebuild when the cell's OWNED set
/// and arrays are untouched — the mesh cache's rule.</description></item>
/// </list>
/// <para>
/// <b>Exactness.</b> Re-run stages go through the same code paths as the full
/// compile (<see cref="Csg.CarveSingle"/>, <see cref="ChunkWelder.WeldCell"/>,
/// <see cref="ChunkBspBuilder.BuildCellTree"/>,
/// <see cref="ChunkMeshBuilder.BuildArtifact"/>), so for the editing gestures
/// this path accepts the result is bit-identical to a from-scratch compile of
/// the same placements. Geometry v2 clips in authored placement order, so
/// new overlaps and sweep-rank crossings can be patched locally.
/// </para>
/// <para>
/// <b>Threading:</b> pure CPU work over the snapshot and the immutable
/// previous world — background-thread safe, and the previous world (still
/// live on the render thread) is never mutated: chunks and carry pages are
/// copy-on-write, the chunk map is layered.
/// </para>
/// </remarks>
internal static class CsgIncrementalCompiler
{
    /// <summary>
    /// Attempts the incremental compile. False means "cannot patch this edit
    /// exactly" — never an error; the caller falls back to the validated path.
    /// </summary>
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
        // --- Changed set C: dirty-cell residents whose placement differs. ---
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
            // No-op recompile: every artifact carries forward verbatim (the
            // empty mesh delta tells the GPU swap path so explicitly).
            world = CsgWorld.CreatePatched(
                placements, previous.SurfaceCount, prevGrid, previous.ChunkMeshes, dirtyCells, carry,
                previous, chunkMeshDelta: [],
                new CsgCacheStats(snapshot?.Count ?? n, 0), new CsgWeldStats(snapshot?.Count ?? n, 0),
                new CsgBspStats(prevGrid.Count, 0), new CsgMeshStats(previous.ChunkMeshes.Count, 0));
            return true;
        }

        // --- Bounds and residency footprints of the changed placements. ---
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

        // --- Overlap-pair delta. Candidates for "overlaps a changed brush":
        // previous-grid residents of every cell the old or new bounds cover,
        // plus the changed brushes themselves (their previous residency does
        // not cover where they moved to).
        var overlapCandidates = workspace.Set<int>(changed);
        foreach (int c in changed)
        {
            if (oldBounds.TryGetValue(c, out var old)) AddResidentsOfBoundsCells(prevGrid, old, overlapCandidates);
            if (newBounds.TryGetValue(c, out var current)) AddResidentsOfBoundsCells(prevGrid, current, overlapCandidates);
        }

        var recarve = workspace.Set<int>(changed);
        // Symmetric relationship patches for every changed overlap pair.
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

        // Geometry v2 clips in authored order. New overlaps and min-X rank
        // crossings therefore have no dependency on the global sweep history.
        var neighborChanges = workspace.Set<int>(removedPartners.Keys);
        neighborChanges.UnionWith(addedPartners.Keys);
        // A reordering changes clip order even when overlap membership stays
        // identical. Sort every recarved brush's dependencies in the new order.
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
        // --- Re-carve R through the same per-brush core as the full path. ---
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

        // --- Residency deltas per cell (only changed brushes move cells). ---
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

        // --- Re-weld set W: current residents of R's footprints (old and new
        // for moved brushes) — exactly the weld cache's miss set.
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

        // --- Rebuilt cells: W's footprints plus the old footprints of C. ---
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

        // --- Fresh WorldChunk per affected cell (the previous instances stay
        // live in the previous world and are never touched).
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

        // --- Weld W per owner cell through the same core as the full path,
        // with candidate sets recomputed over the patched grid (same math as
        // ChunkWelder.ComputeCandidateSets, scoped to W).
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

        // --- Attach welded surfaces and rebuild each fresh cell's tree. ---
        var freshChunks = workspace.List<WorldChunk>();
        foreach ((_, WorldChunk? chunk) in gridChanges)
        {
            if (chunk is null)
                continue;
            foreach (int o in chunk.OwnedBrushIndices)
                chunk.AddWeldedSurfaces(welded[o]);
            freshChunks.Add(chunk);
        }

        // Sequential below a handful of cells: thread-pool dispatch costs more
        // (and jitters more) than a few small trees; big edits still fan out.
        if (freshChunks.Count <= 4)
        {
            foreach (WorldChunk chunk in freshChunks)
                AttachTree(chunk);
        }
        else
        {
            Parallel.For(0, freshChunks.Count, k => AttachTree(freshChunks[k]));
        }

        // --- Per-cell meshes: rebuild a fresh cell's artifact only when its
        // OWNED welded input actually changed (the mesh cache's rule); a cell
        // rebuilt for resident/BSP reasons alone keeps its artifact instance,
        // which is also the GPU swap path's "cell unchanged" signal.
        PagedArray<ChunkMesh> prevMeshes = previous.ChunkMeshesPaged;
        var meshChanges = new List<(ChunkCoord Coord, ChunkMesh? Mesh)>();
        var toBuild = workspace.List<WorldChunk>();
        var placeholderSlots = workspace.List<int>(); // meshChanges indices awaiting a built artifact, aligned with toBuild
        foreach ((ChunkCoord coord, WorldChunk? chunk) in gridChanges)
        {
            ChunkMesh? prevMesh = FindMesh(prevMeshes, coord);
            if (chunk is null || chunk.WeldedSurfaces.Count == 0)
            {
                if (prevMesh is not null)
                    meshChanges.Add((coord, null)); // cell lost its render geometry
                continue;
            }

            if (prevMesh is not null && prevGrid.TryGet(coord, out WorldChunk prevChunk) &&
                OwnedInputUnchanged(prevChunk, chunk, carry, welded))
            {
                continue; // artifact carried forward — not a change
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

    // Element-wise placement equality: brush reference plus matrix float ==,
    // the same comparison the carve cache's validation uses (see
    // CsgCompileCache for why float == is right here).
    private static bool SamePlacement(in BrushPlacement a, in BrushPlacement b) =>
        ReferenceEquals(a.Brush, b.Brush) && a.Transform == b.Transform;

    // DEBUG-only verification of the trusted-diff contract's order half: every
    // placement outside the changed set must be bitwise identical to the
    // previous compile's at the same index. Compiled out in Release — the
    // whole point of the trusted diff is not paying an O(world) sweep per
    // edit — so dev builds (tests, the demo) catch contract violations loudly
    // while release builds trust their callers.
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

    // Adds every previous-grid resident of every cell the (uninflated) bounds
    // cover. Any brush intersecting the bounds is resident in one of these
    // cells (residency covers a brush's own AABB cells), so the union is a
    // complete overlap-candidate set.
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

    // The cell's resident indices AFTER the edit: the previous residents minus
    // changed brushes that left, plus changed brushes that entered — ascending,
    // like every resident list. Fills `into` (cleared first).
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

    // Binary search over the ascending previous mesh list.
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

    // The mesh cache's owner-identity rule, expressed over the carry: same
    // owned index list and, per owned brush, the same welded array instance
    // (o ∉ W ⇔ the paged slot still holds the carried reference).
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

    // Sorted splice of the ascending previous mesh list with the (ascending)
    // per-cell mesh changes: null removes, non-null replaces or inserts. The
    // steady-state edit (artifacts replaced, no cell gaining or losing its
    // mesh) derives by paged copy-on-write — O(changed pages); cell
    // insertions/removals re-pack via binary-searched block copies
    // (memcpy-cheap, and rare next to in-place edits).
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

    // Per-cell residency delta of the changed brushes: who left, who entered.
    private sealed class CellDelta
    {
        public readonly List<int> Removed = [];
        public readonly List<int> Added = [];
    }
}
