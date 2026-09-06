using System;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Core.Assets;

public sealed partial class AssetManager
{
    private readonly record struct PreparedModelMaterial(bool Used, ModelMaterial Source, string? Path, MaterialDefinition? Definition);

    // CPU-only: path resolution, parsing, and dependent image requests happen
    // before the payload enters the render-thread upload queue.
    private PreparedModelMaterial[] PrepareModelMaterials(ModelData data, bool async)
    {
        var used = new bool[data.Materials.Count];
        foreach (var mesh in data.Meshes)
            if ((uint)mesh.MaterialIndex < (uint)used.Length) used[mesh.MaterialIndex] = true;
        var result = new PreparedModelMaterial[used.Length];
        for (int i = 0; i < result.Length; i++)
        {
            if (!used[i]) continue;
            var source = data.Materials[i];
            string? path = source.AssetPath;
            if (path is null) TryFindMaterialOverride(source.Name, out path);
            MaterialDefinition? definition = null;
            if (path is not null)
            {
                try { definition = ParseMaterialThroughContent(path); }
                catch (Exception ex) { _logger.LogWarning("Model material {Path} is unavailable: {Error}", path, ex.Message); }
            }
            if (async)
            {
                if (definition is not null)
                    foreach (var slot in definition.Textures)
                        TryRequest(slot.TexturePath, slot.Filter, slot.Wrap, slot.ColorSpace);
                else if (path is null && source.DiffuseTexturePath is { } texture)
                    TryRequest(texture, TextureFilter.LinearMipmap, TextureWrap.Repeat, TextureColorSpace.Srgb);
            }
            result[i] = new(true, source, path, definition);
        }
        return result;

        void TryRequest(string path, TextureFilter filter, TextureWrap wrap, TextureColorSpace space)
        {
            try { RequestTexture(path, filter, wrap, space); }
            catch (Exception ex) { _logger.LogWarning("Model texture {Path} is unavailable: {Error}", path, ex.Message); }
        }
    }

    private Material ResolvePreparedModelMaterial(PreparedModelMaterial prepared, bool async)
    {
        if (!prepared.Used) return _defaultMaterial;
        if (prepared.Path is { } path)
        {
            lock (_materialSync) if (_materials.TryGetValue(path, out var cached)) return cached;
            Material material = prepared.Definition is { } definition ? BuildMaterial(path, definition, async) : _defaultMaterial;
            lock (_materialSync)
            {
                if (_materials.TryGetValue(path, out var raced)) return raced;
                _materials[path] = material;
            }
            return material;
        }
        if (prepared.Source.DiffuseTexturePath is not { } texturePath) return _defaultMaterial;
        try
        {
            TextureAsset texture = async ? RequestTexture(texturePath) : LoadTexture(texturePath);
            var material = new Material(_renderer?.DefaultShader) { Name = prepared.Source.Name };
            material.SetVector3(BaseColorParameter, prepared.Source.BaseColor);
            material.SetTexture(DiffuseSlotName, 0, texture);
            return material;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Model texture {Path} is unavailable: {Error}", texturePath, ex.Message);
            return _defaultMaterial;
        }
    }

    private void PublishModel(ModelAsset asset, long sequence, ModelData data, Mesh[] meshes, Material[] materials)
    {
        var metadata = new ModelMetadata(data);
        var previous = asset.Meshes;
        foreach (string warning in data.Warnings) _logger.LogWarning("Model {Path}: {Warning}", asset.RelativePath, warning);
        asset.Data = asset.Options.CpuRetention == ModelCpuRetention.Full ? data : null;
        asset.Meshes = meshes; asset.Materials = materials; asset.Error = null;
        asset.AppliedSequence = sequence; asset.Metadata = metadata;
        foreach (var mesh in previous) _renderer!.DestroyMesh(mesh);
        _logger.LogInformation("Loaded model {Path} ({Submeshes} submesh(es), {Vertices} vertices, {Triangles} triangles, {Materials} material(s))",
            asset.RelativePath, meshes.Length, data.VertexCount, data.IndexCount / 3, materials.Length);
    }
}
