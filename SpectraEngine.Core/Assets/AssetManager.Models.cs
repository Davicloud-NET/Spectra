using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets.Models;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace SpectraEngine.Core.Assets;

/// <summary>
/// The model half of the asset manager: import, GPU upload, material
/// resolution, and cache lifetime for <see cref="ModelAsset"/>.
/// </summary>
/// <remarks>
/// Split into its own file rather than its own class because it shares the
/// texture half's renderer, its upload pump, its content root and its degrade-
/// don't-throw policy — a second type would have had to duplicate all four, and
/// a second pump would have meant a second per-frame call site in the engine.
/// </remarks>
public sealed partial class AssetManager
{
    // Guards _models only. A third lock rather than reusing _materialSync:
    // building a model's materials calls LoadMaterial, which takes that one.
    private readonly object _modelSync = new();
    private readonly Dictionary<string, ModelAsset> _models = new(StringComparer.OrdinalIgnoreCase);

    // Background import -> render thread. Same shape as the texture queue.

    /// <summary>Number of model assets currently cached. Any thread.</summary>
    public int ModelCount
    {
        get { lock (_modelSync) return _models.Count; }
    }

    /// <summary>
    /// Loads a model synchronously: import and GPU upload both happen on the
    /// calling thread, which must be the render thread. This is the load-time
    /// path — use <see cref="RequestModel"/> for anything loaded while frames
    /// are running. Returns the cached handle if the path is already loaded.
    /// </summary>
    /// <remarks>
    /// If an async request for the same path is still in flight, this imports
    /// anyway and wins: it takes a newer ticket, so the background result is
    /// dropped as stale when it lands. The caller asked for a model it can use
    /// on the next line, and that is what it gets.
    /// </remarks>
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

        // Unlike the async path, a synchronous load reports its failure to the
        // caller: it is load-time code that can still decide what to do about a
        // missing prop, and swallowing it would leave an empty handle with no
        // stack to explain it.
        long sequence = asset.NextRequestSequence();
        ModelData data = ReadModelThroughContent(asset);
        using var upload = new ModelJob(this, asset, sequence, data, null, async: false);
        while (!upload.Step(UploadBudget.BytesPerStep).Complete) { }
        _renderer!.FlushUploads(waitForCompletion: true);
        return asset;
    }

    /// <summary>
    /// Requests a model asynchronously. Returns immediately with a handle that
    /// is not yet ready; the file is imported on the thread pool and the GPU
    /// meshes are created by the next <see cref="PumpPendingUploads"/>. Callable
    /// from any thread, once <see cref="AttachRenderer"/> has run.
    /// </summary>
    /// <remarks>
    /// <para>A failed import is reported through <see cref="ModelAsset.Error"/>
    /// and a log line, never as an exception: by the time it fails, the caller
    /// that asked is several frames gone. Asking again after a failure retries.</para>
    /// <para>Calling this repeatedly for a model that is still importing is free
    /// — at most one import per handle is ever in flight — so polling it from a
    /// frame loop is a supported way to wait for one.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">No renderer is attached yet.</exception>
    public ModelAsset RequestModel(string relativePath, ModelImportOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Same attachment proxy RequestTexture uses: _placeholder is the one
        // piece of attach state published for cross-thread reads.
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
    /// Looks up an already-requested model without touching the disk. A hit does
    /// not prove the model is usable — check <see cref="ModelAsset.IsReady"/>.
    /// Any thread.
    /// </summary>
    public bool TryGetModel(string relativePath, [MaybeNullWhen(false)] out ModelAsset asset)
    {
        string key = ContentRoot.NormalizeRelativePath(relativePath);
        lock (_modelSync)
            return _models.TryGetValue(key, out asset);
    }

    /// <summary>
    /// Drops a model from the cache and destroys the GPU meshes it owns through
    /// the creating renderer. Scene nodes still referencing those meshes must be
    /// removed first — this does not hunt them down. Returns false if the path
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

            // The handle is off the cache, so no import can ever be applied to
            // it again (ApplyImport drops results for evicted handles); leaving
            // the flag set would advertise an import that will never land.
            asset.ImportPending = false;
        }

        int destroyed = DestroyModelMeshes(asset);
        _logger.LogInformation("Unloaded model {Path} ({Count} GPU mesh(es))", key, destroyed);
        return true;
    }

    // ---- where a model's bytes come from ---------------------------------

    /// <summary>
    /// Whether the model at <paramref name="relativePath"/> would be served from
    /// a cooked <c>.smodel</c> rather than imported from its authored file.
    /// </summary>
    /// <remarks>
    /// For a host that wants to say which path a load took - a log line, an
    /// editor badge, a test. It asks <see cref="ModelContentPath.Resolve"/>, the
    /// same function the read asks, rather than probing for itself: a second
    /// spelling of the redirection is the failure this whole content layer has
    /// already paid for once.
    /// </remarks>
    public bool IsModelCooked(string relativePath)
    {
        string key = ContentRoot.NormalizeRelativePath(relativePath);
        return ModelContentPath.IsCooked(ModelContentPath.Resolve(Content, key));
    }

    // The model read, and the second of the two that FORK. Any thread: opening a
    // blob, validating a .smodel and copying its arrays are all pure CPU, exactly
    // like the image read beside it, which is what lets the async path run this on
    // the thread pool.
    //
    // The asymmetry between the two arms is real and is documented on
    // ModelContentPath: a cooked model is one self-contained payload and comes
    // through the mounted stack, while an authored one is handed to a native
    // importer that opens the FILE itself and follows the material library beside
    // it, so it can only come from a folder.
    private ModelData ReadModelThroughContent(ModelAsset asset)
    {
        string resolved = ModelContentPath.Resolve(Content, asset.RelativePath);
        if (!ModelContentPath.IsCooked(resolved))
            return ModelImporter.Import(asset.SourcePath, ContentRootPath, asset.Options);

        using ContentBlob blob = OpenOrThrow(resolved);

        // The span dies with the blob at the end of this statement, which is safe
        // for exactly one reason: CookedModelData copies. A builder that handed a
        // span onward would have made this blob's lifetime the model's, and
        // unmapping a pack view under a live span is an access violation with no
        // managed stack.
        return CookedModelData.Build(SmodelReader.Read(blob.Span, resolved), asset.RelativePath);
    }

    // ---- pump ------------------------------------------------------------

    // Claims the single in-flight import slot for this handle. False when one is
    // already running, which is what makes RequestModel idempotent per frame.
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

    // Import off the render thread and hand the (pure CPU) result back through
    // the queue. Failures are queued too, so the pump can log them on the render
    // thread instead of losing them in an unobserved task.
    private void QueueImport(ModelAsset asset)
    {
        long sequence = asset.NextRequestSequence();
        _uploadPipeline!.Queue(asset, () => ModelRequestStale(asset, sequence), () =>
        {
            try { return new ModelJob(this, asset, sequence, ReadModelThroughContent(asset), null, async: true); }
            catch (Exception ex) { return new ModelJob(this, asset, sequence, null, ex.Message, async: true); }
        }, () => EndImport(asset));
    }
    // The name-to-path half is ModelMaterialOverride's, shared with the cook so
    // the two cannot disagree about which file an imported material's name means;
    // the existence half is this manager's, asked of the mounted stack, because an
    // override that ships inside a pack has to be found there and Exists never
    // throws on a name the filesystem would refuse.
    private bool TryFindMaterialOverride(string materialName, [NotNullWhen(true)] out string? path)
    {
        path = ModelMaterialOverride.PathFor(materialName);
        if (path is not null && Content.Exists(path)) return true;

        path = null;
        return false;
    }

    // ---- lifetime --------------------------------------------------------

    // Whether this exact handle is still the cache's entry for its path — false
    // once it was unloaded, or replaced by a later request for the same path.
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

    // Destroys every GPU mesh a model owns and blanks the handle, so anything
    // still holding it sees an unloaded model rather than disposed meshes.
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

    // Called from ReleaseGraphicsResources, on the render thread, before the
    // renderer shuts down.
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
