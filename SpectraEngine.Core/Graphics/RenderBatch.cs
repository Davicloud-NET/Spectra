using System.Numerics;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// Draws sharing one mesh and one material, collapsed into an instanced draw.
/// Its transforms are a contiguous range of the view's instance array.
/// </summary>
/// <param name="Offset">Index of this batch's first transform in the view's instance array.</param>
public readonly record struct RenderBatch(Mesh Mesh, Material? Material, int Offset, int Count);

// Reference identity: equal-looking meshes are still separate GPU buffers.
internal readonly record struct RenderBatchKey(Mesh Mesh, Material? Material);
