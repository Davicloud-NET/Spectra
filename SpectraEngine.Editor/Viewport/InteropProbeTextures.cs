using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using D12 = Silk.NET.Direct3D12;
using D3D11Api = Silk.NET.Direct3D11.D3D11;
using DxgiApi = Silk.NET.DXGI.DXGI;

namespace SpectraEngine.Editor.Viewport;

// Shell-side copy of Core's ComOwnership, which is internal to the engine.
// Silk's ComPtr<T> constructor AddRefs, so a freshly created pointer must
// hand its reference over or the object leaks.
internal static unsafe partial class ProbeCom
{
    // Wraps raw and releases the caller's reference.
    internal static ComPtr<T> Own<T>(T* raw) where T : unmanaged, IComVtbl<T>
    {
        if (raw is null)
            return default;

        var owned = new ComPtr<T>(raw);
        ((IUnknown*)raw)->Release();
        return owned;
    }

    // Clears the field too: ComPtr.Dispose does not null the handle, so a
    // second release would over-release.
    internal static void Release<T>(ref ComPtr<T> field) where T : unmanaged, IComVtbl<T>
    {
        if (field.Handle is null)
            return;

        field.Dispose();
        field = default;
    }

    // Raw BOOL: Silk.NET.Core.Native also defines an UnmanagedType, so the
    // marshalling attribute would be ambiguous here.
    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    internal static partial int CloseHandle(nint handle);
}

// One route's texture and the handle the compositor is asked to import.
// Already cleared to a solid colour by the time the handle is handed out.
internal sealed unsafe class SharedProbeTexture : IDisposable
{
    private ComPtr<ID3D11Texture2D> _texture11;
    private ComPtr<D12.ID3D12Resource> _texture12;
    private nint _handle;
    private readonly bool _ntHandle;

    internal SharedProbeTexture(
        string handleKind,
        int width,
        int height,
        nint handle,
        bool ntHandle,
        bool keyedMutex,
        ComPtr<ID3D11Texture2D> texture11,
        ComPtr<D12.ID3D12Resource> texture12)
    {
        HandleKind = handleKind;
        Width = width;
        Height = height;
        KeyedMutex = keyedMutex;
        _handle = handle;
        _ntHandle = ntHandle;
        _texture11 = texture11;
        _texture12 = texture12;
    }

    // A KnownPlatformGraphicsExternalImageHandleTypes value.
    internal string HandleKind { get; }

    internal int Width { get; }

    internal int Height { get; }

    // A D3D12 resource never carries an IDXGIKeyedMutex.
    internal bool KeyedMutex { get; }

    internal nint Handle => _handle;

    // This side released key 1; the importer acquires it and hands back 0.
    internal uint AcquireKey => 1;

    internal uint ReleaseKey => 0;

    public void Dispose()
    {
        ProbeCom.Release(ref _texture11);
        ProbeCom.Release(ref _texture12);

        // Only an NT handle is a kernel object. Never CloseHandle a legacy
        // global shared handle.
        if (_handle != 0 && _ntHandle)
            _ = ProbeCom.CloseHandle(_handle);
        _handle = 0;
    }
}

// Creates the textures the interop probe hands to the compositor, one per route.
// Adapter is picked by the compositor's LUID: a shared handle only opens on the
// same adapter, so the system default would fail on a hybrid laptop.
// Devices are created per route, so a machine without D3D12 still answers
// routes 1 and 2.
internal sealed unsafe partial class InteropProbeTextures : IDisposable
{
    internal const int TextureSize = 64;

    private const uint SharedResourceRead = 0x80000000u;
    private const uint SharedResourceWrite = 0x00000001u;

    // D3D12 shared handles take GENERIC_ALL and nothing else.
    private const uint GenericAll = 0x10000000u;

    private const uint Infinite = 0xFFFFFFFFu;

    private static readonly float[] SolidColour = [0.10f, 0.55f, 0.90f, 1.0f];

    private readonly ILogger _logger;
    private readonly DxgiApi _dxgi = DxgiApi.GetApi();
    private readonly D3D11Api _d3d11 = D3D11Api.GetApi();
    private readonly D12.D3D12 _d3d12 = D12.D3D12.GetApi();

    private ComPtr<IDXGIAdapter> _adapter;
    private ComPtr<ID3D11Device> _device11;
    private ComPtr<ID3D11DeviceContext> _context11;
    private ComPtr<D12.ID3D12Device> _device12;
    private ComPtr<D12.ID3D12CommandQueue> _queue12;
    private ComPtr<ID3D11Device> _device11On12;
    private ComPtr<ID3D11DeviceContext> _context11On12;

    internal InteropProbeTextures(byte[]? compositorLuid, ILogger logger)
    {
        _logger = logger;
        _adapter = FindAdapter(compositorLuid, out string name);
        AdapterName = name;
        DriverVersion = ReadDriverVersion(_adapter);
    }

    // Never throws. An empty version never matches a recorded one, which only
    // costs a fallback to the native child.
    private static string ReadDriverVersion(ComPtr<IDXGIAdapter> adapter)
    {
        if (adapter.Handle is null)
            return string.Empty;

        try
        {
            Guid device = IDXGIDevice.Guid;
            long umd = 0;
            if (((IDXGIAdapter*)adapter.Handle)->CheckInterfaceSupport(&device, &umd) < 0)
                return string.Empty;

            // Four 16-bit parts, the form Device Manager shows.
            ulong bits = (ulong)umd;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{(bits >> 48) & 0xFFFF}.{(bits >> 32) & 0xFFFF}.{(bits >> 16) & 0xFFFF}.{bits & 0xFFFF}");
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    // Adapter description, or why the system default was used.
    internal string AdapterName { get; }

    // User-mode driver version, or empty when it could not be read.
    internal string DriverVersion { get; private set; } = string.Empty;

    // Route 1: keyed-mutex D3D11 texture, NT handle. The launch-time rehearsal
    // passes a size of one texel.
    internal SharedProbeTexture CreateD3D11NtHandleTexture(int size = TextureSize)
    {
        EnsureDevice11();
        return CreateSharedD3D11Texture(_device11, _context11, ntHandle: true, size);
    }

    // Route 2: the same texture through the legacy global handle.
    internal SharedProbeTexture CreateD3D11GlobalHandleTexture()
    {
        EnsureDevice11();
        return CreateSharedD3D11Texture(_device11, _context11, ntHandle: false, TextureSize);
    }

    // Route 3: committed D3D12 resource on a shared heap, NT handle.
    // No keyed mutex; D3D12 synchronises with fences.
    internal SharedProbeTexture CreateD3D12Texture()
    {
        EnsureDevice12();
        var device = (D12.ID3D12Device*)_device12.Handle;

        var heapProps = new D12.HeapProperties { Type = D12.HeapType.Default };
        var desc = new D12.ResourceDesc
        {
            Dimension = D12.ResourceDimension.Texture2D,
            Alignment = 0,
            Width = TextureSize,
            Height = TextureSize,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = Format.FormatR8G8B8A8Unorm,
            SampleDesc = new SampleDesc(1, 0),
            Layout = D12.TextureLayout.LayoutUnknown,
            Flags = D12.ResourceFlags.AllowRenderTarget,
        };

        var clearValue = new D12.ClearValue { Format = Format.FormatR8G8B8A8Unorm };
        clearValue.Anonymous.Color[0] = SolidColour[0];
        clearValue.Anonymous.Color[1] = SolidColour[1];
        clearValue.Anonymous.Color[2] = SolidColour[2];
        clearValue.Anonymous.Color[3] = SolidColour[3];

        D12.ID3D12Resource* raw = null;
        Guid resourceGuid = D12.ID3D12Resource.Guid;

        // A shared resource must be created in COMMON.
        SilkMarshal.ThrowHResult(device->CreateCommittedResource(
            &heapProps, D12.HeapFlags.Shared, &desc, D12.ResourceStates.Common, &clearValue,
            &resourceGuid, (void**)&raw));
        ComPtr<D12.ID3D12Resource> texture = ProbeCom.Own(raw);

        try
        {
            ClearD3D12Texture(texture);

            void* handle = null;
            SilkMarshal.ThrowHResult(device->CreateSharedHandle(
                (D12.ID3D12DeviceChild*)texture.Handle, (SecurityAttributes*)null, GenericAll,
                (char*)null, &handle));

            return new SharedProbeTexture(
                KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureNtHandle,
                TextureSize, TextureSize, (nint)handle, ntHandle: true, keyedMutex: false,
                default, texture);
        }
        catch
        {
            ProbeCom.Release(ref texture);
            throw;
        }
    }

    // Route 4: route 1's texture on a D3D11On12 device over the D3D12 device.
    internal SharedProbeTexture CreateD3D11On12Texture()
    {
        EnsureDevice11On12();
        return CreateSharedD3D11Texture(_device11On12, _context11On12, ntHandle: true, TextureSize);
    }

    public void Dispose()
    {
        ProbeCom.Release(ref _context11On12);
        ProbeCom.Release(ref _device11On12);
        ProbeCom.Release(ref _queue12);
        ProbeCom.Release(ref _device12);
        ProbeCom.Release(ref _context11);
        ProbeCom.Release(ref _device11);
        ProbeCom.Release(ref _adapter);
    }

    private void EnsureDevice11()
    {
        if (_device11.Handle is not null)
            return;

        D3DFeatureLevel* levels = stackalloc D3DFeatureLevel[1] { D3DFeatureLevel.Level110 };
        D3DFeatureLevel chosen = default;
        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;

        // With an explicit adapter the driver type must be Unknown: D3D11
        // refuses Hardware plus an adapter.
        D3DDriverType driverType = _adapter.Handle is null ? D3DDriverType.Hardware : D3DDriverType.Unknown;

        SilkMarshal.ThrowHResult(_d3d11.CreateDevice(
            (IDXGIAdapter*)_adapter.Handle,
            driverType,
            0,
            (uint)CreateDeviceFlag.BgraSupport,
            levels,
            1,
            D3D11Api.SdkVersion,
            &device,
            &chosen,
            &context));

        _device11 = ProbeCom.Own(device);
        _context11 = ProbeCom.Own(context);
    }

    private void EnsureDevice12()
    {
        if (_device12.Handle is not null)
            return;

        D12.ID3D12Device* device = null;
        Guid deviceGuid = D12.ID3D12Device.Guid;
        SilkMarshal.ThrowHResult(_d3d12.CreateDevice(
            (IUnknown*)_adapter.Handle, D3DFeatureLevel.Level110, &deviceGuid, (void**)&device));
        _device12 = ProbeCom.Own(device);

        // Used by the clear and by D3D11On12CreateDevice.
        var queueDesc = new D12.CommandQueueDesc
        {
            Type = D12.CommandListType.Direct,
            Priority = 0,
            Flags = D12.CommandQueueFlags.None,
            NodeMask = 0,
        };
        D12.ID3D12CommandQueue* queue = null;
        Guid queueGuid = D12.ID3D12CommandQueue.Guid;
        SilkMarshal.ThrowHResult(((D12.ID3D12Device*)_device12.Handle)->CreateCommandQueue(
            &queueDesc, &queueGuid, (void**)&queue));
        _queue12 = ProbeCom.Own(queue);
    }

    private void EnsureDevice11On12()
    {
        if (_device11On12.Handle is not null)
            return;

        EnsureDevice12();

        D3DFeatureLevel* levels = stackalloc D3DFeatureLevel[1] { D3DFeatureLevel.Level110 };
        D3DFeatureLevel chosen = default;
        void* queue = _queue12.Handle;
        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;

        SilkMarshal.ThrowHResult(D3D11On12CreateDevice(
            _device12.Handle, 0, levels, 1, &queue, 1, 0, &device, &context, &chosen));

        _device11On12 = ProbeCom.Own(device);
        _context11On12 = ProbeCom.Own(context);
    }

    // Silk.NET 2.23 has no D3D11On12 binding.
    [LibraryImport("d3d11.dll", EntryPoint = "D3D11On12CreateDevice")]
    private static partial int D3D11On12CreateDevice(
        void* device,
        uint flags,
        D3DFeatureLevel* featureLevels,
        uint featureLevelCount,
        void** commandQueues,
        uint queueCount,
        uint nodeMask,
        ID3D11Device** outDevice,
        ID3D11DeviceContext** outContext,
        D3DFeatureLevel* chosenLevel);

    private static SharedProbeTexture CreateSharedD3D11Texture(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, bool ntHandle, int size)
    {
        // SHARED_NTHANDLE is only legal with SHARED or SHARED_KEYEDMUTEX.
        uint misc = (uint)ResourceMiscFlag.SharedKeyedmutex;
        if (ntHandle)
            misc |= (uint)ResourceMiscFlag.SharedNthandle;

        var desc = new Texture2DDesc
        {
            Width = (uint)size,
            Height = (uint)size,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.FormatR8G8B8A8Unorm,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)(BindFlag.RenderTarget | BindFlag.ShaderResource),
            CPUAccessFlags = 0,
            MiscFlags = misc,
        };

        ID3D11Texture2D* raw = null;
        SilkMarshal.ThrowHResult(((ID3D11Device*)device.Handle)->CreateTexture2D(
            &desc, (SubresourceData*)null, &raw));
        ComPtr<ID3D11Texture2D> texture = ProbeCom.Own(raw);

        ComPtr<IDXGIKeyedMutex> mutex = default;
        ComPtr<ID3D11RenderTargetView> rtv = default;
        try
        {
            IDXGIKeyedMutex* rawMutex = null;
            Guid mutexGuid = IDXGIKeyedMutex.Guid;
            SilkMarshal.ThrowHResult(((ID3D11Texture2D*)texture.Handle)->QueryInterface(
                &mutexGuid, (void**)&rawMutex));
            mutex = ProbeCom.Own(rawMutex);

            // A new keyed mutex starts released on key 0.
            SilkMarshal.ThrowHResult(((IDXGIKeyedMutex*)mutex.Handle)->AcquireSync(0, Infinite));
            try
            {
                ID3D11RenderTargetView* view = null;
                SilkMarshal.ThrowHResult(((ID3D11Device*)device.Handle)->CreateRenderTargetView(
                    (ID3D11Resource*)texture.Handle, (RenderTargetViewDesc*)null, &view));
                rtv = ProbeCom.Own(view);

                fixed (float* colour = SolidColour)
                {
                    ((ID3D11DeviceContext*)context.Handle)->ClearRenderTargetView(
                        (ID3D11RenderTargetView*)rtv.Handle, colour);
                }

                // The importer is on another device: submit before releasing
                // the key.
                ((ID3D11DeviceContext*)context.Handle)->Flush();
            }
            finally
            {
                SilkMarshal.ThrowHResult(((IDXGIKeyedMutex*)mutex.Handle)->ReleaseSync(1));
            }

            (nint handle, string kind) = ntHandle
                ? (CreateNtHandle(texture), KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureNtHandle)
                : (GetGlobalSharedHandle(texture), KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle);

            return new SharedProbeTexture(
                kind, size, size, handle, ntHandle, keyedMutex: true, texture, default);
        }
        catch
        {
            ProbeCom.Release(ref texture);
            throw;
        }
        finally
        {
            ProbeCom.Release(ref rtv);
            ProbeCom.Release(ref mutex);
        }
    }

    private static nint CreateNtHandle(ComPtr<ID3D11Texture2D> texture)
    {
        IDXGIResource1* raw = null;
        Guid guid = IDXGIResource1.Guid;
        SilkMarshal.ThrowHResult(((ID3D11Texture2D*)texture.Handle)->QueryInterface(&guid, (void**)&raw));
        ComPtr<IDXGIResource1> resource = ProbeCom.Own(raw);
        try
        {
            void* handle = null;
            SilkMarshal.ThrowHResult(((IDXGIResource1*)resource.Handle)->CreateSharedHandle(
                (SecurityAttributes*)null, SharedResourceRead | SharedResourceWrite, (char*)null, &handle));
            return (nint)handle;
        }
        finally
        {
            ProbeCom.Release(ref resource);
        }
    }

    private static nint GetGlobalSharedHandle(ComPtr<ID3D11Texture2D> texture)
    {
        IDXGIResource* raw = null;
        Guid guid = IDXGIResource.Guid;
        SilkMarshal.ThrowHResult(((ID3D11Texture2D*)texture.Handle)->QueryInterface(&guid, (void**)&raw));
        ComPtr<IDXGIResource> resource = ProbeCom.Own(raw);
        try
        {
            void* handle = null;
            SilkMarshal.ThrowHResult(((IDXGIResource*)resource.Handle)->GetSharedHandle(&handle));
            return (nint)handle;
        }
        finally
        {
            ProbeCom.Release(ref resource);
        }
    }

    private void ClearD3D12Texture(ComPtr<D12.ID3D12Resource> texture)
    {
        var device = (D12.ID3D12Device*)_device12.Handle;

        ComPtr<D12.ID3D12DescriptorHeap> heap = default;
        ComPtr<D12.ID3D12CommandAllocator> allocator = default;
        ComPtr<D12.ID3D12GraphicsCommandList> list = default;
        ComPtr<D12.ID3D12Fence> fence = default;
        try
        {
            var heapDesc = new D12.DescriptorHeapDesc
            {
                Type = D12.DescriptorHeapType.Rtv,
                NumDescriptors = 1,
                Flags = D12.DescriptorHeapFlags.None,
                NodeMask = 0,
            };
            D12.ID3D12DescriptorHeap* rawHeap = null;
            Guid heapGuid = D12.ID3D12DescriptorHeap.Guid;
            SilkMarshal.ThrowHResult(device->CreateDescriptorHeap(&heapDesc, &heapGuid, (void**)&rawHeap));
            heap = ProbeCom.Own(rawHeap);

            D12.CpuDescriptorHandle rtv =
                ((D12.ID3D12DescriptorHeap*)heap.Handle)->GetCPUDescriptorHandleForHeapStart();
            device->CreateRenderTargetView(
                (D12.ID3D12Resource*)texture.Handle, (D12.RenderTargetViewDesc*)null, rtv);

            D12.ID3D12CommandAllocator* rawAllocator = null;
            Guid allocatorGuid = D12.ID3D12CommandAllocator.Guid;
            SilkMarshal.ThrowHResult(device->CreateCommandAllocator(
                D12.CommandListType.Direct, &allocatorGuid, (void**)&rawAllocator));
            allocator = ProbeCom.Own(rawAllocator);

            D12.ID3D12GraphicsCommandList* rawList = null;
            Guid listGuid = D12.ID3D12GraphicsCommandList.Guid;
            SilkMarshal.ThrowHResult(device->CreateCommandList(
                0, D12.CommandListType.Direct, (D12.ID3D12CommandAllocator*)allocator.Handle,
                (D12.ID3D12PipelineState*)null, &listGuid, (void**)&rawList));
            list = ProbeCom.Own(rawList);

            var listPtr = (D12.ID3D12GraphicsCommandList*)list.Handle;
            var resourcePtr = (D12.ID3D12Resource*)texture.Handle;

            Transition(listPtr, resourcePtr, D12.ResourceStates.Common, D12.ResourceStates.RenderTarget);
            fixed (float* colour = SolidColour)
                listPtr->ClearRenderTargetView(rtv, colour, 0, (Silk.NET.Maths.Box2D<int>*)null);
            Transition(listPtr, resourcePtr, D12.ResourceStates.RenderTarget, D12.ResourceStates.Common);

            SilkMarshal.ThrowHResult(listPtr->Close());

            var executeList = (D12.ID3D12CommandList*)listPtr;
            ((D12.ID3D12CommandQueue*)_queue12.Handle)->ExecuteCommandLists(1, &executeList);

            D12.ID3D12Fence* rawFence = null;
            Guid fenceGuid = D12.ID3D12Fence.Guid;
            SilkMarshal.ThrowHResult(device->CreateFence(0, D12.FenceFlags.None, &fenceGuid, (void**)&rawFence));
            fence = ProbeCom.Own(rawFence);
            SilkMarshal.ThrowHResult(((D12.ID3D12CommandQueue*)_queue12.Handle)->Signal(
                (D12.ID3D12Fence*)fence.Handle, 1));

            // Spin with a timeout so the probe cannot hang.
            var waited = Stopwatch.StartNew();
            while (((D12.ID3D12Fence*)fence.Handle)->GetCompletedValue() < 1)
            {
                if (waited.Elapsed > TimeSpan.FromSeconds(2))
                    throw new TimeoutException("the D3D12 clear did not complete within 2 s");
                Thread.Sleep(0);
            }
        }
        finally
        {
            ProbeCom.Release(ref fence);
            ProbeCom.Release(ref list);
            ProbeCom.Release(ref allocator);
            ProbeCom.Release(ref heap);
        }
    }

    private static void Transition(
        D12.ID3D12GraphicsCommandList* list,
        D12.ID3D12Resource* resource,
        D12.ResourceStates before,
        D12.ResourceStates after)
    {
        var barrier = new D12.ResourceBarrier
        {
            Type = D12.ResourceBarrierType.Transition,
            Flags = D12.ResourceBarrierFlags.None,
        };
        barrier.Anonymous.Transition = new D12.ResourceTransitionBarrier
        {
            PResource = resource,
            Subresource = uint.MaxValue,
            StateBefore = before,
            StateAfter = after,
        };
        list->ResourceBarrier(1, &barrier);
    }

    private ComPtr<IDXGIAdapter> FindAdapter(byte[]? compositorLuid, out string name)
    {
        if (compositorLuid is not { Length: 8 })
        {
            name = "system default (the compositor reported no LUID)";
            return default;
        }

        uint low = BitConverter.ToUInt32(compositorLuid, 0);
        int high = BitConverter.ToInt32(compositorLuid, 4);

        IDXGIFactory1* rawFactory = null;
        Guid factoryGuid = IDXGIFactory1.Guid;
        if (_dxgi.CreateDXGIFactory1(&factoryGuid, (void**)&rawFactory) < 0)
        {
            name = "system default (adapters could not be enumerated)";
            return default;
        }

        ComPtr<IDXGIFactory1> factory = ProbeCom.Own(rawFactory);
        try
        {
            for (uint index = 0; ; index++)
            {
                IDXGIAdapter1* rawAdapter = null;
                if (((IDXGIFactory1*)factory.Handle)->EnumAdapters1(index, &rawAdapter) < 0)
                    break;

                ComPtr<IDXGIAdapter1> adapter = ProbeCom.Own(rawAdapter);
                AdapterDesc1 desc = default;
                ((IDXGIAdapter1*)adapter.Handle)->GetDesc1(&desc);

                if (desc.AdapterLuid.Low != low || desc.AdapterLuid.High != high)
                {
                    ProbeCom.Release(ref adapter);
                    continue;
                }

                string description = DescriptionOf(ref desc);
                IDXGIAdapter* asBase = null;
                Guid baseGuid = IDXGIAdapter.Guid;
                int hr = ((IDXGIAdapter1*)adapter.Handle)->QueryInterface(&baseGuid, (void**)&asBase);
                ProbeCom.Release(ref adapter);

                if (hr < 0)
                {
                    name = $"system default ({description} matched the LUID but could not be queried)";
                    return default;
                }

                _logger.LogDebug("Interop probe: creating textures on {Adapter}", description);
                name = description;
                return ProbeCom.Own(asBase);
            }

            name = "system default (no adapter matched the compositor's LUID)";
            return default;
        }
        finally
        {
            ProbeCom.Release(ref factory);
        }
    }

    // Description is a fixed 128-char UTF-16 buffer.
    private static string DescriptionOf(ref AdapterDesc1 desc)
    {
        fixed (char* p = desc.Description)
        {
            var span = new ReadOnlySpan<char>(p, 128);
            int end = span.IndexOf('\0');
            return new string(end < 0 ? span : span[..end]);
        }
    }
}
