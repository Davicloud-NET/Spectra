using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets.Models;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace SpectraEngine.Core.Assets;

// Models: import, GPU upload, material resolution and cache lifetime.
public sealed partial class AssetManager
{
    // Not _materialSync: building a model's materials calls LoadMaterial, which takes that one.
    private readonly object _modelSync = new();
    private readonly Dictionary<string, ModelAsset> _models = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Number of model assets currently cached. Any thread.</summary>
    public int ModelCount
    {
        get { lock (_modelSync) return _models.Count; }
    }

    /// <summary>
    /// Loads a model synchronously: import and GPU upload both happen on the
    /// calling thread, which must be the render thread. Use
    /// <see cref="RequestModel"/> for anything loaded while frames are running.
    /// Returns the cached handle if the path is already loaded.
    /// </summary>
    /// <param name="relativePath">Path under the content root, e.g. <c>Models/crate.obj</c>.</param>
    /// <param name="options">Import tuning; ignored if the path is already cached.</param>
    /// <exception cref="InvalidOperationException">No renderer is attached.</exception>
    /// <exception cref="FileNotFoundException">The model file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not a readable model.</exception>
    public ModelAsset LoadModel(string relativePath, ModelImportOptions? options = null)
    {
        RequireRenderer();

        ModelAsset asset = GetOrCreateModel(relativePath, options);
        if (asset.IsReady) return asset;

        // A newer ticket than any in-flight async request, so that result is dropped as stale.
        long sequence = asset.NextRequestSequence();
        ModelData data = ReadModelThroughContent(asset);
        using var upload = new ModelJob(this, asset, sequence, data, null, async: false);
        while (!upload.Step(UploadBudget.BytesPerStep).Complete) { }
        _renderer!.FlushUploads(waitForCompletion: true);
        return asset;
    }

    /// <summary>
    /// Requests a model asynchronously. Returns at once with a handle that is
    /// not yet ready; the import runs on the thread pool and the GPU meshes are
    /// created by a later <see cref="PumpPendingUploads"/>. Any thread, once
    /// <see cref="AttachRenderer"/> has run. A failed import shows up on
    /// <see cref="ModelAsset.Error"/>; asking again retries. Polling this while
    /// an import is in flight is free.
    /// </summary>
    /// <exception cref="InvalidOperationException">No renderer is attached yet.</exception>
    public ModelAsset RequestModel(string relativePath, ModelImportOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // _placeholder is the attach state that is safe to read cross-thread.
        if (_placeholder is null)
        {
            throw new InvalidOperationException(
                "AssetManager.RequestModel needs a renderer; call AttachRenderer on the render thread first.");
        }

        ModelAsset asset = GetOrCreateModel(relativePath, options);
        if (asset.IsReady) return asset;

        if (TryBeginImport(asset))
            QueueImport(asset);
        return asset;
    }

    /// <summary>
    /// Looks up an already-requested model without touching the disk. A hit may
    /// not be ready yet; check <see cref="ModelAsset.IsReady"/>. Any thread.
    /// </summary>
    public bool TryGetModel(string relativePath, [MaybeNullWhen(false)] out ModelAsset asset)
    {
        string key = ContentRoot.NormalizeRelativePath(relativePath);
        lock (_modelSync)
            return _models.TryGetValue(key, out asset);
    }

    /// <summary>
    /// Drops a model from the cache and destroys the GPU meshes it owns. Remove
    /// scene nodes that reference those meshes first. Returns false if the path
    /// was not loaded. Render thread only.
    /// </summary>
    public bool UnloadModel(string relativePath)
    {
        string key = ContentRoot.NormalizeRelativePath(relativePath);
        ModelAsset? asset;
        lock (_modelSync)
        {
            if (!_models.Remove(key, out asset))
                return false;

            // Results for an evicted handle are dropped, so nothing is pending now.
            asset.ImportPending = false;
        }

        int destroyed = DestroyModelMeshes(asset);
        _logger.LogInformation("Unloaded model {Path} ({Count} GPU mesh(es))", key, destroyed);
        return true;
    }

    /// <summary>
    /// Whether the model at <paramref name="relativePath"/> would be served from
    /// a cooked <c>.smodel</c> rather than imported from its authored file.
    /// </summary>
    public bool IsModelCooked(string relativePath)
    {
        string key = ContentRoot.NormalizeRelativePath(relativePath);
        return ModelContentPath.IsCooked(ModelContentPath.Resolve(Content, key));
    }

    // Any thread. A cooked model comes through the content stack. An authored
    // one goes to the native importer, which opens the file itself, so it can
    // only come from a folder.
    private ModelData ReadModelThroughContent(ModelAsset asset)
    {
        string resolved = ModelContentPath.Resolve(Content, asset.RelativePath);
        if (!ModelContentPath.IsCooked(resolved))
            return ModelImporter.Import(asset.SourcePath, ContentRootPath, asset.Options);

        using ContentBlob blob = OpenOrThrow(resolved);

        // Safe to dispose the blob here only because CookedModelData copies.
        return CookedModelData.Build(SmodelReader.Read(blob.Span, resolved), asset.RelativePath);
    }

    // At most one import in flight per handle.
    private bool TryBeginImport(ModelAsset asset)
    {
        lock (_modelSync)
        {
            if (asset.ImportPending) return false;
            asset.ImportPending = true;
            return true;
        }
    }

    private void EndImport(ModelAsset asset)
    {
        lock (_modelSync)
            asset.ImportPending = false;
    }

    // Failures are queued too, so the pump logs them on the render thread.
    private void QueueImport(ModelAsset asset)
    {
        long sequence = asset.NextRequestSequence();
        _uploadPipeline!.Queue(asset, () => ModelRequestStale(asset, sequence), () =>
        {
            try { return new ModelJob(this, asset, sequence, ReadModelThroughContent(asset), null, async: true); }
            catch (Exception ex) { return new ModelJob(this, asset, sequence, null, ex.Message, async: true); }
        }, () => EndImport(asset));
    }
    // Name-to-path is ModelMaterialOverride's rule, shared with the cook.
    // Existence is asked of the content stack, so an override in a pack is found.
    private bool TryFindMaterialOverride(string materialName, [NotNullWhen(true)] out string? path)
    {
        path = ModelMaterialOverride.PathFor(materialName);
        if (path is not null && Content.Exists(path)) return true;

        path = null;
        return false;
    }

    // False once the handle was unloaded or replaced by a later request.
    private bool IsCachedModel(ModelAsset asset)
    {
        lock (_modelSync)
            return _models.TryGetValue(asset.RelativePath, out ModelAsset? current)
                && ReferenceEquals(current, asset);
    }

    private ModelAsset GetOrCreateModel(string relativePath, ModelImportOptions? options)
    {
        string key = ContentRoot.NormalizeRelativePath(relativePath);
        lock (_modelSync)
        {
            if (_models.TryGetValue(key, out ModelAsset? cached))
                return cached;

            var created = new ModelAsset(
                key,
                ContentRoot.ResolveAbsolute(ContentRootPath, key),
                options ?? ModelImportOptions.Default);
            _models[key] = created;
            return created;
        }
    }

    // Blanks the handle too, so a holder sees an unloaded model, not disposed meshes.
    private int DestroyModelMeshes(ModelAsset asset)
    {
        IReadOnlyList<Mesh> meshes = asset.Meshes;
        for (int i = 0; i < meshes.Count; i++)
            _renderer?.DestroyMesh(meshes[i]);

        int count = meshes.Count;
        asset.Meshes = [];
        asset.Materials = [];
        asset.Data = null;
        asset.Metadata = null;
        return count;
    }

    // Render thread, before the renderer shuts down.
    private void ReleaseModelResources()
    {

        ModelAsset[] assets;
        lock (_modelSync)
        {
            assets = new ModelAsset[_models.Count];
            _models.Values.CopyTo(assets, 0);
            _models.Clear();
        }

        int destroyed = 0;
        foreach (ModelAsset asset in assets)
            destroyed += DestroyModelMeshes(asset);

        if (destroyed > 0)
            _logger.LogInformation("Asset manager released {Count} GPU model meshes", destroyed);
    }

}
