using Microsoft.Extensions.Logging;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SpectraEngine.Core.Graphics.Shaders;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace SpectraEngine.Core.Graphics.OpenGL;

public class OpenGLRenderer : Renderer
{
    private GL? _gl;
    private IRenderSurface? _surface;

    // Tracked so Shutdown can free stragglers. Render thread only.
    private readonly HashSet<Mesh> _meshes = new(ReferenceEqualityComparer.Instance);
    private readonly List<ShaderProgram> _shaders = [];
    private readonly HashSet<Texture> _textures = new(ReferenceEqualityComparer.Instance);
    private readonly List<IOpenGLRenderPipeline> _pipelines = [];
    private readonly List<RenderTarget> _renderTargets = [];
    private int _pipelineIndex;
    private OpenGLLineBatch? _lineBatch;
    private ShaderProgram? _debugShader;
    private OpenGLSrgbTarget? _srgbTarget;

    public override GraphicsBackend Backend => GraphicsBackend.OpenGL;

    public override string CurrentPipelineName =>
        _pipelines.Count == 0 ? "None" : _pipelines[_pipelineIndex].Name;

    // Cached: read for every host snapshot.
    private string[] _pipelineNames = [];

    public override IReadOnlyList<string> PipelineNames
    {
        get
        {
            if (_pipelineNames.Length != _pipelines.Count)
            {
                var names = new string[_pipelines.Count];
                for (int i = 0; i < names.Length; i++)
                    names[i] = _pipelines[i].Name;
                _pipelineNames = names;
            }
            return _pipelineNames;
        }
    }

    public OpenGLRenderer(ILogger<Renderer> logger, IShaderCompiler shaderCompiler)
        : base(logger, shaderCompiler)
    {
    }

    public override void Initialize(IRenderSurface surface)
    {
        _surface = surface;
        _gl = GL.GetApi(surface.GLContext
            ?? throw new InvalidOperationException(
                "The OpenGL backend needs a surface carrying a GL context; this one has none. " +
                "A window created with GraphicsAPI.None, or an embedded surface offering only a " +
                "native handle, can only drive a D3D backend."));

        // Do not query the window's size here: this is the render thread and it
        // would race the main thread's glfwPollEvents. The engine feeds the latch.

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.FrontFace(FrontFaceDirection.Ccw);

        EnableFramebufferSrgb(_gl);

        DefaultShader = CreateBaseShader(BaseShaders.LitFileName);
        _debugShader = CreateBaseShader(BaseShaders.DebugLineFileName);
        _lineBatch = new OpenGLLineBatch(_gl);

        // First registered is the default.
        RegisterPipeline(new DeferredPipeline());
        RegisterPipeline(new ForwardPipeline());
        RegisterPipeline(new WireframePipeline());

        _logger.LogInformation("Renderer initialized (OpenGL, pipeline={Pipeline})", CurrentPipelineName);
    }

    /// <summary>Whether what reaches the display is sRGB-encoded, by either route.</summary>
    public bool FramebufferSrgb { get; private set; }

    /// <summary>
    /// True when encoding comes from an offscreen sRGB buffer because the
    /// window's own framebuffer is linear.
    /// </summary>
    public bool UsesSrgbTarget => _srgbTarget is not null;

    // The enable does nothing on a framebuffer that is not sRGB-capable, so
    // ask the driver and fall back to the offscreen target.
    private void EnableFramebufferSrgb(GL gl)
    {
        gl.Enable(EnableCap.FramebufferSrgb);

        if (QueryDefaultFramebufferSrgb(gl))
        {
            FramebufferSrgb = true;
            _logger.LogInformation("Framebuffer sRGB encoding enabled (window framebuffer)");
            return;
        }

        // The usual case: Silk.NET 2.23 cannot ask GLFW for an sRGB window.
        _srgbTarget = new OpenGLSrgbTarget();
        FramebufferSrgb = true;
        _logger.LogInformation(
            "Framebuffer sRGB encoding enabled (offscreen target; the window framebuffer is linear)");
    }

    private void AbandonSrgbTarget()
    {
        _srgbTarget = null;
        FramebufferSrgb = false;
        _logger.LogWarning(
            "The offscreen sRGB target could not be created; colour output will be uncorrected " +
            "and will not match the D3D backends");
    }

    private static unsafe bool QueryDefaultFramebufferSrgb(GL gl)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        // Drain stale errors so a driver rejecting the query is detectable.
        while (gl.GetError() != GLEnum.NoError) { }

        int encoding = 0;
        gl.GetFramebufferAttachmentParameter(
            GLEnum.Framebuffer,
            GLEnum.BackLeft,
            GLEnum.FramebufferAttachmentColorEncoding,
            &encoding);

        return gl.GetError() == GLEnum.NoError && encoding == (int)GLEnum.Srgb;
    }

    /// <summary>Adds a pipeline to the rotation. The first one registered is the default.</summary>
    public void RegisterPipeline(IOpenGLRenderPipeline pipeline)
    {
        pipeline.Initialize(this);
        _pipelines.Add(pipeline);
    }

    public override bool TrySelectPipeline(string name)
    {
        for (int i = 0; i < _pipelines.Count; i++)
        {
            if (!string.Equals(_pipelines[i].Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            _pipelineIndex = i;
            _logger.LogInformation("Pipeline selected: {Pipeline}", CurrentPipelineName);
            return true;
        }

        return false;
    }

    /// <summary>Makes the context current on the render thread and sets the swap interval there.</summary>
    // glfwSwapInterval acts on the calling thread's current context, so one set
    // on the main thread never reaches the render thread's context.
    public override void AcquireContext(IRenderSurface surface)
    {
        base.AcquireContext(surface);
        UncappedPresentationAvailable = surface.GLContext is not null;
        if (UncappedPresentation)
            _logger.LogInformation("Uncapped presentation {Status}", UncappedPresentationAvailable
                ? "requested (GL swap interval 0; driver overrides may still apply)" : "unavailable without a GL context");
        _appliedSwapInterval = UncappedPresentation ? 0 : VSync ? 1 : 0;
        surface.GLContext?.SwapInterval(_appliedSwapInterval);
    }

    // The swap interval is context state, so a VSync change is re-applied in
    // Present, where the context is current.
    private int _appliedSwapInterval;

    /// <inheritdoc/>
    public override void Present(IRenderSurface surface)
    {
        int wanted = UncappedPresentation ? 0 : VSync ? 1 : 0;
        if (wanted != _appliedSwapInterval)
        {
            _appliedSwapInterval = wanted;
            surface.GLContext?.SwapInterval(wanted);
        }

        base.Present(surface);
    }

    /// <inheritdoc/>
    // glUniform writes into the active program.
    protected override bool BindsProgramBeforeUniforms => true;

    /// <inheritdoc/>
    public override bool TargetOriginIsTopLeft => false;

    public override string NextPipeline()
    {
        if (_pipelines.Count == 0)
            return "None";
        _pipelineIndex = (_pipelineIndex + 1) % _pipelines.Count;
        _logger.LogInformation("Pipeline switched to {Pipeline}", CurrentPipelineName);
        return CurrentPipelineName;
    }

    public override void Render(Scene.Scene? scene, RenderView view, double deltaTime)
    {
        HotReloader.PumpPendingReloads();

        // Once per frame, not per pipeline run: ProbeTarget runs the pipeline twice.
        BeginFrameInstanceBuffers();

        if (_pipelines.Count == 0 || _gl is null || _surface is null)
            return;

        if (Profiler.Enabled && Profiler.GpuTimer is null && !_gpuTimingUnavailable)
        {
            try { Profiler.GpuTimer = new OpenGLGpuTimer(_gl); }
            catch (Exception ex) { _gpuTimingUnavailable = true; _logger.LogWarning(ex, "OpenGL GPU timestamps unavailable"); }
        }
        using var gpuTiming = new Diagnostics.GpuTimestampTimer.FrameScope(Profiler.Enabled ? Profiler.GpuTimer : null);

        var context = new OpenGLRenderContext
        {
            Renderer = this,
            Gl = _gl,
            Scene = scene,
            View = view,
            DeltaTime = deltaTime,
        };

        // Probe first, so the window pass has the last word.
        if (ProbeTarget is { } probe)
        {
            FrameTarget = probe;
            _pipelines[_pipelineIndex].Execute(context);
        }

        RenderTarget? sceneTarget = HdrEnabled ? EnsureSceneTarget() : null;
        FrameTarget = sceneTarget;
        _pipelines[_pipelineIndex].Execute(context);

        if (sceneTarget is null)
        {
            DrawOverlay(scene);
            return;
        }

        ResolveTo(sceneTarget.ColorTexture!, null, scene);
    }

    protected override void DrawFullscreen(PostPass pass, Mesh geometry)
    {
        GL gl = _gl!;

        // WireframePipeline leaves polygon mode on Line.
        gl.Disable(EnableCap.DepthTest);
        gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);

        // Use first: glUniform writes into the active program.
        pass.Shader.Use();
        pass.ApplyTo(pass.Shader);
        geometry.Draw();

        gl.Enable(EnableCap.DepthTest);
    }

    // No flip: GL's framebuffer origin is bottom-left, which is the contract.
    internal override unsafe (byte R, byte G, byte B, byte A) ReadTargetPixel(
        RenderTarget target, int x, int y)
    {
        if (target.ColorTexture is not OpenGLTexture color)
            throw new ArgumentException("The target has no colour attachment to read.", nameof(target));

        GL gl = _gl!;
        uint fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fbo);
        gl.FramebufferTexture2D(
            FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, color.Handle, 0);

        var pixel = new byte[4];
        fixed (byte* p = pixel)
            gl.ReadPixels(x, y, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, p);

        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        gl.DeleteFramebuffer(fbo);
        return (pixel[0], pixel[1], pixel[2], pixel[3]);
    }

    // Rows come back bottom-first already. RGBA8 rows satisfy GL's default pack
    // alignment of 4; a three-channel read would need it changed.
    internal override unsafe void ReadTargetPixels(
        RenderTarget target, int x, int y, int width, int height, Span<byte> destination)
    {
        PixelReadback.ValidateRegion(target, x, y, width, height, destination);
        if (target.ColorTexture is not OpenGLTexture color)
            throw new ArgumentException("The target has no colour attachment to read.", nameof(target));

        GL gl = _gl!;
        uint fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fbo);
        gl.FramebufferTexture2D(
            FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, color.Handle, 0);

        fixed (byte* p = destination)
        {
            gl.ReadPixels(
                x, y, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }

        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        gl.DeleteFramebuffer(fbo);
    }

    private bool _passUsedSrgbTarget;

    protected override void SetViewportCore(int x, int y, int width, int height) =>
        _gl!.Viewport(x, y, (uint)width, (uint)height);

    /// <inheritdoc/>
    protected override void ApplyDepthBias(DepthBias bias)
    {
        GL gl = _gl!;
        if (bias.IsZero)
        {
            gl.Disable(EnableCap.PolygonOffsetFill);
            return;
        }

        gl.Enable(EnableCap.PolygonOffsetFill);
        gl.PolygonOffset(bias.SlopeScaled, bias.Constant);
    }

    protected override void BeginPassCore(
        RenderTarget? target, ReadOnlySpan<RenderTarget> targets, in PassClear clear)
    {
        GL gl = _gl!;
        Vector2D<int> size = PassSize;

        if (target is OpenGLRenderTarget offscreen)
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, offscreen.Framebuffer);
            offscreen.BindExtraColorTargets(gl, targets);
            _passUsedSrgbTarget = false;
        }
        else
        {
            _passUsedSrgbTarget = _srgbTarget is not null && _srgbTarget.Begin(gl, size.X, size.Y);
            if (_srgbTarget is { Usable: false })
                AbandonSrgbTarget();
            if (!_passUsedSrgbTarget)
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        SetViewportCore(0, 0, size.X, size.Y);

        uint mask = 0;
        if (clear.Color is { } color)
        {
            gl.ClearColor(color.X, color.Y, color.Z, color.W);
            mask |= (uint)ClearBufferMask.ColorBufferBit;
        }
        if (clear.Depth is { } depth)
        {
            gl.ClearDepth(depth);
            mask |= (uint)(ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
        }
        if (mask != 0)
            gl.Clear(mask);
    }

    protected override void EndPassCore(RenderTarget? target, ReadOnlySpan<RenderTarget> targets)
    {
        if (target is OpenGLRenderTarget offscreen && targets.Length > 1)
            offscreen.UnbindExtraColorTargets(_gl!, targets);

        if (target is not null)
        {
            _gl!.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            return;
        }

        if (!_passUsedSrgbTarget) return;

        _srgbTarget!.Present(_gl!, PassSize.X, PassSize.Y);
        _passUsedSrgbTarget = false;
    }

    public override RenderTarget CreateRenderTarget(in RenderTargetDesc desc)
    {
        var target = new OpenGLRenderTarget(_gl!, desc);
        target.Unregister = () => _renderTargets.Remove(target);
        _renderTargets.Add(target);
        return target;
    }

    /// <inheritdoc/>
    protected override void FlushDebugDrawCore(Scene.Camera camera)
    {
        if (DebugDraw.VertexCount == 0 || _debugShader is null || _lineBatch is null || _gl is null)
            return;

        // Overlay lines draw on top of everything.
        _gl.Disable(EnableCap.DepthTest);

        _debugShader.Use();
        _debugShader.SetUniform("uView", camera.View);
        _debugShader.SetUniform("uProjection", camera.Projection);
        _lineBatch.Draw(DebugDraw.Vertices, (uint)DebugDraw.VertexCount);

        _gl.Enable(EnableCap.DepthTest);
    }

    /// <inheritdoc/>
    protected override void FlushWorldLinesCore(
        Scene.Camera camera, ShaderProgram program, float nudge, GBuffer? gbuffer)
    {
        if (_lineBatch is null || _gl is null)
            return;

        if (gbuffer is null)
        {
            // Forward: hardware depth test. LessEqual because a grid on a floor
            // is coplanar with it and GL defaults to Less. No depth write:
            // the lines are translucent.
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Lequal);
            _gl.DepthMask(false);
        }
        else
        {
            // Deferred: the shader tests against the G-buffer depth texture.
            // The pass target's own depth is stale.
            _gl.Disable(EnableCap.DepthTest);
        }

        // Separate, so stored alpha matches D3D's SrcBlendAlpha = One. Plain
        // BlendFunc would scale alpha by SrcAlpha too.
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFuncSeparate(
            BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha,
            BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);

        // Use first: glUniform writes into the active program.
        program.Use();
        program.SetUniform("uView", camera.View);
        program.SetUniform("uProjection", camera.Projection);
        program.SetUniform("uCameraPosition", camera.Position);
        program.SetUniform("uDepthNudge", nudge);
        program.SetUniform("uFadeCenter", WorldLines.FadeCenter);
        program.SetUniform("uFadeStart", WorldLines.FadeStart);
        program.SetUniform("uFadeEnd", WorldLines.FadeEnd);
        program.SetUniform("uOpacity", WorldLines.Opacity);

        if (gbuffer is not null)
        {
            program.SetUniform("uNdcToUv", NdcToUv);
            program.SetUniform("uDepthToNdc", DepthToNdcZ);
            program.SetUniform("uGBufferSize", new Vector2(gbuffer.Width, gbuffer.Height));
            program.SetTexture("uDepth", 0, gbuffer.Depth);
        }

        _lineBatch.Draw(WorldLines.Vertices, (uint)WorldLines.VertexCount);

        // Context state: restore for the next pass.
        _gl.Disable(EnableCap.Blend);
        _gl.DepthMask(true);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
    }

    // Test seams: draw through the real sRGB path and read the window back.
    internal bool BeginSrgbTargetForTest(int width, int height)
        => _gl is not null && _srgbTarget is not null && _srgbTarget.Begin(_gl, width, height);

    internal void PresentSrgbTargetForTest(int width, int height)
    {
        if (_gl is not null)
            _srgbTarget?.Present(_gl, width, height);
    }

    public override void Shutdown()
    {
        Profiler.GpuTimer?.Dispose();
        Profiler.GpuTimer = null;
        foreach (var pipeline in _pipelines)
            pipeline.Dispose();
        _pipelines.Clear();

        _lineBatch?.Dispose();
        _lineBatch = null;
        _debugShader = null;

        if (_gl is not null) _srgbTarget?.Dispose(_gl);
        _srgbTarget = null;

        // Before the sweeps below, which dispose what these reference.
        ReleaseFrameResources();

        foreach (var mesh in _meshes)
            mesh.Dispose();
        _meshes.Clear();

        foreach (var target in _renderTargets)
            target.Dispose();
        _renderTargets.Clear();

        foreach (var texture in _textures)
            texture.Dispose();
        _textures.Clear();

        foreach (var shader in _shaders)
            shader.Dispose();
        _shaders.Clear();
        DefaultShader = null;

        _gl?.Dispose();
        _gl = null;

        base.Shutdown();
        _logger.LogInformation("Renderer shut down (OpenGL)");
    }

    public override Mesh CreateMesh(ReadOnlySpan<float> vertices, ReadOnlySpan<uint> indices,
        ReadOnlySpan<VertexAttribute> attributes, MeshCpuAccess cpuAccess = MeshCpuAccess.Retained)
    {
        MeshesCreated++;
        var mesh = OpenGLMesh.Create(_gl!, vertices, indices, attributes, cpuAccess);
        mesh.Unregister = () => _meshes.Remove(mesh);
        _meshes.Add(mesh);
        return mesh;
    }

    public override MeshUpload BeginMeshUpload(ReadOnlyMemory<float> vertices, ReadOnlyMemory<uint> indices,
        ReadOnlySpan<VertexAttribute> attributes, MeshCpuAccess cpuAccess = MeshCpuAccess.Retained, Bsp.Aabb? knownBounds = null)
    {
        MeshesCreated++;
        var mesh = OpenGLMesh.Create(_gl!, vertices.Span, indices.Span, attributes, cpuAccess, deferred: true, knownBounds: knownBounds);
        mesh.Unregister = () => _meshes.Remove(mesh);
        _meshes.Add(mesh);
        return new MeshUpload(this, mesh, vertices, indices, uploaded: false);
    }

    /// <inheritdoc/>
    public override InstanceBuffer CreateInstanceBuffer(
        int capacityInstances, ReadOnlySpan<VertexAttribute> attributes, ShaderProgram program)
    {
        // program unused: GL binds attributes into the VAO, not to a shader.
        int floats = ValidateInstanceLayout(capacityInstances, attributes);
        return new OpenGLInstanceBuffer(_gl!, capacityInstances, attributes, floats);
    }

    protected override Texture CreateTextureCore(in TextureUploadDesc desc)
    {
        var texture = OpenGLTexture.Create(_gl!, in desc);
        texture.Unregister = () => _textures.Remove(texture);
        _textures.Add(texture);
        return texture;
    }

    public override TextureUpload BeginTextureUpload(in TextureUploadDesc desc)
    {
        desc.Validate();
        var texture = OpenGLTexture.Create(_gl!, in desc, deferred: true);
        texture.Unregister = () => _textures.Remove(texture);
        _textures.Add(texture);
        return new TextureUpload(this, texture, desc);
    }

    public override ShaderProgram CreateShader(string vertexSource, string fragmentSource)
    {
        var shader = OpenGLShaderProgram.Create(_gl!, vertexSource, fragmentSource);
        _shaders.Add(shader);
        return shader;
    }

    public override ShaderProgram CreateShader(PipelineBlob blob)
    {
        if (blob.Backend != GraphicsBackend.OpenGL)
            throw new ArgumentException($"Expected OpenGL blob, got {blob.Backend}");

        if (blob.Format != ShaderDataFormat.SourceText)
            throw new ArgumentException($"OpenGL requires SourceText format, got {blob.Format}");

        string vertexSource = Encoding.UTF8.GetString(blob.VertexData
            ?? throw new InvalidOperationException("Compiled shader has no vertex stage"));
        string fragmentSource = Encoding.UTF8.GetString(blob.FragmentData
            ?? throw new InvalidOperationException("Compiled shader has no fragment stage"));

        var shader = OpenGLShaderProgram.Create(_gl!, vertexSource, fragmentSource);
        _shaders.Add(shader);
        return shader;
    }
}
