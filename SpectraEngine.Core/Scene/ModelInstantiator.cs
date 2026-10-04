using SpectraEngine.Core.Assets;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// Turns a loaded <see cref="ModelAsset"/> into a <see cref="SceneNode"/>
/// subtree: one node per imported <see cref="ModelNode"/>, carrying that node's
/// local transform, with a <see cref="MeshRenderer"/> per submesh. Instances
/// share the model's GPU meshes, so remove them from the scene before
/// <see cref="AssetManager.UnloadModel"/>. Render thread only.
/// </summary>
// The subtree is built detached and attached once, so the scene sees finished
// nodes with composed world matrices.
public static class ModelInstantiator
{
    /// <summary>
    /// Builds the subtree and attaches it under <paramref name="parent"/>,
    /// returning its root.
    /// </summary>
    /// <param name="name">
    /// Name for the root node; null uses the model's own root name, falling back
    /// to the file name.
    /// </param>
    /// <exception cref="InvalidOperationException">The model is not loaded yet.</exception>
    public static SceneNode InstantiateInto(SceneNode parent, ModelAsset model, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        return parent.AddChild(Instantiate(model, name));
    }

    /// <summary>
    /// Builds the subtree without attaching it, so it can be positioned before
    /// the scene sees it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The model is not loaded yet.</exception>
    public static SceneNode Instantiate(ModelAsset model, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        ModelMetadata data = model.Metadata
            ?? throw new InvalidOperationException(
                $"Model '{model.RelativePath}' is not loaded yet" +
                (model.Error is { } error ? $" ({error})" : "; pump the asset manager until it is ready") +
                ".");

        if (model.Meshes.Count != data.Meshes.Count)
        {
            throw new InvalidOperationException(
                $"Model '{model.RelativePath}' has {data.Meshes.Count} submesh(es) but " +
                $"{model.Meshes.Count} GPU mesh(es); the asset is in an inconsistent state.");
        }

        string rootName = FirstNonEmpty(name, data.Root.Name, model.RelativePath);
        // Recursion depth is bounded by ModelImporter.MaxNodeDepth.
        return BuildNode(data.Root, model, data, rootName);
    }

    private static SceneNode BuildNode(
        ModelNode source, ModelAsset model, ModelMetadata data, string name)
    {
        var node = new SceneNode(name)
        {
            LocalTransform = new Transform
            {
                Position = source.Position,
                Rotation = source.Rotation,
                Scale = source.Scale,
            },
        };

        AttachMeshes(node, source, model, data);

        IReadOnlyList<ModelNode> children = source.Children;
        for (int i = 0; i < children.Count; i++)
        {
            ModelNode child = children[i];
            node.AddChild(BuildNode(child, model, data, FirstNonEmpty(null, child.Name, "Node")));
        }

        return node;
    }

    // A scene node holds one renderable, so several submeshes become child
    // nodes. A single submesh attaches directly.
    private static void AttachMeshes(
        SceneNode node, ModelNode source, ModelAsset model, ModelMetadata data)
    {
        IReadOnlyList<int> meshIndices = source.MeshIndices;
        if (meshIndices.Count == 0)
            return;

        if (meshIndices.Count == 1)
        {
            Attach(node, model, data, meshIndices[0]);
            return;
        }

        for (int i = 0; i < meshIndices.Count; i++)
        {
            int meshIndex = meshIndices[i];
            ModelMeshInfo mesh = data.Meshes[meshIndex];
            SceneNode part = node.CreateChild(FirstNonEmpty(null, mesh.Name, $"Submesh{i}"));
            Attach(part, model, data, meshIndex);
        }
    }

    // Source second: the MeshRenderer setter can clear MeshSource.
    private static void Attach(SceneNode node, ModelAsset model, ModelMetadata data, int meshIndex)
    {
        node.MeshRenderer = CreateRenderer(model, data, meshIndex);
        node.MeshSource = new MeshSource(model.RelativePath, meshIndex);
    }

    private static MeshRenderer CreateRenderer(ModelAsset model, ModelMetadata data, int meshIndex)
    {
        ModelMeshInfo source = data.Meshes[meshIndex];
        return new MeshRenderer(model.Meshes[meshIndex], model.MaterialFor(source.MaterialIndex));
    }

    private static string FirstNonEmpty(string? preferred, string fallback, string lastResort)
    {
        if (!string.IsNullOrWhiteSpace(preferred)) return preferred;
        if (!string.IsNullOrWhiteSpace(fallback)) return fallback;
        return lastResort;
    }
}
