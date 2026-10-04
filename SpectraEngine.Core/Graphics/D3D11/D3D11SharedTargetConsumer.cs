using Microsoft.Extensions.Logging;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System;

using D3D11Api = Silk.NET.Direct3D11.D3D11;
using DxgiApi = Silk.NET.DXGI.DXGI;

namespace SpectraEngine.Core.Graphics.D3D11;

// A second D3D11 device that opens the producer's shared texture and takes
// the consumer's turn on it. Used by both backends: the shared texture is a
// D3D11 one on D3D12 too. Each turn copies the whole texture, as a real
// compositor would, so the measured hand-over rate is honest. No debug layer:
// its cost would land in the measurement.
internal sealed unsafe class D3D11SharedTargetConsumer : ISharedTargetConsumer
{
    // WAIT_TIMEOUT. AcquireSync returns it as a positive HRESULT.
    private const int WaitTimeout = 0x00000102;

    private ComPtr<ID3D11Device> _device;
    private ComPtr<ID3D11DeviceContext> _context;
    private ComPtr<ID3D11Texture2D> _shared;
    private ComPtr<ID3D11Texture2D> _snapshot;
    private ComPtr<IDXGIKeyedMutex> _mutex;

    private D3D11SharedTargetConsumer(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        ComPtr<ID3D11Texture2D> shared,
        ComPtr<ID3D11Texture2D> snapshot,
        ComPtr<IDXGIKeyedMutex> mutex)
    {
        _device = device;
        _context = context;
        _shared = shared;
        _snapshot = snapshot;
        _mutex = mutex;
    }

    // A shared resource only opens on the adapter that created it, so try
    // each adapter in turn. Null when none takes the handle.
    internal static D3D11SharedTargetConsumer? TryOpen(
        nint sharedHandle, int width, int height, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (sharedHandle == 0)
        {
            logger.LogError("The producer published no shared handle to open.");
            return null;
        }

        var dxgi = DxgiApi.GetApi();
        IDXGIFactory1* factoryPtr = null;
        Guid factoryGuid = IDXGIFactory1.Guid;
        if (dxgi.CreateDXGIFactory1(&factoryGuid, (void**)&factoryPtr) < 0)
        {
            logger.LogError("Could not enumerate adapters to open the shared target on.");
            return null;
        }

        ComPtr<IDXGIFactory1> factory = ComOwnership.Own(factoryPtr);
        try
        {
            for (uint index = 0; ; index++)
            {
                IDXGIAdapter1* adapterPtr = null;
                if (((IDXGIFactory1*)factory.Handle)->EnumAdapters1(index, &adapterPtr) < 0)
                    break;

                ComPtr<IDXGIAdapter1> adapter = ComOwnership.Own(adapterPtr);
                try
                {
                    // Skip WARP: it would turn this into a measurement of WARP.
                    AdapterDesc1 desc = default;
                    ((IDXGIAdapter1*)adapter.Handle)->GetDesc1(&desc);
                    if ((desc.Flags & (uint)AdapterFlag.Software) != 0)
                        continue;

                    if (TryOpenOn((IDXGIAdapter1*)adapter.Handle, sharedHandle, width, height)
                        is { } opened)
                    {
                        return opened;
                    }
                }
                finally
                {
                    ComOwnership.Release(ref adapter);
                }
            }
        }
        finally
        {
            ComOwnership.Release(ref factory);
        }

        logger.LogError(
            "No adapter on this machine would open the producer's shared handle 0x{Handle:X}.",
            sharedHandle);
        return null;
    }

    private static D3D11SharedTargetConsumer? TryOpenOn(
        IDXGIAdapter1* adapter, nint sharedHandle, int width, int height)
    {
        ComPtr<ID3D11Device> device = default;
        ComPtr<ID3D11DeviceContext> context = default;
        ComPtr<ID3D11Texture2D> shared = default;
        ComPtr<ID3D11Texture2D> snapshot = default;
        ComPtr<IDXGIKeyedMutex> mutex = default;

        try
        {
            var api = D3D11Api.GetApi();
            ID3D11Device* devicePtr = null;
            ID3D11DeviceContext* contextPtr = null;
            D3DFeatureLevel chosen = default;

            // Unknown driver type: Hardware plus an explicit adapter is E_INVALIDARG.
            SilkMarshal.ThrowHResult(api.CreateDevice(
                (IDXGIAdapter*)adapter, D3DDriverType.Unknown, 0, 0u,
                (D3DFeatureLevel*)null, 0u, D3D11Api.SdkVersion,
                &devicePtr, &chosen, &contextPtr));

            device = ComOwnership.Own(devicePtr);
            context = ComOwnership.Own(contextPtr);

            shared = OpenShared(devicePtr, sharedHandle);
            snapshot = CreateSnapshot(devicePtr, width, height);

            IDXGIKeyedMutex* mutexPtr = null;
            Guid mutexGuid = IDXGIKeyedMutex.Guid;
            SilkMarshal.ThrowHResult(((ID3D11Texture2D*)shared.Handle)->QueryInterface(
                &mutexGuid, (void**)&mutexPtr));
            mutex = ComOwnership.Own(mutexPtr);

            return new D3D11SharedTargetConsumer(device, context, shared, snapshot, mutex);
        }
        catch (Exception)
        {
            // Expected for every adapter but one, so not logged. TryOpen reports
            // once if none takes it.
            ComOwnership.Release(ref mutex);
            ComOwnership.Release(ref snapshot);
            ComOwnership.Release(ref shared);
            ComOwnership.Release(ref context);
            ComOwnership.Release(ref device);
            return null;
        }
    }

    /// <inheritdoc/>
    public bool TakeTurn(int timeoutMs)
    {
        if (_mutex.Handle is null) return false;

        var mutex = (IDXGIKeyedMutex*)_mutex.Handle;
        int hr = mutex->AcquireSync(Renderer.SharedConsumerKey, (uint)Math.Max(0, timeoutMs));

        // hr < 0 alone misses the timeout.
        if (hr == WaitTimeout) return false;
        if (hr < 0) return false;

        try
        {
            var context = (ID3D11DeviceContext*)_context.Handle;
            context->CopyResource((ID3D11Resource*)_snapshot.Handle, (ID3D11Resource*)_shared.Handle);
            context->Flush();
        }
        finally
        {
            // A missed release deadlocks the producer on its next frame.
            mutex->ReleaseSync(Renderer.SharedProducerKey);
        }

        return true;
    }

    public void Dispose()
    {
        ComOwnership.Release(ref _mutex);
        ComOwnership.Release(ref _snapshot);
        ComOwnership.Release(ref _shared);
        ComOwnership.Release(ref _context);
        ComOwnership.Release(ref _device);
    }

    private static ComPtr<ID3D11Texture2D> OpenShared(ID3D11Device* device, nint sharedHandle)
    {
        // OpenSharedResource1: the older call refuses an NT handle with E_INVALIDARG.
        ID3D11Device1* device1Ptr = null;
        Guid device1Guid = ID3D11Device1.Guid;
        SilkMarshal.ThrowHResult(device->QueryInterface(&device1Guid, (void**)&device1Ptr));
        ComPtr<ID3D11Device1> device1 = ComOwnership.Own(device1Ptr);
        try
        {
            ID3D11Texture2D* texturePtr = null;
            Guid textureGuid = ID3D11Texture2D.Guid;
            SilkMarshal.ThrowHResult(device1Ptr->OpenSharedResource1(
                (void*)sharedHandle, &textureGuid, (void**)&texturePtr));
            return ComOwnership.Own(texturePtr);
        }
        finally
        {
            ComOwnership.Release(ref device1);
        }
    }

    private static ComPtr<ID3D11Texture2D> CreateSnapshot(ID3D11Device* device, int width, int height)
    {
        // UNORM to match the shared resource (not its sRGB view): CopyResource needs equal formats.
        var desc = new Texture2DDesc
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.FormatR8G8B8A8Unorm,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.ShaderResource,
            CPUAccessFlags = 0,
            MiscFlags = 0,
        };

        ID3D11Texture2D* texturePtr = null;
        SilkMarshal.ThrowHResult(device->CreateTexture2D(&desc, null, &texturePtr));
        return ComOwnership.Own(texturePtr);
    }
}
