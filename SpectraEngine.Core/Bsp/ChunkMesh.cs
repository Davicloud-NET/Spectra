using System.Collections.Generic;
using SpectraEngine.Core.Assets;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// A contiguous slice of a mesh's index array that wears one material.
/// Used by the monolithic <see cref="CsgWorld.BuildMesh"/>, not by the render path.
/// </summary>
/// <param name="IndexCount">Always a multiple of 3.</param>
public readonly record struct MaterialRun(MaterialRef Material, int IndexStart, int IndexCount);

/// <summary>
/// One chunk cell's geometry for a single material, in the standard 8-float
/// vertex layout. Zero-based and self-contained: one GPU mesh, one draw.
/// </summary>
/// <param name="Material">Resolved to a real material on the render thread at upload time.</param>
/// <param name="Vertices">Interleaved, 8 floats per vertex. Treat as immutable.</param>
/// <param name="Indices">Based at this submesh's first vertex. Never empty. Treat as immutable.</param>
public readonly record struct ChunkSubmesh(MaterialRef Material, float[] Vertices, uint[] Indices);

/// <summary>
/// The render mesh of one chunk cell: a <see cref="ChunkSubmesh"/> per face
/// material plus the AABB to cull against. Immutable; a reused instance tells
/// the swap path the cell is unchanged.
/// </summary>
// Per-material arrays rather than index ranges: every backend keeps a plain
// mesh.Draw() and a render item stays one mesh, one material, one matrix.
public sealed class ChunkMesh
{
    internal ChunkMesh(ChunkCoord coord, ChunkSubmesh[] submeshes, Aabb renderBounds)
    {
        Coord = coord;
        Submeshes = submeshes;
        RenderBounds = renderBounds;
    }

    /// <summary>The cell this mesh belongs to.</summary>
    public ChunkCoord Coord { get; }

    /// <summary>
    /// One entry per material, in ascending material id. Empty only when
    /// every owned surface is degenerate.
    /// </summary>
    public IReadOnlyList<ChunkSubmesh> Submeshes { get; }

    /// <summary>
    /// Union AABB of the cell's owned surfaces. Cull against this, not the
    /// cell's own box: a brush that crosses a border sticks out of its owner cell.
    /// </summary>
    public Aabb RenderBounds { get; }
}
