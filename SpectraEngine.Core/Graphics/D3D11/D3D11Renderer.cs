using Microsoft.Extensions.Logging;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using SpectraEngine.Core.Graphics.Shaders;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

// Disambiguate: our namespace and Silk.NET's API class are both named "D3D11".
using D3D11Api = Silk.NET.Direct3D11.D3D11;
using DxgiApi = Silk.NET.DXGI.DXGI;

namespace SpectraEngine.Core.Graphics.D3D11;

/// <summary>
/// Direct3D 11 implementation of <see cref="Renderer"/>. Owns the device, the
/// immediate context, the swap chain and the back-buffer/depth views.
/// </summary>
public sealed unsafe class D3D11Renderer : Renderer
{
    private readonly D3D11Api _d3d11 = D3D11Api.GetApi();
    private readonly DxgiApi _dxgi = DxgiApi.GetApi();
    internal readonly D3DCompiler _d3dCompiler = D3DCompiler.GetApi();

    private IRenderSurface? _surface;
    private ComPtr<ID3D11Device> _device;
    private ComPtr<ID3D11DeviceContext> _context;
    private ComPtr<IDXGISwapChain1> _swapChain;
    private ComPtr<ID3D11RenderTargetView> _backBufferRtv;
    private ComPtr<ID3D11Texture2D> _depthBuffer;
    private ComPtr<ID3D11DepthStencilView> _depthView;
    private ComPtr<ID3D11RasterizerState> _solidRasterizer;

    // Depth-biased twin of _solidRasterizer, rebuilt when the bias values change.
    private ComPtr<ID3D11RasterizerState> _biasedRasterizer;
    private DepthBias _biasedRasterizerFor;
    private ComPtr<ID3D11DepthStencilState> _defaultDepth;
    private ComPtr<ID3D11DepthStencilState> _overlayDepth;
    private ComPtr<ID3D11DepthStencilState> _worldLineDepth;
    private ComPtr<ID3D11BlendState> _alphaBlend;

    // Tracked so Shutdown can free stragglers. Render thread only, so no lock.
    private readonly HashSet<Mesh> _meshes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Texture> _textures = new(ReferenceEqualityComparer.Instance);
    private readonly List<ShaderProgram> _shaders = [];
    private readonly List<ID3D11RenderPipeline> _pipelines = [];
    private readonly List<RenderTarget> _renderTargets = [];
    private int _pipelineIndex;

    // One cache for the one immediate context. Reset wherever the context's
    // SRV slots are cleared.
    private readonly D3D11BindCache _bindCache = new();

    // Resizes run in Render, not in a window event: the context is the render thread's.
    private Vector2D<int> _swapChainSize;

    // Last size ResizeBuffers refused. Stops a failed resize being retried
    // every frame; cleared by the next one that succeeds.
    private Vector2D<int>? _failedResizeSize;

    private bool _deviceLost;

    // Somebody else presents: no swap chain, the frame resolves into _presentTarget.
    private bool _composited;

    // Shared target the consumer imports. Null on a window surface.
    private D3D11RenderTarget? _presentTarget;

    private SharedTargetRetirement? _retirement;
    private int _presentGeneration;

    // Releasing a key this side never took hands the texture over mid-write.
    private bool _sharedWriteHeld;

    // A hidden consumer times out every frame, so log the first one only.
    private bool _sharedTimeoutLogged;

    private D3D11LineBatch? _lineBatch;
    private ShaderProgram? _debugShader;

    // Only present on a device created with the Debug flag.
    private ComPtr<ID3D11InfoQueue> _infoQueue;

    // Swap-chain rejections are explained on DXGI's queue, not the device's.
    private DxgiDebugMessages? _dxgiMessages;

    /// <summary>
    /// Remaps GL clip z [-1, 1] to D3D's [0, 1]. Post-multiplied onto the
    /// projection so one shader source works on both.
    /// </summary>
    // Row vectors: z_d3d = 0.5*z_gl + 0.5*w_gl.
    public static readonly Matrix4x4 GlToD3dClipZ = new(
        1f, 0f,    0f,   0f,
        0f, 1f,    0f,   0f,
        0f, 0f,   0.5f, 0f,
        0f, 0f,   0.5f, 1f);


    /// <inheritdoc/>
    public override Matrix4x4 ClipZCorrection => GlToD3dClipZ;

    /// <inheritdoc/>
    // Identity: GlToD3dClipZ already put clip z in 0..1.
    public override Vector2 DepthToNdcZ => new(1f, 0f);

    public override GraphicsBackend Backend => GraphicsBackend.D3D11;

    /// <summary>D3D11 creates its own device, so the window must not bring up an OpenGL context.</summary>
    public override GraphicsAPI WindowApi => GraphicsAPI.None;

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

    public override void AcquireContext(IRenderSurface surface) { /* D3D11 immediate context isn't thread-affine */ }
    public override void ReleaseContext(IRenderSurface surface) { }

    public override void Present(IRenderSurface surface)
    {
        if (_swapChain.Handle is not null && !_deviceLost)
        {
            // A TDR usually surfaces here.
            int hr = ((IDXGISwapChain1*)_swapChain.Handle)->Present(VSync ? 1u : 0u, 0);
            if (hr < 0)
            {
                if (DxgiInterop.IsDeviceLost(hr))
                    throw DeviceLost(hr, "presenting a frame");
                SilkMarshal.ThrowHResult(hr);
            }
        }

        // Outside the swap-chain guard: a composited surface has no chain, and
        // the debug layer is the only error detector it has.
        DrainDebugMessages();
    }

    private void DrainDebugMessages()
    {
        int errors = _dxgiMessages?.Drain(_logger, "D3D11") ?? 0;

        if (_infoQueue.Handle is null)
        {
            NoteDebugLayerErrors(errors);
            return;
        }

        var queue = (ID3D11InfoQueue*)_infoQueue.Handle;
        ulong count = queue->GetNumStoredMessages();
        for (ulong i = 0; i < count; i++)
        {
            nuint byteLength = 0;
            if (queue->GetMessageA(i, null, &byteLength) < 0 || byteLength == 0)
                continue;

            byte[] storage = new byte[(int)byteLength];
            fixed (byte* p = storage)
            {
                var msg = (Message*)p;
                if (queue->GetMessageA(i, msg, &byteLength) < 0)
                    continue;

                string text = Encoding.ASCII.GetString(msg->PDescription, (int)msg->DescriptionByteLength).TrimEnd('\0');
                switch (msg->Severity)
                {
                    case MessageSeverity.Corruption:
                    case MessageSeverity.Error:
                        errors++;
                        _logger.LogError("D3D11 debug layer: {Message}", text);
                        break;
                    case MessageSeverity.Warning:
                        _logger.LogWarning("D3D11 debug layer: {Message}", text);
                        break;
                    default:
                        _logger.LogDebug("D3D11 debug layer: {Message}", text);
                        break;
                }
            }
        }
        if (count > 0)
            queue->ClearStoredMessages();

        NoteDebugLayerErrors(errors);
    }

    internal ComPtr<ID3D11Device> Device => _device;
    internal ComPtr<ID3D11DeviceContext> Context => _context;

    public D3D11Renderer(ILogger<Renderer> logger, IShaderCompiler shaderCompiler)
        : base(logger, shaderCompiler)
    {
    }

    public override void Initialize(IRenderSurface surface)
    {
        if (UncappedPresentation)
            _logger.LogInformation("Uncapped presentation unavailable: this D3D11 surface uses the existing bitblt/shared presentation path");
        _surface = surface;

        // The latch, not window.FramebufferSize: the main thread is pumping
        // GLFW events and that read is not thread safe.
        Vector2D<int> size = FramebufferSize;
        _swapChainSize = size;
        _composited = surface.Kind == RenderSurfaceKind.Composited;

        CreateDevice(surface);

        if (!_composited)
        {
            CreateSwapChain(surface.NativeHandle, size.X, size.Y);
            CreateBackBufferViews((uint)size.X, (uint)size.Y);
        }

        CreateDefaultStates();

        CreateDefaultShader();
        _debugShader = CreateBaseShader(BaseShaders.DebugLineFileName);
        _lineBatch = new D3D11LineBatch(_device, _context, (D3D11ShaderProgram)_debugShader!);

        // First registered is the default.
        RegisterPipeline(new D3D11DeferredPipeline());
        RegisterPipeline(new D3D11ForwardPipeline());
        RegisterPipeline(new D3D11WireframePipeline());

        // The host needs the shared handle as soon as Initialize returns.
        EnsurePresentTarget();

        DrainDebugMessages();
        _logger.LogInformation(
            "Renderer initialized (D3D11, pipeline={Pipeline}, surface={Surface})",
            CurrentPipelineName, _composited ? "composited (no swap chain)" : "window");
    }

    public void RegisterPipeline(ID3D11RenderPipeline pipeline)
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
        DrainPendingResize();
        HotReloader.PumpPendingReloads();

        // Once per frame, not per pipeline run: ProbeTarget runs the pipeline twice.
        BeginFrameInstanceBuffers();

        if (_pipelines.Count == 0 || _surface is null) return;

        if (Profiler.Enabled && Profiler.GpuTimer is null && !_gpuTimingUnavailable)
        {
            try { Profiler.GpuTimer = new D3D11GpuTimer((ID3D11Device*)_device.Handle, (ID3D11DeviceContext*)_context.Handle); }
            catch (Exception ex) { _gpuTimingUnavailable = true; _logger.LogWarning(ex, "D3D11 GPU timestamps unavailable"); }
        }
        using var gpuTiming = new Diagnostics.GpuTimestampTimer.FrameScope(Profiler.Enabled ? Profiler.GpuTimer : null);

        // Null on a window surface: null means the back buffer below.
        RenderTarget? present = EnsurePresentTarget();

        // Pane collapsed or mid-layout. Not an error.
        if (_composited && present is null) return;

        var ctx = new D3D11RenderContext
        {
            Renderer = this,
            Device = _device,
            Context = _context,
            Scene = scene,
            View = view,
            DeltaTime = deltaTime,
        };

        // The probe never touches the shared target, so no key needed.
        if (ProbeTarget is { } probe)
        {
            FrameTarget = probe;
            _pipelines[_pipelineIndex].Execute(ctx);
        }

        // Before the live bracket: a consumer turn queued against a generation
        // this frame's resize retired would otherwise wait forever.
        _retirement?.OfferTurns();

        RenderTarget? sceneTarget = HdrEnabled ? EnsureSceneTarget() : present;

        // The bracket covers the pipeline too: with HDR off it draws straight
        // into the shared texture.
        if (present is not null && !BeginSharedWrite())
        {
            // Consumer never took its turn (hidden pane). Skip the frame
            // rather than block the engine.
            return;
        }

        try
        {
            FrameTarget = sceneTarget;
            _pipelines[_pipelineIndex].Execute(ctx);

            // The overlay goes where the resolve went. On a composited surface
            // the window RTV is null and the draw would vanish without an error.
            if (sceneTarget is null || ReferenceEquals(sceneTarget, present))
            {
                DrawOverlay(scene, present);
                return;
            }

            ResolveTo(sceneTarget.ColorTexture!, present, scene);

            // Same source, same frame, into a plain sRGB target for comparison.
            if (CompareTarget is { } reference)
                ResolveTo(sceneTarget.ColorTexture!, reference, scene);
        }
        finally
        {
            if (present is not null) EndSharedWrite();
        }
    }

    protected override void DrawFullscreen(PostPass pass, Mesh geometry)
    {
        var context = (ID3D11DeviceContext*)_context.Handle;

        // Set both: the state here is whatever the last pipeline left.
        context->OMSetDepthStencilState((ID3D11DepthStencilState*)_overlayDepth.Handle, 0);
        context->RSSetState((ID3D11RasterizerState*)_solidRasterizer.Handle);

        // Use last: SetUniform only stages values, Use uploads them. GL is the other way round.
        pass.ApplyTo(pass.Shader);
        pass.Shader.Use();
        geometry.Draw();

        context->OMSetDepthStencilState((ID3D11DepthStencilState*)_defaultDepth.Handle, 0);
    }

    /// <inheritdoc/>
    internal override (byte R, byte G, byte B, byte A) ReadTargetPixel(
        RenderTarget target, int x, int y)
    {
        Span<byte> one = stackalloc byte[4];
        ReadTargetPixels(target, x, y, 1, 1, one);
        return (one[0], one[1], one[2], one[3]);
    }

    /// <inheritdoc/>
    // D3D targets are top-left origin and the contract's y counts from the
    // bottom, so rows are flipped here. Use the mapped RowPitch, never width * 4:
    // the driver may pad rows.
    internal override void ReadTargetPixels(
        RenderTarget target, int x, int y, int width, int height, Span<byte> destination)
    {
        PixelReadback.ValidateRegion(target, x, y, width, height, destination);
        if (target.ColorTexture is not D3D11Texture color)
            throw new ArgumentException("The target has no colour attachment to read.", nameof(target));

        var dev = (ID3D11Device*)_device.Handle;
        var context = (ID3D11DeviceContext*)_context.Handle;

        var desc = new Texture2DDesc
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = color.DxgiFormat,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Staging,
            BindFlags = 0,
            CPUAccessFlags = (uint)CpuAccessFlag.Read,
            MiscFlags = 0,
        };

        ID3D11Texture2D* stagingPtr = null;
        SilkMarshal.ThrowHResult(dev->CreateTexture2D(&desc, null, &stagingPtr));
        ComPtr<ID3D11Texture2D> staging = ComOwnership.Own(stagingPtr);

        try
        {
            uint top = (uint)(target.Height - y - height);
            var box = new Silk.NET.Direct3D11.Box
            {
                Left = (uint)x,
                Top = top,
                Front = 0,
                Right = (uint)(x + width),
                Bottom = top + (uint)height,
                Back = 1,
            };
            context->CopySubresourceRegion(
                (ID3D11Resource*)stagingPtr, 0, 0, 0, 0, color.Resource, 0, &box);

            MappedSubresource mapped = default;
            SilkMarshal.ThrowHResult(context->Map((ID3D11Resource*)stagingPtr, 0, Map.Read, 0, &mapped));
            try
            {
                PixelReadback.CopyRowsBottomFirst((byte*)mapped.PData, mapped.RowPitch, width, height, destination);
            }
            finally
            {
                context->Unmap((ID3D11Resource*)stagingPtr, 0);
            }
        }
        finally
        {
            ComOwnership.Release(ref staging);
        }
    }

    protected override void SetViewportCore(int x, int y, int width, int height)
    {
        var viewport = new Viewport
        {
            TopLeftX = x, TopLeftY = y,
            Width = width, Height = height,
            MinDepth = 0f, MaxDepth = 1f,
        };
        ((ID3D11DeviceContext*)_context.Handle)->RSSetViewports(1, &viewport);
    }

    protected override void BeginPassCore(
        RenderTarget? target, ReadOnlySpan<RenderTarget> targets, in PassClear clear)
    {
        var ctx = (ID3D11DeviceContext*)_context.Handle;

        ID3D11RenderTargetView* rtv;
        ID3D11DepthStencilView* dsv;
        if (target is D3D11RenderTarget offscreen)
        {
            // The attachment may still be bound as an SRV from last frame.
            // D3D11 would unbind it for us but warn on the debug layer.
            UnbindPixelShaderResources();
            rtv = offscreen.Rtv;
            dsv = offscreen.Dsv;
        }
        else
        {
            rtv = (ID3D11RenderTargetView*)_backBufferRtv.Handle;
            dsv = (ID3D11DepthStencilView*)_depthView.Handle;
        }

        // A depth-only target has no RTV. Any other null RTV means the views
        // are gone (failed resize, device loss), so skip the pass.
        bool depthOnly = target is D3D11RenderTarget { Desc.Color: false };
        if (rtv is null && !depthOnly) return;

        Vector2D<int> size = PassSize;
        SetViewportCore(0, 0, size.X, size.Y);

        if (clear.Color is { } color && rtv is not null)
        {
            Span<float> value = stackalloc float[4] { color.X, color.Y, color.Z, color.W };
            fixed (float* pColor = value)
                ctx->ClearRenderTargetView(rtv, pColor);
        }
        // A depth-less target is legal; clearing a null DSV is not.
        if (clear.Depth is { } depth && dsv is not null)
            ctx->ClearDepthStencilView(dsv, (uint)(ClearFlag.Depth | ClearFlag.Stencil), depth, 0);

        if (targets.Length > 1)
        {
            // Attachment 0 was cleared above; the rest are cleared in the loop.
            ID3D11RenderTargetView** views = stackalloc ID3D11RenderTargetView*[targets.Length];

            // Outside the loop: a stackalloc in a loop never frees its space.
            Span<float> clearValue = stackalloc float[4];
            if (clear.Color is { } extraColor)
            {
                clearValue[0] = extraColor.X;
                clearValue[1] = extraColor.Y;
                clearValue[2] = extraColor.Z;
                clearValue[3] = extraColor.W;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                var extra = (D3D11RenderTarget)targets[i];
                views[i] = extra.Rtv;
                if (i > 0 && clear.Color is not null)
                {
                    fixed (float* pExtra = clearValue)
                        ctx->ClearRenderTargetView(extra.Rtv, pExtra);
                }
            }
            ctx->OMSetRenderTargets((uint)targets.Length, views, dsv);
            return;
        }

        if (rtv is null)
            ctx->OMSetRenderTargets(0, null, dsv);
        else
            ctx->OMSetRenderTargets(1, &rtv, dsv);
    }

    protected override void EndPassCore(RenderTarget? target, ReadOnlySpan<RenderTarget> targets)
    {
        if (target is null) return;

        // Unbind so the attachment can be sampled.
        var ctx = (ID3D11DeviceContext*)_context.Handle;
        ID3D11RenderTargetView* none = null;
        ctx->OMSetRenderTargets(1, &none, (ID3D11DepthStencilView*)null);
    }

    private void UnbindPixelShaderResources()
    {
        const int Slots = D3D11BindCache.TrackedSlots;
        var ctx = (ID3D11DeviceContext*)_context.Handle;
        ID3D11ShaderResourceView** none = stackalloc ID3D11ShaderResourceView*[Slots];
        for (int i = 0; i < Slots; i++) none[i] = null;
        ctx->PSSetShaderResources(0, Slots, none);

        // Or SetTexture skips a rebind and the next pass samples null.
        _bindCache.Reset();
    }

    public override RenderTarget CreateRenderTarget(in RenderTargetDesc desc)
    {
        var target = new D3D11RenderTarget(_device, desc);
        target.Unregister = () => _renderTargets.Remove(target);
        _renderTargets.Add(target);
        return target;
    }

    // The shared target on a composited surface, null on a window surface.
    // Rebuilt on a size change, never resized in place: the consumer imported
    // the NT handle. The old one is retired, not freed, because the consumer
    // may still be reading it.
    private RenderTarget? EnsurePresentTarget()
    {
        if (!_composited) return null;

        Vector2D<int> size = FramebufferSize;

        // Collapsed or mid-layout: skip the frame, as EnsureSceneTarget does.
        // The existing target stays, so the imported handle is still valid.
        if (size.X <= 0 || size.Y <= 0) return null;

        if (_presentTarget is { } existing && existing.Width == size.X && existing.Height == size.Y)
            return existing;

        _retirement ??= new SharedTargetRetirement(_logger);

        if (_presentTarget is { } outgoing)
        {
            int retiring = _presentGeneration;
            _presentTarget = null;

            // The flag is about the live target's key.
            _sharedWriteHeld = false;

            // The mutex lives as long as the retired target does.
            IDXGIKeyedMutex* retiredMutex = outgoing.Color is { IsShared: true } retiredColor
                ? retiredColor.KeyedMutex
                : null;

            _retirement.Retire(
                retiring,
                () => DestroyRenderTarget(outgoing),
                () => SharedTargetTurn.Offer(retiredMutex, _logger, retiring));
        }

        // Srgb view over a UNORM resource: the view encodes, and the consumer
        // does not decode a second time. Depth is only used with HDR off, when
        // the scene renders straight into this target.
        var fresh = (D3D11RenderTarget)CreateRenderTarget(new RenderTargetDesc(
            size.X, size.Y, TextureFormat.Rgba8, TextureColorSpace.Srgb,
            Depth: true, TextureFilter.Linear, TextureWrap.Clamp, Color: true,
            RenderTargetSharing.KeyedMutex));

        _presentTarget = fresh;
        _presentGeneration = _retirement.Next();
        _sharedTimeoutLogged = false;

        _logger.LogInformation(
            "Shared present target {Width}x{Height}, generation {Generation}, handle 0x{Handle:X}.",
            size.X, size.Y, _presentGeneration, fresh.Color?.SharedHandle ?? 0);

        return fresh;
    }

    internal RenderTarget? PresentTargetForTest => _presentTarget;

    // Lets a test resize the target without a scene.
    internal RenderTarget? EnsurePresentTargetForTest() => EnsurePresentTarget();

    private D3D11Texture? SharedColor =>
        _presentTarget?.Color is { IsShared: true } color ? color : null;

    /// <inheritdoc/>
    public override bool TryGetSharedHandle(out SharedTargetHandle handle)
    {
        if (SharedColor is { } color && _presentTarget is { } target)
        {
            handle = new SharedTargetHandle(
                color.SharedHandle, target.Width, target.Height, _presentGeneration);
            return true;
        }

        handle = default;
        return false;
    }

    /// <inheritdoc/>
    public override bool BeginSharedWrite(int timeoutMs = 100)
    {
        if (SharedColor is not { } color) return false;

        // Taking the key twice and releasing once deadlocks the consumer.
        if (_sharedWriteHeld)
            throw new InvalidOperationException("BeginSharedWrite was called while the shared key was already held.");

        // Timed so the profiler can tell waiting from working.
        long acquireStartedAt = Stopwatch.GetTimestamp();
        int hr = color.KeyedMutex->AcquireSync(SharedProducerKey, (uint)Math.Max(0, timeoutMs));
        RecordSharedAcquireWait(Stopwatch.GetTimestamp() - acquireStartedAt);

        // WAIT_TIMEOUT (0x102) is a positive HRESULT, so hr < 0 misses it.
        if (hr == WaitTimeout)
        {
            if (!_sharedTimeoutLogged)
            {
                _sharedTimeoutLogged = true;
                _logger.LogInformation(
                    "Shared target key not available within {Timeout} ms; skipping the shared write while the " +
                    "consumer is not taking its turn. It keeps the last frame it was given.", timeoutMs);
            }
            return false;
        }

        if (hr < 0)
        {
            _logger.LogError(
                "Acquiring the shared target key failed: {Code} (0x{Hr:X8}). Skipping this frame's shared write.",
                DxgiInterop.Describe(hr), hr);
            return false;
        }

        // WAIT_ABANDONED (0x80): the key is ours, the previous holder died.
        if (hr == WaitAbandoned)
            _logger.LogWarning("The shared target key was abandoned by its previous holder; taking it anyway.");

        if (_sharedTimeoutLogged)
        {
            _sharedTimeoutLogged = false;
            _logger.LogInformation("Shared target key available again; resuming shared writes.");
        }

        _sharedWriteHeld = true;
        return true;
    }

    /// <inheritdoc/>
    public override void EndSharedWrite()
    {
        if (!_sharedWriteHeld) return;
        _sharedWriteHeld = false;

        if (SharedColor is not { } color) return;

        // Flush before handing the key over. There is no Present to submit the
        // frame, and the consumer is on another device.
        ((ID3D11DeviceContext*)_context.Handle)->Flush();

        int hr = color.KeyedMutex->ReleaseSync(SharedConsumerKey);
        if (hr < 0)
        {
            _logger.LogError(
                "Releasing the shared target key failed: {Code} (0x{Hr:X8}). The consumer will not get this frame.",
                DxgiInterop.Describe(hr), hr);
        }
    }

    /// <inheritdoc/>
    public override void NotifySharedTargetReleased(int generation)
    {
        int released = _retirement?.ConsumerReleased(generation) ?? 0;
        if (released > 0)
            _logger.LogDebug("Released {Count} retired shared target generation(s) up to {Generation}.", released, generation);
    }

    /// <inheritdoc/>
    internal override bool TakeSharedConsumerTurn(int timeoutMs = 100) =>
        WithConsumerKey(timeoutMs, static _ => { });

    /// <inheritdoc/>
    internal override bool TryReadSharedPixels(Span<byte> destination, int timeoutMs = 100)
    {
        if (_presentTarget is not { } target) return false;

        // A Span cannot be captured by the lambda.
        byte[] scratch = new byte[PixelReadback.ByteCount(target.Width, target.Height)];
        bool read = WithConsumerKey(timeoutMs, self => self.ReadTargetPixels(target, scratch));
        if (read) scratch.CopyTo(destination);
        return read;
    }

    // Runs work holding the consumer key, then hands the producer key back.
    // The release must happen even if work throws, or the producer deadlocks.
    private bool WithConsumerKey(int timeoutMs, Action<D3D11Renderer> work)
    {
        if (SharedColor is not { } color) return false;

        int hr = color.KeyedMutex->AcquireSync(SharedConsumerKey, (uint)Math.Max(0, timeoutMs));
        if (hr == WaitTimeout) return false;
        if (hr < 0)
        {
            _logger.LogError(
                "Taking the shared target's consumer turn failed: {Code} (0x{Hr:X8}).",
                DxgiInterop.Describe(hr), hr);
            return false;
        }

        try
        {
            work(this);
        }
        finally
        {
            int released = color.KeyedMutex->ReleaseSync(SharedProducerKey);
            if (released < 0)
            {
                _logger.LogError(
                    "Handing the shared target's key back failed: {Code} (0x{Hr:X8}). The next frame will time out.",
                    DxgiInterop.Describe(released), released);
            }
        }

        return true;
    }

    // WAIT_TIMEOUT. AcquireSync returns it as a positive HRESULT.
    private const int WaitTimeout = 0x00000102;

    // WAIT_ABANDONED: acquired, but the previous holder never released.
    private const int WaitAbandoned = 0x00000080;

    /// <inheritdoc/>
    protected override void FlushDebugDrawCore(Scene.Camera camera)
    {
        if (DebugDraw.VertexCount == 0 || _debugShader is null || _lineBatch is null) return;

        var debug = (D3D11ShaderProgram)_debugShader;
        debug.SetUniform("uView", camera.View);
        debug.SetUniform("uProjection", camera.Projection * GlToD3dClipZ);
        debug.Use();
        var ctx = (ID3D11DeviceContext*)_context.Handle;
        ctx->OMSetDepthStencilState((ID3D11DepthStencilState*)_overlayDepth.Handle, 0);
        _lineBatch.Draw(DebugDraw.Vertices, (uint)DebugDraw.VertexCount);
        ctx->OMSetDepthStencilState((ID3D11DepthStencilState*)_defaultDepth.Handle, 0);
    }

    /// <inheritdoc/>
    protected override void FlushWorldLinesCore(
        Scene.Camera camera, ShaderProgram program, float nudge, GBuffer? gbuffer)
    {
        var typed = (D3D11ShaderProgram)program;

        // One batch per program: an input layout only works with the shader
        // signature it was created against. Under another it fails every draw.
        if (!_worldLineBatches.TryGetValue(program, out D3D11LineBatch? batch))
        {
            batch = new D3D11LineBatch(_device, _context, typed);
            _worldLineBatches[program] = batch;
        }

        // Use() last: it uploads what SetUniform and SetTexture staged.
        typed.SetUniform("uView", camera.View);
        typed.SetUniform("uProjection", camera.Projection * GlToD3dClipZ);
        typed.SetUniform("uCameraPosition", camera.Position);
        typed.SetUniform("uDepthNudge", nudge);
        typed.SetUniform("uFadeCenter", WorldLines.FadeCenter);
        typed.SetUniform("uFadeStart", WorldLines.FadeStart);
        typed.SetUniform("uFadeEnd", WorldLines.FadeEnd);
        typed.SetUniform("uOpacity", WorldLines.Opacity);

        if (gbuffer is not null)
        {
            typed.SetUniform("uNdcToUv", NdcToUv);
            typed.SetUniform("uDepthToNdc", DepthToNdcZ);
            typed.SetUniform("uGBufferSize", new Vector2(gbuffer.Width, gbuffer.Height));
            // Safe to sample: the open pass uses the frame target's depth, not the G-buffer's.
            typed.SetTexture("uDepth", 0, gbuffer.Depth);
        }

        typed.Use();

        var ctx = (ID3D11DeviceContext*)_context.Handle;

        // Forward tests hardware depth. Deferred turns it off: the shader
        // compares against the sampled G-buffer depth.
        ctx->OMSetDepthStencilState(
            (ID3D11DepthStencilState*)(gbuffer is null ? _worldLineDepth.Handle : _overlayDepth.Handle), 0);
        ctx->OMSetBlendState((ID3D11BlendState*)_alphaBlend.Handle, null, 0xFFFFFFFF);
        batch.Draw(WorldLines.Vertices, (uint)WorldLines.VertexCount);
        ctx->OMSetBlendState(null, null, 0xFFFFFFFF);
        ctx->OMSetDepthStencilState((ID3D11DepthStencilState*)_defaultDepth.Handle, 0);
    }

    private readonly Dictionary<ShaderProgram, D3D11LineBatch> _worldLineBatches = [];

    public override Mesh CreateMesh(ReadOnlySpan<float> vertices, ReadOnlySpan<uint> indices,
        ReadOnlySpan<VertexAttribute> attributes, MeshCpuAccess cpuAccess = MeshCpuAccess.Retained)
    {
        MeshesCreated++;
        var litShader = (D3D11ShaderProgram?)DefaultShader
            ?? throw new InvalidOperationException("Default shader must be created before meshes.");
        var mesh = D3D11Mesh.Create(_device, vertices, indices, attributes, litShader.VertexBytecode, cpuAccess);
        mesh.Unregister = () => _meshes.Remove(mesh);
        _meshes.Add(mesh);
        return mesh;
    }

    public override MeshUpload BeginMeshUpload(ReadOnlyMemory<float> vertices, ReadOnlyMemory<uint> indices,
        ReadOnlySpan<VertexAttribute> attributes, MeshCpuAccess cpuAccess = MeshCpuAccess.Retained, Bsp.Aabb? knownBounds = null)
    {
        MeshesCreated++;
        var litShader = (D3D11ShaderProgram?)DefaultShader
            ?? throw new InvalidOperationException("Default shader must be created before meshes.");
        var mesh = D3D11Mesh.Create(_device, vertices.Span, indices.Span, attributes, litShader.VertexBytecode, cpuAccess, deferred: true, knownBounds: knownBounds);
        mesh.Unregister = () => _meshes.Remove(mesh);
        _meshes.Add(mesh);
        return new MeshUpload(this, mesh, vertices, indices, uploaded: false);
    }

    /// <inheritdoc/>
    public override InstanceBuffer CreateInstanceBuffer(
        int capacityInstances, ReadOnlySpan<VertexAttribute> attributes, ShaderProgram program)
    {
        int floats = ValidateInstanceLayout(capacityInstances, attributes);

        // The layout only works with this program's signature.
        if (program is not D3D11ShaderProgram d3dProgram)
            throw new ArgumentException("Shader program belongs to another backend.", nameof(program));

        return new D3D11InstanceBuffer(
            _device, capacityInstances,
            VertexAttribute.StandardLayout, attributes, floats, d3dProgram.VertexBytecode);
    }

    protected override Texture CreateTextureCore(in TextureUploadDesc desc)
    {
        var texture = D3D11Texture.Create(_device, in desc);
        texture.Unregister = () => _textures.Remove(texture);
        _textures.Add(texture);
        return texture;
    }

    public override TextureUpload BeginTextureUpload(in TextureUploadDesc desc)
    {
        desc.Validate();
        var texture = D3D11Texture.Create(_device, in desc, deferred: true);
        texture.Unregister = () => _textures.Remove(texture);
        _textures.Add(texture);
        return new TextureUpload(this, texture, desc);
    }

    public override ShaderProgram CreateShader(string vertexSource, string fragmentSource)
    {
        var shader = D3D11ShaderProgram.Create(_d3dCompiler, _device, _context, _bindCache, vertexSource, fragmentSource);
        _shaders.Add(shader);
        return shader;
    }

    public override ShaderProgram CreateShader(PipelineBlob blob)
    {
        if (blob.Backend != GraphicsBackend.D3D11)
            throw new ArgumentException($"Expected D3D11 blob, got {blob.Backend}");
        if (blob.Format != ShaderDataFormat.SourceText)
            throw new ArgumentException($"D3D11 expects HLSL SourceText, got {blob.Format}");

        string vs = Encoding.UTF8.GetString(blob.VertexData
            ?? throw new InvalidOperationException("Compiled shader has no vertex stage"));
        string ps = Encoding.UTF8.GetString(blob.FragmentData
            ?? throw new InvalidOperationException("Compiled shader has no fragment stage"));

        return CreateShader(vs, ps);
    }

    public override void Shutdown()
    {
        Profiler.GpuTimer?.Dispose();
        Profiler.GpuTimer = null;
        foreach (var pipeline in _pipelines)
            pipeline.Dispose();
        _pipelines.Clear();

        _lineBatch?.Dispose();

        foreach (D3D11LineBatch batch in _worldLineBatches.Values)
            batch.Dispose();

        _worldLineBatches.Clear();
        _lineBatch = null;
        _debugShader = null;

        foreach (var mesh in _meshes) mesh.Dispose();
        _meshes.Clear();

        // Before the target loop: releasing a retired generation mutates _renderTargets.
        _retirement?.ReleaseAll();
        _retirement = null;
        _presentTarget = null;
        _presentGeneration = 0;
        _sharedWriteHeld = false;

        ReleaseFrameResources();

        foreach (var target in _renderTargets)
            target.Dispose();
        _renderTargets.Clear();
        foreach (var tex in _textures) tex.Dispose();
        _textures.Clear();
        foreach (var sh in _shaders) sh.Dispose();
        _shaders.Clear();
        DefaultShader = null;

        // Release, not Dispose: Shutdown can run twice (the engine's crash
        // handler), and ComPtr.Dispose keeps the handle, so it would over-release.
        ReleaseBackBufferViews();
        ComOwnership.Release(ref _solidRasterizer);
        ComOwnership.Release(ref _biasedRasterizer);
        ComOwnership.Release(ref _defaultDepth);
        ComOwnership.Release(ref _overlayDepth);
        ComOwnership.Release(ref _worldLineDepth);
        ComOwnership.Release(ref _alphaBlend);
        EnsureSwapChainWindowed();
        ComOwnership.Release(ref _swapChain);
        ComOwnership.Release(ref _infoQueue);
        _dxgiMessages?.Dispose();
        _dxgiMessages = null;
        ComOwnership.Release(ref _context);
        ComOwnership.Release(ref _device);

        base.Shutdown();
        _logger.LogInformation("Renderer shut down (D3D11)");
    }

    private void CreateDevice(IRenderSurface surface)
    {
        bool composited = surface.Kind == RenderSurfaceKind.Composited;
        if (!composited && (surface.Kind != RenderSurfaceKind.Win32 || surface.NativeHandle == 0))
        {
            throw new InvalidOperationException(
                $"The D3D11 backend needs a Win32 surface with an HWND, or a composited surface it does not " +
                $"present to; this one is {surface.Kind}. On another platform, or for a surface that offers only " +
                "a GL context, use the OpenGL backend.");
        }

        D3DFeatureLevel featureLevel = default;
        D3DFeatureLevel[] requested = [D3DFeatureLevel.Level110];

        // The debug layer needs the Graphics Tools feature. Fall back without it.
        const uint baseFlags = (uint)CreateDeviceFlag.BgraSupport;
        const uint debugFlags = baseFlags | (uint)CreateDeviceFlag.Debug;

        // Null means the system default. With an explicit adapter the driver
        // type must be Unknown: D3D11 refuses Hardware plus an adapter.
        ComPtr<IDXGIAdapter> chosenAdapter = DxgiAdapters.Find(_dxgi, PreferredAdapter, _logger, out string adapterName);
        AdapterName = adapterName;
        D3DDriverType driverType = chosenAdapter.Handle is null ? D3DDriverType.Hardware : D3DDriverType.Unknown;

        fixed (D3DFeatureLevel* featureLevels = requested)
        {
            int hr = EnableDebugLayer ? _d3d11.CreateDevice(
                chosenAdapter,
                driverType,
                Software: 0,
                Flags: debugFlags,
                pFeatureLevels: featureLevels,
                FeatureLevels: (uint)requested.Length,
                SDKVersion: D3D11Api.SdkVersion,
                ppDevice: ref _device,
                pFeatureLevel: ref featureLevel,
                ppImmediateContext: ref _context) : -1;

            if (hr < 0)
            {
                if (EnableDebugLayer)
                    _logger.LogInformation("D3D11 debug layer unavailable (hr=0x{Hr:X}); creating without it.", hr);
                else
                    _logger.LogInformation("D3D11 debug layer off (not requested).");

                SilkMarshal.ThrowHResult(_d3d11.CreateDevice(
                    chosenAdapter,
                    driverType,
                    Software: 0,
                    Flags: baseFlags,
                    pFeatureLevels: featureLevels,
                    FeatureLevels: (uint)requested.Length,
                    SDKVersion: D3D11Api.SdkVersion,
                    ppDevice: ref _device,
                    pFeatureLevel: ref featureLevel,
                    ppImmediateContext: ref _context));

                EnsureDeviceCreated();
            }
            else
            {
                EnsureDeviceCreated();
                DebugLayerActive = true;
                _logger.LogInformation("D3D11 debug layer active.");

                ID3D11InfoQueue* infoQueue = null;
                Guid infoQueueGuid = ID3D11InfoQueue.Guid;
                if (((ID3D11Device*)_device.Handle)->QueryInterface(&infoQueueGuid, (void**)&infoQueue) >= 0)
                    _infoQueue = ComOwnership.Own(infoQueue);

                // The device debug flag brings up the DXGI debug layer too.
                _dxgiMessages = DxgiDebugMessages.Acquire(_dxgi);
            }
        }
    }

    // D3D11CreateDevice can return success and a null device under driver
    // resource pressure. ThrowHResult does not catch that.
    private void EnsureDeviceCreated()
    {
        if (_device.Handle is not null) return;

        throw new GraphicsDeviceLostException(
            "D3D11CreateDevice reported success and returned no device. That is the driver " +
            "declining to create one, usually under resource pressure from another device or " +
            "context coming up at the same moment, and it is not a state this renderer can " +
            "continue from.");
    }

    private void CreateSwapChain(nint hwnd, int width, int height)
    {
        // device -> DXGI device -> adapter -> factory
        ComPtr<IDXGIDevice> dxgiDevice = default;
        Guid dxgiDeviceGuid = IDXGIDevice.Guid;
        SilkMarshal.ThrowHResult(((ID3D11Device*)_device.Handle)->QueryInterface(
            &dxgiDeviceGuid, (void**)dxgiDevice.GetAddressOf()));

        IDXGIAdapter* adapter = null;
        SilkMarshal.ThrowHResult(dxgiDevice.GetAdapter(&adapter));
        Guid factoryGuid = IDXGIFactory2.Guid;
        IDXGIFactory2* factory = null;
        SilkMarshal.ThrowHResult(adapter->GetParent(&factoryGuid, (void**)&factory));

        // Bitblt model (Discard): GetBuffer(0) stays stable, so the cached RTV
        // does too. Flip models rotate the back buffer on every Present.
        // Flags = 0, no ALLOW_MODE_SWITCH: fullscreen is borderless windowed.
        var desc = new SwapChainDesc1
        {
            Width = (uint)width,
            Height = (uint)height,
            // The back buffer encodes sRGB on write. A bitblt chain may be
            // created _SRGB outright; a flip chain (D3D12) may not.
            Format = Silk.NET.DXGI.Format.FormatR8G8B8A8UnormSrgb,
            Stereo = 0,
            SampleDesc = new SampleDesc(1, 0),
            BufferUsage = DxgiApi.UsageRenderTargetOutput,
            BufferCount = 1,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.Discard,
            AlphaMode = AlphaMode.Unspecified,
            Flags = 0,
        };

        IDXGISwapChain1* swapChainPtr = null;
        SilkMarshal.ThrowHResult(factory->CreateSwapChainForHwnd(
            (IUnknown*)_device.Handle,
            hwnd,
            &desc,
            null,
            null,
            &swapChainPtr));
        _swapChain = ComOwnership.Own(swapChainPtr);

        // The window association is per factory, so it must be made on the
        // one that created the chain, before it is released.
        DxgiInterop.SuppressAltEnter(factory, hwnd, _logger, "D3D11");

        adapter->Release();
        factory->Release();
        dxgiDevice.Dispose();
    }

    private void CreateBackBufferViews(uint width, uint height)
    {
        Guid texGuid = ID3D11Texture2D.Guid;
        ID3D11Texture2D* backBuffer = null;
        SilkMarshal.ThrowHResult(((IDXGISwapChain1*)_swapChain.Handle)->GetBuffer(0, &texGuid, (void**)&backBuffer));
        ID3D11RenderTargetView* rtv = null;
        SilkMarshal.ThrowHResult(((ID3D11Device*)_device.Handle)->CreateRenderTargetView((ID3D11Resource*)backBuffer, null, &rtv));
        // Own, not new ComPtr<>(): the constructor AddRefs, and a leaked RTV
        // keeps the back buffer alive, which makes ResizeBuffers fail.
        _backBufferRtv = ComOwnership.Own(rtv);
        backBuffer->Release();

        var depthDesc = new Texture2DDesc
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Silk.NET.DXGI.Format.FormatD24UnormS8Uint,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.DepthStencil,
            CPUAccessFlags = 0,
            MiscFlags = 0,
        };
        ID3D11Texture2D* depthTex = null;
        SilkMarshal.ThrowHResult(((ID3D11Device*)_device.Handle)->CreateTexture2D(&depthDesc, null, &depthTex));
        _depthBuffer = ComOwnership.Own(depthTex);

        ID3D11DepthStencilView* dsv = null;
        SilkMarshal.ThrowHResult(((ID3D11Device*)_device.Handle)->CreateDepthStencilView((ID3D11Resource*)depthTex, null, &dsv));
        _depthView = ComOwnership.Own(dsv);
    }

    // Must be safe to call twice: the resize path and Shutdown can both get here.
    private void ReleaseBackBufferViews()
    {
        ComOwnership.Release(ref _depthView);
        ComOwnership.Release(ref _depthBuffer);
        ComOwnership.Release(ref _backBufferRtv);
    }

    private void CreateDefaultStates()
    {
        // Same defaults as the OpenGL backend.
        var rastDesc = new RasterizerDesc
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.Back,
            FrontCounterClockwise = 1,
            DepthBias = 0,
            DepthBiasClamp = 0f,
            SlopeScaledDepthBias = 0f,
            DepthClipEnable = 1,
            ScissorEnable = 0,
            MultisampleEnable = 0,
            AntialiasedLineEnable = 0,
        };
        ID3D11RasterizerState* rast = null;
        SilkMarshal.ThrowHResult(((ID3D11Device*)_device.Handle)->CreateRasterizerState(&rastDesc, &rast));
        _solidRasterizer = ComOwnership.Own(rast);
        ((ID3D11DeviceContext*)_context.Handle)->RSSetState(rast);

        var depthDesc = new DepthStencilDesc
        {
            DepthEnable = 1,
            DepthWriteMask = DepthWriteMask.All,
            DepthFunc = ComparisonFunc.Less,
            StencilEnable = 0,
        };
        ID3D11DepthStencilState* depth = null;
        SilkMarshal.ThrowHResult(((ID3D11Device*)_device.Handle)->CreateDepthStencilState(&depthDesc, &depth));
        _defaultDepth = ComOwnership.Own(depth);
        ((ID3D11DeviceContext*)_context.Handle)->OMSetDepthStencilState(depth, 0);

        // Depth off: debug lines draw on top.
        var overlayDesc = new DepthStencilDesc
        {
            DepthEnable = 0,
            DepthWriteMask = DepthWriteMask.Zero,
            DepthFunc = ComparisonFunc.Always,
            StencilEnable = 0,
        };
        ID3D11DepthStencilState* overlay = null;
        SilkMarshal.ThrowHResult(((ID3D11Device*)_device.Handle)->CreateDepthStencilState(&overlayDesc, &overlay));
        _overlayDepth = ComOwnership.Own(overlay);

        // World lines. LessEqual: a grid on a floor is coplanar with it.
        // No depth write: the lines are alpha-blended.
        var worldLineDesc = new DepthStencilDesc
        {
            DepthEnable = 1,
            DepthWriteMask = DepthWriteMask.Zero,
            DepthFunc = ComparisonFunc.LessEqual,
            StencilEnable = 0,
        };
        ID3D11DepthStencilState* worldLine = null;
        SilkMarshal.ThrowHResult(((ID3D11Device*)_device.Handle)->CreateDepthStencilState(&worldLineDesc, &worldLine));
        _worldLineDepth = ComOwnership.Own(worldLine);

        // Straight alpha, for world lines.
        var blendDesc = new BlendDesc();
        blendDesc.RenderTarget[0] = new RenderTargetBlendDesc
        {
            BlendEnable = 1,
            SrcBlend = Blend.SrcAlpha,
            DestBlend = Blend.InvSrcAlpha,
            BlendOp = BlendOp.Add,
            SrcBlendAlpha = Blend.One,
            DestBlendAlpha = Blend.InvSrcAlpha,
            BlendOpAlpha = BlendOp.Add,
            RenderTargetWriteMask = (byte)ColorWriteEnable.All,
        };
        ID3D11BlendState* alphaBlend = null;
        SilkMarshal.ThrowHResult(((ID3D11Device*)_device.Handle)->CreateBlendState(&blendDesc, &alphaBlend));
        _alphaBlend = ComOwnership.Own(alphaBlend);
    }

    // Resizes the swap chain to the framebuffer latch. Render thread, between
    // frames. A failed resize keeps running at the old size; only a device
    // loss ends the run.
    private void DrainPendingResize()
    {
        // Read the latch once: the main thread may publish another size while
        // ResizeBuffers runs, and _swapChainSize must record what was built.
        Vector2D<int> newSize = FramebufferSize;

        if (!SwapChainResizePolicy.ShouldResize(
                newSize, _swapChainSize, _failedResizeSize, _swapChain.Handle is not null, _deviceLost))
            return;

        // ResizeBuffers fails with INVALID_CALL while anything holds the back
        // buffer, so drop every binding and flush queued work first.
        var ctx = (ID3D11DeviceContext*)_context.Handle;
        ctx->ClearState();
        ctx->Flush();
        _bindCache.Reset();

        ReleaseBackBufferViews();

        int hr = ((IDXGISwapChain1*)_swapChain.Handle)->ResizeBuffers(
            0u,
            (uint)newSize.X,
            (uint)newSize.Y,
            Silk.NET.DXGI.Format.FormatUnknown,
            0u);

        if (hr < 0)
        {
            if (DxgiInterop.IsDeviceLost(hr))
                throw DeviceLost(hr, $"resizing the swap chain to {newSize.X}×{newSize.Y}");

            _logger.LogError(
                "D3D11 ResizeBuffers to {Width}×{Height} failed: {Code} (0x{Hr:X8}). Staying at {OldWidth}×{OldHeight}.",
                newSize.X, newSize.Y, DxgiInterop.Describe(hr), hr, _swapChainSize.X, _swapChainSize.Y);
            _failedResizeSize = newSize;

            // The chain kept its old buffers, so rebuild the views at the old size.
            CreateBackBufferViews((uint)_swapChainSize.X, (uint)_swapChainSize.Y);
            RestoreDefaultContextState(ctx);
            DrainDebugMessages();
            return;
        }

        CreateBackBufferViews((uint)newSize.X, (uint)newSize.Y);
        _swapChainSize = newSize;
        _failedResizeSize = null;
        RestoreDefaultContextState(ctx);
    }

    /// <inheritdoc/>
    protected override void ApplyDepthBias(DepthBias bias)
    {
        var context = (ID3D11DeviceContext*)_context.Handle;
        if (context is null) return;

        if (bias.IsZero)
        {
            context->RSSetState((ID3D11RasterizerState*)_solidRasterizer.Handle);
            return;
        }

        if (_biasedRasterizer.Handle is null || _biasedRasterizerFor != bias)
        {
            ComOwnership.Release(ref _biasedRasterizer);
            var desc = new RasterizerDesc
            {
                FillMode = FillMode.Solid,
                CullMode = CullMode.Back,
                FrontCounterClockwise = 1,
                DepthBias = bias.Constant,
                // No clamp: a caster edge-on to the light casts no shadow anyway.
                DepthBiasClamp = 0f,
                SlopeScaledDepthBias = bias.SlopeScaled,
                DepthClipEnable = !bias.ClampDepth,
                ScissorEnable = 0,
                MultisampleEnable = 0,
                AntialiasedLineEnable = 0,
            };
            ID3D11RasterizerState* state = null;
            SilkMarshal.ThrowHResult(
                ((ID3D11Device*)_device.Handle)->CreateRasterizerState(&desc, &state));
            _biasedRasterizer = ComOwnership.Own(state);
            _biasedRasterizerFor = bias;
        }

        context->RSSetState((ID3D11RasterizerState*)_biasedRasterizer.Handle);
    }

    // ClearState wiped these. Both exits of the resize path need them back.
    private void RestoreDefaultContextState(ID3D11DeviceContext* ctx)
    {
        ctx->RSSetState((ID3D11RasterizerState*)_solidRasterizer.Handle);
        ctx->OMSetDepthStencilState((ID3D11DepthStencilState*)_defaultDepth.Handle, 0);
    }

    // Releasing a chain still in exclusive fullscreen is undefined behaviour.
    // The engine never enters it, but SuppressAltEnter can fail on an odd driver.
    private void EnsureSwapChainWindowed()
    {
        if (_swapChain.Handle is null || _deviceLost) return;

        int fullscreen = 0;
        IDXGIOutput* output = null;
        if (((IDXGISwapChain1*)_swapChain.Handle)->GetFullscreenState(&fullscreen, &output) >= 0)
        {
            if (fullscreen != 0)
            {
                _logger.LogWarning("D3D11 swap chain was in exclusive fullscreen at shutdown; returning it to windowed first.");
                ((IDXGISwapChain1*)_swapChain.Handle)->SetFullscreenState(false, (IDXGIOutput*)null);
            }
            if (output is not null)
                output->Release();
        }
    }

    private GraphicsDeviceLostException DeviceLost(int hr, string action)
    {
        int reason = _device.Handle is not null
            ? ((ID3D11Device*)_device.Handle)->GetDeviceRemovedReason()
            : 0;

        // Set first: the throw unwinds into Shutdown, which must not touch the dead device.
        _deviceLost = true;

        DrainDebugMessages();

        return new GraphicsDeviceLostException(
            $"D3D11 device lost while {action}: {DxgiInterop.Describe(hr)} (0x{hr:X8}); " +
            $"ID3D11Device::GetDeviceRemovedReason = {DxgiInterop.Describe(reason)} (0x{reason:X8}). " +
            "The engine cannot recreate a device mid-run, so this ends the session.");
    }
}
