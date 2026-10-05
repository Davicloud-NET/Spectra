using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// One cell of an adopted compiled world: where it is, the box culling tests it
/// against, and the flat solid-leaf tree queries walk.
/// </summary>
/// <param name="RenderBounds">The cell's render bounds from <c>CHDR</c>. Can overhang the cell.</param>
/// <param name="Bsp">
/// The cell's tree, read over the map's own bytes, or null. A cell can have
/// geometry and no tree.
/// </param>
public readonly record struct CompiledStaticWorldChunk(
    ChunkCoord Coord,
    Aabb RenderBounds,
    FlatBspTree? Bsp,
    int TriangleCount);

/// <summary>
/// A static world that arrived baked: per-cell geometry already on the GPU,
/// per-cell BSP trees read off the compiled map, and the world brushes as
/// collision hulls. While a scene holds one it refuses to carve, since the
/// chunks already contain every world brush.
/// </summary>
// Owns the map's ContentBlob. The BSP nodes are a window into it, possibly a
// memory-mapped view, and unmapping under a live span is an access violation.
public sealed class CompiledStaticWorld : IDisposable
{
    private readonly CompiledStaticWorldChunk[] _chunks;
    private readonly BrushPlacement[] _collision;
    private readonly ChunkCoord _cellMin;
    private readonly ChunkCoord _cellMax;
    private ContentBlob? _file;

    /// <param name="chunks">
    /// The cells, in ascending <see cref="ChunkCoord.CompareTo"/> order, as <c>CHDR</c> stores them.
    /// </param>
    /// <param name="file">The map's bytes. This object takes ownership.</param>
    /// <param name="collision">The world brushes as collision hulls, in node order.</param>
    public CompiledStaticWorld(
        string source, CompiledStaticWorldChunk[] chunks, ContentBlob? file, BrushPlacement[]? collision = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(chunks);

        Source = source;
        _chunks = chunks;
        _file = file;
        _collision = collision ?? [];

        int triangles = 0;
        for (int i = 0; i < chunks.Length; i++)
        {
            triangles += chunks[i].TriangleCount;

            ChunkCoord cell = chunks[i].Coord;
            _cellMin = i == 0 ? cell : Min(_cellMin, cell);
            _cellMax = i == 0 ? cell : Max(_cellMax, cell);
        }

        TriangleCount = triangles;

        // Buckets the hulls by cell. Nothing is carved, so the cells get no
        // surfaces.
        var noSurfaces = new Polygon[_collision.Length][];
        Array.Fill(noSurfaces, []);
        CollisionCells = ChunkGrid.Build(_collision, noSurfaces);
    }

    /// <summary>What to call this map in a message: a logical asset path.</summary>
    public string Source { get; }

    /// <summary>The cells, in ascending cell order.</summary>
    public IReadOnlyList<CompiledStaticWorldChunk> Chunks => _chunks;

    /// <summary>Triangles across every cell, as uploaded.</summary>
    public int TriangleCount { get; }

    /// <summary>
    /// The baked world brushes as convex hulls, each with the world transform it
    /// was baked at, in node order. No node carries these brushes, so nothing
    /// can carve them again. A hull's faces name their materials when the map
    /// kept them, and the default material when it did not.
    /// </summary>
    public IReadOnlyList<BrushPlacement> CollisionPlacements => _collision;

    /// <summary>
    /// Which hulls of <see cref="CollisionPlacements"/> each cell owns and which
    /// reach into it. These cells hold no surfaces and no tree: ask
    /// <see cref="Chunks"/> for a cell's tree.
    /// </summary>
    public ChunkGrid CollisionCells { get; }

    /// <summary>Cells that carry a queryable tree.</summary>
    public int BspChunkCount
    {
        get
        {
            int trees = 0;
            for (int i = 0; i < _chunks.Length; i++)
            {
                if (_chunks[i].Bsp is not null) trees++;
            }

            return trees;
        }
    }

    /// <summary>
    /// True when <paramref name="point"/> lies inside the baked solid. Covers the
    /// static world only, not part brushes or mesh nodes.
    /// </summary>
    public bool ContainsPoint(Vector3 point) =>
        TryGetChunk(ChunkCoord.FromPosition(point), out CompiledStaticWorldChunk chunk)
        && chunk.Bsp is { } tree
        && tree.ContainsPoint(point);

    /// <summary>
    /// Casts a ray against the baked solid and reports the first surface entered,
    /// as <see cref="CsgWorld.Raycast"/> does over a live world. Same scope as
    /// <see cref="ContainsPoint"/>.
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out BspRaycastHit hit) =>
        ChunkRayWalk.Cast(new RayCells(this), origin, direction, maxDistance, out hit);

    /// <summary>Finds the cell at <paramref name="coord"/>, if this map has one.</summary>
    public bool TryGetChunk(ChunkCoord coord, out CompiledStaticWorldChunk chunk)
    {
        int lo = 0, hi = _chunks.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int order = _chunks[mid].Coord.CompareTo(coord);
            if (order == 0)
            {
                chunk = _chunks[mid];
                return true;
            }

            if (order < 0) lo = mid + 1;
            else hi = mid - 1;
        }

        chunk = default;
        return false;
    }

    /// <summary>
    /// Releases the map's bytes. Idempotent. Queries afterwards throw
    /// <see cref="ObjectDisposedException"/>, so call it only once the world has left the scene.
    /// </summary>
    public void Dispose()
    {
        ContentBlob? file = _file;
        _file = null;
        file?.Dispose();
    }

    private static ChunkCoord Min(ChunkCoord a, ChunkCoord b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

    private static ChunkCoord Max(ChunkCoord a, ChunkCoord b) =>
        new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    private readonly struct RayCells(CompiledStaticWorld world) : IChunkRayCells
    {
        public bool TryGetCellBounds(out ChunkCoord min, out ChunkCoord max)
        {
            min = world._cellMin;
            max = world._cellMax;
            return world._chunks.Length > 0;
        }

        public bool ContainsPoint(Vector3 point) => world.ContainsPoint(point);

        public bool RaycastCell(
            ChunkCoord cell, Vector3 origin, Vector3 direction, float maxDistance, out BspRaycastHit hit)
        {
            if (world.TryGetChunk(cell, out CompiledStaticWorldChunk chunk) && chunk.Bsp is { } tree)
                return tree.Raycast(origin, direction, maxDistance, out hit);

            hit = default;
            return false;
        }
    }
}
