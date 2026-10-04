using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Generic;
using System.Threading;

namespace SpectraEngine.Core.Assets;

/// <summary>
/// A handle to a model asset: one per content path, shared by every instance of
/// the model. Owned by <see cref="AssetManager"/>; never dispose its meshes.
/// </summary>
// RelativePath, SourcePath and Options are readable anywhere. Everything else
// is written on the render thread and should only be read there.
public sealed class ModelAsset
{
    // Ticket per import, so a sync load issued during a background import wins
    // whichever finishes first.
    private long _requestSequence;

    internal ModelAsset(string relativePath, string sourcePath, ModelImportOptions options)
    {
        RelativePath = relativePath;
        SourcePath = sourcePath;
        Options = options;
    }

    /// <summary>Normalised content-root-relative path this model was loaded from.</summary>
    public string RelativePath { get; }

    /// <summary>Absolute path of the backing file on disk.</summary>
    public string SourcePath { get; }

    /// <summary>
    /// Import settings this handle was created with. A second request for the
    /// same path reuses the cached handle and these options.
    /// </summary>
    public ModelImportOptions Options { get; }

    /// <summary>Full import data when the CPU retention policy is Full, otherwise null.</summary>
    public ModelData? Data { get; internal set; }

    /// <summary>Retained for every CPU policy, including GPU-only assets.</summary>
    public ModelMetadata? Metadata { get; internal set; }

    /// <summary>
    /// GPU meshes, index-aligned with <see cref="ModelData.Meshes"/>. Empty
    /// until the import lands.
    /// </summary>
    public IReadOnlyList<Mesh> Meshes { get; internal set; } = [];

    /// <summary>
    /// Resolved materials, index-aligned with <see cref="ModelData.Materials"/>.
    /// Empty until the import lands.
    /// </summary>
    public IReadOnlyList<Material> Materials { get; internal set; } = [];

    /// <summary>
    /// Why the last import failed, or null. A failed handle stays cached, and
    /// asking for the same path again retries.
    /// </summary>
    public string? Error { get; internal set; }

    // Guarded by the asset manager's model lock. Stops a caller polling
    // RequestModel every frame from spawning a task per frame.
    internal bool ImportPending { get; set; }

    /// <summary>True once the import landed and the GPU resources exist.</summary>
    public bool IsReady => Metadata is not null;

    /// <summary>The model's bounds in its own space. Empty before it is ready.</summary>
    public Aabb LocalBounds => Metadata?.LocalBounds ?? default;

    /// <summary>
    /// The material a submesh draws with. An out-of-range index falls back to
    /// the last material instead of throwing.
    /// </summary>
    public Material MaterialFor(in ModelMesh mesh)
        => MaterialFor(mesh.MaterialIndex);

    public Material MaterialFor(int materialIndex)
    {
        IReadOnlyList<Material> materials = Materials;
        if (materials.Count == 0)
        {
            throw new InvalidOperationException(
                $"Model '{RelativePath}' has no materials; it is not loaded yet.");
        }

        int index = materialIndex;
        if ((uint)index >= (uint)materials.Count)
            index = materials.Count - 1;
        return materials[index];
    }

    internal long NextRequestSequence() => Interlocked.Increment(ref _requestSequence);
    internal long RequestSequence => Interlocked.Read(ref _requestSequence);

    internal long AppliedSequence { get; set; }
}
