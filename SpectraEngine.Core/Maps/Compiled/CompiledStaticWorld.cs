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
/// A static world that arrived baked: per-cell geometry already on the GPU and
/// per-cell BSP trees read off the compiled map. While a scene holds one it
/// refuses to carve, since the chunks already contain every world brush.
/// </summary>
// Owns the map's ContentBlob. The BSP nodes are a window into it, possibly a
// memory-mapped view, and unmapping under a live span is an access violation.
public sealed class CompiledStaticWorld : IDisposable
{
    private readonly CompiledStaticWorldChunk[] _chunks;
    private ContentBlob? _file;

    /// <param name="chunks">
    /// The cells, in ascending <see cref="ChunkCoord.CompareTo"/> order, as <c>CHDR</c> stores them.
    /// </param>
    /// <param name="file">The map's bytes. This object takes ownership.</param>
    public CompiledStaticWorld(string source, CompiledStaticWorldChunk[] chunks, ContentBlob? file)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(chunks);

        Source = source;
        _chunks = chunks;
        _file = file;

        int triangles = 0;
        for (int i = 0; i < chunks.Length; i++) triangles += chunks[i].TriangleCount;
        TriangleCount = triangles;
    }

    /// <summary>What to call this map in a message: a logical asset path.</summary>
    public string Source { get; }

    /// <summary>The cells, in ascending cell order.</summary>
    public IReadOnlyList<CompiledStaticWorldChunk> Chunks => _chunks;

    /// <summary>Triangles across every cell, as uploaded.</summary>
    public int TriangleCount { get; }

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
}
