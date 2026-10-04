using Microsoft.Extensions.Logging;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using SpectraEngine.Core.Graphics.Shaders;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

// Disambiguate: our namespace and Silk.NET's API class are both named "D3D12".
using D3D12Api = Silk.NET.Direct3D12.D3D12;
using DxgiApi = Silk.NET.DXGI.DXGI;

namespace SpectraEngine.Core.Graphics.D3D12;

/// <summary>
/// Direct3D 12 implementation of <see cref="Renderer"/>. Owns the device, the
/// queue, the swap chain and the per-frame command list that pipelines record into.
/// </summary>
public sealed unsafe partial class D3D12Renderer : Renderer
{
    internal const Format BackBufferFormat = Format.FormatR8G8B8A8Unorm;

    // A flip-model chain can't be created _SRGB, so the sRGB encode comes from
    // an _SRGB view over the _UNORM buffer. PSOs compile against this format.
    internal const Format BackBufferRtvFormat = Format.FormatR8G8B8A8UnormSrgb;
    internal const Format DepthFormat = Format.FormatD24UnormS8Uint;
    private const uint BufferCount = 2;

    // API limit for a shader-visible sampler heap.
    private const uint MaxSamplerRingCapacity = 2048;

    // Resource-binding tier 1 limit for a shader-visible CBV/SRV/UAV heap.
    private const uint MaxSrvRingCapacity = 1_000_000;

    // D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING
    internal const uint DefaultComponentMapping = 5768;

    internal readonly D3D12Api D3D12Api = D3D12Api.GetApi();
    private readonly DxgiApi _dxgi = DxgiApi.GetApi();
    private readonly D3DCompiler _d3dCompiler = D3DCompiler.GetApi();

    private IRenderSurface? _surface;
    private ComPtr<ID3D12Device> _device;
    private ComPtr<ID3D12CommandQueue> _queue;
    private ComPtr<IDXGISwapChain3> _swapChain;
    private ref ComPtr<ID3D12CommandAllocator> _commandAllocator => ref _frame.Allocator;
    private ref ComPtr<ID3D12GraphicsCommandList> _commandList => ref _frame.List;
    private ComPtr<ID3D12InfoQueue> _infoQueue;
    private DxgiDebugMessages? _dxgiMessages;

    private ComPtr<ID3D12DescriptorHeap> _rtvHeap;
    private ComPtr<ID3D12DescriptorHeap> _dsvHeap;
    private readonly ComPtr<ID3D12Resource>[] _backBuffers = new ComPtr<ID3D12Resource>[BufferCount];
    private ComPtr<ID3D12Resource> _depthBuffer;
    private uint _rtvStride;
    private uint _frameIndex;
    private uint _swapChainFlags;

    private ComPtr<ID3D12Fence> _fence;
    private ulong _fenceValue;
    private nint _fenceEvent;

    // Per-frame linear upload allocator.
    private ref ComPtr<ID3D12Resource> _uploadRing => ref _frame.UploadRing;
    private ref byte* _uploadRingCpu => ref _frame.UploadCpu;
    private ref ulong _uploadRingGpuVa => ref _frame.UploadGpuVa;
    private ref uint _uploadRingCapacity => ref _frame.UploadCapacity;
    private uint _uploadRingOffset;

    // Rings outgrown mid-frame. The open list still holds GPU VAs into them,
    // so they live until the frame's fence completes.
    private List<ComPtr<ID3D12Resource>> _retiredUploadRings => _frame.RetiredUploadRings;

    // Shader-visible descriptor rings, reset each frame. They grow between
    // frames only.
    private ref ComPtr<ID3D12DescriptorHeap> _srvRing => ref _frame.SrvRing;
    private ref ComPtr<ID3D12DescriptorHeap> _samplerRing => ref _frame.SamplerRing;
    private uint _srvStride;
    private uint _samplerStride;
    private uint _srvRingOffset;
    private uint _samplerRingOffset;
    private ref uint _srvRingCapacity => ref _frame.SrvCapacity;
    private ref uint _samplerRingCapacity => ref _frame.SamplerCapacity;
    private ref uint _srvRingPeak => ref _frame.SrvPeak;
    private ref uint _samplerRingPeak => ref _frame.SamplerPeak;

    // Last table staged this frame, reused by a draw that finds the rings full.
    private GpuDescriptorHandle _lastSrvTable;
    private GpuDescriptorHandle _lastSamplerTable;
    private uint _stagedSlotCount;
    private bool _ringOverflowReported;

    // Headroom for draws outside the RenderView, such as the debug overlay.
    private const int DescriptorReserveSlackDraws = 64;

    // Tracked so Shutdown can free stragglers. Render thread only.
    private readonly HashSet<Mesh> _meshes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Texture> _textures = new(ReferenceEqualityComparer.Instance);
    private readonly List<ShaderProgram> _shaders = [];
    private readonly List<ID3D12RenderPipeline> _pipelines = [];
    private readonly List<RenderTarget> _renderTargets = [];
    private int _pipelineIndex;

    // The swap chain's current size. Compared against the framebuffer latch
    // each frame; the resize runs on the render thread between frames.
    private Vector2D<int> _swapChainSize;

    // The last size ResizeBuffers refused, so a failed resize is not retried
    // every frame. Cleared by the next resize that succeeds.
    private Vector2D<int>? _failedResizeSize;

    // Set once the device is gone, so nothing else calls into it.
    private bool _deviceLost;

    // A surface somebody else presents: no swap chain, the frame resolves
    // into _presentTarget.
    private bool _composited;

    // A private D3D12 target, not a shared one: the compositor refuses a
    // D3D12-created handle (E_NOINTERFACE), so the frame is copied out through
    // the bridge. Null on a window surface.
    private D3D12RenderTarget? _presentTarget;

    // D3D11 front end over this device and queue. Owns the shared texture.
    private D3D12On11Bridge? _bridge;

    private SharedTargetRetirement? _retirement;
    private int _presentGeneration;

    // So EndSharedWrite does not release a key that a timed-out Begin never took.
    private bool _sharedWriteHeld;

    // A consumer that is not drawn times out every frame. Log it once.
    private bool _sharedTimeoutLogged;

    private D3D12LineBatch? _lineBatch;
    private ShaderProgram? _debugShader;
    private D3D12Texture? _fallbackTexture;
    private bool _isRecording;

    /// <summary>Remaps GL clip z (-1..1) to D3D's 0..1. Row-vector: z_d3d = 0.5*z_gl + 0.5*w_gl.</summary>
    public static readonly Matrix4x4 GlToD3dClipZ = new(
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 0.5f, 0f,
        0f, 0f, 0.5f, 1f);


    /// <inheritdoc/>
    public override Matrix4x4 ClipZCorrection => GlToD3dClipZ;

    /// <inheritdoc/>
    // Identity: clip z is already 0..1 here.
    public override Vector2 DepthToNdcZ => new(1f, 0f);

    public override GraphicsBackend Backend => GraphicsBackend.D3D12;

    /// <inheritdoc/>
    public override GraphicsAPI WindowApi => GraphicsAPI.None;

    public override void AcquireContext(IRenderSurface surface) { /* not thread-affine */ }
    public override void ReleaseContext(IRenderSurface surface) { }

    public override string CurrentPipelineName =>
        _pipelines.Count == 0 ? "None" : _pipelines[_pipelineIndex].Name;

    // Cached: read on every host snapshot.
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

    internal ID3D12Device* DevicePtr => (ID3D12Device*)_device.Handle;

    // Null outside a frame.
    internal ID3D12GraphicsCommandList* CurrentList => _isRecording ? (ID3D12GraphicsCommandList*)_commandList.Handle : null;

    // Meshes resolve their PSO against this.
    internal D3D12ShaderProgram? CurrentProgram { get; set; }

    internal FillMode CurrentFillMode { get; set; } = FillMode.Solid;

    // Starts at 1. Programs use it to spot their first Use() in a frame.
    internal ulong FrameNumber { get; private set; }

    // Last-bound state of the open command list, so a draw repeating the
    // previous one skips the calls. Every set of these states must go through
    // the Bind* methods, and the list reset must call ResetLastBoundState.
    private nint _lastRootSignature;
    private nint _lastPso;
    private int _lastTopology = -1;

    // By root parameter index. A root signature change invalidates them.
    private readonly ulong[] _lastRootCbv = new ulong[16];

    internal void BindRootSignature(ID3D12GraphicsCommandList* list, nint rootSignature)
    {
        if (_lastRootSignature == rootSignature) return;
        _lastRootSignature = rootSignature;
        Array.Clear(_lastRootCbv);
        list->SetGraphicsRootSignature((ID3D12RootSignature*)rootSignature);
    }

    internal void BindRootCbv(ID3D12GraphicsCommandList* list, int rootParam, ulong gpuVa)
    {
        if ((uint)rootParam < (uint)_lastRootCbv.Length)
        {
            if (_lastRootCbv[rootParam] == gpuVa) return;
            _lastRootCbv[rootParam] = gpuVa;
        }
        list->SetGraphicsRootConstantBufferView((uint)rootParam, gpuVa);
    }

    internal void BindPipelineState(ID3D12GraphicsCommandList* list, ID3D12PipelineState* pso)
    {
        if (_lastPso == (nint)pso) return;
        _lastPso = (nint)pso;
        list->SetPipelineState(pso);
    }

    internal void BindTopology(ID3D12GraphicsCommandList* list, D3DPrimitiveTopology topology)
    {
        if (_lastTopology == (int)topology) return;
        _lastTopology = (int)topology;
        list->IASetPrimitiveTopology(topology);
    }

    // Top of every frame: the list reset drops bindings, and a hot reload may
    // have recreated objects at recycled addresses.
    private void ResetLastBoundState()
    {
        _lastRootSignature = 0;
        _lastPso = 0;
        _lastTopology = -1;
        Array.Clear(_lastRootCbv);
    }

    public D3D12Renderer(ILogger<Renderer> logger, IShaderCompiler shaderCompiler)
        : base(logger, shaderCompiler)
    {
    }

    public override void Initialize(IRenderSurface surface)
    {
        _surface = surface;

        // The latch, not window.FramebufferSize: GLFW window queries are not
        // safe from the render thread.
        Vector2D<int> size = FramebufferSize;
        _swapChainSize = size;
        _composited = surface.Kind == RenderSurfaceKind.Composited;

        CreateDevice();
        CreateQueue();

        if (!_composited)
            CreateSwapChain(surface, (uint)size.X, (uint)size.Y);
        if (UncappedPresentation)
            _logger.LogInformation("Uncapped presentation {Status}", UncappedPresentationAvailable
                ? "enabled (DXGI allow tearing)" : "unavailable for this surface or adapter");

        CreateFrameResources((uint)size.X, (uint)size.Y);

        DefaultShader = CreateBaseShader(BaseShaders.LitFileName);
        _debugShader = CreateBaseShader(BaseShaders.DebugLineFileName);
        _lineBatch = new D3D12LineBatch(this, (D3D12ShaderProgram)_debugShader);

        // 1x1 white, so an unset texture slot in a descriptor table is still valid.
        ReadOnlySpan<byte> white = [255, 255, 255, 255];
        _fallbackTexture = new D3D12Texture(this, TextureUploadDesc.SingleLevel(
            white, 1, 1, TextureFormat.Rgba8, TextureColorSpace.Srgb,
            TextureFilter.Nearest, TextureWrap.Repeat));

        // The first one registered is the default.
        RegisterPipeline(new D3D12DeferredPipeline());
        RegisterPipeline(new D3D12ForwardPipeline());
        RegisterPipeline(new D3D12WireframePipeline());

        // The host needs the shared handle as soon as Initialize returns.
        EnsurePresentTarget();

        DrainDebugMessages();
        _logger.LogInformation(
            "Renderer initialized (D3D12, pipeline={Pipeline}, surface={Surface})",
            CurrentPipelineName, _composited ? "composited (no swap chain)" : "window");
    }

    private void CreateDevice()
    {
        // Opt-in: the layer validates every command-list call.
        if (!EnableDebugLayer)
        {
            _logger.LogInformation("D3D12 debug layer off (not requested).");
        }
        else
        {
            ID3D12Debug* debug = null;
            Guid debugGuid = ID3D12Debug.Guid;
            if (D3D12Api.GetDebugInterface(&debugGuid, (void**)&debug) >= 0)
            {
                debug->EnableDebugLayer();
                debug->Release();
                DebugLayerActive = true;
                _logger.LogInformation("D3D12 debug layer active.");
            }
            else
            {
                _logger.LogInformation("D3D12 debug layer unavailable; creating without it.");
            }
        }

        // A null adapter is the system default.
        ComPtr<IDXGIAdapter> adapter = DxgiAdapters.Find(_dxgi, PreferredAdapter, _logger, out string adapterName);
        AdapterName = adapterName;

        ID3D12Device* device = null;
        Guid deviceGuid = ID3D12Device.Guid;
        try
        {
            SilkMarshal.ThrowHResult(D3D12Api.CreateDevice(
                (IUnknown*)adapter.Handle, D3DFeatureLevel.Level110, &deviceGuid, (void**)&device));
        }
        finally
        {
            ComOwnership.Release(ref adapter);
        }
        _device = ComOwnership.Own(device);

        ID3D12InfoQueue* infoQueue = null;
        Guid infoQueueGuid = ID3D12InfoQueue.Guid;
        if (device->QueryInterface(&infoQueueGuid, (void**)&infoQueue) >= 0)
            _infoQueue = ComOwnership.Own(infoQueue);
    }

    // Separate from the swap chain: a composited surface has no chain but
    // still needs the queue.
    private void CreateQueue()
    {
        var queueDesc = new CommandQueueDesc
        {
            Type = CommandListType.Direct,
            Priority = 0,
            Flags = CommandQueueFlags.None,
            NodeMask = 0,
        };
        ID3D12CommandQueue* queue = null;
        Guid queueGuid = ID3D12CommandQueue.Guid;
        SilkMarshal.ThrowHResult(DevicePtr->CreateCommandQueue(&queueDesc, &queueGuid, (void**)&queue));
        _queue = ComOwnership.Own(queue);
    }

    private void CreateSwapChain(IRenderSurface surface, uint width, uint height)
    {
        if (surface.Kind != RenderSurfaceKind.Win32 || surface.NativeHandle == 0)
        {
            throw new InvalidOperationException(
                $"The D3D12 backend needs a Win32 surface with an HWND, or a composited surface it does not " +
                $"present to; this one is {surface.Kind}. On another platform, or for a surface that offers " +
                "only a GL context, use the OpenGL backend.");
        }

        nint hwnd = surface.NativeHandle;
        ID3D12CommandQueue* queue = (ID3D12CommandQueue*)_queue.Handle;

        // The DXGI debug layer explains swap-chain rejections. Same gate as the
        // device layer.
        IDXGIFactory2* factory = null;
        Guid factoryGuid = IDXGIFactory2.Guid;
        bool debugFactory = false;
        if (EnableDebugLayer)
        {
            int factoryHr = _dxgi.CreateDXGIFactory2(
                DxgiDebugMessages.CreateFactoryDebug, &factoryGuid, (void**)&factory);
            debugFactory = factoryHr >= 0;
            if (debugFactory)
            {
                _dxgiMessages = DxgiDebugMessages.Acquire(_dxgi);
                _logger.LogInformation(
                    "DXGI debug layer {State}.", _dxgiMessages.IsAvailable ? "active" : "requested but no info queue");
            }
            else
            {
                _logger.LogInformation("DXGI debug layer unavailable (hr=0x{Hr:X}); creating the factory without it.", factoryHr);
            }
        }
        if (!debugFactory)
            SilkMarshal.ThrowHResult(_dxgi.CreateDXGIFactory2(0u, &factoryGuid, (void**)&factory));

        IDXGIFactory5* factory5 = null;
        if (factory->QueryInterface(SilkMarshal.GuidPtrOf<IDXGIFactory5>(), (void**)&factory5) >= 0)
        {
            int tearing = 0;
            UncappedPresentationAvailable = factory5->CheckFeatureSupport(
                Silk.NET.DXGI.Feature.PresentAllowTearing, &tearing, sizeof(int)) >= 0 && tearing != 0;
            factory5->Release();
        }
        _swapChainFlags = UncappedPresentation && UncappedPresentationAvailable ? 2048u : 0u; // DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING

        // Flip model is mandatory on D3D12. No ALLOW_MODE_SWITCH: fullscreen is
        // borderless windowed and DXGI never drives a mode switch.
        var desc = new SwapChainDesc1
        {
            Width = width,
            Height = height,
            Format = BackBufferFormat,
            Stereo = 0,
            SampleDesc = new SampleDesc(1, 0),
            BufferUsage = DxgiApi.UsageRenderTargetOutput,
            BufferCount = BufferCount,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Unspecified,
            Flags = _swapChainFlags,
        };

        IDXGISwapChain1* swapChain1 = null;
        SilkMarshal.ThrowHResult(factory->CreateSwapChainForHwnd(
            (IUnknown*)queue, hwnd, &desc, null, null, &swapChain1));

        IDXGISwapChain3* swapChain3 = null;
        Guid sc3Guid = IDXGISwapChain3.Guid;
        SilkMarshal.ThrowHResult(swapChain1->QueryInterface(&sc3Guid, (void**)&swapChain3));
        swapChain1->Release();
        _swapChain = ComOwnership.Own(swapChain3);

        // The window association is per-factory: it must be made on the factory
        // that created the chain, before that factory is released.
        DxgiInterop.SuppressAltEnter(factory, hwnd, _logger, "D3D12");
        factory->Release();

        _frameIndex = _swapChain.GetCurrentBackBufferIndex();
    }

    private void CreateFrameResources(uint width, uint height)
    {
        _rtvHeap = CreateDescriptorHeap(DescriptorHeapType.Rtv, BufferCount, shaderVisible: false);
        _dsvHeap = CreateDescriptorHeap(DescriptorHeapType.Dsv, 1, shaderVisible: false);
        _rtvStride = DevicePtr->GetDescriptorHandleIncrementSize(DescriptorHeapType.Rtv);
        _srvStride = DevicePtr->GetDescriptorHandleIncrementSize(DescriptorHeapType.CbvSrvUav);
        _samplerStride = DevicePtr->GetDescriptorHandleIncrementSize(DescriptorHeapType.Sampler);

        // A composited surface has no chain, and its present target has its own depth.
        if (!_composited)
            CreateBackBufferViews(width, height);

        CreateFrameContexts();

        ID3D12Fence* fence = null;
        Guid fenceGuid = ID3D12Fence.Guid;
        SilkMarshal.ThrowHResult(DevicePtr->CreateFence(0, FenceFlags.None, &fenceGuid, (void**)&fence));
        _fence = ComOwnership.Own(fence);
        _fenceEvent = Kernel32.CreateEvent(0, 0, 0, null);
        if (_fenceEvent == 0)
            throw new InvalidOperationException("Failed to create fence event.");

    }

    private void MapUploadRing()
    {
        var res = (ID3D12Resource*)_uploadRing.Handle;
        void* cpu = null;
        var readRange = new Silk.NET.Direct3D12.Range { Begin = 0, End = 0 };
        SilkMarshal.ThrowHResult(res->Map(0, &readRange, &cpu));
        _uploadRingCpu = (byte*)cpu;
        _uploadRingGpuVa = res->GetGPUVirtualAddress();
    }

    private void CreateBackBufferViews(uint width, uint height)
    {
        var rtvStart = ((ID3D12DescriptorHeap*)_rtvHeap.Handle)->GetCPUDescriptorHandleForHeapStart();
        for (uint i = 0; i < BufferCount; i++)
        {
            ID3D12Resource* backBuffer = null;
            Guid resGuid = ID3D12Resource.Guid;
            SilkMarshal.ThrowHResult(((IDXGISwapChain3*)_swapChain.Handle)->GetBuffer(i, &resGuid, (void**)&backBuffer));
            // Own, not new ComPtr<>(): the constructor AddRefs, and a leftover
            // back-buffer reference makes DXGI refuse every ResizeBuffers.
            _backBuffers[i] = ComOwnership.Own(backBuffer);

            // Explicit desc: null would take the resource's _UNORM format and
            // skip the sRGB encode.
            var rtvDesc = new RenderTargetViewDesc
            {
                Format = BackBufferRtvFormat,
                ViewDimension = RtvDimension.Texture2D,
            };
            rtvDesc.Anonymous.Texture2D = new Tex2DRtv { MipSlice = 0, PlaneSlice = 0 };

            var handle = new CpuDescriptorHandle { Ptr = rtvStart.Ptr + i * _rtvStride };
            DevicePtr->CreateRenderTargetView(backBuffer, &rtvDesc, handle);
        }

        var depthDesc = new ResourceDesc
        {
            Dimension = ResourceDimension.Texture2D,
            Alignment = 0,
            Width = width,
            Height = height,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = DepthFormat,
            SampleDesc = new SampleDesc(1, 0),
            Layout = TextureLayout.LayoutUnknown,
            Flags = ResourceFlags.AllowDepthStencil,
        };
        var heapProps = new HeapProperties { Type = HeapType.Default };
        var clearValue = new ClearValue { Format = DepthFormat };
        clearValue.Anonymous.DepthStencil = new DepthStencilValue { Depth = 1f, Stencil = 0 };

        ID3D12Resource* depth = null;
        Guid depthGuid = ID3D12Resource.Guid;
        SilkMarshal.ThrowHResult(DevicePtr->CreateCommittedResource(
            &heapProps, HeapFlags.None, &depthDesc, ResourceStates.DepthWrite, &clearValue, &depthGuid, (void**)&depth));
        _depthBuffer = ComOwnership.Own(depth);

        var dsvHandle = ((ID3D12DescriptorHeap*)_dsvHeap.Handle)->GetCPUDescriptorHandleForHeapStart();
        DevicePtr->CreateDepthStencilView(depth, null, dsvHandle);
    }

    // Must be idempotent: a failed resize and then Shutdown both get here.
    private void ReleaseBackBufferViews()
    {
        for (int i = 0; i < _backBuffers.Length; i++)
            ComOwnership.Release(ref _backBuffers[i]);
        ComOwnership.Release(ref _depthBuffer);
    }

    public void RegisterPipeline(ID3D12RenderPipeline pipeline)
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

        // Once per frame, not per pipeline run: with ProbeTarget set the
        // pipeline runs twice into one command list.
        BeginFrameInstanceBuffers();

        if (_pipelines.Count == 0 || _surface is null) return;

        // Null on a window surface, where null means the back buffer.
        RenderTarget? present = EnsurePresentTarget();

        // Composited with no target: the pane is collapsed. Not an error.
        if (_composited && present is null) return;

        BeginRecording();
        var list = (ID3D12GraphicsCommandList*)_commandList.Handle;

        _assetOnlyRecording = false; // This list now also owns the rendered frame.

        if (Profiler.Enabled && Profiler.GpuTimer is null && !_gpuTimingUnavailable)
        {
            try { Profiler.GpuTimer = new D3D12GpuTimer(this, (ID3D12CommandQueue*)_queue.Handle); }
            catch (Exception ex) { _gpuTimingUnavailable = true; _logger.LogWarning(ex, "D3D12 GPU timestamps unavailable"); }
        }
        if (Profiler.Enabled) Profiler.GpuTimer?.BeginFrame();

        // Size the rings for this view before the heaps are bound. This is the
        // only place a heap can be swapped: nothing is bound yet.
        ReserveDescriptorRings(view);
        BindDescriptorHeaps();

        if (!_composited)
        {
            Transition(list, (ID3D12Resource*)_backBuffers[_frameIndex].Handle,
                ResourceStates.Present, ResourceStates.RenderTarget);
        }

        var context = new D3D12RenderContext
        {
            Renderer = this,
            Scene = scene,
            View = view,
            DeltaTime = deltaTime,
        };
        if (ProbeTarget is { } probe)
        {
            FrameTarget = probe;
            _pipelines[_pipelineIndex].Execute(context);
        }

        // HDR off: draw straight into the presented target.
        RenderTarget? sceneTarget = HdrEnabled ? EnsureSceneTarget() : present;
        FrameTarget = sceneTarget;
        _pipelines[_pipelineIndex].Execute(context);

        // The overlay goes wherever the resolve goes. A composited surface has
        // no back-buffer RTV to draw it through.
        if (sceneTarget is null || ReferenceEquals(sceneTarget, present))
        {
            DrawOverlay(scene, present);
        }
        else
        {
            ResolveTo(sceneTarget.ColorTexture!, present, scene);

            // Reference picture for --viewport-compare: same source, plain sRGB target.
            if (CompareTarget is { } reference)
                ResolveTo(sceneTarget.ColorTexture!, reference, scene);
        }

        if (!_composited)
        {
            Transition(list, (ID3D12Resource*)_backBuffers[_frameIndex].Handle,
                ResourceStates.RenderTarget, ResourceStates.Present);
        }

        if (Profiler.Enabled) Profiler.GpuTimer?.EndFrame();
        SilkMarshal.ThrowHResult(list->Close());
        _isRecording = false;

        ID3D12CommandList* executeList = (ID3D12CommandList*)list;
        ((ID3D12CommandQueue*)_queue.Handle)->ExecuteCommandLists(1, &executeList);
        if (Profiler.Enabled) (Profiler.GpuTimer as D3D12GpuTimer)?.Submitted();

        // After the execute: the bridge copies on this same queue, so the copy
        // lands behind the draws.
        PublishSharedFrame();
        SignalSubmission();
    }

    // Copies the finished frame to the consumer through the bridge. The mutex
    // covers only this copy; nothing else here touches the shared texture.
    private void PublishSharedFrame()
    {
        // First, so a turn queued against a just-retired generation is answered.
        _retirement?.OfferTurns();

        if (_bridge is not { HasSurface: true } bridge) return;

        // Consumer never took its turn (hidden pane): skip this frame's copy.
        if (!BeginSharedWrite()) return;

        try
        {
            bridge.Publish();
        }
        finally
        {
            EndSharedWrite();
        }
    }

    // Shared by the frame and the out-of-frame path so neither misses a reset.
    // FrameNumber must bump here, together with the upload ring rewind:
    // programs rebind a cached slice while the number is unchanged.
    private void BeginRecording()
    {
        if (_isRecording) return; // Asset copies can prefix this frame's direct command list.
        _assetOnlyRecording = false;
        AcquireFrameContext();
        var allocator = (ID3D12CommandAllocator*)_commandAllocator.Handle;
        var list = (ID3D12GraphicsCommandList*)_commandList.Handle;
        SilkMarshal.ThrowHResult(allocator->Reset());
        SilkMarshal.ThrowHResult(list->Reset(allocator, (ID3D12PipelineState*)null));
        _isRecording = true;
        CurrentProgram = null;
        CurrentFillMode = FillMode.Solid;

        FrameNumber++;
        ResetLastBoundState();
        _uploadRingOffset = 0;
        _srvRingOffset = 0;
        _samplerRingOffset = 0;
        _stagedSlotCount = 0;
        _ringOverflowReported = false;
    }

    // After any ring reservation, before anything draws.
    private void BindDescriptorHeaps()
    {
        ID3D12DescriptorHeap** heaps = stackalloc ID3D12DescriptorHeap*[2]
        {
            (ID3D12DescriptorHeap*)_srvRing.Handle,
            (ID3D12DescriptorHeap*)_samplerRing.Handle,
        };
        ((ID3D12GraphicsCommandList*)_commandList.Handle)->SetDescriptorHeaps(2, heaps);
    }

    private void EndRecordingAndWait()
    {
        var list = (ID3D12GraphicsCommandList*)_commandList.Handle;
        SilkMarshal.ThrowHResult(list->Close());
        _isRecording = false;

        ID3D12CommandList* executeList = (ID3D12CommandList*)list;
        ((ID3D12CommandQueue*)_queue.Handle)->ExecuteCommandLists(1, &executeList);
        SignalSubmission();
        WaitForGpu();
    }

    /// <inheritdoc/>
    // No immediate context here, so out-of-frame work gets its own list.
    protected internal override void BeginOutOfFrameCommands()
    {
        FlushUploads();
        if (_isRecording)
            throw new InvalidOperationException("Out-of-frame commands cannot be issued while a frame is recording.");

        BeginRecording();
        BindDescriptorHeaps();
    }

    /// <inheritdoc/>
    protected internal override void EndOutOfFrameCommands() => EndRecordingAndWait();

    internal override (byte R, byte G, byte B, byte A) ReadTargetPixel(
        RenderTarget target, int x, int y)
    {
        Span<byte> one = stackalloc byte[4];
        ReadTargetPixels(target, x, y, 1, 1, one);
        return (one[0], one[1], one[2], one[3]);
    }

    // Rows are flipped: a D3D target's origin is top-left and the caller's y
    // counts from the bottom. The footprint's row pitch is 256-aligned, so the
    // buffer is sized by GetCopyableFootprints and copied out row by row.
    internal override void ReadTargetPixels(
        RenderTarget target, int x, int y, int width, int height, Span<byte> destination)
    {
        PixelReadback.ValidateRegion(target, x, y, width, height, destination);
        if (target is not D3D12RenderTarget d3dTarget || target.ColorTexture is not D3D12Texture color)
            throw new ArgumentException("The target has no colour attachment to read.", nameof(target));
        if (_isRecording)
            throw new InvalidOperationException("A pixel readback cannot be issued while a frame is recording.");

        var sourceDesc = new ResourceDesc
        {
            Dimension = ResourceDimension.Texture2D,
            Alignment = 0,
            Width = (ulong)width,
            Height = (uint)height,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = color.DxgiFormat,
            SampleDesc = new SampleDesc(1, 0),
            Layout = TextureLayout.LayoutUnknown,
            Flags = ResourceFlags.None,
        };

        PlacedSubresourceFootprint footprint;
        uint numRows;
        ulong rowSize;
        ulong totalBytes;
        DevicePtr->GetCopyableFootprints(&sourceDesc, 0, 1, 0, &footprint, &numRows, &rowSize, &totalBytes);

        ComPtr<ID3D12Resource> readback = CreateReadbackBuffer((uint)totalBytes);
        try
        {
            BeginRecording();
            var list = (ID3D12GraphicsCommandList*)_commandList.Handle;

            ResourceStates previous = d3dTarget.ColorState;
            d3dTarget.TransitionColor(list, ResourceStates.CopySource);

            var dst = new TextureCopyLocation
            {
                PResource = (ID3D12Resource*)readback.Handle,
                Type = TextureCopyType.PlacedFootprint,
            };
            dst.Anonymous.PlacedFootprint = footprint;
            var src = new TextureCopyLocation
            {
                PResource = color.Resource,
                Type = TextureCopyType.SubresourceIndex,
            };
            src.Anonymous.SubresourceIndex = 0;

            uint top = (uint)(target.Height - y - height);
            var box = new Silk.NET.Direct3D12.Box
            {
                Left = (uint)x,
                Top = top,
                Front = 0,
                Right = (uint)(x + width),
                Bottom = top + (uint)height,
                Back = 1,
            };
            list->CopyTextureRegion(&dst, 0, 0, 0, &src, &box);

            d3dTarget.TransitionColor(list, previous);
            EndRecordingAndWait();

            void* mapped = null;
            var readRange = new Silk.NET.Direct3D12.Range { Begin = 0, End = (nuint)totalBytes };
            SilkMarshal.ThrowHResult(((ID3D12Resource*)readback.Handle)->Map(0, &readRange, &mapped));
            try
            {
                PixelReadback.CopyRowsBottomFirst(
                    (byte*)mapped, footprint.Footprint.RowPitch, width, height, destination);
            }
            finally
            {
                var written = new Silk.NET.Direct3D12.Range { Begin = 0, End = 0 };
                ((ID3D12Resource*)readback.Handle)->Unmap(0, &written);
            }
        }
        finally
        {
            ComOwnership.Release(ref readback);
        }
    }

    internal ComPtr<ID3D12Resource> CreateReadbackBuffer(uint sizeBytes)
    {
        var heapProps = new HeapProperties { Type = HeapType.Readback };
        var desc = new ResourceDesc
        {
            Dimension = ResourceDimension.Buffer,
            Alignment = 0,
            Width = sizeBytes,
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = Format.FormatUnknown,
            SampleDesc = new SampleDesc(1, 0),
            Layout = TextureLayout.LayoutRowMajor,
            Flags = ResourceFlags.None,
        };
        ID3D12Resource* res = null;
        Guid resGuid = ID3D12Resource.Guid;
        SilkMarshal.ThrowHResult(DevicePtr->CreateCommittedResource(
            &heapProps, HeapFlags.None, &desc, ResourceStates.CopyDest, null, &resGuid, (void**)&res));
        return ComOwnership.Own(res);
    }

    public override void Present(IRenderSurface surface)
    {
        if (_deviceLost) return;
        FlushUploads(); // A collapsed viewport can skip rendering but still receive assets.

        if (_swapChain.Handle is not null)
        {
            bool uncapped = UncappedPresentation && _swapChainFlags != 0;
            int hr = ((IDXGISwapChain3*)_swapChain.Handle)->Present(uncapped ? 0u : VSync ? 1u : 0u,
                uncapped ? 512u : 0u); // DXGI_PRESENT_ALLOW_TEARING
            if (hr < 0)
            {
                if (DxgiInterop.IsDeviceLost(hr))
                    throw DeviceLost(hr, "presenting a frame");
                SilkMarshal.ThrowHResult(hr);
            }
        }

        ReleaseCompletedResources();
        RecycleRetiredMeshBuffers();

        if (_swapChain.Handle is not null)
            _frameIndex = _swapChain.GetCurrentBackBufferIndex();

        // Outside the swap-chain guard: on a composited surface the debug layer
        // is the only error detector there is.
        DrainDebugMessages();
    }

    // Only after the frame fence completed.
    private void DisposeRetiredUploadRings()
    {
        if (_retiredUploadRings.Count == 0) return;
        // Dispose is fine here: the Clear below drops the entries.
        foreach (var ring in _retiredUploadRings)
        {
            ((ID3D12Resource*)ring.Handle)->Unmap(0, null);
            ring.Dispose();
        }
        _retiredUploadRings.Clear();
    }

    // Sizes the rings for every draw in the view plus slack, at the widest SRV
    // table any loaded program declares. Top of the frame, before heaps are bound.
    private void ReserveDescriptorRings(RenderView view)
    {
        uint perDraw = 0;
        for (int i = 0; i < _shaders.Count; i++)
        {
            if (_shaders[i] is D3D12ShaderProgram program)
                perDraw = Math.Max(perDraw, program.SrvCount);
        }

        if (perDraw == 0) return;

        long draws = (long)view.Items.Count + view.WorldItems.Count + DescriptorReserveSlackDraws;
        ulong required = (ulong)draws * perDraw;

        _srvRingCapacity = EnsureRingCapacity(
            ref _srvRing, DescriptorHeapType.CbvSrvUav, _srvRingCapacity, required, MaxSrvRingCapacity, "SRV");
        _samplerRingCapacity = EnsureRingCapacity(
            ref _samplerRing, DescriptorHeapType.Sampler, _samplerRingCapacity, required,
            MaxSamplerRingCapacity, "sampler");
    }

    // Stops at the ring's maximum. StageDescriptors degrades past it.
    private uint EnsureRingCapacity(
        ref ComPtr<ID3D12DescriptorHeap> ring,
        DescriptorHeapType type,
        uint capacity,
        ulong required,
        uint maximum,
        string label)
    {
        if (required <= capacity || capacity >= maximum) return capacity;

        uint target = capacity;
        while (target < required && target < maximum)
        {
            target = target > maximum / 2 ? maximum : target * 2;
        }

        // Release first: if creation throws, the field must not hold a freed handle.
        ComOwnership.Release(ref ring);
        ring = CreateDescriptorHeap(type, target, shaderVisible: true);
        _logger.LogInformation(
            "D3D12 {Label} descriptor ring sized to {Capacity} slots for a {Required}-descriptor frame",
            label, target, required);
        return target;
    }

    // Backstop under ReserveDescriptorRings: grows a ring when last frame's
    // peak passed 75% of it. Between frames only.
    private void GrowDescriptorRingsIfNeeded()
    {
        if (_srvRingPeak * 4 > _srvRingCapacity * 3)
        {
            uint newCapacity = _srvRingCapacity;
            while (_srvRingPeak * 4 > newCapacity * 3)
                newCapacity *= 2;
            ComOwnership.Release(ref _srvRing);
            _srvRing = CreateDescriptorHeap(DescriptorHeapType.CbvSrvUav, newCapacity, shaderVisible: true);
            _srvRingCapacity = newCapacity;
            _logger.LogInformation("D3D12 SRV descriptor ring grown to {Capacity} slots", newCapacity);
        }

        if (_samplerRingPeak * 4 > _samplerRingCapacity * 3 && _samplerRingCapacity < MaxSamplerRingCapacity)
        {
            uint newCapacity = _samplerRingCapacity;
            while (_samplerRingPeak * 4 > newCapacity * 3 && newCapacity < MaxSamplerRingCapacity)
                newCapacity *= 2;
            ComOwnership.Release(ref _samplerRing);
            _samplerRing = CreateDescriptorHeap(DescriptorHeapType.Sampler, newCapacity, shaderVisible: true);
            _samplerRingCapacity = newCapacity;
            _logger.LogInformation("D3D12 sampler descriptor ring grown to {Capacity} slots", newCapacity);
        }

        _srvRingPeak = 0;
        _samplerRingPeak = 0;
    }

    // Blocks until the queue has finished all submitted work.
    internal void WaitForGpu()
    {
        FlushUploads();
        using var timing = Profiler.Measure(SpectraEngine.Core.Diagnostics.FramePhase.GpuWait);
        // A dead device never signals.
        if (_fence.Handle is null || _deviceLost) return;
        ulong value = ++_fenceValue;
        SilkMarshal.ThrowHResult(((ID3D12CommandQueue*)_queue.Handle)->Signal((ID3D12Fence*)_fence.Handle, value));
        if (((ID3D12Fence*)_fence.Handle)->GetCompletedValue() < value)
        {
            SilkMarshal.ThrowHResult(((ID3D12Fence*)_fence.Handle)->SetEventOnCompletion(value, (void*)_fenceEvent));
            Kernel32.WaitForSingleObject(_fenceEvent, Kernel32.Infinite);
        }
    }

    internal static void Transition(ID3D12GraphicsCommandList* list, ID3D12Resource* resource,
        ResourceStates before, ResourceStates after)
    {
        var barrier = new ResourceBarrier
        {
            Type = ResourceBarrierType.Transition,
            Flags = ResourceBarrierFlags.None,
        };
        barrier.Anonymous.Transition = new ResourceTransitionBarrier
        {
            PResource = resource,
            Subresource = uint.MaxValue, // all subresources
            StateBefore = before,
            StateAfter = after,
        };
        list->ResourceBarrier(1, &barrier);
    }

    private CpuDescriptorHandle CurrentBackBufferRtv
    {
        get
        {
            var start = ((ID3D12DescriptorHeap*)_rtvHeap.Handle)->GetCPUDescriptorHandleForHeapStart();
            return new CpuDescriptorHandle { Ptr = start.Ptr + _frameIndex * _rtvStride };
        }
    }

    private CpuDescriptorHandle DepthStencilView =>
        ((ID3D12DescriptorHeap*)_dsvHeap.Handle)->GetCPUDescriptorHandleForHeapStart();

    protected override void BeginPassCore(
        RenderTarget? target, ReadOnlySpan<RenderTarget> targets, in PassClear clear)
    {
        var list = CurrentList;
        if (list is null) return;

        // A composited surface has no back-buffer views, so the null target's
        // RTV slot was never written.
        if (_composited && target is null) return;

        Vector2D<int> size = PassSize;
        SetViewportAndScissor(size.X, size.Y);

        CpuDescriptorHandle rtv;
        CpuDescriptorHandle dsv;
        bool hasDepth;
        bool hasColor = true;

        if (target is D3D12RenderTarget offscreen)
        {
            // Writable here, readable again in EndPassCore.
            offscreen.TransitionColor(list, ResourceStates.RenderTarget);
            offscreen.TransitionDepth(list, ResourceStates.DepthWrite);

            // A depth-only target has no RTV formats, and the PSO must agree.
            _currentTargetState = offscreen.HasColor
                ? new D3D12TargetState(offscreen.ColorFormat, 1, offscreen.DepthViewFormat, 1)
                : new D3D12TargetState(Format.FormatUnknown, 0, offscreen.DepthViewFormat, 1);

            rtv = offscreen.Rtv;
            dsv = offscreen.Dsv;
            hasDepth = offscreen.HasDepth;
            hasColor = offscreen.HasColor;
        }
        else
        {
            _currentTargetState = D3D12TargetState.BackBuffer;
            rtv = CurrentBackBufferRtv;
            dsv = DepthStencilView;
            hasDepth = true;
        }

        if (clear.Color is { } color && hasColor)
        {
            float* value = stackalloc float[4] { color.X, color.Y, color.Z, color.W };
            list->ClearRenderTargetView(rtv, value, 0, null);
        }
        if (clear.Depth is { } depth && hasDepth)
            list->ClearDepthStencilView(dsv, ClearFlags.Depth | ClearFlags.Stencil, depth, 0, 0, null);

        if (targets.Length > 1)
        {
            // Each attachment needs its own barrier and clear, and the PSO is
            // compiled against all their formats.
            CpuDescriptorHandle* views = stackalloc CpuDescriptorHandle[targets.Length];
            var formats = new Format[targets.Length];
            // Outside the loop: a stackalloc in a loop does not reuse its space.
            float* clearValue = stackalloc float[4];
            if (clear.Color is { } extraColor)
            {
                clearValue[0] = extraColor.X;
                clearValue[1] = extraColor.Y;
                clearValue[2] = extraColor.Z;
                clearValue[3] = extraColor.W;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                var extra = (D3D12RenderTarget)targets[i];
                if (i > 0)
                {
                    extra.TransitionColor(list, ResourceStates.RenderTarget);
                    if (clear.Color is not null)
                        list->ClearRenderTargetView(extra.Rtv, clearValue, 0, null);
                }
                views[i] = extra.Rtv;
                formats[i] = extra.ColorFormat;
            }

            // The first target owns depth.
            Format depthFormat = targets[0] is D3D12RenderTarget first
                ? first.DepthViewFormat
                : Format.FormatUnknown;
            _currentTargetState = D3D12TargetState.ForTargets(formats, depthFormat);
            list->OMSetRenderTargets((uint)targets.Length, views, 0, hasDepth ? &dsv : null);
            return;
        }

        if (!hasColor)
        {
            list->OMSetRenderTargets(0, null, 0, &dsv);
        }
        else if (hasDepth)
        {
            list->OMSetRenderTargets(1, &rtv, 0, &dsv);
        }
        else
        {
            list->OMSetRenderTargets(1, &rtv, 0, null);
        }
    }

    protected override void EndPassCore(RenderTarget? target, ReadOnlySpan<RenderTarget> targets)
    {
        for (int i = 1; i < targets.Length; i++)
        {
            if (CurrentList is not null && targets[i] is D3D12RenderTarget extra)
                extra.TransitionColor(CurrentList, ResourceStates.PixelShaderResource);
        }

        _currentTargetState = D3D12TargetState.BackBuffer;

        var list = CurrentList;
        if (list is null || target is not D3D12RenderTarget offscreen) return;

        // Back to readable now, not lazily at the first sample.
        offscreen.TransitionColor(list, ResourceStates.PixelShaderResource);
        offscreen.TransitionDepth(list, ResourceStates.PixelShaderResource);
    }

    // What every PSO built during the open pass must be compiled against.
    internal D3D12TargetState CurrentTargetState => _currentTargetState;

    private D3D12TargetState _currentTargetState = D3D12TargetState.BackBuffer;

    public override RenderTarget CreateRenderTarget(in RenderTargetDesc desc)
    {
        var target = new D3D12RenderTarget(this, desc);
        target.Unregister = () => _renderTargets.Remove(target);
        _renderTargets.Add(target);
        return target;
    }

    // Composited only; null on a window surface or while the pane has no size.
    // The private target and the bridge's shared texture are created and
    // retired as a pair. A size change builds a new generation: the consumer
    // imported the handle and may still be reading the old one.
    private RenderTarget? EnsurePresentTarget()
    {
        if (!_composited) return null;

        Vector2D<int> size = FramebufferSize;

        // No size: skip the frame, as EnsureSceneTarget does. The existing pair
        // stays, so the consumer's imported handle remains valid.
        if (size.X <= 0 || size.Y <= 0) return null;

        if (_presentTarget is { } existing && existing.Width == size.X && existing.Height == size.Y)
            return existing;

        _retirement ??= new SharedTargetRetirement(_logger);
        _bridge ??= D3D12On11Bridge.Create(DevicePtr, (ID3D12CommandQueue*)_queue.Handle);

        if (_presentTarget is { } outgoing)
        {
            int retiring = _presentGeneration;
            _presentTarget = null;

            // The flag is about the live surface's key, and that surface is changing.
            _sharedWriteHeld = false;

            // The bridge's copy may still be reading the outgoing target.
            WaitForGpu();

            // Read before Detach: the bridge only reports the live surface's mutex.
            IDXGIKeyedMutex* retiredMutex = _bridge.KeyedMutex;

            IDisposable? retiredSurface = _bridge.Detach();
            _retirement.Retire(
                retiring,
                () =>
                {
                    retiredSurface?.Dispose();
                    DestroyRenderTarget(outgoing);
                },
                () => SharedTargetTurn.Offer(retiredMutex, _logger, retiring));
        }

        // sRGB so the target's view encodes, like the back buffer. The bridge's
        // shared texture is UNORM, so nothing encodes twice.
        // No sharing on the target itself: the bridge does that.
        // Depth is only used with HDR off, when the scene draws straight in here.
        var fresh = (D3D12RenderTarget)CreateRenderTarget(new RenderTargetDesc(
            size.X, size.Y, TextureFormat.Rgba8, TextureColorSpace.Srgb,
            Depth: true, TextureFilter.Linear, TextureWrap.Clamp, Color: true));

        _bridge.Attach(size.X, size.Y, ((D3D12Texture)fresh.ColorTexture!).Resource);

        _presentTarget = fresh;
        _presentGeneration = _retirement.Next();
        _sharedTimeoutLogged = false;

        _logger.LogInformation(
            "Shared present target {Width}x{Height}, generation {Generation}, handle 0x{Handle:X} " +
            "(through a D3D11On12 bridge).",
            size.X, size.Y, _presentGeneration, _bridge.SharedHandle);

        return fresh;
    }

    internal RenderTarget? PresentTargetForTest => _presentTarget;

    internal RenderTarget? EnsurePresentTargetForTest() => EnsurePresentTarget();

    // Clears the present target and publishes it as the end of a frame does.
    // A pass outside a frame needs its own command scope on D3D12.
    internal void WriteAndPublishForTest(Vector4 clear)
    {
        if (_presentTarget is null) return;

        BeginOutOfFrameCommands();
        try
        {
            BeginPass(_presentTarget, PassClear.To(clear));
            EndPass();
        }
        finally
        {
            EndOutOfFrameCommands();
        }

        PublishSharedFrame();
    }

    /// <inheritdoc/>
    public override bool TryGetSharedHandle(out SharedTargetHandle handle)
    {
        if (_bridge is { HasSurface: true } bridge && _presentTarget is { } target)
        {
            handle = new SharedTargetHandle(
                bridge.SharedHandle, target.Width, target.Height, _presentGeneration);
            return true;
        }

        handle = default;
        return false;
    }

    /// <inheritdoc/>
    public override bool BeginSharedWrite(int timeoutMs = 100)
    {
        if (_bridge is not { HasSurface: true } bridge) return false;

        // Taking the key twice and releasing it once deadlocks the consumer.
        if (_sharedWriteHeld)
            throw new InvalidOperationException("BeginSharedWrite was called while the shared key was already held.");

        // Timed: frame time alone can't tell waiting here from working.

        long acquireStartedAt = Stopwatch.GetTimestamp();

        int hr = bridge.KeyedMutex->AcquireSync(SharedProducerKey, (uint)Math.Max(0, timeoutMs));

        RecordSharedAcquireWait(Stopwatch.GetTimestamp() - acquireStartedAt);

        // WAIT_TIMEOUT (0x102) is a positive HRESULT: hr < 0 would read it as acquired.
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

        // WAIT_ABANDONED (0x80): acquired, but the last holder died holding it.
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
    // No flush here, unlike D3D11: the bridge flushes in Publish.
    public override void EndSharedWrite()
    {
        if (!_sharedWriteHeld) return;
        _sharedWriteHeld = false;

        if (_bridge is not { HasSurface: true } bridge) return;

        int hr = bridge.KeyedMutex->ReleaseSync(SharedConsumerKey);
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

    // Reads the bridge's shared texture, not the present target: the copy
    // between them is the thing being checked.
    internal override bool TryReadSharedPixels(Span<byte> destination, int timeoutMs = 100)
    {
        if (_bridge is not { HasSurface: true } bridge) return false;

        // The bridge's copy is queued, not finished. Wait for it.
        WaitForGpu();

        byte[] scratch = new byte[PixelReadback.ByteCount(bridge.SharedWidth, bridge.SharedHeight)];
        bool read = WithConsumerKey(timeoutMs, _ => bridge.ReadShared(scratch));
        if (read) scratch.CopyTo(destination);
        return read;
    }

    // Runs work holding the consumer key, then hands the producer key back.
    // A timeout is not a failure.
    private bool WithConsumerKey(int timeoutMs, Action<D3D12Renderer> work)
    {
        if (_bridge is not { HasSurface: true } bridge) return false;

        int hr = bridge.KeyedMutex->AcquireSync(SharedConsumerKey, (uint)Math.Max(0, timeoutMs));
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
            int released = bridge.KeyedMutex->ReleaseSync(SharedProducerKey);
            if (released < 0)
            {
                _logger.LogError(
                    "Handing the shared target's key back failed: {Code} (0x{Hr:X8}). The next frame will time out.",
                    DxgiInterop.Describe(released), released);
            }
        }

        return true;
    }

    // WAIT_TIMEOUT
    private const int WaitTimeout = 0x00000102;

    // WAIT_ABANDONED
    private const int WaitAbandoned = 0x00000080;

    // Typeless, so it can be both written and sampled.
    internal ComPtr<ID3D12Resource> CreateDepthResource(uint width, uint height)
    {
        var heapProps = new HeapProperties { Type = HeapType.Default };
        var desc = new ResourceDesc
        {
            Dimension = ResourceDimension.Texture2D,
            Alignment = 0,
            Width = width,
            Height = height,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = Format.FormatR32Typeless,
            SampleDesc = new SampleDesc(1, 0),
            Layout = TextureLayout.LayoutUnknown,
            // No DenyShaderResource: the light pass samples this.
            Flags = ResourceFlags.AllowDepthStencil,
        };

        // The clear value must name a real depth format, not the typeless one.
        var clearValue = new ClearValue { Format = Format.FormatD32Float };
        clearValue.Anonymous.DepthStencil = new DepthStencilValue { Depth = 1f, Stencil = 0 };

        ID3D12Resource* res = null;
        Guid guid = ID3D12Resource.Guid;
        SilkMarshal.ThrowHResult(DevicePtr->CreateCommittedResource(
            &heapProps, HeapFlags.None, &desc, ResourceStates.DepthWrite, &clearValue,
            &guid, (void**)&res));
        return ComOwnership.Own(res);
    }

    internal ComPtr<ID3D12Resource> CreateRenderTargetResource(uint width, uint height, Format format)
    {
        var heapProps = new HeapProperties { Type = HeapType.Default };
        var desc = new ResourceDesc
        {
            Dimension = ResourceDimension.Texture2D,
            Alignment = 0,
            Width = width,
            Height = height,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = format,
            SampleDesc = new SampleDesc(1, 0),
            Layout = TextureLayout.LayoutUnknown,
            Flags = ResourceFlags.AllowRenderTarget,
        };

        // Matches the usual clear colour. A mismatch is a slower clear and a
        // debug-layer warning.
        var clearValue = new ClearValue { Format = format };
        clearValue.Anonymous.Color[0] = ClearColors.Sky.X;
        clearValue.Anonymous.Color[1] = ClearColors.Sky.Y;
        clearValue.Anonymous.Color[2] = ClearColors.Sky.Z;
        clearValue.Anonymous.Color[3] = ClearColors.Sky.W;

        ID3D12Resource* res = null;
        Guid guid = ID3D12Resource.Guid;
        SilkMarshal.ThrowHResult(DevicePtr->CreateCommittedResource(
            &heapProps, HeapFlags.None, &desc, ResourceStates.PixelShaderResource, &clearValue,
            &guid, (void**)&res));
        return ComOwnership.Own(res);
    }

    protected override void DrawFullscreen(PostPass pass, Mesh geometry)
    {
        FillMode previousFill = CurrentFillMode;
        DepthMode previousDepth = CurrentDepthMode;
        CurrentFillMode = FillMode.Solid;
        CurrentDepthMode = DepthMode.None;

        // Use last: it uploads the uniforms and consumes the pending textures.
        pass.ApplyTo(pass.Shader);
        pass.Shader.Use();
        geometry.Draw();

        CurrentFillMode = previousFill;
        CurrentDepthMode = previousDepth;
    }

    // Depth state for the next mesh draw. Part of the PSO key.
    internal DepthMode CurrentDepthMode { get; set; } = DepthMode.TestWrite;

    internal void SetViewportAndScissor(int width, int height) =>
        SetViewportCore(0, 0, width, height);

    protected override void SetViewportCore(int x, int y, int width, int height)
    {
        var list = CurrentList;
        if (list is null) return;
        var viewport = new Viewport
        {
            TopLeftX = x,
            TopLeftY = y,
            Width = width,
            Height = height,
            MinDepth = 0f,
            MaxDepth = 1f,
        };
        list->RSSetViewports(1, &viewport);

        // The scissor is separate state here and must follow the viewport.
        var scissor = new Box2D<int>(x, y, x + width, y + height);
        list->RSSetScissorRects(1, &scissor);
    }

    /// <inheritdoc/>
    protected override void FlushDebugDrawCore(Scene.Camera camera)
    {
        if (DebugDraw.VertexCount == 0 || _debugShader is null || _lineBatch is null) return;

        var debug = (D3D12ShaderProgram)_debugShader;
        debug.SetUniform("uView", camera.View);
        debug.SetUniform("uProjection", camera.Projection * GlToD3dClipZ);
        debug.Use();
        _lineBatch.Draw(DebugDraw.Vertices, (uint)DebugDraw.VertexCount, DepthMode.None);
    }

    /// <inheritdoc/>
    protected override void FlushWorldLinesCore(
        Scene.Camera camera, ShaderProgram program, float nudge, GBuffer? gbuffer)
    {
        if (_lineBatch is null)
            return;

        var typed = (D3D12ShaderProgram)program;
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
            // Already readable: EndPassCore transitioned it.
            typed.SetTexture("uDepth", 0, gbuffer.Depth);
        }

        typed.Use();

        // Forward tests hardware depth. Deferred turns it off: the shader
        // compares against the G-buffer and discards.
        _lineBatch.Draw(
            WorldLines.Vertices, (uint)WorldLines.VertexCount,
            gbuffer is null ? DepthMode.TestNoWriteEqual : DepthMode.None,
            typed, BlendMode.AlphaBlend);
    }

    internal readonly struct UploadSlice
    {
        public required byte* Cpu { get; init; }
        public required ulong GpuVa { get; init; }
    }

    // Bump-allocates from the frame upload ring, growing it when exhausted.
    internal UploadSlice AllocUpload(uint size, uint alignment)
    {
        uint aligned = (_uploadRingOffset + alignment - 1) / alignment * alignment;
        if (aligned + size > _uploadRingCapacity)
        {
            // The open list holds GPU VAs into the old ring, so it is retired,
            // not destroyed, and stays mapped until this frame's fence.
            _uploadRingCpu = null;
            _retiredUploadRings.Add(_uploadRing);

            // Clear the field before creating the replacement: if that throws,
            // Shutdown must not release the same ring twice.
            _uploadRing = default;

            // Big enough for the whole frame so far, so the next frame fits.
            while (aligned + size > _uploadRingCapacity)
                _uploadRingCapacity *= 2;
            _uploadRing = CreateUploadBuffer(_uploadRingCapacity, "FrameUploadRing");
            MapUploadRing();
            _logger.LogInformation("D3D12 upload ring grown to {Size} KiB", _uploadRingCapacity / 1024);
            aligned = 0;
        }

        _uploadRingOffset = aligned + size;
        return new UploadSlice
        {
            Cpu = _uploadRingCpu + aligned,
            GpuVa = _uploadRingGpuVa + aligned,
        };
    }

    // Copies the pending SRVs and samplers into the shader-visible rings.
    // Unset slots get the white fallback.
    internal (GpuDescriptorHandle SrvTable, GpuDescriptorHandle SamplerTable) StageDescriptors(
        Dictionary<uint, D3D12Texture> pending, uint slotCount)
    {
        // Record demand even when it does not fit, so the next growth is right.
        uint srvDemand = _srvRingOffset + slotCount;
        uint samplerDemand = _samplerRingOffset + slotCount;
        _srvRingPeak = Math.Max(_srvRingPeak, srvDemand);
        _samplerRingPeak = Math.Max(_samplerRingPeak, samplerDemand);

        if (srvDemand > _srvRingCapacity || samplerDemand > _samplerRingCapacity)
        {
            // One draw wider than a whole ring: nothing to hand back, so fatal.
            if (slotCount > _srvRingCapacity || slotCount > _samplerRingCapacity)
                throw new InvalidOperationException(
                    $"A single draw needs {slotCount} descriptors, more than the whole shader-visible ring " +
                    $"(SRV {_srvRingCapacity}, sampler {_samplerRingCapacity}). Raise the initial ring " +
                    "capacities in D3D12Renderer (sampler heaps are capped at 2048 slots by the API).");

            // Too many draws must not throw: this is inside the draw loop, and
            // an exception would leave the command list open. Reuse the last
            // staged table. Those draws sample the wrong textures, but the
            // frame completes.
            if (!_ringOverflowReported)
            {
                _ringOverflowReported = true;
                _logger.LogWarning(
                    "D3D12 shader-visible descriptor rings exhausted mid-frame (SRV {SrvDemand}/{SrvCapacity}, " +
                    "sampler {SamplerDemand}/{SamplerCapacity}); further draws reuse the last staged " +
                    "descriptor table and will sample the wrong textures",
                    srvDemand, _srvRingCapacity, samplerDemand, _samplerRingCapacity);
            }

            if (_stagedSlotCount == slotCount)
                return (_lastSrvTable, _lastSamplerTable);

            // Nothing compatible to reuse: restart the ring. Earlier draws then
            // sample this one's textures.
            _srvRingOffset = 0;
            _samplerRingOffset = 0;
        }

        var srvHeap = (ID3D12DescriptorHeap*)_srvRing.Handle;
        var samplerHeap = (ID3D12DescriptorHeap*)_samplerRing.Handle;
        var srvCpuStart = srvHeap->GetCPUDescriptorHandleForHeapStart();
        var srvGpuStart = srvHeap->GetGPUDescriptorHandleForHeapStart();
        var samplerCpuStart = samplerHeap->GetCPUDescriptorHandleForHeapStart();
        var samplerGpuStart = samplerHeap->GetGPUDescriptorHandleForHeapStart();

        uint srvBase = _srvRingOffset;
        uint samplerBase = _samplerRingOffset;

        for (uint slot = 0; slot < slotCount; slot++)
        {
            var texture = pending.TryGetValue(slot, out var t) ? t : _fallbackTexture!;
            var srvDst = new CpuDescriptorHandle { Ptr = srvCpuStart.Ptr + (srvBase + slot) * _srvStride };
            var samplerDst = new CpuDescriptorHandle { Ptr = samplerCpuStart.Ptr + (samplerBase + slot) * _samplerStride };
            DevicePtr->CopyDescriptorsSimple(1, srvDst, texture.SrvCpu, DescriptorHeapType.CbvSrvUav);
            DevicePtr->CopyDescriptorsSimple(1, samplerDst, texture.SamplerCpu, DescriptorHeapType.Sampler);
        }

        _srvRingOffset += slotCount;
        _samplerRingOffset += slotCount;

        _lastSrvTable = new GpuDescriptorHandle { Ptr = srvGpuStart.Ptr + srvBase * _srvStride };
        _lastSamplerTable = new GpuDescriptorHandle { Ptr = samplerGpuStart.Ptr + samplerBase * _samplerStride };
        _stagedSlotCount = slotCount;
        return (_lastSrvTable, _lastSamplerTable);
    }

    internal ComPtr<ID3D12DescriptorHeap> CreateDescriptorHeap(DescriptorHeapType type, uint count, bool shaderVisible)
    {
        var desc = new DescriptorHeapDesc
        {
            Type = type,
            NumDescriptors = count,
            Flags = shaderVisible ? DescriptorHeapFlags.ShaderVisible : DescriptorHeapFlags.None,
            NodeMask = 0,
        };
        ID3D12DescriptorHeap* heap = null;
        Guid heapGuid = ID3D12DescriptorHeap.Guid;
        SilkMarshal.ThrowHResult(DevicePtr->CreateDescriptorHeap(&desc, &heapGuid, (void**)&heap));
        return ComOwnership.Own(heap);
    }

    internal ComPtr<ID3D12Resource> CreateUploadBuffer(uint sizeBytes, string debugName)
    {
        var heapProps = new HeapProperties { Type = HeapType.Upload };
        var desc = new ResourceDesc
        {
            Dimension = ResourceDimension.Buffer,
            Alignment = 0,
            Width = Math.Max(sizeBytes, 1u),
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = Format.FormatUnknown,
            SampleDesc = new SampleDesc(1, 0),
            Layout = TextureLayout.LayoutRowMajor,
            Flags = ResourceFlags.None,
        };
        ID3D12Resource* res = null;
        Guid resGuid = ID3D12Resource.Guid;
        SilkMarshal.ThrowHResult(DevicePtr->CreateCommittedResource(
            &heapProps, HeapFlags.None, &desc, ResourceStates.GenericRead, null, &resGuid, (void**)&res));
        return ComOwnership.Own(res);
    }

    internal ComPtr<ID3D12Resource> CreateTexture2D(uint width, uint height, ushort mipLevels, Format format)
    {
        var heapProps = new HeapProperties { Type = HeapType.Default };
        var desc = new ResourceDesc
        {
            Dimension = ResourceDimension.Texture2D,
            Alignment = 0,
            Width = width,
            Height = height,
            DepthOrArraySize = 1,
            MipLevels = mipLevels,
            Format = format,
            SampleDesc = new SampleDesc(1, 0),
            Layout = TextureLayout.LayoutUnknown,
            Flags = ResourceFlags.None,
        };
        ID3D12Resource* res = null;
        Guid resGuid = ID3D12Resource.Guid;
        SilkMarshal.ThrowHResult(DevicePtr->CreateCommittedResource(
            &heapProps, HeapFlags.None, &desc, ResourceStates.CopyDest, null, &resGuid, (void**)&res));
        return ComOwnership.Own(res);
    }

    // Blocking upload of every mip, for load time only.
    // Copied row by row: the destination pitch is 256-aligned and the source's
    // is not, so one memcpy per mip shears the texture. Each row copies the
    // tight row size, not either pitch.
    internal void UploadTexture(
        ComPtr<ID3D12Resource> texture,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<TextureMipDesc> mips)
    {
        if (_isRecording)
            throw new InvalidOperationException("Texture upload mid-frame is not supported; create textures at load time.");

        uint mipCount = (uint)mips.Length;
        var texDesc = ((ID3D12Resource*)texture.Handle)->GetDesc();

        var footprints = stackalloc PlacedSubresourceFootprint[(int)mipCount];
        var numRows = stackalloc uint[(int)mipCount];
        var rowSizes = stackalloc ulong[(int)mipCount];
        ulong totalBytes = 0;
        DevicePtr->GetCopyableFootprints(&texDesc, 0, mipCount, 0, footprints, numRows, rowSizes, &totalBytes);

        var staging = CreateUploadBuffer((uint)totalBytes, "TextureStaging");
        var res = (ID3D12Resource*)staging.Handle;
        void* mapped = null;
        var readRange = new Silk.NET.Direct3D12.Range { Begin = 0, End = 0 };
        SilkMarshal.ThrowHResult(res->Map(0, &readRange, &mapped));

        fixed (byte* payloadBase = payload)
        {
            for (int mip = 0; mip < mipCount; mip++)
            {
                uint srcRowPitch = (uint)mips[mip].RowPitch;
                uint dstRowPitch = footprints[mip].Footprint.RowPitch;
                ulong rowBytes = rowSizes[mip];
                byte* src = payloadBase + mips[mip].Offset;
                byte* dst = (byte*)mapped + footprints[mip].Offset;

                for (uint row = 0; row < numRows[mip]; row++)
                {
                    System.Buffer.MemoryCopy(
                        src + row * srcRowPitch,
                        dst + row * dstRowPitch,
                        rowBytes, rowBytes);
                }
            }
        }
        res->Unmap(0, null);

        BeginRecording();
        var list = (ID3D12GraphicsCommandList*)_commandList.Handle;

        for (uint mip = 0; mip < mipCount; mip++)
        {
            var dst = new TextureCopyLocation
            {
                PResource = (ID3D12Resource*)texture.Handle,
                Type = TextureCopyType.SubresourceIndex,
            };
            dst.Anonymous.SubresourceIndex = mip;
            var src = new TextureCopyLocation
            {
                PResource = res,
                Type = TextureCopyType.PlacedFootprint,
            };
            src.Anonymous.PlacedFootprint = footprints[mip];
            list->CopyTextureRegion(&dst, 0, 0, 0, &src, null);
        }

        Transition(list, (ID3D12Resource*)texture.Handle,
            ResourceStates.CopyDest, ResourceStates.PixelShaderResource);

        EndRecordingAndWait();
        staging.Dispose();
    }

    public override Mesh CreateMesh(ReadOnlySpan<float> vertices, ReadOnlySpan<uint> indices,
        ReadOnlySpan<VertexAttribute> attributes, MeshCpuAccess cpuAccess = MeshCpuAccess.Retained)
    {
        MeshesCreated++;
        var mesh = new D3D12Mesh(this, vertices, indices, attributes, cpuAccess);
        mesh.Unregister = () => _meshes.Remove(mesh);
        _meshes.Add(mesh);
        return mesh;
    }

    public override MeshUpload BeginMeshUpload(ReadOnlyMemory<float> vertices, ReadOnlyMemory<uint> indices,
        ReadOnlySpan<VertexAttribute> attributes, MeshCpuAccess cpuAccess = MeshCpuAccess.Retained, Bsp.Aabb? knownBounds = null)
    {
        MeshesCreated++;
        var mesh = new D3D12Mesh(this, vertices.Span, indices.Span, attributes, cpuAccess, deferred: true, knownBounds: knownBounds);
        mesh.Unregister = () => _meshes.Remove(mesh);
        _meshes.Add(mesh);
        return new MeshUpload(this, mesh, vertices, indices, uploaded: false);
    }

    /// <inheritdoc/>
    public override InstanceBuffer CreateInstanceBuffer(
        int capacityInstances, ReadOnlySpan<VertexAttribute> attributes, ShaderProgram program)
    {
        // program is unused: the input layout is part of the PSO here.
        int floats = ValidateInstanceLayout(capacityInstances, attributes);
        return new D3D12InstanceBuffer(
            this, capacityInstances, VertexAttribute.StandardLayout, attributes, floats);
    }

    protected override Texture CreateTextureCore(in TextureUploadDesc desc)
    {
        var texture = new D3D12Texture(this, in desc);
        texture.Unregister = () => _textures.Remove(texture);
        _textures.Add(texture);
        return texture;
    }

    public override TextureUpload BeginTextureUpload(in TextureUploadDesc desc)
    {
        desc.Validate();
        if (PrepareTextureUpload(desc) is { } prepared && prepared.Mips.Length > 1)
        {
            var preparedDesc = new TextureUploadDesc(prepared.Format, desc.ColorSpace, prepared.Payload, prepared.Mips, desc.Filter, desc.Wrap);
            var preparedTexture = new D3D12Texture(this, preparedDesc, deferred: true);
            preparedTexture.Unregister = () => _textures.Remove(preparedTexture);
            _textures.Add(preparedTexture);
            return new TextureUpload(this, preparedTexture, preparedDesc, prepared: prepared.Payload);
        }
        var texture = new D3D12Texture(this, in desc, deferred: true);
        texture.Unregister = () => _textures.Remove(texture);
        _textures.Add(texture);
        return new TextureUpload(this, texture, desc);
    }

    public override ShaderProgram CreateShader(string vertexSource, string fragmentSource)
    {
        var shader = new D3D12ShaderProgram(this, _d3dCompiler, vertexSource, fragmentSource);
        _shaders.Add(shader);
        return shader;
    }

    public override ShaderProgram CreateShader(PipelineBlob blob)
    {
        if (blob.Backend != GraphicsBackend.D3D12)
            throw new ArgumentException($"Expected D3D12 blob, got {blob.Backend}");
        if (blob.Format != ShaderDataFormat.SourceText)
            throw new ArgumentException($"D3D12 expects HLSL SourceText, got {blob.Format}");

        string vs = Encoding.UTF8.GetString(blob.VertexData
            ?? throw new InvalidOperationException("Compiled shader has no vertex stage"));
        string ps = Encoding.UTF8.GetString(blob.FragmentData
            ?? throw new InvalidOperationException("Compiled shader has no fragment stage"));

        return CreateShader(vs, ps);
    }

    // Render thread, between frames. A device loss ends the run; any other
    // failure is logged and the engine keeps rendering at the old size.
    private void DrainPendingResize()
    {
        // Read the latch once: the main thread may publish another size while
        // ResizeBuffers runs.
        Vector2D<int> newSize = FramebufferSize;

        if (!SwapChainResizePolicy.ShouldResize(
                newSize, _swapChainSize, _failedResizeSize, _swapChain.Handle is not null, _deviceLost))
            return;

        WaitForGpu();
        ReleaseBackBufferViews();

        int hr = ((IDXGISwapChain3*)_swapChain.Handle)->ResizeBuffers(
            BufferCount, (uint)newSize.X, (uint)newSize.Y, Format.FormatUnknown, _swapChainFlags);

        if (hr < 0)
        {
            if (DxgiInterop.IsDeviceLost(hr))
                throw DeviceLost(hr, $"resizing the swap chain to {newSize.X}×{newSize.Y}");

            _logger.LogError(
                "D3D12 ResizeBuffers to {Width}×{Height} failed: {Code} (0x{Hr:X8}). Staying at {OldWidth}×{OldHeight}.",
                newSize.X, newSize.Y, DxgiInterop.Describe(hr), hr, _swapChainSize.X, _swapChainSize.Y);
            _failedResizeSize = newSize;

            // The chain is still on its old buffers. Rebuild the views at the
            // old size or the next frame has none.
            CreateBackBufferViews((uint)_swapChainSize.X, (uint)_swapChainSize.Y);
            _frameIndex = _swapChain.GetCurrentBackBufferIndex();
            DrainDebugMessages();
            return;
        }

        CreateBackBufferViews((uint)newSize.X, (uint)newSize.Y);
        _frameIndex = _swapChain.GetCurrentBackBufferIndex();
        _swapChainSize = newSize;
        _failedResizeSize = null;
    }

    // Releasing a chain in exclusive fullscreen is undefined. The engine never
    // enters it, but SuppressAltEnter can fail on an odd driver.
    private void EnsureSwapChainWindowed()
    {
        if (_swapChain.Handle is null || _deviceLost) return;

        int fullscreen = 0;
        IDXGIOutput* output = null;
        if (((IDXGISwapChain3*)_swapChain.Handle)->GetFullscreenState(&fullscreen, &output) >= 0)
        {
            if (fullscreen != 0)
            {
                _logger.LogWarning("D3D12 swap chain was in exclusive fullscreen at shutdown; returning it to windowed first.");
                ((IDXGISwapChain3*)_swapChain.Handle)->SetFullscreenState(false, (IDXGIOutput*)null);
            }
            if (output is not null)
                output->Release();
        }
    }

    private GraphicsDeviceLostException DeviceLost(int hr, string action)
    {
        int reason = _device.Handle is not null ? DevicePtr->GetDeviceRemovedReason() : 0;

        // Set before the throw: Shutdown must not fence-wait on a dead queue.
        _deviceLost = true;

        DrainDebugMessages();

        return new GraphicsDeviceLostException(
            $"D3D12 device lost while {action}: {DxgiInterop.Describe(hr)} (0x{hr:X8}); " +
            $"ID3D12Device::GetDeviceRemovedReason = {DxgiInterop.Describe(reason)} (0x{reason:X8}). " +
            "The engine cannot recreate a device mid-run, so this ends the session.");
    }

    public override void Shutdown()
    {
        WaitForGpu();
        Profiler.GpuTimer?.Dispose();
        Profiler.GpuTimer = null;

        foreach (var pipeline in _pipelines)
            pipeline.Dispose();
        _pipelines.Clear();

        _lineBatch?.Dispose();
        _lineBatch = null;
        _debugShader = null;
        _fallbackTexture?.Dispose();
        _fallbackTexture = null;

        foreach (var mesh in _meshes) mesh.Dispose();
        _meshes.Clear();

        // Before the target loop: releasing a retired generation destroys a
        // target, which mutates the list that loop walks.
        _retirement?.ReleaseAll();
        _retirement = null;
        _presentTarget = null;
        _presentGeneration = 0;
        _sharedWriteHeld = false;

        // After the retirement and before the targets: the bridge's surfaces
        // alias both.
        _bridge?.Dispose();
        _bridge = null;

        ReleaseFrameResources();
        ReleaseMeshBufferPool();

        foreach (var target in _renderTargets)
            target.Dispose();
        _renderTargets.Clear();
        foreach (var tex in _textures) tex.Dispose();
        _textures.Clear();
        foreach (var sh in _shaders) sh.Dispose();
        _shaders.Clear();
        DefaultShader = null;

        // Release, not Dispose: Shutdown can run twice, and a ComPtr keeps its
        // handle after Dispose.
        ReleaseBackBufferViews();
        _isRecording = false;
        ReleaseFrameContexts();
        ReleaseCompletedResources(abandoningRecording: true);
        ComOwnership.Release(ref _rtvHeap);
        ComOwnership.Release(ref _dsvHeap);
        ComOwnership.Release(ref _fence);
        if (_fenceEvent != 0)
        {
            Kernel32.CloseHandle(_fenceEvent);
            _fenceEvent = 0;
        }
        EnsureSwapChainWindowed();
        ComOwnership.Release(ref _swapChain);
        ComOwnership.Release(ref _queue);
        ComOwnership.Release(ref _infoQueue);
        _dxgiMessages?.Dispose();
        _dxgiMessages = null;
        ComOwnership.Release(ref _device);

        base.Shutdown();
        _logger.LogInformation("Renderer shut down (D3D12)");
    }

    private void DrainDebugMessages()
    {
        // DXGI has its own queue: swap-chain rejections are explained only there.
        int errors = _dxgiMessages?.Drain(_logger, "D3D12") ?? 0;

        if (_infoQueue.Handle is null)
        {
            NoteDebugLayerErrors(errors);
            return;
        }

        var queue = (ID3D12InfoQueue*)_infoQueue.Handle;
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
                    // The one error not counted, and only while the bridge exists.
                    // CreateWrappedResource reports it once per wrap because a
                    // D3D12 resource has no D3D11 description, and the wrap then
                    // succeeds. Counting it would leave every composited session
                    // showing one error. It is still logged.
                    case MessageSeverity.Error
                        when _bridge is not null && msg->ID == MessageID.ReflectsharedpropertiesInvalidobject:
                        _logger.LogDebug(
                            "D3D12 debug layer (expected, D3D11On12 wrap): {Message}", text);
                        break;
                    case MessageSeverity.Corruption:
                    case MessageSeverity.Error:
                        errors++;
                        _logger.LogError("D3D12 debug layer: {Message}", text);
                        break;
                    case MessageSeverity.Warning:
                        _logger.LogWarning("D3D12 debug layer: {Message}", text);
                        break;
                    default:
                        _logger.LogDebug("D3D12 debug layer: {Message}", text);
                        break;
                }
            }
        }
        if (count > 0)
            queue->ClearStoredMessages();

        NoteDebugLayerErrors(errors);
    }

    private static class Kernel32
    {
        public const uint Infinite = 0xFFFFFFFF;

        [DllImport("kernel32.dll", EntryPoint = "CreateEventW", SetLastError = true)]
        public static extern nint CreateEvent(nint securityAttributes, int manualReset, int initialState, char* name);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(nint handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int CloseHandle(nint handle);
    }
}
