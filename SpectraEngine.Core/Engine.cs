using Microsoft.Extensions.Logging;
using Silk.NET.Input;
using Silk.NET.Windowing;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Diagnostics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Play;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Windowing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SpectraEngine.Core;

public sealed class Engine
{
    private const string WindowTitle = "Spectra Engine";

    // Caps one step, so a long stall doesn't teleport the fly camera.
    private const double MaxDeltaTime = 0.1;

    private readonly ILogger<Engine> _logger;
    private OffscreenProbe? _offscreenProbe;
    private ViewportCompareProbe? _viewportCompare;
    private PipelineCompareProbe? _pipelineCompare;
    private SharedPacingProbe? _sharedPacing;
    private readonly FpsCounter _fpsCounter = new();

    /// <summary>Where each frame's time goes, phase by phase.</summary>
    public FrameProfiler Profiler => _renderer.Profiler;
    private readonly Renderer _renderer;
    private readonly SceneManager _sceneManager;
    private readonly AssetManager _assetManager;
    private readonly AudioManager _audioManager;
    private readonly InputManager _inputManager;
    private readonly WindowModeLatch _windowModeLatch;

    // Null in embedded mode, where a shell owns the window.
    private IWindow? _window;

    private IRenderSurface? _surface;
    private Thread? _renderThread;
    private FlyCameraController? _cameraController;
    private PlaySession? _play;
    private readonly SpectraConsole _console = new();
    private FirstPersonController? _character;
    private DebugVisualization _debugFlags = DebugVisualization.None;

    // Not a DebugVisualization bit: the character is not in the scene graph.
    private bool _drawCharacter;

    // Rebuilt in place once per frame on the render thread.
    private readonly RenderView _renderView = new();

    private readonly FixedTickAccumulator _physicsTicks = new();

    // Written by the render thread, applied by the main thread:
    // GLFW window calls are main-thread only.
    private volatile string _pendingTitle = WindowTitle;

    // IsClosing is main-thread only, so the main loop latches it here.
    private volatile bool _closeRequested;

    // Set on any render thread exit, crash included, so the main loop stops pumping.
    private volatile bool _renderThreadExited;

    private volatile bool _renderThreadFaulted;

    public Engine(
        ILogger<Engine> logger,
        Renderer renderer,
        SceneManager sceneManager,
        AssetManager assetManager,
        AudioManager audioManager,
        InputManager inputManager)
    {
        _logger = logger;
        _renderer = renderer;
        _sceneManager = sceneManager;
        _assetManager = assetManager;
        _audioManager = audioManager;
        _inputManager = inputManager;
        _windowModeLatch = new WindowModeLatch(logger);
        Host = new EngineHost(logger);
        Host.AttachInput(inputManager);
    }

    // Reused across publishes; rebuilt only when the selection changed.
    private readonly List<Guid> _snapshotSelection = [];
    private Guid[] _publishedSelection = [];

    private int _publishedDebugLayerErrors;

    // A composited host imports on the generation, never on the handle.
    private Renderer.SharedTargetHandle? _publishedSharedTarget;
    private int _publishedSharedGeneration;
    private Func<FrameSnapshotBuilder, FrameSnapshot>? _snapshotBuilder;

    // Render thread only. The one place engine state is read for a UI.
    private void PublishHostFrame(TimeSpan elapsed)
    {
        bool interacting = _sceneManager.Editor?.IsInteracting ?? false;

        // A new debug-layer error publishes at once, so a shell can tie it to
        // what the user was doing. Fields, not locals: a captured local would
        // allocate a display class per frame.
        if (_renderer.DebugLayerErrorCount != _publishedDebugLayerErrors)
            Host.MarkDirty();

        // A new shared-target generation publishes at once too: the host is
        // sampling a resource about to be retired.
        _publishedSharedTarget = _renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle shared)
            ? shared
            : null;

        if (_publishedSharedTarget?.Generation != _publishedSharedGeneration)
        {
            _publishedSharedGeneration = _publishedSharedTarget?.Generation ?? 0;
            Host.MarkDirty();
        }

        Host.PublishFrame(elapsed, _snapshotBuilder ??= builder =>
        {
            ISceneEditor? editor = _sceneManager.Editor;

            _publishedDebugLayerErrors = _renderer.DebugLayerErrorCount;

            // Drain only when a snapshot goes out, or the window's peak is lost.
            _renderer.DrainSharedAcquireWait(out float acquireWaitMs, out float acquirePeakMs);

            return new FrameSnapshot
            {
                FrameNumber = builder.FrameNumber,
                Changes = builder.Changes,
                ChangesOverflowed = builder.ChangesOverflowed,
                FrameTimeMs = _fpsCounter.FrameTimeMs,
                Fps = _fpsCounter.Fps,
                SelectedIds = CaptureSelection(),
                GizmoModeName = editor?.GizmoModeName,
                GizmoStyleName = editor?.GizmoStyleName,
                GizmoOrientationName = editor?.GizmoOrientationName,
                SnapEnabled = editor?.SnapEnabled ?? false,
                SnapIncrement = editor?.SnapIncrement ?? 0f,
                MoveSnapIncrement = editor?.MoveSnapIncrement ?? 0f,
                RotateSnapIncrement = editor?.RotateSnapIncrement ?? 0f,
                ResizeSnapIncrement = editor?.ResizeSnapIncrement ?? 0f,
                NavigationModeName = editor?.NavigationModeName,
                GridModeName = editor?.GridModeName,
                InteractionStateName = editor?.InteractionStateName,
                ViewName = editor?.ViewName,
                CameraPosition = _sceneManager.ActiveScene?.Camera.Position ?? default,
                UndoDepth = editor?.UndoDepth ?? 0,
                RedoDepth = editor?.RedoDepth ?? 0,
                StaticWorldCompileCount = _sceneManager.ActiveScene?.StaticWorldCompileCount ?? 0,
                StaticWorldDefect = _sceneManager.ActiveScene?.StaticWorldDefect,
                DebugLayerErrorCount = _publishedDebugLayerErrors,
                DebugLayerActive = _renderer.DebugLayerActive,
                PlaceholderBoundCount = _assetManager.PlaceholderBoundCount,
                SharedAcquireWaitMs = acquireWaitMs,
                SharedAcquirePeakMs = acquirePeakMs,
                SharedTarget = _publishedSharedTarget,
                IsPlaying = _play is { IsActive: true },
                CanPlay = _character is not null,
                DebugFlags = _debugFlags,
                PipelineName = _renderer.CurrentPipelineName,
                PipelineNames = _renderer.PipelineNames,
                SelectionProperties = CaptureProperties(),
                SelectionEntity = CaptureSelectionEntity(),
                ConsoleLines = _console.Output.Drain(),
            };
        }, interacting);
    }

    // Lines wait for a scene, as host commands do.
    private void DrainConsole()
    {
        if (_sceneManager.ActiveScene is not { } scene)
            return;

        _console.Drain(Host, new ConsoleFrame(scene, _sceneManager.EntityWorld));
    }

    // Last snapshot, so a shell sees the engine stop.
    private void HostShutdownPublish(TimeSpan elapsed)
    {
        Host.SnapshotInterval = TimeSpan.Zero;
        PublishHostFrame(elapsed);
    }

    // Reused across publishes.
    private readonly List<SceneNode> _snapshotNodes = [];
    private readonly List<PropertyRow> _snapshotProperties = [];
    private PropertyRow[] _publishedProperties = [];

    // Rebuilt every publish, not diffed: values move all through a drag.
    private IReadOnlyList<PropertyRow> CaptureProperties()
    {
        if (_sceneManager.ActiveScene is not { } scene || scene.Selection.Count == 0)
        {
            _publishedProperties = [];
            return _publishedProperties;
        }

        _snapshotNodes.Clear();
        _snapshotNodes.AddRange(scene.Selection.Items);

        NodeInspector.Describe(
            _snapshotNodes, _snapshotProperties, scene.EntitySchemas, scene.Selection.FacePlane);

        // Copied out: the working list is reused next publish while a UI may
        // still be reading this one.
        if (_publishedProperties.Length != _snapshotProperties.Count)
            _publishedProperties = new PropertyRow[_snapshotProperties.Count];

        _snapshotProperties.CopyTo(_publishedProperties);
        return _publishedProperties;
    }

    private readonly List<Inspection.EntityTargetInfo> _snapshotTargetNames = [];

    // Null unless exactly one entity node is selected: connection lists don't merge.
    private EntityPanelInfo? CaptureSelectionEntity()
    {
        if (_sceneManager.ActiveScene is not { } scene)
            return null;

        IReadOnlyList<SceneNode> items = scene.Selection.Items;
        if (items.Count != 1)
            return null;

        return EntityPanelInfo.Capture(
            items[0], scene.EntitySchemas, scene, _snapshotTargetNames);
    }

    // Returns the previous array when the selection has not changed.
    private IReadOnlyList<Guid> CaptureSelection()
    {
        if (_sceneManager.ActiveScene is not { } scene)
            return _publishedSelection = [];

        IReadOnlyList<SceneNode> items = scene.Selection.Items;

        _snapshotSelection.Clear();
        for (int i = 0; i < items.Count; i++)
            _snapshotSelection.Add(items[i].Id);

        if (_publishedSelection.Length == _snapshotSelection.Count)
        {
            bool same = true;
            for (int i = 0; i < _snapshotSelection.Count; i++)
            {
                if (_publishedSelection[i] != _snapshotSelection[i])
                {
                    same = false;
                    break;
                }
            }

            if (same)
                return _publishedSelection;
        }

        return _publishedSelection = [.. _snapshotSelection];
    }

    /// <summary>
    /// The surface a UI thread drives this engine through: queue work, ask it to
    /// stop, and hear about finished frames.
    /// </summary>
    public EngineHost Host { get; }

    /// <summary>
    /// The engine's command line. A host adds its own commands before
    /// <see cref="Run"/> and sends lines with
    /// <see cref="EngineHost.SubmitConsoleLine"/>.
    /// </summary>
    // Not named Console: that would hide System.Console in this file.
    public SpectraConsole SpectraConsole => _console;

    /// <summary>
    /// Windowed or borderless fullscreen, as a request latch. Callable from any
    /// thread; the main thread applies it in its event pump.
    /// </summary>
    public IWindowModeLatch WindowMode => _windowModeLatch;

    /// <summary>
    /// Whether play mode is entered as soon as the scene is loaded, rather than
    /// waiting for <see cref="PlayModeKey"/>.
    /// </summary>
    public bool StartInPlayMode { get; set; }

    /// <summary>
    /// Whether to run the offscreen render-target probe once at startup. It
    /// renders the scene twice per probing frame.
    /// </summary>
    public bool RunOffscreenProbe { get; set; }

    /// <summary>
    /// Whether to run the viewport comparison once at startup and then end the
    /// session. Needs a <see cref="RenderSurfaceKind.Composited"/> surface.
    /// </summary>
    public bool RunViewportCompare { get; set; }

    /// <summary>
    /// Null until <see cref="RunViewportCompare"/>'s probe has reported, then
    /// whether the two pictures agreed. A host's exit code.
    /// </summary>
    public bool? ViewportComparePassed { get; private set; }

    /// <summary>
    /// Whether to compare the deferred and forward pipelines' pictures once at
    /// startup and then end the session. The scene has to hold still.
    /// </summary>
    public bool RunPipelineCompare { get; set; }

    /// <summary>
    /// Null until <see cref="RunPipelineCompare"/>'s probe has reported, then
    /// whether the two pictures agreed. A host's exit code.
    /// </summary>
    public bool? PipelineComparePassed { get; private set; }

    /// <summary>
    /// Whether to measure how the engine's frame rate follows the shared
    /// target's consumer, once at startup, and then end the session. Needs a
    /// <see cref="RenderSurfaceKind.Composited"/> surface.
    /// </summary>
    public bool RunSharedPacingProbe { get; set; }

    /// <summary>
    /// Null until <see cref="RunSharedPacingProbe"/>'s probe has reported, then
    /// whether it produced a measurement at all. A host's exit code.
    /// </summary>
    public bool? SharedPacingProbePassed { get; private set; }

    /// <summary>
    /// Name of the rendering pipeline to start on, or null for the backend's
    /// default. An unknown name logs a warning and uses the default.
    /// </summary>
    public string? StartupPipeline { get; set; }

    /// <summary>Whether the frame's directional light casts a shadow. On by default.</summary>
    public bool ShadowsEnabled { get; set; } = true;

    /// <summary>Whether to measure and report where each frame's time goes.</summary>
    public bool ProfileFrames { get; set; }

    /// <summary>
    /// Overrides the renderer's graphics validation layer, or null to keep the
    /// build flavour's default. See <see cref="Renderer.EnableDebugLayer"/>.
    /// </summary>
    public bool? DebugLayer { get; set; }

    /// <summary>Substring of the graphics adapter to run on, or null for the system default.</summary>
    public string? PreferredAdapter { get; set; }

    /// <summary>Window size, or null for the default.</summary>
    public (int Width, int Height)? WindowSize { get; set; }

    /// <summary>The key that enters and leaves play mode.</summary>
    public const InputKey PlayModeKey = InputKey.F8;

    /// <summary>The key that toggles the character capsule overlay.</summary>
    public const InputKey CharacterOverlayKey = InputKey.F9;

    /// <summary>
    /// Creates the window, runs the render thread, and pumps OS events until
    /// shutdown. Returns false when the render thread died on an exception.
    /// </summary>
    public bool Run()
    {
        _logger.LogInformation("Spectra Engine {Version} starting", EngineInfo.VersionString);

        // Before Window.Create and CreateInput: Silk.NET finds its GLFW
        // backends by reflection, which NativeAOT trims.
        SilkPlatform.EnsureRegistered();


        var options = WindowOptions.Default with
        {
            Title = WindowTitle,
            Size = WindowSize is { } requested
                ? new Silk.NET.Maths.Vector2D<int>(requested.Width, requested.Height)
                : new Silk.NET.Maths.Vector2D<int>(1280, 720),
            VSync = false,
            FramesPerSecond = 0,
            UpdatesPerSecond = 0,
            API = _renderer.WindowApi,
        };

        _window = Window.Create(options);
        _window.Initialize();

        // Not enough on OpenGL: glfwSwapInterval acts on the context current on
        // the calling thread, and the render thread takes the context next.
        // OpenGLRenderer.AcquireContext sets it again there.
        _window.VSync = false;

        InitializeSubsystems();

        _inputManager.Initialize(_window.CreateInput());

        AttachSurface(new WindowRenderSurface(_window));

        // Fires during DoEvents on this thread, the only one allowed to touch
        // the cursor, so a focus loss releases a freelook capture at once.
        _window.FocusChanged += _inputManager.OnWindowFocusChanged;

        StartRenderThread();

        var windowModeTarget = new SilkWindowModeTarget(_window);

        // Events are pumped on the thread that created the window. DoEvents
        // blocks during a title-bar drag; the render thread keeps running.
        string appliedTitle = WindowTitle;
        while (!_window.IsClosing && !_renderThreadExited)
        {
            _window.DoEvents();

            if (_window.IsClosing)
                _closeRequested = true;

            string pending = _pendingTitle;
            if (!ReferenceEquals(pending, appliedTitle))
            {
                _window.Title = pending;
                appliedTitle = pending;
            }

            // Cursor and window-mode requests come from the render thread;
            // GLFW only accepts the calls here.
            _inputManager.ApplyPendingCursorMode();
            _inputManager.ApplyPendingCursorShape();

            if (_windowModeLatch.ApplyPendingWindowMode(windowModeTarget) is { } newWindowMode)
            {
                // Don't wait for FramebufferResize: the render thread must not
                // present a frame at the old size into resized buffers.
                var framebuffer = _window.FramebufferSize;
                _renderer.SetFramebufferSize(framebuffer);
                _logger.LogInformation(
                    "Window mode -> {Mode} ({Width}x{Height})", newWindowMode, framebuffer.X, framebuffer.Y);
            }

            Thread.Sleep(1);
        }

        JoinRenderThread();
        ShutdownSubsystems();

        _window.Dispose();
        _window = null;

        _logger.LogInformation("Spectra Engine shut down");
        return !_renderThreadFaulted;
    }

    /// <summary>
    /// Whether the render thread is running. False before <see cref="Start"/>
    /// and after <see cref="Stop"/>.
    /// </summary>
    public bool IsRunning => _renderThread is not null;

    /// <summary>Whether the render thread ended on an exception.</summary>
    public bool Faulted => _renderThreadFaulted;

    /// <summary>
    /// Starts the engine against a surface the caller owns and returns once the
    /// render thread is running. The caller keeps the window: input arrives
    /// through <see cref="Host"/>, and <see cref="WindowMode"/> requests are the
    /// caller's to apply. Call <see cref="Stop"/> to shut down.
    /// </summary>
    public void Start(IRenderSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (_renderThread is not null)
            throw new InvalidOperationException("The engine is already running.");

        _logger.LogInformation(
            "Spectra Engine {Version} starting (embedded, {Kind} surface)",
            EngineInfo.VersionString, surface.Kind);

        InitializeSubsystems();
        AttachSurface(surface);
        StartRenderThread();
    }

    /// <summary>
    /// Stops the render thread and shuts the subsystems down, blocking until
    /// the thread has finished. Returns false when it had died on an exception.
    /// Safe to call twice.
    /// </summary>
    public bool Stop()
    {
        if (_renderThread is null)
            return !_renderThreadFaulted;

        JoinRenderThread();
        ShutdownSubsystems();

        _logger.LogInformation("Spectra Engine shut down");
        return !_renderThreadFaulted;
    }

    private void InitializeSubsystems()
    {
        _assetManager.Initialize();
        _sceneManager.Initialize();
        _audioManager.Initialize();
    }

    private void AttachSurface(IRenderSurface surface)
    {
        _surface = surface;

        // Size queries are not thread-safe on any windowing backend, so the
        // render side only reads this latch.
        _renderer.SetFramebufferSize(surface.PixelSize);
        surface.Resized += _renderer.SetFramebufferSize;

        // Hand a thread-affine context (OpenGL) over to the render thread.
        _renderer.ReleaseContext(surface);
    }

    private void StartRenderThread()
    {
        // Cleared at start, not at the end of a run, so Stop's result and
        // Faulted still describe the session that just ended.
        _closeRequested = false;
        _renderThreadExited = false;
        _renderThreadFaulted = false;

        _renderThread = new Thread(RenderLoop)
        {
            Name = "Spectra Render",
        };
        _renderThread.Start();
    }

    private void JoinRenderThread()
    {
        if (_renderThread is not { } thread)
            return;

        _closeRequested = true;
        thread.Join();
        _renderThread = null;
    }

    private void ShutdownSubsystems()
    {
        // Detach first: a late resize must not reach a renderer shutting down.
        if (_surface is { } surface)
        {
            surface.Resized -= _renderer.SetFramebufferSize;
            _surface = null;
        }

        _inputManager.Shutdown();
        _audioManager.Shutdown();
        _sceneManager.Shutdown();
        _assetManager.Shutdown();
    }

    // Play mode takes the camera and the cursor from an editor that may hold
    // both mid-gesture.

    private void TogglePlayMode()
    {
        if (_play is { IsActive: true }) ExitPlayMode();
        else EnterPlayMode();
    }

    private void EnterPlayMode()
    {
        if (_play is not { IsActive: false } play || _character is not { } character)
            return;

        // Suspend first. It rolls back an open drag and releases the editor's
        // cursor lock, and a spawning entity may fire outputs.
        _sceneManager.Editor?.Suspend();
        play.Enter();
        character.Enter();
    }

    private void ExitPlayMode()
    {
        if (_play is not { IsActive: true } play)
            return;

        // The session first: OnRemove must run before the editor takes the
        // scene back.
        play.Exit();
        _character?.Exit();

        _sceneManager.Editor?.Resume();
    }

    // Exceptions must not escape: the thread is non-background, so an unhandled
    // throw kills the process with no fatal log entry and no flush.
    private void RenderLoop()
    {
        var surface = _surface!;
        try
        {
            _renderer.AcquireContext(surface);

            if (DebugLayer is { } wanted)
                _renderer.EnableDebugLayer = wanted;
            _renderer.PreferredAdapter = PreferredAdapter;

            // Before Initialize, which builds the default programs. Set later,
            // a cooked pack is ignored for them and nothing reports it.
            _renderer.ShaderContent = _assetManager.Content;

            _renderer.Initialize(surface);

            _renderer.ShadowsEnabled = ShadowsEnabled;
            _renderer.Profiler.Enabled = ProfileFrames;

            // Pipelines are registered in Initialize.
            if (StartupPipeline is { Length: > 0 } pipelineName &&
                !_renderer.TrySelectPipeline(pipelineName))
            {
                _logger.LogWarning(
                    "No rendering pipeline named '{Requested}'; staying on {Pipeline}",
                    pipelineName, _renderer.CurrentPipelineName);
            }

            // The placeholder texture is a GPU resource: this thread owns the context.
            _assetManager.AttachRenderer(_renderer);

            _sceneManager.LoadStartupScene(_renderer, _assetManager);

            // The probes come after the scene: they need real geometry.
            if (RunOffscreenProbe)
                _offscreenProbe = new OffscreenProbe(_logger);

            if (RunViewportCompare)
                _viewportCompare = new ViewportCompareProbe(_logger);

            if (RunPipelineCompare)
                _pipelineCompare = new PipelineCompareProbe(_logger);

            if (RunSharedPacingProbe)
            {
                _sharedPacing = new SharedPacingProbe(_logger);

                // Subscribe instead of reading the renderer: the acquire-wait
                // drain resets on read and the snapshot builder already drains.
                Host.FrameCompleted += _sharedPacing.ObserveSnapshot;
            }

            Host.ObserveScene(_sceneManager.ActiveScene);

            if (_sceneManager.ActiveScene is { } activeScene)
            {
                _cameraController = new FlyCameraController(activeScene.Camera, _inputManager);

                // Built here because it needs live input. The scene manager
                // gets it only for the stats line. The play session places
                // its spawn on every Play.
                _character = new FirstPersonController(_logger, activeScene, _inputManager)
                {
                    FallOutHeight = _sceneManager.PlayerFallOutHeight,
                };
                _sceneManager.Character = _character;

                _logger.LogInformation(
                    "{Key} enters play mode (walk the world as a {Height:0.0} sunit character); " +
                    "{OverlayKey} toggles the capsule overlay",
                    PlayModeKey, _character.Tuning.StandHeight, CharacterOverlayKey);
            }

            // Built even with nothing to play: its tick also steps physics.
            var play = new PlaySession(_sceneManager, _character?.Simulation)
            {
                Profiler = Profiler,
                Logger = _logger,
            };
            _play = play;

            // Before play starts, so a startup line sees the level as it was
            // loaded. A line that needs the running level waits a frame.
            DrainConsole();

            if (StartInPlayMode)
                EnterPlayMode();

            _logger.LogInformation("All subsystems initialized");

            var clock = Stopwatch.StartNew();
            double previous = clock.Elapsed.TotalSeconds;

            while (!_closeRequested && !Host.ShutdownRequested)
            {
                Profiler.BeginFrame();
                double now = clock.Elapsed.TotalSeconds;
                double rawDelta = now - previous;
                previous = now;

                // Only the simulation step is clamped. The FPS counter gets the
                // raw delta, or it would hide the stalls it is there to show.
                double deltaTime = Math.Min(rawDelta, MaxDeltaTime);

                _inputManager.Update(deltaTime);

                // Play mode first, so the rest of the frame knows who owns the
                // camera and the cursor. A host request is taken where the key
                // is read, so a Play button and F8 cannot disagree.
                if (Host.TryTakePlayModeRequest(out bool enterPlay))
                {
                    if (enterPlay) EnterPlayMode();
                    else ExitPlayMode();
                }

                if (_inputManager.WasKeyPressed(PlayModeKey))
                    TogglePlayMode();
                else if (play.IsActive && _inputManager.WasKeyPressed(InputKey.Escape))
                    ExitPlayMode();

                if (_inputManager.WasKeyPressed(CharacterOverlayKey))
                    _drawCharacter = !_drawCharacter;

                // After the play-mode block, so a line typed with Play sees
                // the world it started. Before the ticks, so an input fired
                // by hand is delivered this frame.
                DrainConsole();

                bool playing = play.IsActive;

                // One command per frame, replayed by every tick. Sampling per
                // tick would multiply mouse look by the tick count.
                if (playing)
                    _character!.BeginFrame(deltaTime);

                // The editor runs before the scene update so the camera and any
                // gizmo edit are final when the draw list is built. Neither it
                // nor the fly camera runs while the character owns the camera.
                ISceneEditor? editor = _sceneManager.Editor;
                bool editorNavigated = false;
                if (!playing)
                {
                    using var editorTiming = Profiler.Measure(FramePhase.Editor);
                    editorNavigated = editor is not null && editor.Update(deltaTime);
                    if (!editorNavigated)
                        _cameraController?.Update(deltaTime);
                }

                using (Profiler.Measure(FramePhase.Update))
                    _sceneManager.Update(deltaTime, _renderView);

                // Fixed steps only, never frame deltas, or the simulation
                // depends on how fast the machine is.
                IScenePhysics physics = _sceneManager.Physics;
                CharacterCommand command = playing ? _character!.Command : default;
                int ticks = _physicsTicks.Advance(deltaTime);
                for (int tick = 0; tick < ticks; tick++)
                {
                    PlayTickResult result = play.Tick(_physicsTicks.FixedDeltaTime, in command);
                    if (playing)
                        _character!.OnTick(in result);
                }

                // Right before the compile pump, so a UI edit and the recompile
                // it causes land in the same frame.
                Host.DrainCommands(_sceneManager.ActiveScene);

                using (Profiler.Measure(FramePhase.WorldSwap))
                    _sceneManager.ActiveScene?.ProcessStaticWorldCompilation(_renderer, _logger);

                // Same slot as the mesh swap: what is visible and what is solid
                // must change together. Outside the tick loop, since at most
                // one run per frame can do work.
                if (_sceneManager.ActiveScene is { } physicsScene)
                using (Profiler.Measure(FramePhase.CollisionSync))
                    physics.SyncStaticWorld(physicsScene);

                // Part brushes are not in the static-world compile.
                using (Profiler.Measure(FramePhase.PartMeshes))
                    _sceneManager.ActiveScene?.ProcessPartBrushMeshes(_renderer);

                // GPU textures for finished background decodes are created here.
                using (Profiler.Measure(FramePhase.Assets))
                    _assetManager.PumpPendingUploads();

                // The render thread owns the AL context. Runs every frame, or
                // streaming queues drain. Listener first, or positional sounds
                // play at the origin.
                if (_sceneManager.ActiveScene is { } listenerScene)
                {
                    Camera listener = listenerScene.Camera;
                    _audioManager.SetListener(listener.Position, listener.Forward, listener.Up);
                }

                using (Profiler.Measure(FramePhase.Audio))
                    _audioManager.Update();

                // Render-only blend of the last two ticks. Must never write back
                // through a node's transform setters.
                physics.PublishRenderPoses(_physicsTicks.Alpha);

                // Render-only too: never writes back into the mover.
                if (playing)
                    _character!.UpdateView(deltaTime, _physicsTicks.Alpha);

                if (_inputManager.WasKeyPressed(InputKey.F1)) _debugFlags ^= DebugVisualization.Wireframe;
                if (_inputManager.WasKeyPressed(InputKey.F2)) _debugFlags ^= DebugVisualization.Vertices;
                if (_inputManager.WasKeyPressed(InputKey.F3)) _debugFlags ^= DebugVisualization.Aabbs;
                if (_inputManager.WasKeyPressed(InputKey.F4)) _debugFlags ^= DebugVisualization.Normals;
                if (_inputManager.WasKeyPressed(InputKey.F5)) _debugFlags ^= DebugVisualization.SceneGraph;

                Host.TakeDebugVisualizationRequests(
                    out DebugVisualization flagsToSet, out DebugVisualization flagsToClear);
                _debugFlags = (_debugFlags | flagsToSet) & ~flagsToClear;

                if (_inputManager.WasKeyPressed(InputKey.F6))
                    _renderer.NextPipeline();

                if (Host.TakeRequestedPipeline() is { } requestedPipeline &&
                    !_renderer.TrySelectPipeline(requestedPipeline))
                {
                    _logger.LogWarning(
                        "No rendering pipeline named '{Requested}'; staying on {Pipeline}",
                        requestedPipeline, _renderer.CurrentPipelineName);
                }

                // A composited host releasing a retired shared target. Freeing
                // a GPU resource is render-thread work.
                if (Host.TryTakeSharedTargetRelease(out int releasedGeneration))
                    _renderer.NotifySharedTargetReleased(releasedGeneration);

                // Request only. The main thread reshapes the window.
                if (_inputManager.WasKeyPressed(InputKey.F11))
                    _windowModeLatch.ToggleFullscreen();

                _renderer.DebugDraw.Clear();
                _renderer.WorldLines.Clear();
                _renderer.Outlines.Clear();
                if (_sceneManager.ActiveScene is { } scene)
                {
                    // Depth-tested lines: a ground grid is occluded by the floor.
                    editor?.DrawWorld(_renderer.WorldLines);

                    // Depth-off lines, so handles are always visible and pickable.
                    editor?.Draw(_renderer.DebugDraw);

                    if (_drawCharacter)
                        _character?.Draw(_renderer.DebugDraw);

                    if (_debugFlags != DebugVisualization.None)
                        DebugVisualizations.Draw(_renderer.DebugDraw, scene, _debugFlags);
                }

                // After the update and the static-world pump. Aspect ratio comes
                // from the framebuffer latch so the culling frustum matches the
                // projection the pipelines render with.
                if (_sceneManager.ActiveScene is { } viewScene)
                {
                    var framebuffer = _renderer.FramebufferSize;
                    if (framebuffer.Y > 0)
                        viewScene.Camera.AspectRatio = framebuffer.X / (float)framebuffer.Y;
                    using (Profiler.Measure(FramePhase.ViewBuild))
                        viewScene.BuildRenderView(viewScene.Camera, _renderView);
                }
                else
                {
                    _renderView.Clear();
                }

                // The probes run before Render: they decide what this frame writes.
                _offscreenProbe?.Update(_renderer);
                if (_offscreenProbe is { Running: false })
                    _offscreenProbe = null;

                // Ends the run when done: another frame would take the shared
                // key with nobody left to hand it back.
                if (_viewportCompare is { } compare)
                {
                    compare.Update(_renderer);
                    if (!compare.Running)
                    {
                        ViewportComparePassed = compare.Passed;
                        _viewportCompare = null;
                        Host.RequestShutdown();
                        break;
                    }
                }

                if (_pipelineCompare is { } parity)
                {
                    parity.Update(_renderer);
                    if (!parity.Running)
                    {
                        PipelineComparePassed = parity.Passed;
                        _pipelineCompare = null;
                        Host.RequestShutdown();
                        break;
                    }
                }

                if (_sharedPacing is { } pacing)
                {
                    pacing.Update(_renderer);
                    if (!pacing.Running)
                    {
                        SharedPacingProbePassed = pacing.Passed;
                        _sharedPacing = null;
                        Host.RequestShutdown();
                        break;
                    }
                }

                _renderer.Render(_sceneManager.ActiveScene, _renderView, deltaTime);

                if (_fpsCounter.Tick(rawDelta))
                {
                    _pendingTitle =
                        $"{WindowTitle}  —  {_fpsCounter.Fps:0} FPS  ({_fpsCounter.FrameTimeMs:0.00} ms)  —  {_renderer.CurrentPipelineName}";

                    // For the stats line: an unattended run can't read a title.
                    _sceneManager.FrameTimeMs = _fpsCounter.FrameTimeMs;
                    _sceneManager.Fps = _fpsCounter.Fps;
                }

                using (Profiler.Measure(FramePhase.Present))
                    _renderer.Present(surface);

                // After Present: the snapshot describes a finished frame, and a
                // slow handler only delays the next one.
                using (Profiler.Measure(FramePhase.Snapshot))
                    PublishHostFrame(clock.Elapsed);

                Profiler.EndFrame();
            }

            HostShutdownPublish(clock.Elapsed);

            // GPU resources go before the renderer shuts down, on this thread.
            _sceneManager.ActiveScene?.ReleasePartBrushMeshes(_renderer);
            _assetManager.ReleaseGraphicsResources();
            _renderer.Shutdown();
            _renderer.ReleaseContext(surface);
        }
        catch (Exception ex)
        {
            _renderThreadFaulted = true;
            _logger.LogCritical(ex, "Render thread crashed; shutting down");

            // A second failure here must not mask the original crash.
            try
            {
                _assetManager.ReleaseGraphicsResources();
                _renderer.Shutdown();
                _renderer.ReleaseContext(surface);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "Renderer teardown after render-thread crash also failed");
            }
        }
        finally
        {
            _renderThreadExited = true;
        }
    }
}
