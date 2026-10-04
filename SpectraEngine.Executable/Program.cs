using Microsoft.Extensions.Logging;
using Serilog;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.D3D11;
using SpectraEngine.Core.Graphics.D3D12;
using SpectraEngine.Core.Graphics.OpenGL;
using SpectraEngine.Core.Graphics.Shaders;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Projects;
using SpectraEngine.Core.Scene;
using SpectraEngine.Entities;
using SpectraEngine.Executable;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Executable.Editing;
using SpectraEngine.Physics.Box3D;
using SpectraShade.Compiler;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.Debug()
    .WriteTo.File("logs/spectra-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddSerilog(dispose: false);
});

// Argument parsing is inside the try so a bad argument ends as a logged fatal
// with a flushed log file.
try
{
    DemoStartupOptions options = DemoStartupOptions.Parse(
        args, Environment.GetEnvironmentVariable(DemoStartupOptions.SelfTestEnvironmentVariable));

    if (options.SelfTestEnabled)
    {
        Log.Information(
            "Editing self-test ENABLED (from {Source}): every {Interval:0.#} s the demo drives a synthetic " +
            "pick/grab/drag/commit/undo/redo on the self-test brush node and logs one 'Editing self-test: PASS' " +
            "line. That node visibly moves ~1 unit for a handful of frames per run while the static world " +
            "recompiles — that motion IS the test. Drop --selftest (and {EnvVar}) for a scene nothing synthetic touches.",
            options.SelfTestSource == SelfTestSource.Environment
                ? DemoStartupOptions.SelfTestEnvironmentVariable
                : "--selftest",
            EditingSelfTest.IntervalSeconds,
            DemoStartupOptions.SelfTestEnvironmentVariable);
    }

    // Anchors the built-in entity assembly. Nothing here calls into it (maps
    // name its classes as text), so a trimmed or AOT publish would drop it.
    // Must run before the first catalogue read, which freezes the catalogue.
    BuiltinEntities.EnsureRegistered();

    // Logged on every run: trimmed-away registrations only show up in a
    // published build, where every entity would load as a placeholder.
    IReadOnlyList<EntitySchema> catalogue = EntityCatalog.Shared.Schemas;
    string[] catalogueNames = new string[catalogue.Count];
    for (int i = 0; i < catalogueNames.Length; i++)
        catalogueNames[i] = catalogue[i].ClassName;
    string catalogueRoster = catalogueNames.Length == 0 ? "(none)" : string.Join(", ", catalogueNames);

    if (catalogue.Count == 0)
    {
        Log.Error(
            "Entity catalogue: 0 classes registered ({Classes}). The generated module initializers were " +
            "trimmed away, so every entity in every map will load as a placeholder that behaves as nothing.",
            catalogueRoster);
    }
    else
    {
        Log.Information("Entity catalogue: {Count} classes registered ({Classes})", catalogue.Count, catalogueRoster);
    }

    // Exits before a renderer or window exists.
    if (options.ExportEntitySchemaPath is { } exportPath)
    {
        string destination = Path.GetFullPath(exportPath);
        if (Path.GetDirectoryName(destination) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);

        IReadOnlyList<EntitySchema> exported = EntityCatalog.Shared.Schemas;
        byte[] sentDef = SentDef.Write(exported);
        File.WriteAllBytes(destination, sentDef);

        Log.Information(
            "Exported {Types} entity schema(s) to {Path} ({Bytes} bytes, .sentdef v{Version})",
            exported.Count, destination, sentDef.Length, SentDef.Version);
        return;
    }

    var shaderCompiler = new SpectraShadeCompiler();
    Renderer renderer = options.Backend switch
    {
        GraphicsBackend.D3D11 => new D3D11Renderer(loggerFactory.CreateLogger<D3D11Renderer>(), shaderCompiler),
        GraphicsBackend.D3D12 => new D3D12Renderer(loggerFactory.CreateLogger<D3D12Renderer>(), shaderCompiler),
        GraphicsBackend.OpenGL => new OpenGLRenderer(loggerFactory.CreateLogger<OpenGLRenderer>(), shaderCompiler),
        _ => throw new NotSupportedException($"Backend {options.Backend} is not yet implemented; pick opengl, d3d11, or d3d12."),
    };

    renderer.VSync = options.VSync;
    renderer.UncappedPresentation = options.Uncapped;
    renderer.DeferredGBufferLayout = options.GBufferLayout;
    Log.Information("Deferred G-buffer layout: {Layout}", options.GBufferLayout);
    if (renderer is D3D12Renderer d3d12Renderer)
        d3d12Renderer.FrameContextCount = options.FrameContexts;

    SceneManager.ScatterGridOverride = options.ScatterGrid;
    SceneManager.PropCountOverride = options.PropCount;
    SceneManager.LoadMapPathOverride = options.LoadMapPath;
    SceneManager.SaveMapPathOverride = options.SaveMapPath;
    SceneManager.SaveProjectPathOverride = options.SaveProjectPath;

    // Opened before the asset manager is built: the project supplies the content root.
    ProjectLayout? project = null;
    if (options.ProjectPath is { } projectPath)
    {
        project = ProjectLayout.Open(projectPath);
        Log.Information(
            "Project '{Name}' opened from {Path}; content root {Assets}, {Maps} map(s) listed",
            project.Project.Name, project.ManifestPath, project.AssetsPath, project.Project.Maps.Count);

        // An explicit --map wins over the project's startup map.
        if (options.LoadMapPath is null && project.Project.StartupMap is { } startup)
        {
            SceneManager.LoadMapPathOverride = project.Resolve(startup);

            // A --pack run loads the baked map; the loose bundle above stays as
            // the fallback. CompiledMapPath.For is the same redirect the cook
            // uses, so the two can't spell the path differently.
            if (options.BootFromPacks)
                SceneManager.CompiledMapPathOverride = CompiledMapPath.For(startup);
        }
        else if (options.LoadMapPath is null)
            Log.Warning("Project '{Name}' names no startup map; running the demo scene", project.Project.Name);
    }

    var sceneManager = new SceneManager(loggerFactory.CreateLogger<SceneManager>());
    sceneManager.DemoCsgAnimation = options.DemoCsgAnimation;

    // The compare takes its pictures frames apart.
    sceneManager.DemoSpin = !options.PipelineCompare;
    Log.Information("Demo animation: {Mode}", options.DemoCsgAnimation ? "csg" : "off");

    // Must outlive Run: a pack hands out spans into a memory-mapped view, and
    // unmapping under a live span on the render thread is an access violation.
    using ProjectContentMount? packMount = options.BootFromPacks && project is not null
        ? ProjectContentMount.Open(
            loggerFactory.CreateLogger<ProjectContentMount>(),
            project,
            options.DevContentOverlay ? ContentMountProfile.Dev : ContentMountProfile.Shipped)
        : null;

    // The content root stays the project's Assets folder even in a pack run:
    // model imports and SourcePath resolve against it. The mount only decides
    // where the bytes come from.
    var assetManager = packMount is not null && project is not null
        ? new AssetManager(
            loggerFactory.CreateLogger<AssetManager>(),
            project.AssetsPath,
            packMount.Content,
            packMount.HotReloadEnabled)
        : project is null
            ? new AssetManager(loggerFactory.CreateLogger<AssetManager>())
            : new AssetManager(loggerFactory.CreateLogger<AssetManager>(), project.AssetsPath);
    var audioManager = new AudioManager(loggerFactory.CreateLogger<AudioManager>());
    var inputManager = new InputManager(loggerFactory.CreateLogger<InputManager>());

    // A factory because the scene is built later, on the render thread.
    // A null self-test probe is what turns the self-test off.
    sceneManager.EditorFactory = scene => new SceneEditorHost(
        loggerFactory, scene, renderer, inputManager,
        options.SelfTestEnabled && sceneManager.SelfTestNode is { } subject
            ? new EditingSelfTest(loggerFactory.CreateLogger<EditingSelfTest>(), scene, subject)
            : null);

    // A factory so Core never needs box3d.dll. Unset, the scene gets NullScenePhysics.
    sceneManager.PhysicsFactory = _ =>
        new Box3DScenePhysics(loggerFactory.CreateLogger<Box3DScenePhysics>());

    var engine = new Engine(
        loggerFactory.CreateLogger<Engine>(),
        renderer,
        sceneManager,
        assetManager,
        audioManager,
        inputManager)
    {
        StartInPlayMode = options.StartInPlayMode,
        RunOffscreenProbe = options.OffscreenProbe,
        RunPipelineCompare = options.PipelineCompare,
        StartupPipeline = options.Pipeline,
        ShadowsEnabled = options.Shadows,
        ProfileFrames = options.Profile,
        DebugLayer = options.DebugLayer,
        PreferredAdapter = options.Adapter,
        WindowSize = options.WindowSize,
    };

    using var fullscreenCycle = options.FullscreenCycleInterval is { } cycleInterval
        ? new FullscreenCycleHarness(
            loggerFactory.CreateLogger<FullscreenCycleHarness>(), engine.WindowMode, cycleInterval)
        : null;

    if (options.FullscreenCycleInterval is { } describedInterval)
        Log.Information("{Message}", FullscreenCycleHarness.DescribeStartup(describedInterval));

    // The export is written during the load, before the first frame.
    // RequestShutdown, not a window close: this handler runs on the render thread.
    if (options.ExitAfterSave)
    {
        Log.Information("Exiting after the first frame: --exit-after-save was asked for.");
        engine.Host.FrameCompleted += _ => engine.Host.RequestShutdown();
    }

    // Both probes replace the ordinary session and end themselves.
    if (options.ViewportCompare)
    {
        if (!ViewportCompareRun.Run(engine, renderer, loggerFactory.CreateLogger("ViewportCompare")))
            Environment.ExitCode = 1;
        return;
    }

    if (options.PacingProbe)
    {
        if (!SharedPacingRun.Run(engine, loggerFactory.CreateLogger("SharedPacing")))
            Environment.ExitCode = 1;
        return;
    }

    // No renderer disposal here: GPU teardown happens on the render thread.
    // Engine catches a render-thread crash, so Run's return value is the only signal.
    if (!engine.Run())
        Environment.ExitCode = 1;

    if (options.PipelineCompare && engine.PipelineComparePassed != true)
        Environment.ExitCode = 1;
}
catch (ArgumentException ex)
{
    // Usage error: no stack trace.
    Log.Fatal("{Message}", ex.Message);
    Environment.ExitCode = 2;
}
catch (NotSupportedException ex)
{
    // A known but unimplemented backend, e.g. vulkan.
    Log.Fatal("{Message}", ex.Message);
    Environment.ExitCode = 2;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Engine terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
