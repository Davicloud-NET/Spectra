namespace SpectraEngine.Core.Scene;

/// <summary>
/// Where a node's <see cref="MeshRenderer"/> came from: a model file, and which
/// submesh of it. This is what lets a map save a mesh node. A mesh built in
/// code has none.
/// </summary>
/// <param name="ModelPath">Content-root-relative path of the model file.</param>
/// <param name="MeshIndex">
/// Position in <c>ModelAsset.Meshes</c>. Re-exporting the model can change what
/// it points at.
/// </param>
public readonly record struct MeshSource(string ModelPath, int MeshIndex);
