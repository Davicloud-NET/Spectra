using Microsoft.Extensions.Logging;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Graphics.Shaders;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Graphics;

public abstract class Renderer
{
    internal readonly ILogger<Renderer> _logger;
    private readonly IShaderCompiler _shaderCompiler;

    public abstract GraphicsBackend Backend { get; }

    // GLFW answers size queries only on the window's thread, so the main
    // thread latches the size here and the render side reads the latch.
    private readonly object _framebufferSizeLock = new();
    private Vector2D<int> _framebufferSize;

    /// <summary>
    /// The window's framebuffer size as last reported by the main thread.
    /// Render-side code reads this instead of <see cref="IWindow.FramebufferSize"/>.
    /// </summary>
    public Vector2D<int> FramebufferSize
    {
        get { lock (_framebufferSizeLock) return _framebufferSize; }
    }

    /// <summary>
    /// <see cref="FramebufferSize"/> as plain ints, for assemblies that must
    /// not reference Silk.NET.
    /// </summary>
    public void GetFramebufferSize(out int width, out int height)
    {
        lock (_framebufferSizeLock)
        {
            width = _framebufferSize.X;
            height = _framebufferSize.Y;
        }
    }

    // Main thread only.
    internal void SetFramebufferSize(Vector2D<int> size)
    {
        lock (_framebufferSizeLock)
            _framebufferSize = size;
    }

    /// <summary>
    /// The graphics API the host window should be created with. OpenGL needs
    /// <see cref="GraphicsAPI.Default"/>; D3D and Vulkan want
    /// <see cref="GraphicsAPI.None"/> and create their own device.
    /// </summary>
    public virtual GraphicsAPI WindowApi => GraphicsAPI.Default;

    /// <summary>
    /// Makes a thread-affine context (OpenGL) current on the calling thread.
    /// Called once at the start of the render loop.
    /// </summary>
    public virtual void AcquireContext(IRenderSurface surface) => surface.GLContext?.MakeCurrent();

    /// <summary>Releases the context from the calling thread.</summary>
    public virtual void ReleaseContext(IRenderSurface surface) => surface.GLContext?.Clear();

    /// <summary>Presents the most recently rendered frame.</summary>
    public virtual void Present(IRenderSurface surface) => surface.GLContext?.SwapBuffers();

    /// <summary>
    /// Whether <see cref="Present"/> waits for the display's vertical blank.
    /// Off by default; a host may change it while the render thread runs.
    /// </summary>
    // Off so the demo's frame times measure the engine, not the display.
    // Backends read it at Present time.
    public bool VSync
    {
        get => _vsync;
        set => _vsync = value;
    }

    private volatile bool _vsync;

    /// <summary>Requests the backend's uncapped benchmark presentation path. Set before initialization.</summary>
    public bool UncappedPresentation { get; set; }
    /// <summary>Whether this surface supports an uncapped presentation request.</summary>
    public bool UncappedPresentationAvailable { get; protected set; }

    /// <summary>
    /// The backend's shader for general lit geometry. Available after
    /// <see cref="Initialize"/>.
    /// </summary>
    public ShaderProgram? DefaultShader { get; protected set; }

    /// <summary>
    /// Whether to ask the graphics API for its validation layer. Set before
    /// <see cref="Initialize"/>. On in Debug, off in Release.
    /// </summary>
    // Validation is expensive, but DebugLayerErrorCount on D3D only exists
    // while it runs.
    public bool EnableDebugLayer { get; set; } =
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>
    /// Substring of the graphics adapter to run on, or null for the system
    /// default. Matched case-insensitively against the adapter description.
    /// Set before <see cref="Initialize"/>.
    /// </summary>
    public string? PreferredAdapter { get; set; }

    /// <summary>Description of the adapter in use, once a device exists.</summary>
    public string AdapterName { get; protected set; } = "unknown";

    /// <summary>Whether the validation layer is running.</summary>
    public bool DebugLayerActive { get; protected set; }

    /// <summary>
    /// This frame's debug lines, drawn on top of the scene with no depth test.
    /// Push before <see cref="Render"/>; the engine clears it each frame.
    /// </summary>
    public DebugDraw DebugDraw { get; } = new();

    /// <summary>
    /// This frame's depth-tested world lines, such as the ground grid. The
    /// engine clears it each frame.
    /// </summary>
    // Unlike DebugDraw these go through the tone curve, so colours here are
    // authored in linear light.
    public DebugDraw WorldLines { get; } = new();

    /// <summary>Per-phase frame timing. Off unless asked for.</summary>
    public Diagnostics.FrameProfiler Profiler { get; } = new();
    protected bool _gpuTimingUnavailable;

    /// <summary>GPU meshes created over this renderer's life.</summary>
    public long MeshesCreated { get; private protected set; }

    /// <summary>GPU meshes destroyed over this renderer's life.</summary>
    public long MeshesDestroyed { get; private protected set; }

    /// <summary>
    /// Buffers the backend holds for reuse. Zero on backends that do not pool.
    /// </summary>
    public virtual int PooledBufferCount => 0;
    /// <summary>Backend mesh-memory accounting, or null when unavailable.</summary>
    public virtual MeshBufferMemory? MeshMemory => null;

    /// <summary>
    /// Hot-reloads shaders created via <see cref="CreateShaderFromFile"/>. The
    /// render loop calls <see cref="ShaderHotReloader.PumpPendingReloads"/>
    /// once per frame.
    /// </summary>
    public ShaderHotReloader HotReloader { get; }

    protected Renderer(ILogger<Renderer> logger, IShaderCompiler shaderCompiler)
    {
        _logger = logger;
        _shaderCompiler = shaderCompiler;
        HotReloader = new ShaderHotReloader(logger, shaderCompiler, Backend);
        BaseShaders.LogHotReloadState(logger);
    }

    /// <summary>
    /// Compiles SpectraShade source for this backend and creates a shader
    /// program from it.
    /// </summary>
    public ShaderProgram CreateShaderFromSource(string spectraShadeSource)
    {
        ReadOnlySpan<GraphicsBackend> targets = [Backend];
        CompiledShaderFile compiled = _shaderCompiler.Compile(spectraShadeSource, targets);
        PipelineBlob blob = compiled.GetPipeline(Backend)
            ?? throw new InvalidOperationException(
                $"SpectraShade compilation produced no pipeline for {Backend}.");
        return CreateShader(blob);
    }

    /// <summary>
    /// Compiles <paramref name="spectraShadeSource"/> and returns a program built
    /// from its instanced vertex stage, or null if the source marks no uniform
    /// <c>[PerInstance]</c>.
    /// </summary>
    public ShaderProgram? TryCreateInstancedShaderFromSource(string spectraShadeSource)
    {
        ReadOnlySpan<GraphicsBackend> targets = [Backend];
        CompiledShaderFile compiled = _shaderCompiler.Compile(spectraShadeSource, targets);
        PipelineBlob? blob = compiled.GetPipeline(Backend);
        if (blob?.InstancedVertexData is null || blob.FragmentData is null)
            return null;

        return CreateShader(
            System.Text.Encoding.UTF8.GetString(blob.InstancedVertexData),
            System.Text.Encoding.UTF8.GetString(blob.FragmentData));
    }

    /// <summary>
    /// Compiles a SpectraShade source file and registers the program for
    /// hot-reload.
    /// </summary>
    public ShaderProgram CreateShaderFromFile(string absolutePath)
    {
        string source = File.ReadAllText(absolutePath);
        ShaderProgram program = CreateShaderFromSource(source);
        HotReloader.Register(absolutePath, program);
        return program;
    }

    /// <summary>
    /// The content stack built-in shaders resolve through, or null to use the
    /// embedded copies. Set before <see cref="Initialize"/>.
    /// </summary>
    public IContentSource? ShaderContent { get; set; }

    /// <summary>
    /// Builds a built-in shader (a <see cref="BaseShaders"/> file name),
    /// preferring a cooked blob from the content stack over compiling source.
    /// </summary>
    public ShaderProgram CreateBaseShader(string fileName)
    {
        ResolvedShader resolved = BaseShaderResolver.ResolveBuiltIn(ShaderContent, fileName, Backend, _logger);
        if (resolved.Cooked is { } cooked) return CreateShader(cooked);

        ShaderProgram program = CreateShaderFromSource(resolved.Source!);

        if (resolved.WatchPath is { } watch) HotReloader.Register(watch, program);
        return program;
    }

    /// <summary>
    /// The instanced twin of a built-in shader, or null when it declares no
    /// per-instance uniform.
    /// </summary>
    public ShaderProgram? TryCreateInstancedBaseShader(string fileName)
    {
        ResolvedShader resolved = BaseShaderResolver.ResolveBuiltIn(ShaderContent, fileName, Backend, _logger);
        if (resolved.Cooked is not { } cooked)
            return TryCreateInstancedShaderFromSource(resolved.Source!);

        if (cooked.InstancedVertexData is null || cooked.FragmentData is null) return null;

        return CreateShader(
            System.Text.Encoding.UTF8.GetString(cooked.InstancedVertexData),
            System.Text.Encoding.UTF8.GetString(cooked.FragmentData));
    }

    public virtual void Initialize(IRenderSurface surface)
    {
        _logger.LogInformation("Renderer initialized");
    }

    private Vector2D<int> _passSize;
    private bool _inPass;
    private int _currentTargetCount;
    private RenderTarget? _currentTarget;
    private RenderTarget[] _currentTargets = [];

    /// <summary>Maximum colour attachments a single pass may bind.</summary>
    // What both D3D backends allow and the minimum OpenGL guarantees.
    public const int MaxColorTargets = 8;

    /// <summary>
    /// The size of the target the current pass draws into. Pipelines size
    /// viewports and aspect ratios from this, not from <see cref="FramebufferSize"/>.
    /// </summary>
    public Vector2D<int> PassSize => _passSize;

    /// <summary>
    /// Aspect ratio of the current pass's target, or null when it has no height.
    /// </summary>
    public float? PassAspectRatio => _passSize.Y > 0 ? _passSize.X / (float)_passSize.Y : null;

    /// <summary>
    /// Begins a pass into the window's back buffer: binds it, sets the viewport
    /// and applies <paramref name="clear"/>. Match with <see cref="EndPass"/>.
    /// Passes do not nest. Render thread only.
    /// </summary>
    public void BeginPass(in PassClear clear) => BeginPass((RenderTarget?)null, clear);

    /// <summary>
    /// Begins a pass into <paramref name="target"/>, or the back buffer when it
    /// is null. Match with <see cref="EndPass"/>. Render thread only.
    /// </summary>
    public void BeginPass(RenderTarget? target, in PassClear clear)
    {
        BeginPassChecked(target, [], clear);
    }

    /// <summary>
    /// Begins a pass writing to several colour targets at once. All must be the
    /// same size, and depth comes from the first, so create the others with
    /// <c>Depth: false</c>.
    /// </summary>
    public void BeginPass(ReadOnlySpan<RenderTarget> targets, in PassClear clear)
    {
        if (targets.Length == 0)
            throw new ArgumentException("A multi-target pass needs at least one target.", nameof(targets));
        if (targets.Length > MaxColorTargets)
            throw new ArgumentException(
                $"A pass may bind at most {MaxColorTargets} colour targets; got {targets.Length}.",
                nameof(targets));

        for (int i = 1; i < targets.Length; i++)
        {
            if (targets[i].Width != targets[0].Width || targets[i].Height != targets[0].Height)
            {
                throw new ArgumentException(
                    $"Every target in a pass must be the same size; target 0 is " +
                    $"{targets[0].Width}x{targets[0].Height} and target {i} is " +
                    $"{targets[i].Width}x{targets[i].Height}.",
                    nameof(targets));
            }
        }

        BeginPassChecked(targets[0], targets, clear);
    }

    private void BeginPassChecked(RenderTarget? target, ReadOnlySpan<RenderTarget> targets, in PassClear clear)
    {
        if (_inPass)
            throw new InvalidOperationException("BeginPass was called inside a pass; passes do not nest.");

        _passSize = target is null
            ? FramebufferSize
            : new Vector2D<int>(target.Width, target.Height);
        _currentTarget = target;
        if (_currentTargets.Length < targets.Length)
            _currentTargets = new RenderTarget[MaxColorTargets];
        _currentTargetCount = targets.Length;
        targets.CopyTo(_currentTargets);

        _inPass = true;
        BeginPassCore(target, _currentTargets.AsSpan(0, _currentTargetCount), clear);
    }

    /// <summary>Finishes the pass opened by <see cref="BeginPass"/>. Render thread only.</summary>
    public void EndPass()
    {
        if (!_inPass)
            throw new InvalidOperationException("EndPass was called without a matching BeginPass.");

        _inPass = false;
        RenderTarget? target = _currentTarget;
        var targets = _currentTargets.AsSpan(0, _currentTargetCount);
        _currentTarget = null;
        _currentTargetCount = 0;
        EndPassCore(target, targets);
    }

    /// <summary>
    /// Where the pipeline renders this frame: null for the window. Set by the
    /// renderer around <see cref="Render"/>, never by a pipeline.
    /// </summary>
    public RenderTarget? FrameTarget { get; protected set; }

    /// <summary>
    /// When set, every frame is also rendered into this target, in the same
    /// command list as the window's.
    /// </summary>
    // The D3D backends have no headless fixture, so a real offscreen pass plus
    // the debug-layer count is their test. Same command list: calling Render
    // twice would reset D3D12's allocator while the GPU may still read it.
    public RenderTarget? ProbeTarget { get; set; }

    /// <summary>
    /// When set, the frame's resolve also runs into this target, in the same
    /// frame and command list. Needs <see cref="HdrEnabled"/>.
    /// </summary>
    // Detects a double sRGB encode on the shared present path, which raises no
    // error anywhere. With HDR off there is no intermediate to resolve from.
    public RenderTarget? CompareTarget { get; set; }

    /// <summary>
    /// Error and corruption messages the graphics debug layer has reported over
    /// this renderer's life. Zero when no debug layer runs.
    /// </summary>
    public int DebugLayerErrorCount { get; private set; }

    // Render thread only.
    private protected void NoteDebugLayerErrors(int count) => DebugLayerErrorCount += count;

    // Shared colour targets. Both D3D backends implement these, OpenGL does
    // not; the defaults refuse. D3D11 draws into a shared keyed-mutex texture.
    // A D3D12-created handle is refused by the importer (E_NOINTERFACE), so
    // D3D12 renders privately and copies into a D3D11On12-owned texture.

    /// <summary>
    /// The keyed-mutex key the producer acquires and the consumer releases.
    /// </summary>
    // A new keyed mutex starts released on key 0, so the producer goes first.
    public const ulong SharedProducerKey = 0;

    /// <summary>
    /// The keyed-mutex key the producer releases and the consumer acquires.
    /// </summary>
    public const ulong SharedConsumerKey = 1;

    /// <summary>
    /// A colour target something outside this renderer can import, named by an
    /// NT handle. A shared target is recreated on resize, never resized in place.
    /// </summary>
    /// <param name="NtHandle">The shared resource's NT handle, or zero when there is none.</param>
    /// <param name="Generation">
    /// Bumped every time the resource behind the handle is recreated. A consumer
    /// re-imports when it changes.
    /// </param>
    // The size rides with the handle so the two cannot be read a frame apart.
    public readonly record struct SharedTargetHandle(nint NtHandle, int Width, int Height, int Generation);

    /// <summary>
    /// The handle of this renderer's shared colour target, if it has one.
    /// </summary>
    public virtual bool TryGetSharedHandle(out SharedTargetHandle handle)
    {
        handle = default;
        return false;
    }

    /// <summary>
    /// Tells the renderer the consumer has stopped using
    /// <paramref name="generation"/> and every one before it, so their
    /// resources may be freed. Render thread only.
    /// </summary>
    // A resize retires the old resource instead of freeing it, because the
    // consumer may still be sampling it.
    public virtual void NotifySharedTargetReleased(int generation)
    {
    }

    /// <summary>
    /// Takes the shared target's key for this frame's write. False means the key
    /// did not arrive within <paramref name="timeoutMs"/>; skip the shared write
    /// for this frame rather than waiting.
    /// </summary>
    // Producer acquires SharedProducerKey and releases SharedConsumerKey; the
    // consumer does the reverse. Every touch of the texture goes inside the
    // bracket: a clear without the key returns S_OK and writes nothing.
    // A timeout means the consumer is not being drawn. Don't block on it.
    public virtual bool BeginSharedWrite(int timeoutMs = 100) => false;

    // Frame time includes the wait for the consumer to hand the key back, so
    // it is tracked separately to tell a slow engine from a slow consumer.
    private long _sharedAcquireTicks;
    private long _sharedAcquirePeakTicks;
    private int _sharedAcquireSamples;

    /// <summary>
    /// Records one wait for the shared target's key. A backend calls this
    /// around the acquire in <see cref="BeginSharedWrite"/>.
    /// </summary>
    protected void RecordSharedAcquireWait(long ticks)
    {
        _sharedAcquireTicks += ticks;
        _sharedAcquireSamples++;
        if (ticks > _sharedAcquirePeakTicks) _sharedAcquirePeakTicks = ticks;
    }

    /// <summary>
    /// Returns the average and peak acquire wait since the last call, in
    /// milliseconds, and resets them.
    /// </summary>
    public void DrainSharedAcquireWait(out float averageMs, out float peakMs)
    {
        double toMs = 1000.0 / Stopwatch.Frequency;
        averageMs = _sharedAcquireSamples > 0
            ? (float)(_sharedAcquireTicks * toMs / _sharedAcquireSamples)
            : 0f;
        peakMs = (float)(_sharedAcquirePeakTicks * toMs);

        _sharedAcquireTicks = 0;
        _sharedAcquirePeakTicks = 0;
        _sharedAcquireSamples = 0;
    }

    /// <summary>
    /// Hands the shared target to the consumer. Call only after
    /// <see cref="BeginSharedWrite"/> returned true.
    /// </summary>
    public virtual void EndSharedWrite()
    {
    }

    // The two members below stand in for a compositor in headless runs
    // (--viewport-compare). With no consumer, nothing hands key 0 back and the
    // producer's second frame times out.

    // Acquire the consumer key, release the producer key. False if the turn
    // never arrived or there is no shared target.
    internal virtual bool TakeSharedConsumerTurn(int timeoutMs = 100) => false;

    // What an importer of the shared handle would see: packed RGBA8, rows as
    // ReadTargetPixels orders them. On D3D12 this reads the bridge's copy, not
    // the private present target. Holds the key across the read and hands the
    // producer its turn back.
    internal virtual bool TryReadSharedPixels(Span<byte> destination, int timeoutMs = 100) => false;

    private RenderTarget? _sceneTarget;
    private Mesh? _fullscreenTriangle;
    private ShaderProgram? _resolveShader;
    private PostPass? _resolvePass;

    /// <summary>
    /// Whether the scene renders into a half-float target and is tone-mapped on
    /// the way to the window. On by default.
    /// </summary>
    public bool HdrEnabled { get; set; } = true;

    /// <summary>
    /// Linear multiplier applied to scene radiance before the tone curve. 1 is
    /// neutral.
    /// </summary>
    public float Exposure { get; set; } = 1f;

    /// <summary>The scene's HDR target, or null before one exists.</summary>
    public RenderTarget? SceneTarget => _sceneTarget;

    /// <summary>
    /// Creates or resizes the HDR scene target. Null when the window has no size.
    /// </summary>
    // Sized to the window: the culling frustum's aspect ratio is seeded from
    // the framebuffer, and a different shape would make it disagree with what
    // is drawn.
    protected RenderTarget? EnsureSceneTarget()
    {
        Vector2D<int> size = FramebufferSize;
        // Minimised or mid-resize.
        if (size.X <= 0 || size.Y <= 0) return null;

        if (_sceneTarget is null)
        {
            _sceneTarget = CreateRenderTarget(new RenderTargetDesc(
                size.X, size.Y, TextureFormat.Rgba16Float, TextureColorSpace.Linear));
        }
        else
        {
            _sceneTarget.Resize(size.X, size.Y);
        }
        return _sceneTarget;
    }

    /// <summary>The clip-space triangle every full-screen pass draws.</summary>
    protected Mesh EnsureFullscreenTriangle() =>
        _fullscreenTriangle ??= CreateMesh(
            FullscreenTriangle.BuildVertices(Backend),
            FullscreenTriangle.Indices,
            VertexAttribute.StandardLayout,
            MeshCpuAccess.None);

    /// <summary>
    /// Draws <paramref name="source"/> to <paramref name="output"/> through the
    /// tone-mapping resolve. <paramref name="output"/> null means the window.
    /// </summary>
    protected void ResolveTo(Texture source, RenderTarget? output, Scene.Scene? scene)
    {
        using var measured = Profiler.Measure(Diagnostics.FramePhase.Resolve);

        _resolveShader ??= CreateBaseShader(BaseShaders.PostResolveFileName);
        _resolvePass ??= new PostPass(_resolveShader);

        // Its own pass, so before the resolve's opens: passes do not nest.
        RenderTarget? mask = scene is not null ? DrawOutlineMask(scene.Camera) : null;

        // With no mask the sampler still needs a texture, and the shader is
        // told not to read it.
        Texture maskTexture = mask?.ColorTexture ?? source;
        float step = Outlines.Width;

        _resolvePass
            .SetUniform("uResolve", new Vector4(Exposure, mask is null ? 0f : 1f, 0f, 0f))
            .SetUniform("uOutlineStep", mask is null
                ? Vector4.Zero
                : new Vector4(step / mask.Width, step / mask.Height, 0f, 0f))
            .SetUniform("uOutlineSelected", new Vector4(Outlines.SelectedColor, 1f))
            .SetUniform("uOutlineHovered", new Vector4(
                Outlines.HoveredColor, Math.Clamp(Outlines.HoveredStrength, 0f, 1f)))
            .SetTexture("uSource", 0, source)
            .SetTexture("uOutlineMask", 1, maskTexture);

        // No clear: the triangle covers every pixel.
        BeginPass(output, PassClear.Keep);
        try
        {
            DrawFullscreen(_resolvePass, EnsureFullscreenTriangle());

            // Overlay goes on top of the resolved image, outside the tone curve.
            if (scene is not null && DebugDraw.VertexCount > 0)
                FlushDebugDrawCore(scene.Camera);
        }
        finally
        {
            EndPass();
        }
    }

    /// <summary>
    /// The meshes to outline this frame. Cleared by the engine every frame.
    /// </summary>
    public OutlineList Outlines { get; } = new();

    /// <summary>
    /// Whether <see cref="Outlines"/> is drawn. The outline is part of the
    /// tone-mapping resolve, so it needs <see cref="HdrEnabled"/>.
    /// </summary>
    public bool SupportsOutlines => HdrEnabled;

    private RenderTarget? _outlineMask;
    private ShaderProgram? _outlineShader;
    private PostPass? _outlineClearPass;

    // Draws every queued mesh's silhouette into the mask: red for selected,
    // green for hovered. Null when there is nothing to outline.
    //
    // The mask has its own depth buffer and nothing else is in it, so the
    // silhouette is the whole object even where the scene hides part of it.
    private RenderTarget? DrawOutlineMask(Scene.Camera camera)
    {
        if (Outlines.Count == 0) return null;

        Vector2D<int> size = FramebufferSize;
        if (size.X <= 0 || size.Y <= 0) return null;

        // Created and sized before the pass opens, never inside it.
        _outlineShader ??= CreateBaseShader(BaseShaders.OutlineMaskFileName);
        _outlineClearPass ??= new PostPass(_outlineShader);
        Mesh triangle = EnsureFullscreenTriangle();
        if (_outlineMask is null)
            _outlineMask = CreateRenderTarget(new RenderTargetDesc(size.X, size.Y));
        else
            _outlineMask.Resize(size.X, size.Y);

        ShaderProgram shader = _outlineShader;
        Matrix4x4 viewProjection = camera.View * camera.Projection * ClipZCorrection;
        IReadOnlyList<OutlineItem> items = Outlines.Items;

        _outlineClearPass
            .SetUniform("uModel", Matrix4x4.Identity)
            .SetUniform("uViewProjection", Matrix4x4.Identity)
            .SetUniform("uMaskColor", Vector4.Zero);

        BeginPass(_outlineMask, PassClear.DepthOnly);
        try
        {
            // The colour is cleared by a draw, not by the pass. A D3D12 target
            // is created with one optimised clear colour, the sky, and clearing
            // to any other warns every frame.
            DrawFullscreen(_outlineClearPass, triangle);

            for (int i = 0; i < items.Count; i++)
            {
                OutlineItem item = items[i];
                Vector4 channel = item.Group == OutlineGroup.Selected
                    ? new Vector4(1f, 0f, 0f, 1f)
                    : new Vector4(0f, 1f, 0f, 1f);

                // Per draw, in this order: D3D flushes staged uniforms on Use.
                if (BindsProgramBeforeUniforms) shader.Use();
                shader.SetUniform("uModel", item.World);
                shader.SetUniform("uViewProjection", viewProjection);
                shader.SetUniform("uMaskColor", channel);
                if (!BindsProgramBeforeUniforms) shader.Use();
                item.Mesh.Draw();
            }
        }
        finally
        {
            EndPass();
        }

        return _outlineMask;
    }

    /// <summary>
    /// Draws <paramref name="geometry"/> with <paramref name="pass"/>'s program
    /// and values, with depth testing off and solid fill.
    /// </summary>
    // Per backend: the order of Use() and the uniform writes differs.
    protected abstract void DrawFullscreen(PostPass pass, Mesh geometry);

    internal void ResolveForTest(Texture source, RenderTarget? output) => ResolveTo(source, output, null);

    // With a scene, the resolve also draws the outline mask and the overlay.
    internal void ResolveForTest(Texture source, RenderTarget? output, Scene.Scene scene) =>
        ResolveTo(source, output, scene);

    internal Mesh EnsureFullscreenTriangleForTest() => EnsureFullscreenTriangle();

    // Clears a target outside a frame. On D3D12 a pass outside a frame writes
    // into a closed command list, hence the command scope.
    internal void ClearForTest(RenderTarget target, Vector4 color)
    {
        ArgumentNullException.ThrowIfNull(target);

        BeginOutOfFrameCommands();
        try
        {
            BeginPass(target, PassClear.To(color));
            EndPass();
        }
        finally
        {
            EndOutOfFrameCommands();
        }
    }

    private Mesh? _orientationQuadFull;
    private Mesh? _orientationQuadTopHalf;
    private PostPass? _orientationPass;

    // Draws source over target through a quad whose UVs carry no per-backend
    // adjustment, after clearing to opaque black. Used with ReadTargetPixel to
    // measure which way up a texture arrives. Safe outside a frame.
    internal void DrawOrientationQuad(Texture source, RenderTarget target, OrientationQuad.Coverage coverage)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        // Create GPU resources before the command scope opens: on D3D12 a mesh
        // upload and a first PSO execute and wait on their own.
        _resolveShader ??= CreateBaseShader(BaseShaders.PostResolveFileName);
        _orientationPass ??= new PostPass(_resolveShader);

        Mesh quad = coverage == OrientationQuad.Coverage.TopHalf
            ? _orientationQuadTopHalf ??= CreateOrientationQuad(coverage)
            : _orientationQuadFull ??= CreateOrientationQuad(coverage);

        // Exposure fixed at 1 so the measurement does not depend on the run's,
        // and no outline: the mask sampler just needs something bound.
        _orientationPass
            .SetUniform("uResolve", new Vector4(1f, 0f, 0f, 0f))
            .SetTexture("uSource", 0, source)
            .SetTexture("uOutlineMask", 1, source);

        BeginOutOfFrameCommands();
        try
        {
            BeginPass(target, PassClear.To(new Vector4(0f, 0f, 0f, 1f)));
            try
            {
                DrawFullscreen(_orientationPass, quad);
            }
            finally
            {
                EndPass();
            }
        }
        finally
        {
            EndOutOfFrameCommands();
        }
    }

    private Mesh CreateOrientationQuad(OrientationQuad.Coverage coverage) => CreateMesh(
        OrientationQuad.BuildVertices(coverage),
        OrientationQuad.Indices,
        VertexAttribute.StandardLayout,
        MeshCpuAccess.None);

    // Reads one texel of the colour attachment as RGBA8. Coordinates are
    // picture space on every backend: x from the left, y from the bottom.
    // Render thread only; stalls on the GPU. Diagnostics and tests, not frames.
    internal abstract (byte R, byte G, byte B, byte A) ReadTargetPixel(RenderTarget target, int x, int y);

    // Reads a rectangle as packed RGBA8, same picture space: y from the
    // bottom, and row 0 of destination is the region's bottom row.
    // Backends override this with one copy and one map. This default is slow
    // and exists for renderers with no GPU under them.
    internal virtual void ReadTargetPixels(
        RenderTarget target, int x, int y, int width, int height, Span<byte> destination)
    {
        PixelReadback.ValidateRegion(target, x, y, width, height, destination);

        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                (byte r, byte g, byte b, byte a) = ReadTargetPixel(target, x + column, y + row);
                int offset = ((row * width) + column) * 4;
                destination[offset] = r;
                destination[offset + 1] = g;
                destination[offset + 2] = b;
                destination[offset + 3] = a;
            }
        }
    }

    internal void ReadTargetPixels(RenderTarget target, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(target);
        ReadTargetPixels(target, 0, 0, target.Width, target.Height, destination);
    }

    /// <summary>
    /// Opens a command scope for work issued outside a frame. Does nothing on
    /// immediate-mode backends. For diagnostics; nothing may nest inside it.
    /// </summary>
    protected internal virtual void BeginOutOfFrameCommands()
    {
    }

    /// <summary>
    /// Closes the scope <see cref="BeginOutOfFrameCommands"/> opened and blocks
    /// until the GPU has executed it.
    /// </summary>
    protected internal virtual void EndOutOfFrameCommands()
    {
    }

    /// <summary>
    /// Frees the intermediate targets and the full-screen pass resources. Render
    /// thread, before the device goes away.
    /// </summary>
    protected void ReleaseFrameResources()
    {
        if (_sceneTarget is not null)
        {
            DestroyRenderTarget(_sceneTarget);
            _sceneTarget = null;
        }

        if (_outlineMask is not null)
        {
            DestroyRenderTarget(_outlineMask);
            _outlineMask = null;
        }

        _outlineShader = null;
        _outlineClearPass = null;

        _gbuffer?.Dispose();
        _gbuffer = null;

        _shadowMap?.Dispose();
        _shadowMap = null;
        _shadowShader = null;
        _shadowInstancedShader = null;
        _unshadowed = null;

        // Shaders are freed by the backend's program list. Instance buffers
        // are only held here.
        _shadowInstances?.Dispose();
        _shadowInstances = null;
        _shadowInstanceCapacity = 0;
        _shadowInstanceHighWater = 0;

        _gbufferInstancedShader = null;
        _geometryInstances?.Dispose();
        _geometryInstances = null;
        _geometryInstanceCapacity = 0;
        _geometryInstanceHighWater = 0;

        _fullscreenTriangle = null;
        _orientationQuadFull = null;
        _orientationQuadTopHalf = null;
        _orientationPass = null;
        _resolveShader = null;
        _resolvePass = null;
        _gbufferShader = null;
        _lightShader = null;
        _lightPass = null;
    }

    private GBuffer? _gbuffer;
    private ShaderProgram? _gbufferShader;
    private ShaderProgram? _lightShader;
    private PostPass? _lightPass;

    /// <summary>
    /// Multiplied into a GL-convention projection matrix to produce this
    /// backend's clip Z. Identity on OpenGL, the 0..1 remap on D3D.
    /// </summary>
    public virtual Matrix4x4 ClipZCorrection => Matrix4x4.Identity;

    /// <summary>
    /// Scale and bias turning a 0..1 depth texel back into this backend's NDC z.
    /// The inverse of <see cref="ClipZCorrection"/>.
    /// </summary>
    public virtual Vector2 DepthToNdcZ => new(2f, -1f);

    /// <summary>
    /// Whether row zero of a render target is its top row, as on D3D and not
    /// on OpenGL.
    /// </summary>
    public virtual bool TargetOriginIsTopLeft => true;

    private ShadowMap? _shadowMap;
    private ShaderProgram? _shadowShader;
    private ShaderProgram? _shadowInstancedShader;
    private readonly RenderView _shadowView = new();

    // One buffer for the whole shadow pass. Grows, never shrinks.
    private InstanceBuffer? _shadowInstances;
    private int _shadowInstanceCapacity;
    private int _shadowInstanceHighWater;

    // Summed over cascades, not maxed: they share the buffer.
    private int _shadowInstanceDemand;

    // False when the compiler produced no instanced stage.
    private bool _shadowBatchingAvailable = true;

    // Own buffer for the geometry pass, so neither pass's growth can free a
    // buffer the other's open draws reference.
    private ShaderProgram? _gbufferInstancedShader;
    private InstanceBuffer? _geometryInstances;
    private int _geometryInstanceCapacity;
    private int _geometryInstanceHighWater;
    private int _geometryInstanceDemand;
    private bool _geometryBatchingAvailable = true;

    /// <summary>Geometry-pass draws that batching removed this frame.</summary>
    public int GeometryDrawsSaved { get; private set; }

    /// <summary>Shadow-caster draws that batching removed this frame.</summary>
    public int ShadowDrawsSaved { get; private set; }

    /// <summary>The depth offset the rasterizer is currently applying.</summary>
    public DepthBias CurrentDepthBias { get; private set; }

    /// <summary>
    /// Sets the rasterizer's depth offset for the draws that follow. Reset it
    /// to <see cref="DepthBias.None"/> before the pass ends.
    /// </summary>
    public void SetDepthBias(DepthBias bias)
    {
        if (bias == CurrentDepthBias) return;
        CurrentDepthBias = bias;
        ApplyDepthBias(bias);
    }

    /// <summary>Makes <paramref name="bias"/> current on this backend.</summary>
    // D3D12 bakes the bias into the PSO, so it only records the value.
    protected virtual void ApplyDepthBias(DepthBias bias) { }

    /// <summary>
    /// Whether the frame's first directional light casts a shadow. On by default.
    /// </summary>
    public bool ShadowsEnabled { get; set; } = true;

    /// <summary>How dark a shadowed surface goes: 0 is no shadow, 1 is fully unlit by the caster.</summary>
    // Under 1 because nothing bounces light yet.
    public float ShadowStrength { get; set; } = 0.85f;

    /// <summary>The directional shadow map, once one has been created.</summary>
    public ShadowMap? ShadowMap => _shadowMap;

    /// <summary>How many casters the last shadow pass drew.</summary>
    public int ShadowCasterCount { get; private set; }

    // Renders the shadow map and returns the index of the light it was
    // rendered for, or -1 when nothing cast. First directional light only:
    // point lights would need a cube map. Runs before the geometry pass.
    internal int RenderShadowMap(Scene.Scene scene, RenderView view)
    {
        ShadowCasterCount = 0;
        ShadowDrawsSaved = 0;
        if (!ShadowsEnabled) return -1;

        int index = FindShadowCaster(view);
        if (index < 0) return -1;

        ShadowMap map = _shadowMap ??= new ShadowMap(this);
        var direction = new Vector3(
            view.Lights[index].PositionRange.X,
            view.Lights[index].PositionRange.Y,
            view.Lights[index].PositionRange.Z);

        if (!map.Fit(scene.Camera, direction)) return -1;

        _shadowShader ??= CreateBaseShader(BaseShaders.ShadowDepthFileName);

        _shadowInstancedShader ??= TryCreateInstancedBaseShader(BaseShaders.ShadowDepthFileName);

        if (_shadowInstancedShader is null)
            _shadowBatchingAvailable = false;

        // One pass and one depth clear for the whole atlas, then a viewport
        // per cascade. A clear per cascade would wipe the ones already drawn.
        using var measured = Profiler.Measure(Diagnostics.FramePhase.Shadows);

        BeginPass(map.Target, PassClear.DepthOnly);

        // Slope-scaled raster bias is what fixes acne.
        SetDepthBias(map.RasterBias);
        try
        {
            for (int cascade = 0; cascade < map.FittedCascadeCount; cascade++)
            {
                (int x, int y, int size) = map.TileAt(cascade);
                SetPassViewport(x, y, size, size);

                Matrix4x4 lightViewProjection = map.LightViewProjectionAt(cascade);

                // Culled per cascade against the light, not the camera.
                scene.BuildShadowView(lightViewProjection, _shadowView);

                Matrix4x4 lightClip = lightViewProjection * ClipZCorrection;

                // Batches plus SingleItems is all of Items. Don't draw Items too.
                DrawShadowBatches(_shadowView, lightClip);
                DrawShadowCasters(_shadowView.SingleItems, lightClip);

                DrawShadowCasters(_shadowView.WorldItems, lightClip);
            }
        }
        finally
        {
            SetDepthBias(DepthBias.None);
            EndPass();
        }

        return index;
    }

    private static int FindShadowCaster(RenderView view)
    {
        ReadOnlySpan<RenderLight> lights = view.Lights;
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i].IsDirectional)
                return i;
        }
        return -1;
    }

    // One instanced draw per batch. Transforms are uploaded once for the whole
    // view; a batch is a range within that upload.
    private void DrawShadowBatches(RenderView view, in Matrix4x4 lightClip)
    {
        if (view.Batches.Count == 0)
            return;

        if (!_shadowBatchingAvailable)
        {
            DrawShadowBatchesUnbatched(view, lightClip);
            return;
        }

        ReadOnlySpan<Matrix4x4> transforms = view.InstanceTransforms;

        _shadowInstanceDemand += transforms.Length;
        _shadowInstanceHighWater = Math.Max(_shadowInstanceHighWater, _shadowInstanceDemand);

        if (_shadowInstances is null || _shadowInstances.Remaining < transforms.Length)
        {
            // No room this frame. The buffer grows at the next frame start.
            DrawShadowBatchesUnbatched(view, lightClip);
            return;
        }

        // Append, don't overwrite: D3D12 submits the frame as one command
        // list, so every cascade's draws read the buffer as it is at the end.
        int baseInstance = _shadowInstances.Append(
            MemoryMarshal.Cast<Matrix4x4, float>(transforms), transforms.Length);

        ShaderProgram shader = _shadowInstancedShader!;
        if (BindsProgramBeforeUniforms) shader.Use();
        shader.SetUniform("uLightViewProjection", lightClip);
        if (!BindsProgramBeforeUniforms) shader.Use();

        for (int i = 0; i < view.Batches.Count; i++)
        {
            RenderBatch batch = view.Batches[i];

            batch.Mesh.DrawInstanced(_shadowInstances, batch.Count, baseInstance + batch.Offset);
            ShadowCasterCount += batch.Count;
        }

        ShadowDrawsSaved += view.DrawsSaved;
    }

    // Batched items are not in SingleItems, so a batch that could not be
    // instanced has to be drawn here or it casts no shadow.
    private void DrawShadowBatchesUnbatched(RenderView view, in Matrix4x4 lightClip)
    {
        ReadOnlySpan<Matrix4x4> transforms = view.InstanceTransforms;
        ShaderProgram shader = _shadowShader!;

        for (int i = 0; i < view.Batches.Count; i++)
        {
            RenderBatch batch = view.Batches[i];
            for (int n = 0; n < batch.Count; n++)
            {
                if (BindsProgramBeforeUniforms) shader.Use();
                shader.SetUniform("uModel", transforms[batch.Offset + n]);
                shader.SetUniform("uLightViewProjection", lightClip);
                if (!BindsProgramBeforeUniforms) shader.Use();

                batch.Mesh.Draw();
                ShadowCasterCount++;
            }
        }
    }

    /// <summary>
    /// Sizes both instance buffers and rewinds them. Every backend's
    /// <c>Render</c> calls this once at the top of a frame.
    /// </summary>
    // Once per frame, not per pipeline execution: a frame can run the pipeline
    // twice (ProbeTarget) into one D3D12 command list, and a rewind between
    // them would overwrite transforms the recorded draws still read.
    // Buffers only grow here. Freeing one the open list references removes
    // the device.
    protected void BeginFrameInstanceBuffers()
    {
        EnsureShadowInstanceCapacity();
        EnsureGeometryInstanceCapacity();

        _shadowInstanceDemand = 0;
        _geometryInstanceDemand = 0;
        GeometryDrawsSaved = 0;
        _shadowInstances?.BeginFrame();
        _geometryInstances?.BeginFrame();
    }

    // Sized from last frame's high-water mark, in powers of two, never shrunk.
    // Growing inside the pass is DXGI_ERROR_DEVICE_HUNG on D3D12.
    private void EnsureShadowInstanceCapacity()
    {
        int needed = _shadowInstanceHighWater;
        if (needed <= 0 || (_shadowInstances is not null && _shadowInstanceCapacity >= needed))
            return;

        int capacity = Math.Max(64, _shadowInstanceCapacity);
        while (capacity < needed)
            capacity *= 2;

        _shadowInstances?.Dispose();
        _shadowInstances = CreateInstanceBuffer(
            capacity, VertexAttribute.StandardInstanceLayout, _shadowInstancedShader!);
        _shadowInstanceCapacity = capacity;
    }

    private void DrawShadowCasters(System.Collections.Generic.IReadOnlyList<RenderItem> items, in Matrix4x4 lightClip)
    {
        ShaderProgram shader = _shadowShader!;
        for (int i = 0; i < items.Count; i++)
        {
            RenderItem item = items[i];

            // Material ignored, so alpha-tested surfaces cast solid shadows.
            if (BindsProgramBeforeUniforms) shader.Use();
            shader.SetUniform("uModel", item.World);
            shader.SetUniform("uLightViewProjection", lightClip);
            if (!BindsProgramBeforeUniforms) shader.Use();

            item.Mesh.Draw();
            ShadowCasterCount++;
        }
    }

    // An array uniform is written whole, even on a frame with no shadow map.
    private static readonly Matrix4x4[] IdentityCascades =
        [Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity];

    private static readonly Vector4[] EmptyCascadeRects = new Vector4[ShadowMap.MaxCascades];

    /// <summary>
    /// Whether a program must be bound before its uniforms are written.
    /// </summary>
    // True on OpenGL, where glUniform writes into the active program. D3D
    // stages uniforms and flushes them on Use.
    protected virtual bool BindsProgramBeforeUniforms => false;

    /// <summary>The deferred G-buffer, once one has been created.</summary>
    public GBuffer? GBuffer => _gbuffer;

    // Sized to the window, not to FrameTarget. A frame can run the pipeline
    // into targets of different sizes, and resizing mid command list frees a
    // resource the open D3D12 list references.
    internal GBuffer? EnsureGBuffer()
    {
        Vector2D<int> size = FramebufferSize;
        int width = size.X;
        int height = size.Y;

        // Minimised or mid-resize.
        if (width <= 0 || height <= 0) return null;

        if (_gbuffer is null)
            _gbuffer = new GBuffer(this, width, height, DeferredGBufferLayout);
        else
            _gbuffer.Resize(width, height);

        return _gbuffer;
    }

    // The geometry pass draws every surface with this program and takes only
    // the parameters from the material (Material.ApplyTo).
    internal ShaderProgram EnsureGBufferShader() =>
        _gbufferShader ??= CreateBaseShader(GBufferShaderFileName);

    private GBufferLayout _deferredGBufferLayout;
    /// <summary>
    /// The G-buffer layout. Set before the first deferred frame. Standard omits
    /// the custom target.
    /// </summary>
    public GBufferLayout DeferredGBufferLayout
    {
        get => _deferredGBufferLayout;
        set
        {
            if (value == _deferredGBufferLayout) return;
            if (_gbuffer is not null || _gbufferShader is not null || _gbufferInstancedShader is not null)
                throw new InvalidOperationException("Set the G-buffer layout before the first deferred frame.");
            if (value is not (GBufferLayout.Standard or GBufferLayout.Extended)) throw new ArgumentOutOfRangeException(nameof(value));
            _deferredGBufferLayout = value;
        }
    }
    private string GBufferShaderFileName => DeferredGBufferLayout == GBufferLayout.Standard
        ? BaseShaders.GBufferFillCompactFileName : BaseShaders.GBufferFillFileName;

    // Null disables batching, not the pass.
    private ShaderProgram? EnsureGBufferInstancedShader()
    {
        if (_gbufferInstancedShader is not null || !_geometryBatchingAvailable)
            return _gbufferInstancedShader;

        _gbufferInstancedShader = TryCreateInstancedBaseShader(GBufferShaderFileName);

        // Ask once, or a failed compile repeats every frame.
        if (_gbufferInstancedShader is null)
            _geometryBatchingAvailable = false;

        return _gbufferInstancedShader;
    }

    // The whole deferred geometry pass: batches as instanced draws, then
    // unbatched items, then static-world chunks. Call PrepareGeometryInstancing
    // before opening the pass.
    internal void DrawGeometry(RenderView view, Scene.Camera camera, ShaderProgram shader)
    {
        Matrix4x4 projection = camera.Projection * ClipZCorrection;

        DrawGeometryBatches(view, camera, projection, shader);

        // Batches plus SingleItems is all of Items. Don't draw Items too.
        DrawGeometryItems(view.SingleItems, camera, projection, shader);

        DrawGeometryItems(view.WorldItems, camera, projection, shader);
    }

    // Must run outside the geometry pass: it may compile a shader.
    internal void PrepareGeometryInstancing() => EnsureGBufferInstancedShader();

    private void DrawGeometryBatches(
        RenderView view, Scene.Camera camera, in Matrix4x4 projection, ShaderProgram fallback)
    {
        if (view.Batches.Count == 0)
            return;

        ReadOnlySpan<Matrix4x4> transforms = view.InstanceTransforms;
        _geometryInstanceDemand += transforms.Length;
        _geometryInstanceHighWater = Math.Max(_geometryInstanceHighWater, _geometryInstanceDemand);

        if (_gbufferInstancedShader is null || _geometryInstances is null
            || _geometryInstances.Remaining < transforms.Length)
        {
            DrawGeometryBatchesUnbatched(view, camera, projection, fallback);
            return;
        }

        int baseInstance = _geometryInstances.Append(
            MemoryMarshal.Cast<Matrix4x4, float>(transforms), transforms.Length);

        ShaderProgram shader = _gbufferInstancedShader;
        int saved = 0;

        for (int i = 0; i < view.Batches.Count; i++)
        {
            RenderBatch batch = view.Batches[i];

            if (batch.Material is not { } material)
                continue;

            // Don't hoist the view and projection writes out of the loop. D3D
            // stages uniforms and flushes on Use(), so later batches would draw
            // with the previous batch's material. GL would look fine.
            if (BindsProgramBeforeUniforms) shader.Use();
            shader.SetUniform("uView", camera.View);
            shader.SetUniform("uProjection", projection);
            material.ApplyTo(shader);
            if (!BindsProgramBeforeUniforms) shader.Use();

            // uModel arrives per instance.
            batch.Mesh.DrawInstanced(_geometryInstances, batch.Count, baseInstance + batch.Offset);
            saved += batch.Count - 1;
        }

        // Not view.DrawsSaved: that does not know about the material skip.
        GeometryDrawsSaved += saved;
    }

    // Batched items are not in SingleItems, so they must be drawn here.
    private void DrawGeometryBatchesUnbatched(
        RenderView view, Scene.Camera camera, in Matrix4x4 projection, ShaderProgram shader)
    {
        ReadOnlySpan<Matrix4x4> transforms = view.InstanceTransforms;

        for (int i = 0; i < view.Batches.Count; i++)
        {
            RenderBatch batch = view.Batches[i];
            if (batch.Material is not { } material)
                continue;

            for (int n = 0; n < batch.Count; n++)
                DrawGeometryOne(batch.Mesh, material, transforms[batch.Offset + n], camera, projection, shader);
        }
    }

    private void DrawGeometryItems(
        System.Collections.Generic.IReadOnlyList<RenderItem> items,
        Scene.Camera camera, in Matrix4x4 projection, ShaderProgram shader)
    {
        for (int i = 0; i < items.Count; i++)
        {
            RenderItem item = items[i];
            if (item.Material is { } material)
                DrawGeometryOne(item.Mesh, material, item.World, camera, projection, shader);
        }
    }

    private void DrawGeometryOne(
        Mesh mesh, Material material, in Matrix4x4 model,
        Scene.Camera camera, in Matrix4x4 projection, ShaderProgram shader)
    {
        // A material with no shader of its own still supplies parameters.
        if (BindsProgramBeforeUniforms) shader.Use();
        shader.SetUniform("uModel", model);
        shader.SetUniform("uView", camera.View);
        shader.SetUniform("uProjection", projection);
        material.ApplyTo(shader);
        if (!BindsProgramBeforeUniforms) shader.Use();

        mesh.Draw();
    }

    // Frame boundary only, like the shadow buffer.
    private void EnsureGeometryInstanceCapacity()
    {
        int needed = _geometryInstanceHighWater;
        if (needed <= 0 || _gbufferInstancedShader is null
            || (_geometryInstances is not null && _geometryInstanceCapacity >= needed))
        {
            return;
        }

        int capacity = Math.Max(64, _geometryInstanceCapacity);
        while (capacity < needed)
            capacity *= 2;

        _geometryInstances?.Dispose();
        _geometryInstances = CreateInstanceBuffer(
            capacity, VertexAttribute.StandardInstanceLayout, _gbufferInstancedShader);
        _geometryInstanceCapacity = capacity;
    }

    // Shades the G-buffer into FrameTarget in one full-screen pass and writes
    // the sky where no geometry was drawn.
    internal void DrawDeferredLightPass(
        GBuffer gbuffer, RenderView view, Scene.Camera camera, float ambient, int shadowLightIndex)
    {
        using var measured = Profiler.Measure(Diagnostics.FramePhase.Lighting);

        _lightShader ??= CreateBaseShader(BaseShaders.DeferredLightFileName);
        _lightPass ??= new PostPass(_lightShader);

        // Must be the same product the geometry pass uploaded.
        Matrix4x4 worldToClip = camera.View * camera.Projection * ClipZCorrection;
        if (!Matrix4x4.Invert(worldToClip, out Matrix4x4 clipToWorld))
        {
            _logger.LogWarning("Deferred light pass skipped: the view-projection matrix is not invertible.");
            return;
        }

        _lightPass
            .SetUniform("uInverseViewProjection", clipToWorld)
            .SetUniform("uCameraPosition", camera.Position)
            .SetUniform("uSkyColor", new Vector3(ClearColors.Sky.X, ClearColors.Sky.Y, ClearColors.Sky.Z))
            .SetUniform("uDepthToNdc", DepthToNdcZ)
            .SetTexture("uAlbedoAo", 0, gbuffer.Albedo)
            .SetTexture("uNormalRoughness", 1, gbuffer.NormalRoughness)
            .SetTexture("uMaterialData", 2, gbuffer.MaterialData)
            .SetTexture("uEmissive", 3, gbuffer.Emissive)
            .SetTexture("uDepth", 4, gbuffer.Depth);

        // With no shadow map the G-buffer depth fills the sampler slot and a
        // strength of zero turns the lookup off. An unbound slot behaves
        // differently per backend.
        bool casting = shadowLightIndex >= 0 && _shadowMap is not null;
        ShadowMap? map = casting ? _shadowMap : null;

        _lightPass
            .SetUniform("uShadowLightIndex", casting ? shadowLightIndex : -1)
            .SetUniform("uCascadeCount", map?.FittedCascadeCount ?? 0)
            .SetUniform("uShadowStrength", casting ? ShadowStrength : 0f)
            .SetUniform("uShadowTexel", map?.TexelSize ?? 0f)
            .SetUniform("uShadowDepthBias", map?.CompareBias ?? 0f)
            .SetUniform("uShadowFilterRadius", map?.FilterRadius ?? 1f)
            .SetUniform("uTargetSize", new Vector2(PassSize.X, PassSize.Y))
            // Not PassSize: the G-buffer follows the window, the pass may not.
            // The shader snaps its reads to G-buffer texel centres.
            .SetUniform("uGBufferSize", new Vector2(gbuffer.Width, gbuffer.Height))
            // Matches the V flip FullscreenTriangle bakes in for this backend.
            .SetUniform("uUvToNdc", TargetOriginIsTopLeft
                ? new Vector4(2f, -2f, -1f, 1f)
                : new Vector4(2f, 2f, -1f, -1f))
            .SetTexture("uShadowMap", 5, map?.Depth ?? gbuffer.Depth);

        _lightPass
            .SetUniform("uWorldToShadow", map is not null ? map.WorldToShadow : IdentityCascades)
            .SetUniform("uCascadeRects", map is not null ? map.CascadeRects : EmptyCascadeRects);

        LightUpload.Apply(_lightPass, view, ambient);

        // No clear: the triangle covers every pixel.
        BeginPass(FrameTarget, PassClear.Keep);
        try
        {
            DrawFullscreen(_lightPass, EnsureFullscreenTriangle());
        }
        finally
        {
            EndPass();
        }
    }

    // The lit shader's shadow sampler. Unit 0 is the material's.
    private const int LitShadowMapUnit = 1;

    // Fills that sampler on a frame that casts no shadow. An unbound slot
    // behaves differently per backend.
    private Texture? _unshadowed;

    /// <summary>
    /// Creates <see cref="DefaultShader"/> and what <see cref="DrawLit"/> binds
    /// beside it. Every backend calls this while it initialises.
    /// </summary>
    // Not on first use: D3D12 cannot upload a texture once a frame is open.
    protected void CreateDefaultShader()
    {
        DefaultShader = CreateBaseShader(BaseShaders.LitFileName);
        _unshadowed = CreateTexture(
            [255, 255, 255, 255], 1, 1, TextureFormat.Rgba8, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp);
    }

    // What every draw of one DrawLit call shares.
    private readonly struct LitFrame(
        RenderView view, Matrix4x4 viewMatrix, Matrix4x4 projection, Vector3 cameraPosition,
        float ambient, Vector2 targetSize, ShadowMap? map, int shadowLightIndex)
    {
        public readonly RenderView View = view;
        public readonly Matrix4x4 ViewMatrix = viewMatrix;
        public readonly Matrix4x4 Projection = projection;
        public readonly Vector3 CameraPosition = cameraPosition;
        public readonly float Ambient = ambient;
        public readonly Vector2 TargetSize = targetSize;

        // Null when nothing casts.
        public readonly ShadowMap? Map = map;
        public readonly int ShadowLightIndex = shadowLightIndex;
    }

    /// <summary>
    /// Draws the view inside an open pass, each surface with its material's own
    /// program, lit the way the deferred light pass lights it.
    /// </summary>
    /// <param name="shadowLightIndex">
    /// What <see cref="RenderShadowMap"/> returned for this frame, or -1 for no shadow.
    /// </param>
    // The forward and wireframe pipelines of all three backends draw through
    // this, so there is one upload to keep in step with DrawDeferredLightPass.
    //
    // The order is DrawGeometry's. A parameter a material leaves out keeps the
    // previous draw's value, so the order is part of the picture.
    internal void DrawLit(RenderView view, Scene.Camera camera, float ambient, int shadowLightIndex)
    {
        using var measured = Profiler.Measure(Diagnostics.FramePhase.Geometry);

        bool casting = shadowLightIndex >= 0 && _shadowMap is not null;
        var frame = new LitFrame(
            view, camera.View, camera.Projection * ClipZCorrection, camera.Position, ambient,
            new Vector2(PassSize.X, PassSize.Y), casting ? _shadowMap : null, casting ? shadowLightIndex : -1);

        ReadOnlySpan<Matrix4x4> transforms = view.InstanceTransforms;
        for (int i = 0; i < view.Batches.Count; i++)
        {
            RenderBatch batch = view.Batches[i];
            if (batch.Material is not { } material)
                continue;

            for (int n = 0; n < batch.Count; n++)
                DrawLitOne(batch.Mesh, material, transforms[batch.Offset + n], frame);
        }

        // Batches plus SingleItems is all of Items. Don't draw Items too.
        DrawLitItems(view.SingleItems, frame);
        DrawLitItems(view.WorldItems, frame);
    }

    private void DrawLitItems(System.Collections.Generic.IReadOnlyList<RenderItem> items, in LitFrame frame)
    {
        for (int i = 0; i < items.Count; i++)
        {
            RenderItem item = items[i];
            if (item.Material is { } material)
                DrawLitOne(item.Mesh, material, item.World, frame);
        }
    }

    private void DrawLitOne(Mesh mesh, Material material, in Matrix4x4 model, in LitFrame frame)
    {
        // A material with no program of its own draws with the lit one, as it
        // does in the geometry pass.
        if ((material.Shader ?? DefaultShader) is not { } shader) return;

        ShadowMap? map = frame.Map;

        // Every uniform on every draw: two materials can have two programs,
        // and each program keeps its own copy.
        if (BindsProgramBeforeUniforms) shader.Use();
        shader.SetUniform("uModel", model);
        shader.SetUniform("uView", frame.ViewMatrix);
        shader.SetUniform("uProjection", frame.Projection);
        shader.SetUniform("uCameraPosition", frame.CameraPosition);
        shader.SetUniform("uTargetSize", frame.TargetSize);
        shader.SetUniform("uNdcToUv", NdcToUv);

        // The same values DrawDeferredLightPass uploads.
        shader.SetUniform("uShadowLightIndex", frame.ShadowLightIndex);
        shader.SetUniform("uCascadeCount", map?.FittedCascadeCount ?? 0);
        shader.SetUniform("uShadowStrength", map is not null ? ShadowStrength : 0f);
        shader.SetUniform("uShadowTexel", map?.TexelSize ?? 0f);
        shader.SetUniform("uShadowDepthBias", map?.CompareBias ?? 0f);
        shader.SetUniform("uShadowFilterRadius", map?.FilterRadius ?? 1f);
        shader.SetUniform("uWorldToShadow", map is not null ? map.WorldToShadow : IdentityCascades);
        shader.SetUniform("uCascadeRects", map is not null ? map.CascadeRects : EmptyCascadeRects);
        if ((map?.Depth ?? _unshadowed) is { } shadowTexture)
            shader.SetTexture("uShadowMap", LitShadowMapUnit, shadowTexture);

        LightUpload.Apply(shader, frame.View, frame.Ambient);
        material.ApplyTo(shader);
        if (!BindsProgramBeforeUniforms) shader.Use();

        mesh.Draw();
    }

    /// <summary>
    /// Draws the frame's <see cref="DebugDraw"/> lines with depth testing off,
    /// inside an open pass.
    /// </summary>
    protected abstract void FlushDebugDrawCore(Scene.Camera camera);

    /// <summary>
    /// Draws <see cref="WorldLines"/> alpha-blended with no depth write, inside
    /// an open pass. A null <paramref name="gbuffer"/> depth-tests in hardware;
    /// otherwise the shader tests against the G-buffer's depth texture.
    /// </summary>
    /// <param name="nudge">
    /// How far toward the camera to bias the line, as a fraction of its distance.
    /// </param>
    // Forward: LessEqual, since a grid on a floor is coplanar with it.
    // Deferred: the open pass's own depth is stale, hence the shader test.
    // The nudge is a parameter because Use() and uniform order differ per backend.
    protected abstract void FlushWorldLinesCore(
        Scene.Camera camera, ShaderProgram program, float nudge, GBuffer? gbuffer);

    /// <summary>
    /// Draws this frame's <see cref="WorldLines"/> into the open forward or
    /// wireframe scene pass. Call it last in that pass.
    /// </summary>
    public void FlushWorldLines(Scene.Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (WorldLines.VertexCount == 0)
            return;

        FlushWorldLinesCore(camera, EnsureWorldLineShader(), WorldLineDepthNudge, null);
    }

    /// <summary>
    /// Draws this frame's <see cref="WorldLines"/> over the lit deferred frame,
    /// in its own pass on <see cref="FrameTarget"/> after the light pass.
    /// </summary>
    public void FlushWorldLinesDeferred(Scene.Camera camera, GBuffer gbuffer)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(gbuffer);
        if (WorldLines.VertexCount == 0)
            return;

        ShaderProgram program = EnsureWorldLineBlendShader();

        BeginPass(FrameTarget, PassClear.Keep);
        try
        {
            FlushWorldLinesCore(camera, program, WorldLineDepthNudge, gbuffer);
        }
        finally
        {
            EndPass();
        }
    }

    /// <summary>
    /// ndc.xy to uv for this backend's targets, with the per-backend V flip.
    /// </summary>
    protected Vector4 NdcToUv => TargetOriginIsTopLeft
        ? new Vector4(0.5f, -0.5f, 0.5f, 0.5f)
        : new Vector4(0.5f, 0.5f, 0.5f, 0.5f);

    /// <summary>
    /// How far toward the camera a world line is nudged, as a fraction of its
    /// distance to it.
    /// </summary>
    // LessEqual is not enough for a coplanar line: interpolated depths land an
    // ulp apart and the line stipples. 8 mm at ten units.
    public float WorldLineDepthNudge { get; set; } = 0.0008f;

    private ShaderProgram? _worldLineShader;
    private ShaderProgram? _worldLineBlendShader;

    private ShaderProgram EnsureWorldLineShader() =>
        _worldLineShader ??= CreateBaseShader(BaseShaders.WorldLineFileName);

    private ShaderProgram EnsureWorldLineBlendShader() =>
        _worldLineBlendShader ??= CreateBaseShader(BaseShaders.WorldLineBlendFileName);

    /// <summary>
    /// Compiles the shader the world-line flush will need. Call before the pass
    /// opens.
    /// </summary>
    public void PrepareWorldLines(bool gbuffer)
    {
        if (WorldLines.VertexCount == 0)
            return;

        if (gbuffer)
            _ = EnsureWorldLineBlendShader();
        else
            _ = EnsureWorldLineShader();
    }

    /// <summary>
    /// Draws the debug overlay in its own pass on top of the finished frame.
    /// <paramref name="output"/> must be what the frame resolved into; null is
    /// the window.
    /// </summary>
    // Own pass so the overlay's display colours skip exposure and the tone
    // curve. A composited surface has no back buffer, so output must be passed.
    protected void DrawOverlay(Scene.Scene? scene, RenderTarget? output = null)
    {
        if (scene is null || DebugDraw.VertexCount == 0) return;

        BeginPass(output, PassClear.Keep);
        try
        {
            FlushDebugDrawCore(scene.Camera);
        }
        finally
        {
            EndPass();
        }
    }

    /// <summary>
    /// Narrows the open pass to a sub-rectangle of its target, in texels. The y
    /// origin is the texture's v = 0 edge on every backend. The next
    /// <see cref="BeginPass"/> resets it.
    /// </summary>
    // GL's bottom-left viewport and bottom-left row zero cancel the same way
    // D3D's two top-left conventions do.
    public void SetPassViewport(int x, int y, int width, int height)
    {
        if (!_inPass)
            throw new InvalidOperationException("SetPassViewport was called outside a pass.");

        SetViewportCore(x, y, width, height);
    }

    /// <summary>Applies a viewport rectangle. See <see cref="SetPassViewport"/> for the y convention.</summary>
    protected abstract void SetViewportCore(int x, int y, int width, int height);

    /// <summary>Binds the target, sets the viewport and clears.</summary>
    /// <param name="target">The single target, or null for the window.</param>
    /// <param name="targets">
    /// The full attachment set for a multi-target pass, or empty for a
    /// single-target one.
    /// </param>
    protected abstract void BeginPassCore(
        RenderTarget? target, ReadOnlySpan<RenderTarget> targets, in PassClear clear);

    /// <summary>
    /// Finishes the pass on the backend, e.g. transitions an offscreen target
    /// back to readable.
    /// </summary>
    protected abstract void EndPassCore(RenderTarget? target, ReadOnlySpan<RenderTarget> targets);

    /// <summary>
    /// Creates an offscreen render target. Render thread only. Release it with
    /// <see cref="DestroyRenderTarget"/>, not by disposing it.
    /// </summary>
    public abstract RenderTarget CreateRenderTarget(in RenderTargetDesc desc);

    /// <summary>
    /// Disposes a target created by <see cref="CreateRenderTarget"/>. Render
    /// thread only.
    /// </summary>
    public void DestroyRenderTarget(RenderTarget target)
    {
        if (ReferenceEquals(target, _currentTarget))
            throw new InvalidOperationException(
                "A render target cannot be destroyed while a pass is drawing into it.");

        target.Unregister?.Invoke();
        target.Unregister = null;
        target.Dispose();
    }

    /// <summary>
    /// Renders one frame from <paramref name="view"/>, the culled draw list
    /// <see cref="Scene.Scene.BuildRenderView"/> built. Render thread only.
    /// </summary>
    public virtual void Render(Scene.Scene? scene, RenderView view, double deltaTime)
    {
    }

    public virtual void Shutdown()
    {
        HotReloader.Dispose();
        _logger.LogInformation("Renderer shut down");
    }

    /// <summary>Name of the rendering pipeline in use, e.g. "Forward".</summary>
    public abstract string CurrentPipelineName { get; }

    /// <summary>
    /// Every registered pipeline's name, in registration order. Empty before
    /// <see cref="Initialize"/>.
    /// </summary>
    // Backends return one cached list: it rides every frame snapshot.
    public virtual IReadOnlyList<string> PipelineNames => Array.Empty<string>();

    /// <summary>Cycles to the next registered pipeline and returns its name.</summary>
    public abstract string NextPipeline();

    /// <summary>
    /// Switches to the pipeline named <paramref name="name"/>,
    /// case-insensitively. Returns false and changes nothing when there is none.
    /// </summary>
    public abstract bool TrySelectPipeline(string name);

    /// <summary>
    /// Uploads an interleaved vertex/index stream as a GPU mesh.
    /// <paramref name="cpuAccess"/> decides whether the mesh keeps CPU copies
    /// for raycasts, bounds and debug wireframes; pass
    /// <see cref="MeshCpuAccess.None"/> for meshes nothing reads back.
    /// </summary>
    public abstract Mesh CreateMesh(
        ReadOnlySpan<float> vertices,
        ReadOnlySpan<uint> indices,
        ReadOnlySpan<VertexAttribute> attributes,
        MeshCpuAccess cpuAccess = MeshCpuAccess.Retained);

    /// <summary>Uploads geometry once. Draw views share its GPU and picking storage.</summary>
    public SharedMeshStorage CreateSharedMeshStorage(
        ReadOnlySpan<float> vertices, ReadOnlySpan<uint> indices,
        ReadOnlySpan<VertexAttribute> attributes, MeshCpuAccess cpuAccess = MeshCpuAccess.Retained) =>
        new(this, CreateMesh(vertices, indices, attributes, cpuAccess));

    /// <summary>Begins an unpublished upload. Source memories must stay immutable until completion or disposal.</summary>
    public virtual MeshUpload BeginMeshUpload(ReadOnlyMemory<float> vertices, ReadOnlyMemory<uint> indices,
        ReadOnlySpan<VertexAttribute> attributes, MeshCpuAccess cpuAccess = MeshCpuAccess.Retained, Bsp.Aabb? knownBounds = null) =>
        new(this, CreateMesh(vertices.Span, indices.Span, attributes, cpuAccess), vertices, indices, uploaded: true);

    /// <summary>
    /// Creates a buffer holding <paramref name="capacityInstances"/> instances
    /// of <paramref name="attributes"/>, for <see cref="Mesh.DrawInstanced"/>.
    /// Every attribute must name <see cref="VertexAttribute.InstanceSlot"/>.
    /// </summary>
    /// <param name="program">The shader the buffer will be drawn under.</param>
    // D3D11 validates an input layout against one vertex shader signature, so
    // the layout must be built for the program that draws it. GL and D3D12
    // ignore the parameter.
    public abstract InstanceBuffer CreateInstanceBuffer(
        int capacityInstances,
        ReadOnlySpan<VertexAttribute> attributes,
        ShaderProgram program);

    /// <summary>
    /// Throws if <paramref name="attributes"/> is not a valid instance layout.
    /// Returns the floats per instance.
    /// </summary>
    protected static int ValidateInstanceLayout(
        int capacityInstances, ReadOnlySpan<VertexAttribute> attributes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityInstances);

        if (attributes.Length == 0)
            throw new ArgumentException("An instance layout needs at least one attribute.", nameof(attributes));

        int floats = 0;
        for (int i = 0; i < attributes.Length; i++)
        {
            if (attributes[i].InputSlot != VertexAttribute.InstanceSlot)
            {
                throw new ArgumentException(
                    $"Attribute at location {attributes[i].Location} names input slot " +
                    $"{attributes[i].InputSlot}, but an instance buffer binds " +
                    $"{VertexAttribute.InstanceSlot}.",
                    nameof(attributes));
            }

            floats += (int)attributes[i].ComponentCount;
        }

        return floats;
    }

    /// <summary>
    /// Disposes a mesh created by <see cref="CreateMesh"/>. Use this instead of
    /// <see cref="Mesh.Dispose"/>, which leaves the mesh in the renderer's
    /// tracking list. Render thread only.
    /// </summary>
    public void DestroyMesh(Mesh mesh)
    {
        MeshesDestroyed++;
        mesh.Unregister?.Invoke();
        mesh.Unregister = null;
        mesh.Dispose();
    }

    /// <summary>
    /// Uploads <paramref name="pixels"/> as a 2D texture. Rows are tightly
    /// packed, row 0 first, and row 0 is sampled at v = 0 on every backend.
    /// A colour space the format cannot carry falls back to linear.
    /// </summary>
    // Uploaded textures need no per-backend flip. The GL/D3D origin difference
    // only applies to render targets. The engine treats v = 0 as the bottom of
    // the picture (ImageDecoder flips rows).
    // colorSpace has no default: this layer cannot tell colour from data.
    public Texture CreateTexture(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        TextureFormat format,
        TextureColorSpace colorSpace,
        TextureFilter filter = TextureFilter.Linear,
        TextureWrap wrap = TextureWrap.Repeat)
        => CreateTexture(TextureUploadDesc.SingleLevel(
            pixels, width, height, format, colorSpace, filter, wrap));

    /// <summary>
    /// Uploads a texture from an explicit per-mip layout over one payload, the
    /// form a cooked block-compressed image arrives in. Same orientation and
    /// colour-space contract as the single-span overload.
    /// </summary>
    // Validated here so all three backends refuse a bad layout the same way.
    public Texture CreateTexture(in TextureUploadDesc desc)
    {
        desc.Validate();
        return CreateTextureCore(in desc);
    }

    /// <summary>
    /// The backend's half of <see cref="CreateTexture(in TextureUploadDesc)"/>.
    /// The descriptor is already validated. Registers the result for tracking.
    /// </summary>
    protected abstract Texture CreateTextureCore(in TextureUploadDesc desc);

    /// <summary>Begins an unpublished texture upload. The payload span is not retained.</summary>
    public virtual TextureUpload BeginTextureUpload(in TextureUploadDesc desc) =>
        new(this, CreateTexture(desc), desc, uploaded: true);

    /// <summary>Submits queued upload commands, optionally waiting for them to finish.</summary>
    public virtual void FlushUploads(bool waitForCompletion = false) { }

    internal virtual PreparedTextureData? PrepareTextureUpload(in TextureUploadDesc desc) => null;

    /// <summary>
    /// Disposes a texture created by <see cref="CreateTexture"/>. Render thread
    /// only.
    /// </summary>
    public void DestroyTexture(Texture texture)
    {
        texture.Unregister?.Invoke();
        texture.Unregister = null;
        texture.Dispose();
    }

    public abstract ShaderProgram CreateShader(string vertexSource, string fragmentSource);

    /// <summary>Creates a shader program from a compiled SpectraShade blob.</summary>
    public abstract ShaderProgram CreateShader(PipelineBlob blob);

    /// <summary>Loads a .specshadecomp file and creates a shader program from it.</summary>
    public ShaderProgram LoadCompiledShader(string path)
    {
        var blob = ShaderFileReader.ReadPipelineFromFile(path, Backend)
            ?? throw new InvalidOperationException($"Compiled shader '{path}' has no data for {Backend}");
        return CreateShader(blob);
    }
}
