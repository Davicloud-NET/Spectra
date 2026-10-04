namespace SpectraEngine.Core.Graphics;

/// <summary>
/// What CPU-side geometry a mesh keeps after its GPU upload. Meshes on scene
/// nodes need it for picking and bounds; chunk and part-brush meshes do not.
/// </summary>
public enum MeshCpuAccess
{
    /// <summary>Keep positions, normals and indices for CPU readers.</summary>
    Retained,

    /// <summary>GPU only. The arrays stay empty; <see cref="Mesh.LocalBounds"/> is still computed.</summary>
    None,
}
