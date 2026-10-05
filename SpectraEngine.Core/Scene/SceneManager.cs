using Microsoft.Extensions.Logging;
using Silk.NET.Maths;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Projects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;

namespace SpectraEngine.Core.Scene;

public sealed class SceneManager
{
    private readonly ILogger<SceneManager> _logger;

    // The demo pillar bobs as a world brush to keep the async recompile busy.
    private const float PillarBobAmplitude = 0.5f;
    private const double PillarBobPeriodSeconds = 4.0;

    private const double CompileLogIntervalSeconds = 5.0;

    private const double ScreenProbeIntervalSeconds = 5.0;

    // Scattered boxes around the authored structures, one per grid site and
    // jittered so none touch. They give culling chunks it cannot see and keep
    // the pillar's recompiles to a few dirty cells of a large world.
    private const int PartGridSites = 14;      // 14x14 sites, 4 skipped at the center

    /// <summary>
    /// Overrides the scatter grid's side length, for measuring how cost scales
    /// with content. Null keeps the demo's own <c>14</c>. The area grows with
    /// it, so spacing stays the same.
    /// </summary>
    public static int? ScatterGridOverride { get; set; }
    private const float PartAreaSize = 200f;   // world units per side

    /// <summary>
    /// How many props to scatter, all sharing one <see cref="Bsp.Brush"/>, for
    /// measuring what repeated content costs. Null or zero places none.
    /// </summary>
    public static int? PropCountOverride { get; set; }

    /// <summary>
    /// A <c>.smap</c> bundle to run instead of the authored demo scene, or null
    /// for the demo. A bundle that fails to load logs and leaves the demo up.
    /// </summary>
    public static string? LoadMapPathOverride { get; set; }

    /// <summary>
    /// A baked <c>.scmap</c> to run instead of the demo scene, or null. A
    /// content path resolved through the mounted sources, not a file path.
    /// Wins over <see cref="LoadMapPathOverride"/>.
    /// </summary>
    public static string? CompiledMapPathOverride { get; set; }

    /// <summary>
    /// A <c>.smap</c> bundle to write the finished scene into, or null to write
    /// nothing.
    /// </summary>
    public static string? SaveMapPathOverride { get; set; }

    /// <summary>
    /// A folder to export the finished scene into as a standalone project, or
    /// null to export nothing.
    /// </summary>
    public static string? SaveProjectPathOverride { get; set; }

    private const float PropHalfExtent = 0.4f;   // a crate, near enough
    private const float PropSpacing = 3f;        // world units between grid sites
    private const float PropStackHeight = 6f;    // vertical jitter, so it reads as a cloud

    // Content-root-relative paths.
    private const string GridMaterialPath = "Materials/dev_grid.spectramat";
    private const string FloorMaterialPath = "Materials/floor.spectramat";
    private const string WallMaterialPath = "Materials/wall.spectramat";
    private const string PillarMaterialPath = "Materials/checker_orange.spectramat";
    private const string AccentMaterialPath = "Materials/checker_gray.spectramat";
    private const string OrbiterTexturePath = "Textures/gradient_mask.png";

    // PBR reference row: smooth metal, rough metal, smooth dielectric, rough
    // dielectric, emissive.
    private static readonly string[] PbrMaterialPaths =
    [
        "Materials/pbr_gold.spectramat",
        "Materials/pbr_copper_brushed.spectramat",
        "Materials/pbr_plastic_red.spectramat",
        "Materials/pbr_rubber.spectramat",
        "Materials/pbr_emissive.spectramat",
    ];
    private const string CrateModelPath = "Models/crate.obj";
    private const string SignpostModelPath = "Models/signpost.gltf";

    // Brush.CreateBox plane order: +X, -X, +Y, -Y, +Z, -Z.
    private const int BoxFacePlusZ = 4;

    // The model fixtures are authored at 32 units per world unit.
    private const float ModelUnitsPerWorldUnit = 32f;

    // No FlipTextureV for glTF: Assimp already converts the UV origin, so
    // asking again mirrors the texture.
    private static readonly ModelImportOptions GltfImportOptions = ModelImportOptions.Default;

    // The two floor corners with no pillar, clear of the orbiting cube.
    private static readonly Vector3 CratePosition = new(2.3f, -1f, -2.3f);
    private static readonly Vector3 SignpostPosition = new(-2.3f, -1f, 2.3f);

    private static readonly Vector3 PillarHalfExtent = new(0.2f, 1.1f, 0.2f);

    private SceneNode? _spinner;
    private DemoBobAnimation? _pillarBob;
    /// <summary>Opt-in CSG stress fixture. Off leaves the built-in scene at rest.</summary>
    public bool DemoCsgAnimation { get; set; }

    /// <summary>
    /// Whether the built-in scene's spinning cube turns. Off for a check that
    /// compares pictures taken frames apart.
    /// </summary>
    public bool DemoSpin { get; set; } = true;
    private double _elapsed;

    // Both periodic lines wait one interval: the render view is built after
    // the first update, so a line at t=0 would report an empty view.
    private double _nextCompileLogTime = CompileLogIntervalSeconds;

    private AssetManager? _assets;

    // Non-null from the async request until the import lands or fails.
    private ModelAsset? _pendingModel;
    private int _modelsRequested;
    private int _modelsPlaced;

    private double _nextScreenProbeTime = ScreenProbeIntervalSeconds;

    private Renderer? _renderer;

    public SceneManager(ILogger<SceneManager> logger)
    {
        _logger = logger;
    }

    /// <summary>Smoothed frame time in milliseconds, published by the engine loop.</summary>
    public double FrameTimeMs { get; set; }

    /// <summary>Smoothed frames per second, published by the engine loop.</summary>
    public double Fps { get; set; }

    /// <summary>The scene currently being simulated and rendered, if one is loaded.</summary>
    public Scene? ActiveScene { get; private set; }

    /// <summary>
    /// Builds the editing layer for a freshly loaded scene, or null to run
    /// without one. The host sets this before <see cref="Engine.Run"/>; it is
    /// invoked once on the render thread after the scene is complete.
    /// </summary>
    public Func<Scene, ISceneEditor>? EditorFactory { get; set; }

    /// <summary>
    /// Builds the physics backend for a freshly loaded scene. Null gives
    /// <see cref="Physics.NullScenePhysics"/>, so Core needs no native library.
    /// </summary>
    public Func<Scene, IScenePhysics>? PhysicsFactory { get; set; }

    /// <summary>The physics backend this run installed. Never null.</summary>
    public IScenePhysics Physics { get; private set; } = NullScenePhysics.Instance;

    /// <summary>
    /// The editing layer this run installed, or null when the host supplied no
    /// <see cref="EditorFactory"/>.
    /// </summary>
    public ISceneEditor? Editor { get; private set; }

    /// <summary>
    /// The brush node a host-side editing self-test manipulates, or null
    /// before the demo scene is loaded.
    /// </summary>
    // Must sit in chunk cells the bobbing pillar never dirties, so a recompile
    // of its cell can only come from the test's drag.
    public SceneNode? SelfTestNode { get; private set; }

    /// <summary>Where a first-person character starts, once the demo scene is loaded.</summary>
    public Vector3 PlayerSpawn { get; private set; }

    /// <summary>The yaw a spawned character faces, in radians.</summary>
    public float PlayerSpawnYaw { get; private set; }

    /// <summary>Below this height a character has left the authored world and should be respawned.</summary>
    public float PlayerFallOutHeight { get; private set; } = float.NegativeInfinity;

    /// <summary>
    /// The first-person character the engine installed, or null when nothing
    /// walks. Set by the engine; read here for the stats line.
    /// </summary>
    public Physics.Character.FirstPersonController? Character { get; set; }

    /// <summary>
    /// The classes <see cref="StartEntityWorld"/> can build, or null for the
    /// process-wide <see cref="EntityCatalog.Shared"/>.
    /// </summary>
    public EntityCatalog? EntityCatalog { get; set; }

    /// <summary>
    /// What every loaded scene's <see cref="Scene.EntitySchemas"/> is stamped
    /// with. Null exports the running catalogue to <c>.sentdef</c> bytes and
    /// reads it back, so the round trip runs on every load.
    /// </summary>
    public EntitySchemaCatalog? EntitySchemas { get; set; }

    /// <summary>
    /// The live entity runtime, or null when nothing is playing. Exists only
    /// during play mode. Render thread only.
    /// </summary>
    public EntityWorld? EntityWorld { get; private set; }

    private IEntityTrace? _entityTrace;

    /// <summary>
    /// What watches the entity runtime, or null for nothing. Kept between
    /// play sessions: each new world has it before it activates, and a
    /// running one gets a change at once. Render thread only.
    /// </summary>
    public IEntityTrace? EntityTrace
    {
        get => _entityTrace;
        set
        {
            _entityTrace = value;
            if (EntityWorld is { } world)
                world.Trace = value;
        }
    }

    // Not cached: a host may assign EntityCatalog between loads. Reading
    // Schemas freezes the catalogue.
    private EntitySchemaCatalog ResolveEntitySchemas() =>
        EntitySchemas ?? EntitySchemaCatalog.LoadFromSentDef(
            SentDef.Write((EntityCatalog ?? Entities.EntityCatalog.Shared).Schemas));

    /// <summary>
    /// Builds the entity runtime over the active scene and activates it. A
    /// no-op when one is already running or no scene is loaded.
    /// </summary>
    public void StartEntityWorld()
    {
        if (EntityWorld is not null || ActiveScene is not { } scene)
            return;

        // Before Activate, or the outputs fired while spawning go unseen and
        // their deliveries on the first tick seem to come from nowhere.
        var world = new EntityWorld(scene, _logger, EntityCatalog) { Trace = _entityTrace };
        world.Activate();
        EntityWorld = world;

        _logger.LogDebug(
            "Entity runtime active: {Entities} entity(ies), {Names} name(s)",
            world.Entities.Count, world.Index?.NameCount ?? 0);
    }

    /// <summary>Tears the entity runtime down. Harmless when none is running.</summary>
    public void StopEntityWorld()
    {
        EntityWorld?.Deactivate();
        EntityWorld = null;
    }

    /// <summary>
    /// Tears the entity runtime down because the graph under it is about to be
    /// replaced. Play mode itself keeps running.
    /// </summary>
    // A map load keeps node ids, so a surviving world would rebind its stale
    // entities onto the new nodes.
    public void OnSceneReplaced()
    {
        if (EntityWorld is null)
            return;

        StopEntityWorld();
        _logger.LogInformation(
            "The scene was replaced while the entity runtime was live, so it was torn down. " +
            "Re-enter play mode to run the new scene's entities.");
    }

    public void Initialize()
    {
        _logger.LogInformation("Scene manager initialized");
    }

    /// <summary>Which scene <see cref="LoadStartupScene"/> builds.</summary>
    // Per instance, not static: the shell makes a session per project.
    public StartupSceneKind Startup { get; set; } = StartupSceneKind.Demo;

    /// <summary>
    /// Builds the startup scene <see cref="Startup"/> names. Render thread,
    /// once the renderer and the asset manager are up.
    /// </summary>
    public void LoadStartupScene(Renderer renderer, AssetManager assets)
    {
        if (Startup == StartupSceneKind.Baseplate)
            LoadBaseplateScene(renderer, assets);
        else
            LoadDemoScene(renderer, assets);
    }

    /// <summary>
    /// Builds the editor's blank scene: a sun and a ground plate. Render
    /// thread only.
    /// </summary>
    public void LoadBaseplateScene(Renderer renderer, AssetManager assets)
    {
        _renderer = renderer;
        _assets = assets;

        var scene = new Scene("Untitled");
        scene.Assets = assets;
        scene.EntitySchemas = ResolveEntitySchemas();
        scene.Camera.Position = new Vector3(9f, 6f, 11f);
        scene.Camera.LookAt(Vector3.Zero);

        PopulateBaseplate(scene);
        scene.RebuildStaticWorld(renderer);

        PlayerSpawn = new Vector3(0f, 1f, 0f);
        PlayerSpawnYaw = 0f;
        PlayerFallOutHeight = -50f;

        ActiveScene = scene;

        // Editor and physics adopt the scene only once it is complete.
        Editor = EditorFactory?.Invoke(scene);
        Physics = PhysicsFactory?.Invoke(scene) ?? NullScenePhysics.Instance;

        _logger.LogInformation(
            "Baseplate scene loaded: {Nodes} node(s), content root {Root}",
            scene.Root.Children.Count, assets.ContentRootPath);
    }

    /// <summary>
    /// Adds the baseplate starter content to a scene: a directional sun and a
    /// 64x64 ground plate whose top face sits at y = 0. Also what a new map
    /// starts as.
    /// </summary>
    // The plate names no material: a new project's content root is empty, so
    // it draws with the neutral grey instead of warning about a missing file.
    public static void PopulateBaseplate(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        SceneNode sun = scene.Root.CreateChild("Sun");
        sun.LocalRotation = Light.RotationForDirection(new Vector3(-0.35f, -0.85f, -0.4f));
        sun.Light = new Light
        {
            Kind = LightKind.Directional,
            Color = ColorSpace.SrgbToLinear(new Vector3(1f, 0.96f, 0.88f)),
            Intensity = 11f,
        };

        // Centred extents, offset on the node: the compile places a brush by
        // the node's matrix and ignores the brush's own translation.
        SceneNode plate = scene.Root.CreateChild("Baseplate");
        plate.LocalPosition = new Vector3(0f, -0.5f, 0f);
        plate.Brush = Bsp.Brush.CreateBox(new Vector3(-32f, -0.5f, -32f), new Vector3(32f, 0.5f, 32f));
    }

    /// <summary>
    /// Builds the demo scene, the engine's end-to-end smoke test: meshes, a
    /// brush-built room, scattered parts and two imported props. Render thread
    /// only; needs an initialized renderer and asset manager.
    /// </summary>
    public void LoadDemoScene(Renderer renderer, AssetManager assets)
    {
        _renderer = renderer;
        _assets = assets;

        var loadClock = Stopwatch.StartNew();

        var scene = new Scene("Demo");
        scene.Camera.Position = new Vector3(0f, 1.5f, 5f);
        scene.Camera.LookAt(Vector3.Zero);
        // Before the first compile: the swap resolves face materials through it.
        scene.Assets = assets;
        scene.EntitySchemas = ResolveEntitySchemas();

        var (vertices, indices) = Primitives.Cube();
        var cubeMesh = renderer.CreateMesh(vertices, indices, VertexAttribute.StandardLayout);
        var shader = renderer.DefaultShader
            ?? throw new InvalidOperationException("Renderer has no default shader; initialize it first.");

        var assetClock = Stopwatch.StartNew();

        Material worldMaterial = assets.LoadMaterial(GridMaterialPath);
        Material cubeMaterial = assets.LoadMaterial(PillarMaterialPath);

        // Loaded before the compile, or the swap frame would read them from
        // disk synchronously.
        assets.LoadMaterial(FloorMaterialPath);
        assets.LoadMaterial(WallMaterialPath);
        assets.LoadMaterial(AccentMaterialPath);

        // Async, and a texture no material references, so every run exercises
        // the placeholder swap and the upload pump. Linear: it is a
        // one-channel gradient and R8 has no sRGB form.
        TextureAsset orbiterTexture = assets.RequestTexture(
            OrbiterTexturePath, colorSpace: TextureColorSpace.Linear);

        double surfaceMs = assetClock.Elapsed.TotalMilliseconds;

        // One prop per model path: the crate loads synchronously, the signpost
        // is imported in the background and placed by Update.
        ModelAsset? crate = LoadProp(assets, CrateModelPath, ModelImportOptions.Default);
        _pendingModel = RequestProp(assets, SignpostModelPath, GltfImportOptions);

        assetClock.Stop();
        double modelMs = assetClock.Elapsed.TotalMilliseconds - surfaceMs;

        var center = scene.Root.CreateChild("SpinningCube");
        center.MeshRenderer = new MeshRenderer(cubeMesh, cubeMaterial);
        _spinner = center;

        var orbiter = center.CreateChild("Orbiter");
        orbiter.LocalTransform = new Transform
        {
            Position = new Vector3(2f, 0f, 0f),
            Rotation = Quaternion.Identity,
            Scale = new Vector3(0.4f, 0.4f, 0.4f),
        };
        orbiter.MeshRenderer = new MeshRenderer(cubeMesh,
            new Material(shader)
                .SetVector3("uBaseColor", new Vector3(0.3f, 0.6f, 1f))
                .SetTexture("uDiffuse", 0, orbiterTexture));

        // Before the world compile: a node added after it bumps the structure
        // version and costs the first background recompile its incremental path.
        if (crate is not null)
            PlaceProp(scene, crate, "Crate", CratePosition);

        AddPbrSpheres(scene, renderer, assets);

        double worldMs = BuildStaticWorld(scene, renderer, worldMaterial);

        // Load before save, so naming both paths copies a bundle through the
        // engine's own reader and writer.
        if (CompiledMapPathOverride is { } compiledPath
            && LoadCompiledMapInto(scene, renderer, assets, compiledPath, out double compiledMs))
        {
            worldMs += compiledMs;
        }
        else if (LoadMapPathOverride is { } loadPath)
        {
            worldMs += LoadMapInto(scene, renderer, loadPath);
        }

        if (SaveMapPathOverride is { } savePath)
            SaveMapFrom(scene, savePath);

        if (SaveProjectPathOverride is { } projectPath)
            SaveProjectFrom(scene, assets, projectPath);

        ActiveScene = scene;

        // Last: the editor reads the camera and selection as soon as it is built.
        Editor = EditorFactory?.Invoke(scene);

        Physics = PhysicsFactory?.Invoke(scene) ?? NullScenePhysics.Instance;
        if (Physics.IsSimulating)
            _logger.LogInformation("Physics backend installed: {Backend}", Physics.GetType().Name);

        loadClock.Stop();

        _logger.LogInformation(
            "Demo scene '{Name}' loaded in {TotalMs:0.0} ms " +
            "(assets {AssetMs:0.0} ms = {SurfaceMs:0.0} ms materials/textures + {ModelMs:0.0} ms models, " +
            "static world {WorldMs:0.0} ms); content root {Root}; " +
            "{Materials} material(s), {Textures} texture(s), {Models} model(s) requested ({Placed} placed so far)",
            scene.Name, loadClock.Elapsed.TotalMilliseconds, assetClock.Elapsed.TotalMilliseconds,
            surfaceMs, modelMs, worldMs,
            assets.ContentRootPath, assets.MaterialCount, assets.TextureCount, _modelsRequested, _modelsPlaced);
    }

    // Replaces the graph and static world with a baked map from the mounted
    // content. False when there is no compiled map at that path or it does not
    // load; the caller then falls back to the authored bundle. The carve count
    // is logged on every load: it must be zero.
    private bool LoadCompiledMapInto(
        Scene scene, Renderer renderer, AssetManager assets, string contentPath, out double milliseconds)
    {
        var clock = Stopwatch.StartNew();
        milliseconds = 0;

        if (!assets.Content.TryOpen(contentPath, out Assets.Sources.ContentBlob? file))
        {
            _logger.LogError(
                "No compiled map at '{Path}' in the mounted content. Run 'scook cook <project>' to bake the " +
                "project's maps; falling back to the authored bundle, which is NOT what a shipped build would " +
                "load", contentPath);

            return false;
        }

        long carvesBefore = Csg.CarveInvocationsOnThisThread;

        try
        {
            // The loader takes the blob and keeps it: the world's BSP nodes
            // are a view into these bytes, mapped on a pack. Do not dispose.
            CompiledMapLoadReport report = CompiledMapLoader.Load(scene, renderer, file, contentPath);

            // An adopted world never compiles, so the bob and the self-test
            // have nothing to do.
            _pillarBob = null;
            SelfTestNode = null;

            clock.Stop();
            milliseconds = clock.Elapsed.TotalMilliseconds;

            _logger.LogInformation(
                "Compiled map '{Path}' loaded in {Ms:0.0} ms: scene '{Name}', {Nodes} node(s), {Chunks} " +
                "chunk(s) as {Submeshes} GPU mesh(es) and {Triangles} triangle(s), {Trees} BSP tree(s), " +
                "{Materials} material(s) interned, {Skipped} unknown section(s) skipped; "
                + "{Carves} carve(s) run",
                contentPath, milliseconds, scene.Name, report.NodesLoaded, report.ChunksLoaded,
                report.SubmeshesUploaded, report.TriangleCount, report.BspChunksLoaded,
                report.MaterialsInterned, report.SkippedSections,
                Csg.CarveInvocationsOnThisThread - carvesBefore);

            if (report.BakedBrushSourcesSkipped > 0)
            {
                _logger.LogInformation(
                    "Static world guard: {Count} baked brush(es) offered authored planes and were not " +
                    "re-carved. Carving them would draw those walls twice", report.BakedBrushSourcesSkipped);
            }

            if (report.Describe() is { } lost)
                _logger.LogWarning("Compiled map load is incomplete. {What}", lost);

            _logger.LogWarning("Compiled map limits. {Gaps}", CompiledMapLoadReport.DescribeFormatGaps());

            return true;
        }
        catch (ScmapFormatException ex)
        {
            clock.Stop();
            _logger.LogError(ex,
                "Could not load compiled map '{Path}'; falling back to the authored bundle", contentPath);

            return false;
        }
    }

    private double LoadMapInto(Scene scene, Renderer renderer, string bundlePath)
    {
        var clock = Stopwatch.StartNew();

        // Both are live node references that the replaced graph detaches, so
        // rebind them afterwards. By name, not id: the demo is built in code
        // and mints new ids every run.
        string? bobName = _pillarBob?.Node.Name;
        string? selfTestName = SelfTestNode?.Name;

        try
        {
            var report = new MapLoadReport();
            MapSceneBinder.ApplyTo(MapBundle.Load(bundlePath), scene, report);

            scene.RebuildStaticWorld(renderer);

            SceneNode? bobNode = FindDemoNode(scene, bobName);
            _pillarBob = bobNode is null
                ? null
                : new DemoBobAnimation(bobNode, PillarBobAmplitude, PillarBobPeriodSeconds);
            SelfTestNode = FindDemoNode(scene, selfTestName);

            clock.Stop();

            _logger.LogInformation(
                "Loaded map bundle '{Path}' in {Ms:0.0} ms: scene '{Name}', {Nodes} root node(s); "
                + "demo animation {Bob}, self-test node {Test}",
                bundlePath, clock.Elapsed.TotalMilliseconds, scene.Name, scene.Root.Children.Count,
                _pillarBob is null ? "dropped" : "rebound", SelfTestNode is null ? "dropped" : "rebound");

            if (report.Describe() is { } missing)
                _logger.LogWarning("Map load is incomplete. {What}", missing);
        }
        catch (Exception ex) when (ex is MapFormatException or IOException or UnauthorizedAccessException)
        {
            clock.Stop();
            _logger.LogError(ex,
                "Could not load map bundle '{Path}'; running the authored demo scene instead", bundlePath);
        }

        return clock.Elapsed.TotalMilliseconds;
    }

    // Root children only: that is where the demo's two nodes live.
    private static SceneNode? FindDemoNode(Scene scene, string? name)
    {
        if (name is null) return null;

        foreach (SceneNode child in scene.Root.Children)
        {
            if (string.Equals(child.Name, name, StringComparison.Ordinal))
                return child;
        }
        return null;
    }

    // Exports the scene as a standalone project folder. Copies the whole
    // content root: working out what a map needs is the cook's dependency walk.
    private void SaveProjectFrom(Scene scene, AssetManager assets, string projectPath)
    {
        try
        {
            string name = SanitiseProjectName(scene.Name);
            ProjectLayout project = ProjectLayout.Create(projectPath, name);

            int copied = CopyTree(assets.ContentRootPath, project.AssetsPath);

            string mapRelative = $"{ProjectFormat.MapsFolder}/{name}{MapFormat.BundleExtension}";
            var report = new MapSaveReport();
            MapBundle.Save(project.Resolve(mapRelative), MapSceneBinder.FromScene(scene, report));

            if (!project.Project.Maps.Contains(mapRelative))
                project.Project.Maps.Add(mapRelative);
            project.Project.StartupMap = mapRelative;
            project.Save();

            _logger.LogInformation(
                "Exported project '{Name}' to {Path}: {Map}, {Files} content file(s) copied",
                name, project.Root, mapRelative, copied);

            if (report.Describe() is { } lost)
                _logger.LogWarning("Project export is incomplete. {What}", lost);
        }
        catch (Exception ex) when (ex is MapFormatException or ProjectFormatException
                                      or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not export a project to '{Path}'", projectPath);
        }
    }

    private static string SanitiseProjectName(string sceneName)
    {
        Span<char> buffer = stackalloc char[sceneName.Length];
        int length = 0;
        foreach (char c in sceneName)
        {
            if (char.IsLetterOrDigit(c) || c is '_' or '-')
                buffer[length++] = c;
        }
        return length == 0 ? "Project" : new string(buffer[..length]);
    }

    private static int CopyTree(string from, string to)
    {
        if (!Directory.Exists(from)) return 0;

        int copied = 0;
        foreach (string source in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(from, source);
            string destination = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            copied++;
        }
        return copied;
    }

    private void SaveMapFrom(Scene scene, string bundlePath)
    {
        try
        {
            var report = new MapSaveReport();
            bool wrote = MapBundle.Save(bundlePath, MapSceneBinder.FromScene(scene, report));

            _logger.LogInformation("Saved map bundle '{Path}' ({State})",
                bundlePath, wrote ? "written" : "unchanged, byte for byte");

            if (report.Describe() is { } lost)
                _logger.LogWarning("Map save is incomplete. {What}", lost);
        }
        catch (Exception ex) when (ex is MapFormatException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not save map bundle '{Path}'", bundlePath);
        }
    }

    // Synchronous. LoadModel throws on failure; the demo runs without the prop.
    private ModelAsset? LoadProp(AssetManager assets, string path, ModelImportOptions options)
    {
        _modelsRequested++;
        try
        {
            return assets.LoadModel(path, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Demo prop {Path} failed to load; the demo runs without it", path);
            return null;
        }
    }

    // Async. Failure is reported on the handle.
    private ModelAsset? RequestProp(AssetManager assets, string path, ModelImportOptions options)
    {
        _modelsRequested++;
        return assets.RequestModel(path, options);
    }

    // A holder node carries placement and unit scale, so the model root's own
    // transform is kept.
    private void PlaceProp(Scene scene, ModelAsset model, string name, Vector3 position)
    {
        var holder = scene.Root.CreateChild(name);
        holder.LocalTransform = new Transform
        {
            Position = position,
            Rotation = Quaternion.Identity,
            Scale = new Vector3(1f / ModelUnitsPerWorldUnit),
        };
        ModelInstantiator.InstantiateInto(holder, model);
        _modelsPlaced++;
    }

    // Authors the demo's brushes and compiles them. Returns the compile time
    // in milliseconds.
    private double BuildStaticWorld(Scene scene, Renderer renderer, Material worldMaterial)
    {
        // Fallback for faces that name no material: here, the scattered parts.
        scene.StaticWorldMaterial = worldMaterial;

        // Size lives in the brush, placement on the node. Walls and pillars
        // sit flush on the floor slab (top y = -1, rim x/z = ±3). Each
        // structure wears its own material so shared cells split per material.
        MaterialRef floorMaterial = MaterialRegistry.Intern(FloorMaterialPath);
        MaterialRef wallMaterial = MaterialRegistry.Intern(WallMaterialPath);
        MaterialRef pillarMaterial = MaterialRegistry.Intern(PillarMaterialPath);
        MaterialRef accentMaterial = MaterialRegistry.Intern(AccentMaterialPath);

        AddBrushNode(scene, "Floor", new Vector3(0f, -1.1f, 0f), new Vector3(3f, 0.1f, 3f), floorMaterial);
        AddBrushNode(scene, "WallNorth", new Vector3(0f, -0.1f, -3.1f), new Vector3(3.1f, 1f, 0.1f), wallMaterial);
        AddBrushNode(scene, "WallWest", new Vector3(-3.1f, -0.1f, 0.05f), new Vector3(0.1f, 1f, 3.05f), wallMaterial);

        // Subtractive doorway, flush through the wall: its ±z planes coincide
        // with the wall's. This is the coplanar-cut regression fixture, so do
        // not resize it.
        var doorway = scene.Root.CreateChild("DoorwayCut");
        doorway.LocalPosition = new Vector3(0f, -0.45f, -3.1f);
        doorway.Brush = Brush
            .CreateBox(new Vector3(-0.5f, -0.65f, -0.15f), new Vector3(0.5f, 0.65f, 0.15f), accentMaterial)
            .WithOperation(BrushOperation.Subtractive);

        AddDemoLights(scene);

        SceneNode bobbingPillar = AddBrushNode(
            scene, "PillarA", new Vector3(-2f, 0.1f, -2f), PillarHalfExtent, pillarMaterial);
        _pillarBob = new DemoBobAnimation(bobbingPillar, PillarBobAmplitude, PillarBobPeriodSeconds);

        // One brush, two materials: the camera-facing face wears the gray
        // checker. Also the self-test's subject, in cells PillarA never dirties.
        var pillarB = scene.Root.CreateChild("PillarB");
        pillarB.LocalPosition = new Vector3(2f, 0.1f, 2f);
        pillarB.Brush = Brush
            .CreateBox(-PillarHalfExtent, PillarHalfExtent, pillarMaterial)
            .WithFaceMaterial(BoxFacePlusZ, accentMaterial);
        SelfTestNode = pillarB;

        // Human-scale course beside the room, which is too small to walk in.
        int playAreaBrushes = DemoPlayArea.Build(scene, floorMaterial, wallMaterial, accentMaterial);
        PlayerSpawn = DemoPlayArea.Spawn;
        PlayerSpawnYaw = DemoPlayArea.SpawnYaw;
        PlayerFallOutHeight = DemoPlayArea.FallOutHeight;

        // One part brush, clear of everything else so it cannot z-fight.
        var floatingPart = scene.Root.CreateChild("FloatingPart");
        floatingPart.LocalPosition = new Vector3(0f, 2.6f, -3f);
        floatingPart.LocalRotation = Quaternion.CreateFromYawPitchRoll(0.6f, 0.3f, 0f);
        floatingPart.BrushKind = BrushKind.Part;
        floatingPart.Brush = Brush
            .CreateBox(new Vector3(-0.4f, -0.4f, -0.4f), new Vector3(0.4f, 0.4f, 0.4f), pillarMaterial)
            .WithFaceMaterial(BoxFacePlusZ, accentMaterial);

        int partCount = AddScatteredParts(scene);
        int propCount = AddSharedProps(scene, accentMaterial);
        if (propCount > 0)
        {
            // Two placeholder names for one value: a repeated name with one
            // argument drops the whole line.
            _logger.LogInformation(
                "Props: {Nodes} part-brush node(s) sharing 1 brush instance -> " +
                "1 GPU mesh and {Draws} draw(s) differing only in world matrix",
                propCount, propCount);
        }

        // Synchronous: the probes below need the compiled world now.
        var stopwatch = Stopwatch.StartNew();
        scene.RebuildStaticWorld(renderer);
        stopwatch.Stop();
        var world = scene.StaticWorld!;

        bool floorSolid = world.ContainsPoint(new Vector3(0f, -1.1f, 0f));
        bool pillarSolid = world.ContainsPoint(new Vector3(-2f, 0f, -2f));
        bool airEmpty = !world.ContainsPoint(new Vector3(0f, 3f, 0f));
        bool rayHitsFloor = world.Raycast(
            new Vector3(0f, 3f, 0f), -Vector3.UnitY, 10f, out var hit);

        // The cut must be open and must not take the lintel above it.
        bool doorwayOpen = !world.ContainsPoint(new Vector3(0f, -0.45f, -3.1f));
        bool lintelSolid = world.ContainsPoint(new Vector3(0f, 0.75f, -3.1f));

        // Same check on the play area's door, which is flush in x rather than z.
        bool playDoorOpen = !world.ContainsPoint(new Vector3(143.5f, 3.0f, 0f));
        bool playDoorJamb = world.ContainsPoint(new Vector3(143.5f, 3.0f, 2f));
        bool playFloorSolid = world.ContainsPoint(new Vector3(133f, -0.5f, 0f));
        bool playChasmOpen = !world.ContainsPoint(new Vector3(153.5f, -2.5f, 10f));

        _logger.LogInformation(
            "Static world: {Brushes} brush nodes ({Parts} scattered parts, {Play} play area) -> " +
            "{Surfaces} carved surfaces " +
            "in {Chunks} chunks wearing {Materials} distinct face material(s), compiled in {Ms:0.0} ms; " +
            "floor-solid={Floor}, pillar-solid={Pillar}, air-empty={Air}, ray-hit={Hit} at y={Y:0.000}, " +
            "doorway-open={Doorway}, lintel-solid={Lintel}; " +
            "play area: floor-solid={PlayFloor}, door-open={PlayDoor}, jamb-solid={PlayJamb}, " +
            "chasm-open={PlayChasm}",
            world.Brushes.Count, partCount, playAreaBrushes, world.Surfaces.Count, world.Chunks.Count,
            CountFaceMaterials(scene), stopwatch.Elapsed.TotalMilliseconds,
            floorSolid, pillarSolid, airEmpty, rayHitsFloor, hit.Point.Y,
            doorwayOpen, lintelSolid,
            playFloorSolid, playDoorOpen, playDoorJamb, playChasmOpen);

        return stopwatch.Elapsed.TotalMilliseconds;
    }

    // Allocates only while a character is active.
    private string DescribeCharacter()
    {
        if (Character is not { Active: true } character)
            return Character is null ? "not installed" : "idle";

        Physics.Character.CharacterState state = character.State;
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "at ({0:0.0}, {1:0.0}, {2:0.0}), {3:0.0} sunit/s, {4}, {5} respawn(s), " +
            "{6} lane rebuild(s), {7} uncovered cut brush(es), {8} dropped plane(s)",
            state.Position.X, state.Position.Y, state.Position.Z,
            character.HorizontalSpeed,
            state.Grounded ? "grounded" : "airborne",
            character.Respawns,
            character.Collision.WorldLaneRebuilds,
            character.Collision.UncoveredCutBrushes,
            character.Collision.DroppedPlanes);
    }

    // Allocates only while an entity world is running.
    private string DescribeEntities()
    {
        if (EntityWorld is not { IsActive: true } world)
            return "not running";

        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0} live, {1} name(s), {2} dispatch(es) last tick, {3} pending, " +
            "{4} budget trip(s), {5} discarded",
            world.Entities.Count,
            world.Index?.NameCount ?? 0,
            world.LastTickDispatchCount,
            world.PendingEventCount,
            world.DispatchBudgetTripCount,
            world.DiscardedEventCount);
    }

    // Distinct material ids on the uploaded chunks. The demo names five, so a
    // lower number means a face payload was lost. Load time only.
    private static int CountFaceMaterials(Scene scene)
    {
        var seen = new HashSet<int>();
        IReadOnlyList<StaticWorldChunkMesh> chunks = scene.StaticWorldChunkMeshes;
        for (int i = 0; i < chunks.Count; i++)
        {
            StaticWorldSubmesh[] submeshes = chunks[i].Submeshes;
            for (int s = 0; s < submeshes.Length; s++)
                seen.Add(submeshes[s].SourceMaterial.Id);
        }
        return seen.Count;
    }

    // One box per grid site. Fixed-seed LCG, not System.Random, so every run
    // builds the same world.
    private static int AddScatteredParts(Scene scene)
    {
        int sites = ScatterGridOverride ?? PartGridSites;
        const float spacing = PartAreaSize / PartGridSites;
        float halfArea = sites * spacing * 0.5f;
        ulong state = 0x5CA77E12EDB0B5EDUL;

        int count = 0;
        for (int gx = 0; gx < sites; gx++)
        {
            for (int gz = 0; gz < sites; gz++)
            {
                // Skip sites overlapping the authored room ([-5,5] in x/z).
                float siteMinX = -halfArea + gx * spacing;
                float siteMinZ = -halfArea + gz * spacing;
                if (siteMinX < 5f && siteMinX + spacing > -5f &&
                    siteMinZ < 5f && siteMinZ + spacing > -5f)
                    continue;

                // Half-extents in [0.4, 1.6]; the jitter keeps the box inside
                // its site, so parts never touch.
                var halfExtent = new Vector3(
                    0.4f + NextFloat01(ref state) * 1.2f,
                    0.4f + NextFloat01(ref state) * 1.2f,
                    0.4f + NextFloat01(ref state) * 1.2f);
                float cx = siteMinX + 2f + NextFloat01(ref state) * (spacing - 4f);
                float cz = siteMinZ + 2f + NextFloat01(ref state) * (spacing - 4f);
                float bottom = -1f + NextFloat01(ref state) * 0.5f;

                AddBrushNode(scene, $"Part{count}", new Vector3(cx, bottom + halfExtent.Y, cz), halfExtent);
                count++;
            }
        }

        return count;
    }

    // Part-brush nodes that all share one brush instance. Returns the number
    // placed.
    private static int AddSharedProps(Scene scene, MaterialRef material)
    {
        int count = PropCountOverride ?? 0;
        if (count <= 0)
            return 0;

        // One instance: the part mesh cache keys on reference identity, so
        // this is one GPU mesh.
        Brush shared = Brush.CreateBox(
            new Vector3(-PropHalfExtent), new Vector3(PropHalfExtent), material);

        // Fixed spacing, so more props means more content, not denser content.
        int side = (int)Math.Ceiling(Math.Sqrt(count));
        float half = (side - 1) * PropSpacing * 0.5f;
        ulong state = 0x9E3779B97F4A7C15UL;

        SceneNode props = scene.Root.CreateChild("Props");

        int placed = 0;
        for (int gx = 0; gx < side && placed < count; gx++)
        {
            for (int gz = 0; gz < side && placed < count; gz++)
            {
                float x = -half + gx * PropSpacing;
                float z = -half + gz * PropSpacing;

                // Keep clear of the room: its probes raycast through here.
                if (MathF.Abs(x) < 8f && MathF.Abs(z) < 8f)
                    continue;

                SceneNode node = props.CreateChild($"Prop{placed}");
                node.LocalPosition = new Vector3(
                    x, 0.5f + NextFloat01(ref state) * PropStackHeight, z);
                node.LocalRotation = Quaternion.CreateFromYawPitchRoll(
                    NextFloat01(ref state) * MathF.Tau, 0f, 0f);

                // Kind before brush, or the node is briefly a world brush.
                node.BrushKind = BrushKind.Part;
                node.Brush = shared;
                placed++;
            }
        }

        return placed;
    }

    // 64-bit LCG (Knuth MMIX constants), float in [0, 1) from the high 24 bits.
    private static float NextFloat01(ref ulong state)
    {
        state = state * 6364136223846793005UL + 1442695040888963407UL;
        return (state >> 40) * (1.0f / (1 << 24));
    }

    // One sun and a few point lights, fewer than the light cap. Colours are
    // authored in sRGB and converted to linear.
    private static void AddDemoLights(Scene scene)
    {
        // Direction comes from the node's forward axis; derive the rotation
        // from the direction, don't type euler angles.
        SceneNode sun = scene.Root.CreateChild("Sun");
        sun.LocalRotation = Light.RotationForDirection(new Vector3(-0.35f, -0.85f, -0.4f));
        sun.Light = new Light
        {
            Kind = LightKind.Directional,
            Color = ColorSpace.SrgbToLinear(new Vector3(1f, 0.96f, 0.88f)),

            // Tuned for a Lambert term divided by pi.
            Intensity = 11f,
        };

        AddPointLight(scene, "LampWarm", new Vector3(-3f, 2.2f, 2.5f),
            new Vector3(1f, 0.55f, 0.2f), intensity: 45f, range: 9f);
        AddPointLight(scene, "LampCool", new Vector3(3.2f, 1.8f, -2.2f),
            new Vector3(0.3f, 0.6f, 1f), intensity: 38f, range: 8f);

        AddPointLight(scene, "PlayAreaLamp", DemoPlayArea.Center + new Vector3(-8f, 4f, 0f),
            new Vector3(0.9f, 0.9f, 1f), intensity: 110f, range: 18f);
    }

    private static void AddPointLight(
        Scene scene, string name, Vector3 position, Vector3 displayColor, float intensity, float range)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.LocalPosition = position;
        node.Light = new Light
        {
            Kind = LightKind.Point,
            Color = ColorSpace.SrgbToLinear(displayColor),
            Intensity = intensity,
            Range = range,
        };
    }

    // One sphere per PBR test material, as mesh nodes. Spheres because a flat
    // face cannot show a highlight's shape.
    private static void AddPbrSpheres(Scene scene, Renderer renderer, AssetManager assets)
    {
        var (vertices, indices) = Primitives.Sphere();
        Mesh mesh = renderer.CreateMesh(vertices, indices, VertexAttribute.StandardLayout);

        var row = scene.Root.CreateChild("PbrReference");

        // Above the wall tops (y = 0.9), centred on x.
        float spacing = 1.15f;
        float firstX = -0.5f * spacing * (PbrMaterialPaths.Length - 1);

        for (int i = 0; i < PbrMaterialPaths.Length; i++)
        {
            Material material = assets.LoadMaterial(PbrMaterialPaths[i]);
            var node = row.CreateChild($"PbrSphere{i}");
            node.LocalTransform = new Transform
            {
                Position = new Vector3(firstX + i * spacing, 1.7f, -1f),
                Rotation = Quaternion.Identity,
                Scale = new Vector3(0.9f),
            };
            node.MeshRenderer = new MeshRenderer(mesh, material);
        }
    }

    private long _lastMeshesCreated;

    private int _lastCompileCount;

    private double CompileRate(Scene scene)
    {
        int now = scene.StaticWorldCompileCount;
        double rate = (now - _lastCompileCount) / CompileLogIntervalSeconds;
        _lastCompileCount = now;
        return rate;
    }

    private long _lastAllocatedBytes;
    private long _lastRenderThreadBytes;
    private int _lastGen0;
    private int _lastGen1;
    private int _lastGen2;

    private double MeshCreationRate()
    {
        if (_renderer is null) return 0;
        long now = _renderer.MeshesCreated;
        double rate = (now - _lastMeshesCreated) / CompileLogIntervalSeconds;
        _lastMeshesCreated = now;
        return rate;
    }

    // Allocation rate and collection counts over the last log interval. The
    // rate, not the heap size: steady per-frame garbage keeps the heap flat.
    private string DescribeMemory()
    {
        long allocated = GC.GetTotalAllocatedBytes(precise: false);
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);

        // Called on the render thread, so this is the frame loop's own share.
        long onThisThread = GC.GetAllocatedBytesForCurrentThread();

        double megabytesPerSecond =
            (allocated - _lastAllocatedBytes) / (1024.0 * 1024.0) / CompileLogIntervalSeconds;
        double renderMegabytesPerSecond =
            (onThisThread - _lastRenderThreadBytes) / (1024.0 * 1024.0) / CompileLogIntervalSeconds;
        string collections =
            $"{(gen0 - _lastGen0) / CompileLogIntervalSeconds:0.0}/s gen0, " +
            $"{gen1 - _lastGen1} gen1, {gen2 - _lastGen2} gen2";

        _lastAllocatedBytes = allocated;
        _lastRenderThreadBytes = onThisThread;
        _lastGen0 = gen0;
        _lastGen1 = gen1;
        _lastGen2 = gen2;

        string buffers = _renderer?.MeshMemory is { } memory
            ? $", mesh buffers active/retired/pooled {memory.Active / 1048576.0:0.00}/{memory.Retired / 1048576.0:0.00}/{memory.Pooled / 1048576.0:0.00} MiB"
            : string.Empty;
        return $"{megabytesPerSecond:0.0} MB/s allocated ({renderMegabytesPerSecond:0.0} on the render thread), " +
               $"{collections}, {GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024)} MB heap{buffers}";
    }

    // Zero casters with shadows on usually means a cull bug.
    private static string DescribeShadows(Renderer? renderer)
    {
        if (renderer is null) return "no renderer";
        if (!renderer.ShadowsEnabled) return "shadows off";
        if (renderer.ShadowMap is not { } map) return "shadows on, no caster yet";

        string batched = renderer.ShadowDrawsSaved > 0
            ? $", {renderer.ShadowDrawsSaved} draw(s) saved by instancing"
            : string.Empty;

        return $"shadows on ({renderer.ShadowCasterCount} caster(s){batched}, " +
               $"{map.CascadeCount} cascade(s) in a {map.Resolution}px atlas, " +
               $"texel {map.WorldTexelSize:0.000} sunit near / " +
               $"{map.CoarsestWorldTexelSize:0.000} far, {map.Distance:0} sunit range)";
    }

    private static string DescribeGeometryBatching(Renderer? renderer) =>
        renderer is { GeometryDrawsSaved: > 0 }
            ? $", {renderer.GeometryDrawsSaved} geometry draw(s) saved by instancing"
            : string.Empty;

    private static string DescribeGizmo(ISceneEditor? editor) =>
        editor is null ? "none" : $"{editor.GizmoModeName}/{editor.GizmoStyleName}";

    private static SceneNode AddBrushNode(
        Scene scene, string name, Vector3 center, Vector3 halfExtent, MaterialRef material = default)
    {
        var node = scene.Root.CreateChild(name);
        node.LocalPosition = center;
        node.Brush = Brush.CreateBox(-halfExtent, halfExtent, material);
        return node;
    }

    /// <summary>
    /// Per-frame demo update: animation, the async prop hand-off, and the two
    /// periodic log lines. Render thread only. <paramref name="renderView"/>
    /// is last frame's draw list.
    /// </summary>
    public void Update(double deltaTime, RenderView renderView)
    {
        _elapsed += deltaTime;

        if (_spinner is not null && DemoSpin)
        {
            _spinner.LocalRotation = Quaternion.CreateFromYawPitchRoll(
                (float)_elapsed * 0.6f,
                (float)_elapsed * 0.4f,
                0f);
        }

        // Runs after the editor, so the animation must yield to edits made
        // this frame; DemoBobAnimation re-centres on them.
        if (DemoCsgAnimation) _pillarBob?.Advance(_elapsed);

        if (ActiveScene is { } scene)
        {
            ProcessPendingProp(scene);

            // The periodic stats line a headless smoke run greps. Fixed arity,
            // so its length does not grow with the world.
            if (_elapsed >= _nextCompileLogTime)
            {
                _nextCompileLogTime = _elapsed + CompileLogIntervalSeconds;
                AssetManager? assets = _assets;
                ISceneEditor? editor = Editor;
                _logger.LogInformation(
                    "Assets: {Textures} texture(s), {Materials} material(s), " +
                    "{PlaceholderBound} placeholder-bound, " +
                    "{Models} model(s) requested / {Placed} placed; " +
                    "world: {ChunksVisible} of {ChunksTotal} chunks visible, " +
                    "{BatchesVisible} of {BatchesTotal} material batches; " +
                    "scene: {NodesVisible} of {NodesTotal} mesh nodes, " +
                    "{PartsVisible} of {PartsTotal} part brush(es){InertParts}; " +
                    "recompiled {Count} times, last touched {DirtyCells} dirty cell(s); " +
                    "physics: {PhysicsBackend}, {PhysicsBodies} body(ies) / {PhysicsShapes} shape(s); " +
                    "editing: {Selected} selected, {GizmoMode} gizmo, {Navigation} navigation, " +
                    "undo {UndoDepth} / redo {RedoDepth}; " +
                    "rendering: {Pipeline} pipeline, {Shadows}{GeometryBatching}, {FrameMs:0.00} ms/frame ({Fps:0} fps) [{Phases}]; " +
                    "churn: {MeshRate:0} mesh(es)/s, {CompileRate:0} compile(s)/s, {Pooled} buffer(s) pooled; " +
                    "memory: {Memory}; " +
                    "character: {CharacterMode}; " +
                    "entities: {EntityRuntime}",
                    assets?.TextureCount ?? 0, assets?.MaterialCount ?? 0,
                    assets?.PlaceholderBoundCount ?? 0,
                    _modelsRequested, _modelsPlaced,
                    renderView.WorldChunksVisible, renderView.WorldChunksTotal,
                    renderView.WorldMaterialBatchesVisible, renderView.WorldMaterialBatchesTotal,
                    renderView.VisibleCount, renderView.TotalCount,
                    renderView.PartBrushesVisible, renderView.PartBrushesTotal,
                    scene.InertPartBrushCount > 0
                        ? $", {scene.InertPartBrushCount} INERT (subtractive parts carve nothing and draw nothing)"
                        : string.Empty,
                    scene.StaticWorldCompileCount, scene.LastCompileDirtyCells.Count,
                    Physics.IsSimulating ? Physics.GetType().Name : "none",
                    Physics.BodyCount, Physics.StaticShapeCount,
                    editor?.SelectionCount ?? 0, DescribeGizmo(editor),
                    editor?.NavigationModeName ?? "none",
                    editor?.UndoDepth ?? 0, editor?.RedoDepth ?? 0,
                    _renderer?.CurrentPipelineName ?? "none", DescribeShadows(_renderer),
                    DescribeGeometryBatching(_renderer), FrameTimeMs, Fps,
                    _renderer?.Profiler.Describe() ?? "not measured", MeshCreationRate(), CompileRate(scene), _renderer?.PooledBufferCount ?? 0,
                    DescribeMemory(),
                    DescribeCharacter(),
                    DescribeEntities());
            }

            if (_elapsed >= _nextScreenProbeTime)
            {
                _nextScreenProbeTime = _elapsed + ScreenProbeIntervalSeconds;
                RunScreenProbe(scene, renderView);
            }
        }
    }

    // Places the async prop on the frame its import lands. Attaching it costs
    // one full static-world revalidation.
    private void ProcessPendingProp(Scene scene)
    {
        if (_pendingModel is not { } model) return;

        if (model.IsReady)
        {
            _pendingModel = null;
            PlaceProp(scene, model, "Signpost", SignpostPosition);
            _logger.LogInformation(
                "Async prop {Path} landed {Seconds:0.00} s after load and was placed in the scene",
                model.RelativePath, _elapsed);
            return;
        }

        if (model.Error is { } error)
        {
            _pendingModel = null;
            _logger.LogWarning(
                "Async prop {Path} failed to import ({Error}); the demo runs without it",
                model.RelativePath, error);
        }
    }

    // Casts a ray through the viewport centre and logs what it hit. Logged as
    // "Scene probe" so a grep cannot confuse it with the editing self-test.
    private void RunScreenProbe(Scene scene, RenderView renderView)
    {
        if (!TryGetViewportSize(out Vector2 viewport))
            return;

        Ray3 ray = scene.Camera.ScreenPointToRay(viewport * 0.5f, viewport);
        if (scene.Raycast(in ray, out SceneRaycastHit hit))
        {
            _logger.LogInformation(
                "Scene probe: center ray hit '{Node}' at {Distance:0.00} m; " +
                "{Visible} of {Total} mesh nodes, {ChunksVisible} of {ChunksTotal} world chunks visible",
                hit.Node.Name, hit.Distance, renderView.VisibleCount, renderView.TotalCount,
                renderView.WorldChunksVisible, renderView.WorldChunksTotal);
        }
        else
        {
            _logger.LogInformation(
                "Scene probe: center ray hit nothing; " +
                "{Visible} of {Total} mesh nodes, {ChunksVisible} of {ChunksTotal} world chunks visible",
                renderView.VisibleCount, renderView.TotalCount,
                renderView.WorldChunksVisible, renderView.WorldChunksTotal);
        }
    }

    // False while minimized: a zero-sized viewport divides by zero.
    private bool TryGetViewportSize(out Vector2 viewport)
    {
        Vector2D<int> framebuffer = _renderer?.FramebufferSize ?? default;
        if (framebuffer.X <= 0 || framebuffer.Y <= 0)
        {
            viewport = default;
            return false;
        }

        viewport = new Vector2(framebuffer.X, framebuffer.Y);
        return true;
    }

    public void Shutdown()
    {
        // Before the scene goes: entities are owed their OnRemove.
        StopEntityWorld();

        ActiveScene = null;
        Editor = null;

        // Disposing the shared null backend is harmless.
        Physics.Dispose();
        Physics = NullScenePhysics.Instance;
        SelfTestNode = null;
        _spinner = null;
        _pillarBob = null;
        _renderer = null;
        // Not unloaded here: the asset manager owns and releases it.
        _pendingModel = null;
        _assets = null;
        _logger.LogInformation("Scene manager shut down");
    }
}
