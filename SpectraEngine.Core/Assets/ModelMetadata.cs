using System.Collections.Generic;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Assets;

/// <summary>Hierarchy and material information without references to upload arrays.</summary>
public sealed class ModelMetadata
{
    public string SourcePath { get; }
    public IReadOnlyList<ModelMeshInfo> Meshes { get; }
    public IReadOnlyList<ModelMaterial> Materials { get; }
    public ModelNode Root { get; }
    public Aabb LocalBounds { get; }
    public int VertexCount { get; }
    public int IndexCount { get; }
    internal ModelMetadata(ModelData data)
    {
        SourcePath = data.SourcePath; Materials = data.Materials; Root = data.Root;
        LocalBounds = data.LocalBounds; VertexCount = data.VertexCount; IndexCount = data.IndexCount;
        var meshes = new ModelMeshInfo[data.Meshes.Count];
        for (int i = 0; i < meshes.Length; i++)
        {
            var mesh = data.Meshes[i];
            meshes[i] = new(mesh.Name, mesh.MaterialIndex, mesh.LocalBounds, mesh.VertexCount, mesh.IndexCount);
        }
        Meshes = meshes;
    }
}

public readonly record struct ModelMeshInfo(string Name, int MaterialIndex, Aabb LocalBounds, int VertexCount, int IndexCount);
