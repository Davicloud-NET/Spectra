using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// One single-material piece of a static-world chunk on the GPU. The material
/// is resolved once per upload, so the draw-list build only copies it.
/// </summary>
/// <param name="SourceMaterial">
/// The interned material reference, kept so the entry can be re-resolved. A
/// chunk adopted from a compiled map has no CPU arrays to read it from.
/// </param>
/// <param name="Mesh">The GPU mesh; destroyed when its chunk is replaced or removed.</param>
/// <param name="Material">
/// The resolved material, or null when nothing resolved. Pipelines skip a null.
/// </param>
public readonly record struct StaticWorldSubmesh(MaterialRef SourceMaterial, Mesh Mesh, Material? Material);

/// <summary>
/// One chunk of a scene's static world on the GPU, one submesh per material.
/// </summary>
/// <param name="Coord">The cell this entry belongs to.</param>
/// <param name="RenderBounds">
/// The box frustum culling tests. Not <see cref="ChunkCoord.Bounds"/>: a
/// brush is owned by one cell and its surfaces can overhang it.
/// </param>
/// <param name="Artifact">
/// The compiled per-cell mesh data, or null for a chunk adopted from a
/// compiled map. Only used to detect change at swap: same instance, same entry.
/// </param>
/// <param name="Submeshes">
/// One per material the cell wears. Never null; empty when the cell has no
/// drawable geometry.
/// </param>
public readonly record struct StaticWorldChunkMesh(
    ChunkCoord Coord,
    Aabb RenderBounds,
    ChunkMesh? Artifact,
    StaticWorldSubmesh[] Submeshes)
{
    /// <summary>Builds an entry for a cell a live compile produced.</summary>
    public StaticWorldChunkMesh(ChunkMesh artifact, StaticWorldSubmesh[] submeshes)
        : this(artifact.Coord, artifact.RenderBounds, artifact, submeshes)
    {
    }
}
