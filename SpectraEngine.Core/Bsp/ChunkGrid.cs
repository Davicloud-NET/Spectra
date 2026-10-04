using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// Sparse grid of <see cref="WorldChunk"/> cells for a compiled static world,
/// keyed by cell coordinate. No world extents. Immutable once built.
/// </summary>
public sealed class ChunkGrid
{
    /// <summary>
    /// How far a brush's world AABB is inflated before its cell coverage is computed.
    /// </summary>
    // Geometry within an epsilon of a cell boundary can snap or weld to vertices
    // on the far side, so such a brush must be resident in both cells. Twice the
    // larger tolerance covers a snap followed by a weld test. Narrowing this
    // breaks per-cell weld equivalence with a global weld.
    public const float WeldBand = 2f * (Polygon.Epsilon >= VertexSnapper.GridSize ? Polygon.Epsilon : VertexSnapper.GridSize);

    // Layered lookup so a patch never re-inserts every cell. _base is shared with
    // ancestor grids and never written after its build. _overlay holds the deltas
    // since then (null value = cell removed) and is compacted once it grows.
    private readonly Dictionary<ChunkCoord, WorldChunk> _base;
    private readonly Dictionary<ChunkCoord, WorldChunk?>? _overlay;
    private readonly PagedArray<WorldChunk> _orderedChunks;

    // Inclusive cell bounds, valid while Count > 0. Exact after a full build;
    // patches only grow it, since shrinking needs an O(cells) sweep. A superset
    // is fine: it is only used to prove a region empty.
    private readonly ChunkCoord _cellMin;
    private readonly ChunkCoord _cellMax;

    private ChunkGrid(
        Dictionary<ChunkCoord, WorldChunk> baseChunks,
        Dictionary<ChunkCoord, WorldChunk?>? overlay,
        PagedArray<WorldChunk> orderedChunks,
        ChunkCoord cellMin, ChunkCoord cellMax)
    {
        _base = baseChunks;
        _overlay = overlay;
        _orderedChunks = orderedChunks;
        _cellMin = cellMin;
        _cellMax = cellMax;
    }

    /// <summary>
    /// Inclusive cell bounds containing every occupied cell; false when the grid is empty.
    /// May be larger than the occupied set, so only use it to prove a region empty.
    /// </summary>
    public bool TryGetCellBounds(out ChunkCoord min, out ChunkCoord max)
    {
        min = _cellMin;
        max = _cellMax;
        return Count > 0;
    }

    /// <summary>Number of occupied cells.</summary>
    public int Count => _orderedChunks.Count;

    internal ChunkGrid RemapIndices(Func<int, int> map)
    {
        var chunks = new Dictionary<ChunkCoord, WorldChunk>(Count);
        var ordered = new WorldChunk[Count];
        for (int i = 0; i < ordered.Length; i++)
        {
            var chunk = _orderedChunks[i].RemapIndices(map);
            chunks.Add(chunk.Coord, chunk);
            ordered[i] = chunk;
        }
        return new(chunks, null, PagedArray<WorldChunk>.From(ordered), _cellMin, _cellMax);
    }

    // Adds the residents of every cell the bounds touch. Caller owns the set.
    internal void CollectResidents(in Aabb bounds, HashSet<int> results)
    {
        if (Count == 0) return;
        ChunkCoord from = ChunkCoord.FromPosition(bounds.Min);
        ChunkCoord to = ChunkCoord.FromPosition(bounds.Max);
        int minX = Math.Max(from.X, _cellMin.X), maxX = Math.Min(to.X, _cellMax.X);
        int minY = Math.Max(from.Y, _cellMin.Y), maxY = Math.Min(to.Y, _cellMax.Y);
        int minZ = Math.Max(from.Z, _cellMin.Z), maxZ = Math.Min(to.Z, _cellMax.Z);
        if (minX > maxX || minY > maxY || minZ > maxZ) return;
        double cells = ((double)maxX - minX + 1) * ((double)maxY - minY + 1) * ((double)maxZ - minZ + 1);
        if (cells > Count * 2.0)
        {
            foreach (WorldChunk chunk in _orderedChunks)
                if (chunk.Coord.X >= minX && chunk.Coord.X <= maxX &&
                    chunk.Coord.Y >= minY && chunk.Coord.Y <= maxY &&
                    chunk.Coord.Z >= minZ && chunk.Coord.Z <= maxZ)
                    foreach (int resident in chunk.ResidentBrushIndices) results.Add(resident);
            return;
        }
        for (long z = minZ; z <= maxZ; z++)
        for (long y = minY; y <= maxY; y++)
        for (long x = minX; x <= maxX; x++)
            if (TryGet(new ChunkCoord((int)x, (int)y, (int)z), out WorldChunk chunk))
                foreach (int resident in chunk.ResidentBrushIndices) results.Add(resident);
    }

    /// <summary>Looks up the chunk at <paramref name="coord"/>, if that cell is occupied.</summary>
    public bool TryGet(ChunkCoord coord, out WorldChunk chunk)
    {
        if (_overlay is not null && _overlay.TryGetValue(coord, out WorldChunk? patched))
        {
            chunk = patched!;
            return patched is not null;
        }
        if (_base.TryGetValue(coord, out WorldChunk? found))
        {
            chunk = found;
            return true;
        }
        chunk = null!;
        return false;
    }

    /// <summary>
    /// Every occupied chunk in ascending <see cref="ChunkCoord"/> order.
    /// Use this whenever order matters.
    /// </summary>
    public IReadOnlyList<WorldChunk> OrderedChunks => _orderedChunks;

    /// <summary>The brush's world AABB inflated by <see cref="WeldBand"/>.</summary>
    public static Aabb InflatedBounds(in BrushPlacement placement) =>
        placement.WorldBounds.Expanded(WeldBand);

    /// <summary>
    /// The one cell that holds the placement's render surfaces: the cell
    /// containing the centre of its inflated AABB.
    /// </summary>
    public static ChunkCoord OwnerCell(in BrushPlacement placement) =>
        ChunkCoord.FromPosition(InflatedBounds(placement).Center);

    /// <summary>
    /// Every cell the placement's inflated AABB touches, in ascending order.
    /// </summary>
    // Loop nesting matches ChunkCoord.CompareTo. Dirty-cell diffing compares
    // footprints element by element.
    public static ChunkCoord[] ComputeFootprint(in BrushPlacement placement)
    {
        Aabb inflated = InflatedBounds(placement);
        ChunkCoord min = ChunkCoord.FromPosition(inflated.Min);
        ChunkCoord max = ChunkCoord.FromPosition(inflated.Max);

        var cells = new ChunkCoord[(max.X - min.X + 1) * (max.Y - min.Y + 1) * (max.Z - min.Z + 1)];
        int i = 0;
        for (int x = min.X; x <= max.X; x++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                    cells[i++] = new ChunkCoord(x, y, z);
            }
        }
        return cells;
    }

    // Placements are visited in index order, so every chunk's index lists
    // come out ascending.
    internal static ChunkGrid Build(IReadOnlyList<BrushPlacement> placements, IReadOnlyList<Polygon[]> perBrushSurfaces)
    {
        var chunks = new Dictionary<ChunkCoord, WorldChunk>();

        for (int i = 0; i < placements.Count; i++)
        {
            BrushPlacement placement = placements[i];
            Aabb inflated = InflatedBounds(in placement);
            ChunkCoord owner = ChunkCoord.FromPosition(inflated.Center);
            ChunkCoord min = ChunkCoord.FromPosition(inflated.Min);
            ChunkCoord max = ChunkCoord.FromPosition(inflated.Max);

            for (int x = min.X; x <= max.X; x++)
            {
                for (int y = min.Y; y <= max.Y; y++)
                {
                    for (int z = min.Z; z <= max.Z; z++)
                    {
                        var coord = new ChunkCoord(x, y, z);
                        if (!chunks.TryGetValue(coord, out WorldChunk? chunk))
                            chunks[coord] = chunk = new WorldChunk(coord);

                        chunk.AddResident(i);
                        if (coord == owner)
                            chunk.AddOwned(i, perBrushSurfaces[i]);
                    }
                }
            }
        }

        var ordered = new WorldChunk[chunks.Count];
        chunks.Values.CopyTo(ordered, 0);
        // Coords are unique, so the unstable sort is still deterministic.
        Array.Sort(ordered, static (a, b) => a.Coord.CompareTo(b.Coord));
        (ChunkCoord cellMin, ChunkCoord cellMax) = ComputeCellBounds(ordered);
        return new ChunkGrid(chunks, overlay: null, PagedArray<WorldChunk>.From(ordered), cellMin, cellMax);
    }

    private static (ChunkCoord Min, ChunkCoord Max) ComputeCellBounds(IReadOnlyList<WorldChunk> chunks)
    {
        if (chunks.Count == 0)
            return (default, default);

        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
        foreach (WorldChunk chunk in chunks)
        {
            ChunkCoord c = chunk.Coord;
            if (c.X < minX) minX = c.X;
            if (c.Y < minY) minY = c.Y;
            if (c.Z < minZ) minZ = c.Z;
            if (c.X > maxX) maxX = c.X;
            if (c.Y > maxY) maxY = c.Y;
            if (c.Z > maxZ) maxZ = c.Z;
        }
        return (new ChunkCoord(minX, minY, minZ), new ChunkCoord(maxX, maxY, maxZ));
    }

    // Derives a grid with cells replaced, added or (null chunk) removed.
    // changes: ascending by coord, no duplicates, and every chunk fresh, since
    // the previous grid's chunks are still live. previous is left untouched.
    internal static ChunkGrid Patch(ChunkGrid previous, IReadOnlyList<(ChunkCoord Coord, WorldChunk? Chunk)> changes)
    {
        // Replace-only edits go through paged copy-on-write, O(changed pages).
        // Added or removed cells re-pack with block copies.
        PagedArray<WorldChunk> prevOrdered = previous._orderedChunks;
        int sizeDelta = 0;
        bool replaceOnly = true;
        var replacements = new List<(int Index, WorldChunk Value)>(changes.Count);
        foreach ((ChunkCoord coord, WorldChunk? chunk) in changes)
        {
            int pos = LowerBound(prevOrdered, coord, 0);
            bool existed = pos < prevOrdered.Count && prevOrdered[pos].Coord == coord;
            sizeDelta += (chunk is not null ? 1 : 0) - (existed ? 1 : 0);
            if (existed && chunk is not null)
                replacements.Add((pos, chunk));
            else
                replaceOnly = false;
        }

        PagedArray<WorldChunk> ordered;
        if (replaceOnly)
        {
            ordered = prevOrdered.WithReplacements(replacements);
        }
        else
        {
            var packed = new WorldChunk[prevOrdered.Count + sizeDelta];
            int src = 0, dst = 0;
            foreach ((ChunkCoord coord, WorldChunk? chunk) in changes)
            {
                int pos = LowerBound(prevOrdered, coord, src);
                prevOrdered.CopyTo(src, packed, dst, pos - src);
                dst += pos - src;
                src = pos < prevOrdered.Count && prevOrdered[pos].Coord == coord ? pos + 1 : pos;
                if (chunk is not null)
                    packed[dst++] = chunk;
            }
            prevOrdered.CopyTo(src, packed, dst, prevOrdered.Count - src);
            ordered = PagedArray<WorldChunk>.From(packed);
        }

        // Grow the box over added cells. Removals never shrink it.
        ChunkCoord cellMin = previous._cellMin;
        ChunkCoord cellMax = previous._cellMax;
        bool hasBounds = previous.Count > 0;
        foreach ((ChunkCoord coord, WorldChunk? chunk) in changes)
        {
            if (chunk is null)
                continue;
            if (!hasBounds)
            {
                cellMin = cellMax = coord;
                hasBounds = true;
                continue;
            }
            cellMin = new ChunkCoord(
                Math.Min(cellMin.X, coord.X), Math.Min(cellMin.Y, coord.Y), Math.Min(cellMin.Z, coord.Z));
            cellMax = new ChunkCoord(
                Math.Max(cellMax.X, coord.X), Math.Max(cellMax.Y, coord.Y), Math.Max(cellMax.Z, coord.Z));
        }

        // Clone the small parent overlay and apply the changes. Past ~base/8
        // entries it is compacted, the one amortized O(cells) step.
        Dictionary<ChunkCoord, WorldChunk?> overlay = previous._overlay is not null
            ? new Dictionary<ChunkCoord, WorldChunk?>(previous._overlay)
            : [];
        foreach ((ChunkCoord coord, WorldChunk? chunk) in changes)
            overlay[coord] = chunk;

        if (overlay.Count > Math.Max(64, previous._base.Count / 8))
        {
            var flat = new Dictionary<ChunkCoord, WorldChunk>(ordered.Count);
            foreach (WorldChunk chunk in ordered)
                flat.Add(chunk.Coord, chunk);
            // Already visiting every cell, so tighten the box too.
            (ChunkCoord exactMin, ChunkCoord exactMax) = ComputeCellBounds(ordered);
            return new ChunkGrid(flat, overlay: null, ordered, exactMin, exactMax);
        }

        return new ChunkGrid(previous._base, overlay, ordered, cellMin, cellMax);
    }

    // First index in [from, count) whose coordinate is >= coord.
    private static int LowerBound(PagedArray<WorldChunk> ordered, ChunkCoord coord, int from)
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

    // Placement order matches Build's, which keeps WeldedSurfaces and Surfaces
    // aligned per brush. Called once after the per-cell weld.
    internal void AttachWeldedSurfaces(IReadOnlyList<BrushPlacement> placements, Polygon[][] weldedPerBrush)
    {
        for (int i = 0; i < placements.Count; i++)
        {
            BrushPlacement placement = placements[i];
            if (TryGet(OwnerCell(in placement), out WorldChunk chunk))
                chunk.AddWeldedSurfaces(weldedPerBrush[i]);
        }
    }
}
