using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets.Images;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Numerics;

namespace SpectraEngine.Core.Assets;

/// <summary>
/// Loads content and owns the GPU resources it creates from it. Callers never
/// dispose what they get from here; they call an <c>Unload*</c> method or let
/// <see cref="ReleaseGraphicsResources"/> clean up. Decoding runs on the thread
/// pool; GPU creation and destruction happen on the render thread. Missing
/// content degrades to <see cref="DefaultMaterial"/> or the placeholder texture
/// with a warning instead of throwing.
/// </summary>
// Every texture and material read goes through Content, never System.IO.File,
// and every existence probe asks the same stack on the same resolved path
// (ImageContentPath.Resolve). If a probe and an open disagree, a packed build
// binds the placeholder into every material and logs nothing.
public sealed partial class AssetManager : IDisposable
{
    /// <summary>Name carried by <see cref="DefaultMaterial"/>.</summary>
    public const string DefaultMaterialName = "default";

    /// <summary>Name carried by <see cref="NeutralMaterial"/>.</summary>
    public const string NeutralMaterialName = "neutral";

    private const string DiffuseSlotName = "uDiffuse";
    private const string BaseColorParameter = "uBaseColor";

    // Seeded on every material: one program draws every deferred surface, so an
    // unset parameter keeps the previous draw's value.
    private const string RoughnessParameter = "uRoughness";
    private const string MetallicParameter = "uMetallic";
    private const string AmbientOcclusionParameter = "uAmbientOcclusion";
    private const string EmissiveParameter = "uEmissive";
    private const string ShadingModelParameter = "uShadingModel";

    private readonly ILogger _logger;

    // Guards _textures and the TextureAsset.LoadFailed / PendingDecodes flags.
    // Never held across a decode or a GPU call.
    private readonly object _sync = new();

    // Path -> variants, in load order. Sampler state is baked into the GPU
    // texture, so one image loaded with two filter/wrap/colour-space
    // combinations needs two textures.
    private readonly Dictionary<string, List<TextureAsset>> _textures =
        new(StringComparer.OrdinalIgnoreCase);

    // Separate from _sync: building a material calls LoadTexture, which takes _sync.
    private readonly object _materialSync = new();
    private readonly Dictionary<string, Material> _materials = new(StringComparer.OrdinalIgnoreCase);

    // Paths normalisation rejects, so each is warned about once. Guarded by _materialSync.
    private readonly HashSet<string> _unusableMaterialPaths = new(StringComparer.OrdinalIgnoreCase);

    // Never replaced, so DefaultMaterial is non-null before attach and after teardown.
    private readonly Material _defaultMaterial;

    // For faces that name no material. Not the default material: that one
    // means a reference failed.
    private readonly Material _neutralMaterial;

    // One white texel. Written on the render thread, read from any thread.
    private volatile Texture? _white;

    // Watcher thread -> render thread: absolute paths of files that changed.
    private readonly ConcurrentQueue<string> _changedFiles = new();

    // One watcher per directory, not per file. Render thread only.
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);

    // Reused across pumps to avoid a per-frame allocation.
    private HashSet<string>? _reloadScratch;
    private List<TextureAsset>? _reloadTargets;

    // Written on the render thread, read from any thread.
    private volatile Texture? _placeholder;

    private Renderer? _renderer;
    private bool _graphicsReleased;
    private bool _disposed;

    /// <summary>
    /// Creates a manager over the process's default content root
    /// (<see cref="ContentRoot.Path"/>).
    /// </summary>
    public AssetManager(ILogger logger)
        : this(logger, ContentRoot.Path, ContentRoot.IsDeveloperBuild)
    {
    }

    /// <summary>
    /// Creates a manager over an explicit content root. The content stack is a
    /// single <see cref="LooseFileSource"/> over that folder.
    /// </summary>
    public AssetManager(ILogger logger, string contentRoot, bool hotReloadEnabled = true)
        : this(logger, contentRoot, CreateLooseStack(logger, contentRoot), hotReloadEnabled)
    {
    }

    /// <summary>
    /// Creates a manager over an explicit content stack, for a packed build, a
    /// mod overlay or a cook. The stack decides where texture and material
    /// bytes come from; <paramref name="contentRoot"/> stays the filesystem
    /// anchor for model import and source paths.
    /// </summary>
    public AssetManager(
        ILogger logger, string contentRoot, ContentSourceStack content, bool hotReloadEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(contentRoot);
        ArgumentNullException.ThrowIfNull(content);

        _logger = logger;
        ContentRootPath = Path.GetFullPath(contentRoot);
        HotReloadEnabled = hotReloadEnabled;
        Content = content;
        Acoustics = new MaterialAcoustics(logger, content);

        _defaultMaterial = new Material(null) { Name = DefaultMaterialName };
        SeedBuiltInParameters(_defaultMaterial);

        _neutralMaterial = new Material(null) { Name = NeutralMaterialName };
        SeedBuiltInParameters(_neutralMaterial);
        _neutralMaterial.SetVector3(BaseColorParameter, NeutralBaseColorLinear);
    }

    // #8C8C99, the base colour dev_grid.spectramat names.
    private static readonly Vector3 NeutralBaseColorLinear =
        ColorSpace.SrgbToLinear(new Vector3(140f / 255f, 140f / 255f, 153f / 255f));

    private static ContentSourceStack CreateLooseStack(ILogger logger, string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(contentRoot);

        var stack = new ContentSourceStack();
        stack.Mount(new LooseFileSource(logger, contentRoot));
        return stack;
    }

    /// <summary>
    /// Absolute content root every relative asset path resolves against.
    /// </summary>
    public string ContentRootPath { get; }

    /// <summary>
    /// Where content bytes come from. Mounted at start-up; safe to read from any thread.
    /// </summary>
    public ContentSourceStack Content { get; }

    /// <summary>
    /// What each material is made of, for sound, read from
    /// <see cref="Content"/>. Any thread.
    /// </summary>
    public MaterialAcoustics Acoustics { get; }

    /// <summary>
    /// Whether changed files are re-decoded and swapped in. Set before loading
    /// anything; flipping it later only affects assets loaded afterwards.
    /// Render thread.
    /// </summary>
    public bool HotReloadEnabled { get; set; }

    /// <summary>
    /// The magenta/black checker bound while an async load is in flight and
    /// after a failed load. Null until <see cref="AttachRenderer"/> runs.
    /// </summary>
    public Texture? PlaceholderTexture => _placeholder;

    /// <summary>
    /// Number of texture assets currently cached, counting each sampler-state
    /// variant of an image. Any thread.
    /// </summary>
    public int TextureCount
    {
        get
        {
            lock (_sync)
            {
                int count = 0;
                foreach (List<TextureAsset> variants in _textures.Values)
                    count += variants.Count;
                return count;
            }
        }
    }

    /// <summary>
    /// Whether this material path is known not to resolve. Answered from the
    /// cache without touching the disk, so a material nothing has tried to load
    /// reports false.
    /// </summary>
    public bool IsMaterialMissing(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return false;

        string key;
        try
        {
            key = ContentRoot.NormalizeRelativePath(relativePath);
        }
        catch (ArgumentException)
        {
            return true;
        }

        lock (_materialSync)
        {
            if (_unusableMaterialPaths.Contains(key)) return true;

            return _materials.TryGetValue(key, out Material? material)
                && ReferenceEquals(material, _defaultMaterial);
        }
    }

    /// <summary>
    /// Forgets that a material path failed, so the next load reads the disk.
    /// A failed load is otherwise cached as the fallback for the whole session.
    /// Render thread.
    /// </summary>
    /// <returns>Whether an entry was dropped.</returns>
    public bool ForgetFailedMaterial(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return false;

        string key;
        try
        {
            key = ContentRoot.NormalizeRelativePath(relativePath);
        }
        catch (ArgumentException)
        {
            return false;
        }

        // It may have asked before the file was there, when nothing drew with it.
        Acoustics.Forget(key);

        lock (_materialSync)
        {
            bool forgotten = _unusableMaterialPaths.Remove(key);

            // Only the fallback. A real material here is shared with what draws it.
            if (_materials.TryGetValue(key, out Material? material) &&
                ReferenceEquals(material, _defaultMaterial))
            {
                _materials.Remove(key);
                forgotten = true;
            }

            return forgotten;
        }
    }

    /// <summary>Number of materials currently cached. Any thread.</summary>
    public int MaterialCount
    {
        get { lock (_materialSync) return _materials.Count; }
    }

    /// <summary>
    /// The fallback for a material that is missing or unreadable: the lit
    /// shader with the magenta checker. Never null; it has no shader or texture
    /// until <see cref="AttachRenderer"/> runs.
    /// </summary>
    public Material DefaultMaterial => _defaultMaterial;

    /// <summary>
    /// The surface for geometry that names no material at all: a face carrying
    /// <see cref="Bsp.MaterialRef.Default"/>. Flat grey, not the error checker.
    /// Never null; it has no shader or texture until
    /// <see cref="AttachRenderer"/> runs.
    /// </summary>
    public Material NeutralMaterial => _neutralMaterial;

    /// <summary>
    /// How many cached references are standing on a failure right now: textures
    /// whose decode failed, materials whose file could not be read, and sampler
    /// slots left holding the magenta checker. A decode still in flight is not
    /// counted. Any thread.
    /// </summary>
    // Only sees materials in the cache, so every hand-out must go through LoadMaterial.
    public int PlaceholderBoundCount
    {
        get
        {
            int count = 0;

            lock (_sync)
            {
                foreach (List<TextureAsset> variants in _textures.Values)
                {
                    foreach (TextureAsset asset in variants)
                    {
                        if (asset.LoadFailed) count++;
                    }
                }
            }

            // The two locks are never nested.
            Texture? placeholder = _placeholder;
            lock (_materialSync)
            {
                count += _unusableMaterialPaths.Count;
                foreach (Material material in _materials.Values)
                {
                    // A path cached as the fallback counts once, not once more
                    // for the checker in its slot.
                    if (ReferenceEquals(material, _defaultMaterial)) count++;
                    else count += material.CountBindingsTo(placeholder);
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Optional hook that turns a material file's <c>shader</c> name into a
    /// program. Set it on the render thread before loading materials; returning
    /// null (or leaving it unset) falls back to
    /// <see cref="Renderer.DefaultShader"/>.
    /// </summary>
    public Func<string, ShaderProgram?>? ShaderResolver { get; set; }

    /// <summary>
    /// CPU-side start-up: reports the resolved content root. No GPU work, so the
    /// engine calls this on the OS-event thread before the render thread exists.
    /// </summary>
    public void Initialize()
    {
        _logger.LogInformation("Content sources: {Sources}", Content.Describe());

        if (!Directory.Exists(ContentRootPath))
        {
            _logger.LogWarning(
                "Asset manager initialized, but content root does not exist: {Root}", ContentRootPath);
            return;
        }

        if (HotReloadEnabled)
        {
            _logger.LogInformation(
                "Asset manager initialized; content root {Root} (hot-reload on)", ContentRootPath);
            return;
        }

        // Warn only when hot reload was lost, not when the host turned it off.
        string? reason = ContentRoot.NotFromSourceTreeReason;
        if (reason is null)
        {
            _logger.LogInformation(
                "Asset manager initialized; content root {Root} (hot-reload off, disabled by the host)",
                ContentRootPath);
            return;
        }

        _logger.LogWarning(
            "Asset manager initialized; content root {Root} (hot-reload OFF: {Reason}). " +
            "Assets load from the copy beside the executable and edits to them will not be picked up.",
            ContentRootPath, reason);
    }

    /// <summary>
    /// Binds the renderer that will own every texture created from here and
    /// builds the placeholder. Render thread only, after
    /// <see cref="Renderer.Initialize"/>.
    /// </summary>
    public void AttachRenderer(Renderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (ReferenceEquals(_renderer, renderer)) return;
        if (_renderer is not null)
            throw new InvalidOperationException(
                "AssetManager is already attached to a renderer; release its resources first.");

        _renderer = renderer;
        _graphicsReleased = false;
        _uploadPipeline = new AssetUploadPipeline(_uploadBudget);
        Texture placeholder = CreatePlaceholder(renderer);
        _placeholder = placeholder;
        Texture white = CreateWhiteTexel(renderer);
        _white = white;

        // Same instance, so anything already holding it starts drawing.
        _defaultMaterial.Shader = renderer.DefaultShader;
        _defaultMaterial.SetTexture(DiffuseSlotName, 0, placeholder);

        _neutralMaterial.Shader = renderer.DefaultShader;
        _neutralMaterial.SetTexture(DiffuseSlotName, 0, white);
        if (renderer.DefaultShader is null)
        {
            _logger.LogWarning(
                "Renderer has no default shader; the default material will not draw until one is assigned");
        }

        _logger.LogDebug("Asset manager attached to {Backend} renderer", renderer.Backend);
    }

    /// <summary>
    /// Loads a texture synchronously: decode and GPU upload both happen on the
    /// calling thread, which must be the render thread. Use
    /// <see cref="RequestTexture"/> for anything loaded while frames are
    /// running. Filter, wrap and colour space are part of the cache identity,
    /// so the same image with different ones is a second texture. A load that
    /// failed earlier is retried into the same handle.
    /// </summary>
    /// <param name="relativePath">Path under the content root, e.g. <c>Textures/dev_grid.png</c>.</param>
    /// <exception cref="InvalidOperationException">No renderer is attached.</exception>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="InvalidDataException">The file is not a supported image.</exception>
    public TextureAsset LoadTexture(
        string relativePath,
        TextureFilter filter = TextureFilter.LinearMipmap,
        TextureWrap wrap = TextureWrap.Repeat,
        TextureColorSpace colorSpace = TextureColorSpace.Srgb)
    {
        Renderer renderer = RequireRenderer();
        string key = ContentRoot.NormalizeRelativePath(relativePath);

        TextureAsset? failed;
        lock (_sync)
        {
            TextureAsset? cached = FindVariant(key, filter, wrap, colorSpace);
            // A failed handle is not a hit; the file may be readable now.
            if (cached is not null && !cached.LoadFailed && !cached.IsPlaceholder) return cached;
            failed = cached;
        }

        string absolute = ContentRoot.ResolveAbsolute(ContentRootPath, key);
        // Ticket before the decode, so a background decode landing meanwhile is stale.
        long sequence = failed?.NextRequestSequence() ?? 0;
        ImageSource image = ReadImageThroughContent(key);
        Texture texture;
        try
        {
            texture = CreateTextureFrom(renderer, key, in image, colorSpace, filter, wrap);
        }
        finally
        {
            // Release the pack reference now; holding it would keep the mount alive.
            image.Dispose();
        }

        if (failed is not null)
        {
            // Rebind the existing handle so materials bound to it recover.
            Texture previous = failed.Texture;
            failed.Texture = texture;
            failed.IsPlaceholder = false;
            failed.AppliedSequence = sequence;
            failed.Version++;
            lock (_sync) failed.LoadFailed = false;

            DestroyOwned(previous);
            EnsureWatching(key);
            _logger.LogInformation(
                "Loaded texture {Path} after an earlier failure ({Description})", key, image.Describe());
            return failed;
        }

        var asset = new TextureAsset(key, absolute, filter, wrap, colorSpace, texture, isPlaceholder: false)
        {
            // Same state an async load reaches after its first pump.
            Version = 1,
        };
        asset.AppliedSequence = asset.NextRequestSequence();

        lock (_sync)
        {
            // RequestTexture on another thread may have inserted the variant
            // while this one decoded. Theirs wins.
            if (FindVariant(key, filter, wrap, colorSpace) is { } raced)
            {
                renderer.DestroyTexture(texture);
                return raced;
            }
            AddVariant(key, asset);
        }

        EnsureWatching(key);
        _logger.LogInformation("Loaded texture {Path} ({Description})", key, image.Describe());
        return asset;
    }

    /// <summary>
    /// Requests a texture asynchronously. Returns at once with a handle bound
    /// to the placeholder; the file is decoded on the thread pool and the real
    /// texture is swapped in by a later <see cref="PumpPendingUploads"/>. Any
    /// thread, once <see cref="AttachRenderer"/> has run. Asking again while a
    /// decode is in flight is free; asking again after a failure retries.
    /// </summary>
    /// <exception cref="InvalidOperationException">No renderer is attached yet.</exception>
    public TextureAsset RequestTexture(
        string relativePath,
        TextureFilter filter = TextureFilter.LinearMipmap,
        TextureWrap wrap = TextureWrap.Repeat,
        TextureColorSpace colorSpace = TextureColorSpace.Srgb)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Texture placeholder = _placeholder
            ?? throw new InvalidOperationException(
                "AssetManager.RequestTexture needs a renderer; call AttachRenderer on the render thread first.");

        string key = ContentRoot.NormalizeRelativePath(relativePath);
        TextureAsset asset;
        bool retry = false;
        lock (_sync)
        {
            if (FindVariant(key, filter, wrap, colorSpace) is { } cached)
            {
                // Retry only when no decode is in flight, so polling per frame
                // does not queue one each time.
                if (!cached.LoadFailed || cached.PendingDecodes > 0)
                    return cached;

                cached.PendingDecodes++;
                asset = cached;
                retry = true;
            }
            else
            {
                asset = new TextureAsset(
                    key,
                    ContentRoot.ResolveAbsolute(ContentRootPath, key),
                    filter,
                    wrap,
                    colorSpace,
                    placeholder,
                    isPlaceholder: true);
                asset.PendingDecodes++;
                AddVariant(key, asset);
            }
        }

        if (retry)
            _logger.LogDebug("Retrying the failed decode of {Path}", key);
        QueueDecode(asset);
        return asset;
    }

    /// <summary>
    /// Looks up an already-loaded texture without touching the disk: the first
    /// sampler-state variant loaded for the path. Any thread, but read the
    /// handle's <see cref="TextureAsset.Texture"/> on the render thread only.
    /// </summary>
    public bool TryGetTexture(string relativePath, [MaybeNullWhen(false)] out TextureAsset asset)
    {
        string key = ContentRoot.NormalizeRelativePath(relativePath);
        lock (_sync)
        {
            if (_textures.TryGetValue(key, out List<TextureAsset>? variants) && variants.Count > 0)
            {
                asset = variants[0];
                return true;
            }
        }

        asset = null;
        return false;
    }

    /// <summary>
    /// Looks up the already-loaded texture for a path and a specific sampler
    /// state. Any thread.
    /// </summary>
    public bool TryGetTexture(
        string relativePath,
        TextureFilter filter,
        TextureWrap wrap,
        [MaybeNullWhen(false)] out TextureAsset asset,
        TextureColorSpace colorSpace = TextureColorSpace.Srgb)
    {
        string key = ContentRoot.NormalizeRelativePath(relativePath);
        lock (_sync)
            asset = FindVariant(key, filter, wrap, colorSpace);
        return asset is not null;
    }

    /// <summary>
    /// Loads a <c>.spectramat</c> material file and returns the cached instance
    /// for that path; see <see cref="MaterialParser"/> for the format. Render
    /// thread only. Never throws for content reasons: a missing or unreadable
    /// file yields <see cref="DefaultMaterial"/>, a missing texture the
    /// placeholder, each with a warning.
    /// </summary>
    /// <param name="relativePath">Path under the content root, e.g. <c>Materials/wall.spectramat</c>.</param>
    /// <exception cref="InvalidOperationException">No renderer is attached.</exception>
    public Material LoadMaterial(string relativePath)
    {
        RequireRenderer();
        string key = ContentRoot.NormalizeRelativePath(relativePath);

        lock (_materialSync)
        {
            if (_materials.TryGetValue(key, out Material? cached))
                return cached;
        }

        Material material;
        if (!Content.Exists(key))
        {
            _logger.LogWarning("Material {Path} not found; using the default material", key);
            material = _defaultMaterial;
        }
        else
        {
            try
            {
                material = BuildMaterial(key);
            }
            catch (Exception ex)
            {
                // I/O only. The parser warns instead of throwing.
                _logger.LogError(ex, "Reading material {Path} failed; using the default material", key);
                material = _defaultMaterial;
            }
        }

        lock (_materialSync)
        {
            // The fallback is cached too, so a repeat request does not probe
            // and warn again. ForgetFailedMaterial undoes it.
            if (_materials.TryGetValue(key, out Material? raced))
                return raced;
            _materials[key] = material;
        }

        return material;
    }

    /// <summary>
    /// Turns the interned <see cref="MaterialRef"/> a compiled surface carries
    /// into a real material. Render thread only, at mesh-upload time; the
    /// background compile must not call it. Anything unresolvable yields
    /// <see cref="DefaultMaterial"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No renderer is attached.</exception>
    public Material ResolveMaterial(MaterialRef reference)
    {
        if (reference.IsDefault || !MaterialRegistry.TryGetPath(reference, out string path))
            return _defaultMaterial;

        try
        {
            return LoadMaterial(path);
        }
        catch (ArgumentException ex)
        {
            // MaterialRegistry interns paths like "../x" that normalisation
            // rejects. This runs inside the static-world swap on the render
            // thread, so degrade and warn once per path instead of throwing.
            bool first;
            lock (_materialSync) first = _unusableMaterialPaths.Add(path);
            if (first)
            {
                _logger.LogWarning(
                    "Material path '{Path}' is not usable ({Message}); using the default material",
                    path, ex.Message);
            }
            return _defaultMaterial;
        }
    }

    /// <summary>
    /// Looks up an already-loaded material without touching the disk. A path
    /// whose file was missing is cached as <see cref="DefaultMaterial"/>, so a
    /// hit does not prove the file existed. Any thread.
    /// </summary>
    public bool TryGetMaterial(string relativePath, [MaybeNullWhen(false)] out Material material)
    {
        string key = ContentRoot.NormalizeRelativePath(relativePath);
        lock (_materialSync)
            return _materials.TryGetValue(key, out material);
    }

    /// <summary>
    /// Advances ready textures and models under their shared upload budget,
    /// publishes completed assets, and queues re-decodes for changed files.
    /// Render thread only, once per frame. Returns the number of assets applied.
    /// </summary>
    public int PumpPendingUploads()
    {
        if (_renderer is null || _graphicsReleased) return 0;
        DispatchFileChanges();
        return _uploadPipeline!.Pump();
    }

    /// <summary>
    /// Drops every sampler-state variant of a texture from the cache and
    /// destroys its GPU resources. Handles still held by a caller fall back to
    /// the placeholder. Returns false if the path was not loaded. Render thread
    /// only.
    /// </summary>
    public bool UnloadTexture(string relativePath)
    {
        string key = ContentRoot.NormalizeRelativePath(relativePath);
        List<TextureAsset>? variants;
        lock (_sync)
        {
            if (!_textures.Remove(key, out variants))
                return false;
        }

        for (int i = 0; i < variants.Count; i++)
        {
            TextureAsset asset = variants[i];
            DestroyOwned(asset.Texture);
            asset.Texture = _placeholder!;
            asset.IsPlaceholder = true;
        }

        StopWatchingIfUnused(key);

        _logger.LogInformation("Unloaded texture {Path}", key);
        return true;
    }

    /// <summary>
    /// Destroys every GPU resource this manager owns and stops all file
    /// watching. Render thread only, before <see cref="Renderer.Shutdown"/>.
    /// Idempotent.
    /// </summary>
    public void ReleaseGraphicsResources()
    {
        if (_graphicsReleased) return;
        _graphicsReleased = true;
        _uploadPipeline?.StopWorkers();
        _uploadPipeline?.ReleaseUploads();
        _renderer?.FlushUploads(waitForCompletion: true);

        foreach (FileSystemWatcher watcher in _watchers.Values)
            watcher.Dispose();
        _watchers.Clear();

        while (_changedFiles.TryDequeue(out _)) { }

        // Models first: their materials reference textures destroyed below.
        ReleaseModelResources();

        var assets = new List<TextureAsset>();
        lock (_sync)
        {
            foreach (List<TextureAsset> variants in _textures.Values)
                assets.AddRange(variants);
            _textures.Clear();
        }

        int destroyed = 0;
        foreach (TextureAsset asset in assets)
        {
            if (DestroyOwned(asset.Texture)) destroyed++;
            asset.IsPlaceholder = true;
        }

        // Material bindings point at the textures just destroyed. The two
        // built-in materials survive, stripped back to their pre-attach state.
        lock (_materialSync) _materials.Clear();
        _defaultMaterial.ClearTextures();
        _defaultMaterial.Shader = null;

        _neutralMaterial.ClearTextures();
        _neutralMaterial.Shader = null;

        if (_placeholder is { } placeholder)
        {
            _renderer?.DestroyTexture(placeholder);
            _placeholder = null;
        }

        if (_white is { } white)
        {
            _renderer?.DestroyTexture(white);
            _white = null;
        }

        _renderer = null;
        if (destroyed > 0)
            _logger.LogInformation("Asset manager released {Count} GPU textures", destroyed);
    }

    /// <summary>
    /// CPU-side teardown. Safe on the main thread, after
    /// <see cref="ReleaseGraphicsResources"/> has run on the render thread; if
    /// it has not, this warns and leaves the GPU alone.
    /// </summary>
    public void Shutdown()
    {
        _uploadPipeline?.StopWorkers();
        // Again: a decode can land after ReleaseGraphicsResources drained the
        // queue, and a cooked one holds a pack reference.
        if (_graphicsReleased || _renderer is null) _uploadPipeline?.ReleaseUploads();

        // An open sound pins its pack's mapping.
        ReleaseAudioResources();

        if (!_graphicsReleased && _renderer is not null)
        {
            _logger.LogWarning(
                "Asset manager shut down with GPU textures still live; " +
                "ReleaseGraphicsResources must run on the render thread before Shutdown");

            foreach (FileSystemWatcher watcher in _watchers.Values)
                watcher.Dispose();
            _watchers.Clear();
            lock (_sync) _textures.Clear();
            lock (_materialSync) _materials.Clear();
            lock (_modelSync) _models.Clear();
            _renderer = null;
        }

        _disposed = true;
        _logger.LogInformation("Asset manager shut down");
    }

    /// <inheritdoc cref="Shutdown"/>
    public void Dispose() => Shutdown();

    // What the watcher callback does. Any thread. Tests call it directly so
    // reload coverage does not depend on filesystem-notification timing.
    internal void NotifyFileChanged(string absolutePath)
        => _changedFiles.Enqueue(Path.GetFullPath(absolutePath));

    // Render thread.
    internal int WatchedDirectoryCount => _watchers.Count;

    // Every material starts from these, so a parameter its file omits has a
    // defined value instead of the previous draw's. Built-in shader only; a
    // custom shader's parameters have no known defaults.
    private static void SeedBuiltInParameters(Material material)
    {
        material.SetVector3(BaseColorParameter, Vector3.One);

        // A plain dielectric.
        material.SetFloat(RoughnessParameter, 0.65f);
        material.SetFloat(MetallicParameter, 0f);
        material.SetFloat(AmbientOcclusionParameter, 1f);
        material.SetVector3(EmissiveParameter, Vector3.Zero);
        material.SetFloat(ShadingModelParameter, 0f);
    }

    // Only the read can throw; LoadMaterial catches it.
    private Material BuildMaterial(string key) => BuildMaterial(key, ParseMaterialThroughContent(key), false);

    private Material BuildMaterial(string key, MaterialDefinition definition, bool asynchronous)
    {
        foreach (string warning in definition.Warnings)
            _logger.LogWarning("Material {Path}: {Warning}", key, warning);

        var material = new Material(ResolveShader(definition.ShaderName, key))
        {
            Name = Path.GetFileNameWithoutExtension(key),
            SourcePath = key,
        };

        // Before the file's own parameters, which override them.
        SeedBuiltInParameters(material);

        IReadOnlyList<MaterialParameter> parameters = definition.Parameters;
        for (int i = 0; i < parameters.Count; i++)
        {
            MaterialParameter parameter = parameters[i];
            switch (parameter.Kind)
            {
                case MaterialParameterKind.Float: material.SetFloat(parameter.Name, parameter.AsFloat); break;
                case MaterialParameterKind.Vector2: material.SetVector2(parameter.Name, parameter.AsVector2); break;
                case MaterialParameterKind.Vector3: material.SetVector3(parameter.Name, parameter.AsVector3); break;
                default: material.SetVector4(parameter.Name, parameter.AsVector4); break;
            }
        }

        IReadOnlyList<MaterialTextureSlot> slots = definition.Textures;
        for (int i = 0; i < slots.Count; i++)
            BindTextureSlot(material, key, slots[i], asynchronous);

        _logger.LogInformation(
            "Loaded material {Path} ({Parameters} parameter(s), {Textures} texture(s))",
            key, material.ParameterCount, material.TextureCount);
        return material;
    }

    // Falls back to the placeholder, never an unbound sampler: that would read
    // whatever the last draw left on the unit.
    private void BindTextureSlot(Material material, string materialKey, in MaterialTextureSlot slot, bool asynchronous = false)
    {
        string textureKey;
        try
        {
            textureKey = ContentRoot.NormalizeRelativePath(slot.TexturePath);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(
                "Material {Path}: texture path '{Texture}' for '{Slot}' is not usable ({Message}); using the placeholder",
                materialKey, slot.TexturePath, slot.Name, ex.Message);
            BindPlaceholder(material, slot);
            return;
        }

        // Must ask the same stack and resolved path as the read below.
        if (!asynchronous && !ImageExists(textureKey))
        {
            _logger.LogWarning(
                "Material {Path}: texture {Texture} for '{Slot}' not found; using the placeholder",
                materialKey, textureKey, slot.Name);
            BindPlaceholder(material, slot);
            return;
        }

        try
        {
            TextureAsset asset = asynchronous ? RequestTexture(textureKey, slot.Filter, slot.Wrap, slot.ColorSpace)
                : LoadTexture(textureKey, slot.Filter, slot.Wrap, slot.ColorSpace);
            // The handle, not its Texture, so the material follows hot reloads.
            material.SetTexture(slot.Name, slot.Unit, asset);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Material {Path}: texture {Texture} for '{Slot}' failed to load ({Message}); using the placeholder",
                materialKey, textureKey, slot.Name, ex.Message);
            BindPlaceholder(material, slot);
        }
    }

    private void BindPlaceholder(Material material, in MaterialTextureSlot slot)
    {
        if (_placeholder is { } placeholder)
            material.SetTexture(slot.Name, slot.Unit, placeholder);
    }

    private ShaderProgram? ResolveShader(string? shaderName, string materialKey)
    {
        ShaderProgram? fallback = _renderer?.DefaultShader;
        if (string.IsNullOrEmpty(shaderName)) return fallback;

        if (ShaderResolver?.Invoke(shaderName) is { } resolved)
            return resolved;

        if (string.Equals(shaderName, MaterialParser.BuiltInShaderName, StringComparison.OrdinalIgnoreCase))
            return fallback;

        _logger.LogWarning(
            "Material {Path}: no shader named '{Shader}'; using the built-in lit shader",
            materialKey, shaderName);
        return fallback;
    }

    private Renderer RequireRenderer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _renderer
            ?? throw new InvalidOperationException(
                "AssetManager has no renderer; call AttachRenderer on the render thread first.");
    }

    // The cooked .simage beside the authored file if a source has one.
    private string ResolveImagePath(string key) => ImageContentPath.Resolve(Content, key);

    private bool ImageExists(string key) => Content.Exists(ResolveImagePath(key));

    // Any thread.
    private ImageSource ReadImageThroughContent(string key)
    {
        string resolved = ResolveImagePath(key);
        ContentBlob blob = OpenOrThrow(resolved);

        if (!ImageContentPath.IsCooked(resolved))
        {
            using (blob) return new ImageSource(ImageDecoder.Decode(blob.Span, key), null, null);
        }

        try
        {
            // The result keeps the blob: its mips are offsets into these bytes.
            SimageInfo cooked = SimageReader.Read(blob.Span, resolved);
            return new ImageSource(null, blob, cooked);
        }
        catch
        {
            blob.Dispose();
            throw;
        }
    }

    private Texture CreateTextureFrom(
        Renderer renderer,
        string key,
        in ImageSource image,
        TextureColorSpace colorSpace,
        TextureFilter filter,
        TextureWrap wrap)
    {
        WarnIfSrgbUnavailable(key, image.Format, colorSpace);

        using var upload = renderer.BeginTextureUpload(ImageDescriptor(image, colorSpace, filter, wrap));
        while (!upload.IsComplete) upload.Step(image.Payload);
        Texture texture = upload.Complete();
        renderer.FlushUploads(waitForCompletion: true);
        return texture;
    }
    private MaterialDefinition ParseMaterialThroughContent(string key)
    {
        using ContentBlob blob = OpenOrThrow(key);
        return MaterialParser.ParseUtf8(blob.Span, key);
    }

    private ContentBlob OpenOrThrow(string key)
    {
        if (!Content.TryOpen(key, out ContentBlob? blob))
            throw new FileNotFoundException($"Content '{key}' is not available from any mounted source.", key);

        return blob;
    }

    // For the hot-reload path. RequestTexture claims its slot inside its own lock.
    private void BeginDecode(TextureAsset asset)
    {
        lock (_sync) asset.PendingDecodes++;
    }

    private void EndDecode(TextureAsset asset)
    {
        lock (_sync)
        {
            if (asset.PendingDecodes > 0) asset.PendingDecodes--;
        }
    }

    // Failures are queued too, so the pump logs them on the render thread.
    private void QueueDecode(TextureAsset asset)
    {
        long sequence = asset.NextRequestSequence();
        _uploadPipeline!.Queue(asset, () => TextureRequestStale(asset, sequence), () =>
        {
            try { return new TextureJob(this, new(asset, sequence, PrepareImage(asset, ReadImageThroughContent(asset.RelativePath)), null)); }
            catch (Exception ex) { return new TextureJob(this, new(asset, sequence, default, ex.Message)); }
        }, () => EndDecode(asset));
    }
    private bool ApplyUpload(in UploadRequest request, Texture? uploaded = null)
    {
        TextureAsset asset = request.Asset;

        // The handle left the cache while the decode ran. A texture created
        // for it now would never be destroyed.
        if (!IsCachedVariant(asset))
        {
            _logger.LogDebug(
                "Dropping the decode of {Path}: its handle was unloaded before the upload landed",
                asset.RelativePath);
            return false;
        }

        // Stale: a newer decode already landed.
        if (request.Sequence <= asset.AppliedSequence)
            return false;

        if (!request.Image.HasContent)
        {
            _logger.LogError("Texture load failed ({Path}): {Error}", asset.RelativePath, request.Error);
            // Keep what is bound and mark the handle retryable; it stays cached.
            asset.AppliedSequence = request.Sequence;
            lock (_sync) asset.LoadFailed = true;
            return false;
        }

        ImageSource image = request.Image;
        Texture created;
        try
        {
            created = uploaded ?? CreateTextureFrom(
                _renderer!, asset.RelativePath, in image, asset.ColorSpace, asset.Filter, asset.Wrap);
        }
        catch (Exception ex)
        {
            // A GPU failure must not take the render loop down mid-drain.
            _logger.LogError(ex, "Creating GPU texture for {Path} failed", asset.RelativePath);
            return false;
        }

        Texture previous = asset.Texture;
        asset.Texture = created;
        asset.IsPlaceholder = false;
        asset.AppliedSequence = request.Sequence;
        asset.Version++;
        lock (_sync) asset.LoadFailed = false;

        DestroyOwned(previous);
        EnsureWatching(asset.RelativePath);

        _logger.LogInformation(
            "Texture {Verb} {Path} ({Description})",
            asset.Version > 1 ? "reloaded" : "ready",
            asset.RelativePath, image.Describe());
        return true;
    }

    // Skips the placeholder and the white texel: they are shared and only go at teardown.
    private bool DestroyOwned(Texture? texture)
    {
        if (texture is null ||
            ReferenceEquals(texture, _placeholder) ||
            ReferenceEquals(texture, _white))
        {
            return false;
        }

        _renderer?.DestroyTexture(texture);
        return true;
    }

    // One save often fires several notifications, so coalesce first.
    private void DispatchFileChanges()
    {
        if (!_changedFiles.TryDequeue(out string? first)) return;

        HashSet<string> seen = _reloadScratch ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        seen.Clear();
        seen.Add(first);
        while (_changedFiles.TryDequeue(out string? path))
            seen.Add(path);

        foreach (string path in seen)
        {
            // Every variant of the file is its own GPU texture and needs a re-decode.
            // Collected under the lock, queued outside it.
            _reloadTargets ??= [];
            _reloadTargets.Clear();
            lock (_sync)
            {
                foreach (List<TextureAsset> variants in _textures.Values)
                {
                    for (int i = 0; i < variants.Count; i++)
                    {
                        if (IsSourceOf(variants[i], path)) _reloadTargets.Add(variants[i]);
                    }
                }
            }

            for (int i = 0; i < _reloadTargets.Count; i++)
            {
                TextureAsset match = _reloadTargets[i];
                _logger.LogDebug("Texture changed on disk, re-decoding: {Path}", match.RelativePath);
                BeginDecode(match);
                QueueDecode(match);
            }
        }
    }

    // Both the authored file and the .simage beside it count: the read picks
    // between them every time.
    private static bool IsSourceOf(TextureAsset asset, string fullPath) =>
        string.Equals(asset.SourcePath, fullPath, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            Path.ChangeExtension(asset.SourcePath, SimageFormat.FileExtension),
            fullPath,
            StringComparison.OrdinalIgnoreCase);

    // A source with no file on disk (a pack) has no watch path and is not watched.
    private void EnsureWatching(string relativePath)
    {
        if (!HotReloadEnabled || _graphicsReleased) return;
        // The resolved path, so a folder holding only .simage files is still watched.
        if (!Content.TryGetWatchPath(ResolveImagePath(relativePath), out string? watchPath)) return;

        string? directory = Path.GetDirectoryName(watchPath);
        if (directory is null || !Directory.Exists(directory)) return;
        if (_watchers.ContainsKey(directory)) return;

        FileSystemWatcher watcher;
        try
        {
            watcher = new FileSystemWatcher(directory)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Not watching {Directory} for texture changes: {Message}", directory, ex.Message);
            return;
        }

        // Thread-pool thread: enqueue only.
        watcher.Changed += (_, e) => NotifyFileChanged(e.FullPath);
        watcher.Created += (_, e) => NotifyFileChanged(e.FullPath);
        watcher.Renamed += (_, e) => NotifyFileChanged(e.FullPath);

        _watchers[directory] = watcher;
        _logger.LogDebug("Watching texture directory: {Directory}", directory);
    }

    private void StopWatchingIfUnused(string relativePath)
    {
        if (!Content.TryGetWatchPath(ResolveImagePath(relativePath), out string? watchPath)) return;

        string? directory = Path.GetDirectoryName(watchPath);
        if (directory is null || !_watchers.TryGetValue(directory, out FileSystemWatcher? watcher)) return;

        lock (_sync)
        {
            foreach (List<TextureAsset> variants in _textures.Values)
            {
                for (int i = 0; i < variants.Count; i++)
                {
                    if (string.Equals(
                            Path.GetDirectoryName(variants[i].SourcePath),
                            directory,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }
        }

        watcher.Dispose();
        _watchers.Remove(directory);
    }

    // Caller holds _sync.
    private TextureAsset? FindVariant(
        string key, TextureFilter filter, TextureWrap wrap, TextureColorSpace colorSpace)
    {
        if (!_textures.TryGetValue(key, out List<TextureAsset>? variants)) return null;

        for (int i = 0; i < variants.Count; i++)
        {
            TextureAsset candidate = variants[i];
            if (candidate.Filter == filter && candidate.Wrap == wrap && candidate.ColorSpace == colorSpace)
                return candidate;
        }
        return null;
    }

    // Caller holds _sync.
    private void AddVariant(string key, TextureAsset asset)
    {
        if (!_textures.TryGetValue(key, out List<TextureAsset>? variants))
        {
            variants = new List<TextureAsset>(1);
            _textures[key] = variants;
        }
        variants.Add(asset);
    }

    // False once the handle was unloaded or replaced. Takes _sync itself.
    private bool IsCachedVariant(TextureAsset asset)
    {
        lock (_sync)
            return ReferenceEquals(
                FindVariant(asset.RelativePath, asset.Filter, asset.Wrap, asset.ColorSpace), asset);
    }

    // 8x8 magenta/black checker. Nearest, so it does not blur into flat pink.
    private static Texture CreatePlaceholder(Renderer renderer)
    {
        const int size = 8;
        var pixels = new byte[size * size * 3];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool magenta = ((x / 2) + (y / 2)) % 2 == 0;
                int i = (y * size + x) * 3;
                pixels[i + 0] = magenta ? (byte)255 : (byte)0;
                pixels[i + 1] = 0;
                pixels[i + 2] = magenta ? (byte)255 : (byte)0;
            }
        }

        // sRGB, because it stands in for colour textures.
        return renderer.CreateTexture(
            pixels, size, size, TextureFormat.Rgb8, TextureColorSpace.Srgb,
            TextureFilter.Nearest, TextureWrap.Repeat);
    }

    private static Texture CreateWhiteTexel(Renderer renderer)
    {
        byte[] pixels = [255, 255, 255];
        return renderer.CreateTexture(
            pixels, 1, 1, TextureFormat.Rgb8, TextureColorSpace.Srgb,
            TextureFilter.Nearest, TextureWrap.Repeat);
    }

    // The backends fall back to linear without a word; only this layer knows the path.
    private void WarnIfSrgbUnavailable(string key, TextureFormat format, TextureColorSpace requested)
    {
        if (requested != TextureColorSpace.Srgb || TextureFormatInfo.SupportsSrgb(format))
            return;

        _logger.LogWarning(
            "Texture {Path} decoded as {Format}, which has no sRGB form; loading it as data. " +
            "Mark the slot 'data' in the material to silence this.",
            key, format);
    }

    private readonly record struct UploadRequest(
        TextureAsset Asset, long Sequence, ImageSource Image, string? Error);

    // One image read, decoded or cooked. A cooked payload is a span into a
    // mapped pack view, and this value crosses the upload queue, so the blob
    // that keeps the view alive travels with it.
    private readonly record struct ImageSource(DecodedImage? Decoded, ContentBlob? Blob, SimageInfo? Cooked, PreparedTextureData? Prepared = null)
    {
        // False for a failure value.
        public bool HasContent => Decoded is not null || Cooked is not null || Prepared is not null;

        public ReadOnlySpan<byte> Payload => Prepared is { } prepared ? prepared.Payload
            : Decoded is { } decoded ? decoded.Pixels : Blob is { } blob ? blob.Span : [];

        public TextureFormat Format => Prepared?.Format ?? Decoded?.Format ?? Cooked!.Format;

        public string Describe() => Prepared is { } prepared
            ? $"{prepared.Mips[0].Width}x{prepared.Mips[0].Height}, {prepared.Mips.Length} prepared mips, {prepared.Format}"
            : Decoded is { } decoded
            ? $"{decoded.Width}x{decoded.Height}, {decoded.Channels}ch, {decoded.Format}"
            : $"{Cooked!.Width}x{Cooked.Height}, {Cooked.MipCount} mips, {Cooked.Format}, cooked";

        public void Dispose() => Blob?.Dispose();
    }
}
