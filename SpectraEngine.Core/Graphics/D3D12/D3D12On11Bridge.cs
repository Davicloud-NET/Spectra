using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System;
using System.Runtime.InteropServices;
using D12 = Silk.NET.Direct3D12;
using EngineD3D11 = SpectraEngine.Core.Graphics.D3D11;

namespace SpectraEngine.Core.Graphics.D3D12;

// A D3D11On12 device over the renderer's D3D12 device and queue. It copies
// the frame's present target into a keyed-mutex shared texture that D3D11
// created, because the compositor's import refuses a D3D12-created handle
// (E_NOINTERFACE). The shared texture is built by D3D11Texture, so both
// backends share one UNORM-resource / sRGB-view decision.
//
// D3D12 never touches the shared texture, so the key only has to be held
// across Publish. Writing a keyed-mutex resource without the key returns S_OK
// and writes zeros. Render thread only.
internal sealed unsafe partial class D3D12On11Bridge : IDisposable
{
    private ComPtr<ID3D11Device> _device;
    private ComPtr<ID3D11DeviceContext> _context;

    // ID3D11On12Device. Silk.NET 2.23 does not bind it, so it is held as
    // IUnknown and called through the vtable.
    private ComPtr<IUnknown> _on12;

    private BridgeSurface? _surface;

    private D3D12On11Bridge(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, ComPtr<IUnknown> on12)
    {
        _device = device;
        _context = context;
        _on12 = on12;
    }

    // The bridge records into the renderer's own queue, so its copy is ordered
    // after the frame's command list without an extra fence.
    internal static D3D12On11Bridge Create(D12.ID3D12Device* device12, D12.ID3D12CommandQueue* queue12)
    {
        D3DFeatureLevel* levels = stackalloc D3DFeatureLevel[1] { D3DFeatureLevel.Level110 };
        D3DFeatureLevel chosen = default;
        void* queue = queue12;
        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;

        // No flags: the only texture this device creates is R8G8B8A8.
        SilkMarshal.ThrowHResult(D3D11On12CreateDevice(
            device12, 0u, levels, 1u, &queue, 1u, 0u, &device, &context, &chosen));

        ComPtr<ID3D11Device> owned = ComOwnership.Own(device);
        ComPtr<ID3D11DeviceContext> ownedContext = ComOwnership.Own(context);
        ComPtr<IUnknown> on12 = default;
        try
        {
            IUnknown* raw = null;
            Guid guid = On12DeviceGuid;
            SilkMarshal.ThrowHResult(((ID3D11Device*)owned.Handle)->QueryInterface(&guid, (void**)&raw));
            on12 = ComOwnership.Own(raw);
        }
        catch
        {
            ComOwnership.Release(ref ownedContext);
            ComOwnership.Release(ref owned);
            throw;
        }

        return new D3D12On11Bridge(owned, ownedContext, on12);
    }

    // Zero before a surface exists.
    internal nint SharedHandle => _surface?.Shared.SharedHandle ?? 0;

    // Null before a surface exists.
    internal IDXGIKeyedMutex* KeyedMutex => _surface is { } surface ? surface.Shared.KeyedMutex : null;

    internal bool HasSurface => _surface is not null;

    internal int SharedWidth => _surface?.Shared.Width ?? 0;

    internal int SharedHeight => _surface?.Shared.Height ?? 0;

    // Reads the shared texture back as tight RGBA8, rows bottom-first. Call
    // with the key held. This reads what a consumer gets; reading the present
    // target would skip the Publish copy. Diagnostics only: the Map stalls.
    internal void ReadShared(Span<byte> destination)
    {
        if (_surface is not { } surface) return;

        int width = surface.Shared.Width;
        int height = surface.Shared.Height;
        PixelReadback.ValidateSize(width, height, destination);

        var desc = new Texture2DDesc
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = surface.Shared.DxgiFormat,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Staging,
            BindFlags = 0,
            CPUAccessFlags = (uint)CpuAccessFlag.Read,
            MiscFlags = 0,
        };

        ID3D11Texture2D* stagingPtr = null;
        SilkMarshal.ThrowHResult(((ID3D11Device*)_device.Handle)->CreateTexture2D(&desc, null, &stagingPtr));
        ComPtr<ID3D11Texture2D> staging = ComOwnership.Own(stagingPtr);
        try
        {
            var context = (ID3D11DeviceContext*)_context.Handle;
            context->CopyResource((ID3D11Resource*)stagingPtr, surface.Shared.Resource);

            MappedSubresource mapped = default;
            SilkMarshal.ThrowHResult(context->Map((ID3D11Resource*)stagingPtr, 0, Map.Read, 0, &mapped));
            try
            {
                PixelReadback.CopyRowsBottomFirst(
                    (byte*)mapped.PData, mapped.RowPitch, width, height, destination);
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

    // Builds the shared texture for a new present target and wraps the
    // target's colour resource for D3D11. Once per surface, not per frame.
    internal void Attach(int width, int height, D12.ID3D12Resource* resolveResource)
    {
        if (_surface is not null)
            throw new InvalidOperationException("The bridge already has a surface; detach the old one first.");

        // Srgb + KeyedMutex gives a UNORM resource, so the consumer does not
        // decode a second time. D3D12 already encoded on its own sRGB view.
        var shared = EngineD3D11.D3D11Texture.CreateRenderTargetTexture(
            _device, width, height, TextureFormat.Rgba8, TextureColorSpace.Srgb,
            TextureFilter.Linear, TextureWrap.Clamp, RenderTargetSharing.KeyedMutex);

        ComPtr<ID3D11Resource> wrapped = default;
        try
        {
            wrapped = WrapResource(resolveResource);
        }
        catch
        {
            shared.Dispose();
            throw;
        }

        _surface = new BridgeSurface(shared, wrapped);
    }

    // Hands the live surface to the caller to dispose once the consumer lets
    // go. Free it with the retired target: the alias references its resource.
    internal IDisposable? Detach()
    {
        BridgeSurface? surface = _surface;
        _surface = null;
        return surface;
    }

    // Copies the present target into the shared texture. Call with the key held.
    // The release puts the wrapped resource back in the state D3D12RenderTarget
    // tracks; skip it and the next frame's barrier starts from the wrong state.
    // The flush must come before the key is handed over: nothing else submits
    // this copy, and the consumer is on another device.
    internal void Publish()
    {
        if (_surface is not { } surface) return;

        ID3D11Resource* wrapped = surface.Wrapped;
        AcquireWrappedResources(&wrapped, 1);
        try
        {
            ((ID3D11DeviceContext*)_context.Handle)->CopyResource(surface.Shared.Resource, wrapped);
        }
        finally
        {
            ReleaseWrappedResources(&wrapped, 1);
        }

        ((ID3D11DeviceContext*)_context.Handle)->Flush();
    }

    public void Dispose()
    {
        _surface?.Dispose();
        _surface = null;
        ComOwnership.Release(ref _on12);
        ComOwnership.Release(ref _context);
        ComOwnership.Release(ref _device);
    }

    // ID3D11On12Device vtable order: CreateWrappedResource (3), then
    // ReleaseWrappedResources (4), then AcquireWrappedResources (5). Release
    // comes before Acquire. Both return void, so swapping them still runs.

    private ComPtr<ID3D11Resource> WrapResource(D12.ID3D12Resource* resource12)
    {
        // Must match what the D3D12 resource was created with (AllowRenderTarget).
        var flags = new D3D11ResourceFlags
        {
            BindFlags = (uint)BindFlag.RenderTarget,
            MiscFlags = 0,
            CPUAccessFlags = 0,
            StructureByteStride = 0,
        };

        // In and out states are PixelShaderResource: what D3D12RenderTarget
        // leaves its colour attachment in and tracks it as.
        ID3D11Resource* wrapped = null;
        Guid guid = ID3D11Resource.Guid;

        // this, pResource12, pFlags11, InState, OutState, riid, ppResource11.
        // A vtable call has no arity check.
        var create = (delegate* unmanaged[Stdcall]<
            IUnknown*, IUnknown*, D3D11ResourceFlags*, uint, uint, Guid*, void**, int>)Vtbl[3];

        SilkMarshal.ThrowHResult(create(
            (IUnknown*)_on12.Handle, (IUnknown*)resource12, &flags,
            (uint)D12.ResourceStates.PixelShaderResource,
            (uint)D12.ResourceStates.PixelShaderResource,
            &guid, (void**)&wrapped));

        // CreateWrappedResource took its own reference on resource12.
        return ComOwnership.Own(wrapped);
    }

    private void AcquireWrappedResources(ID3D11Resource** resources, uint count)
    {
        var acquire = (delegate* unmanaged[Stdcall]<IUnknown*, ID3D11Resource**, uint, void>)Vtbl[5];
        acquire((IUnknown*)_on12.Handle, resources, count);
    }

    private void ReleaseWrappedResources(ID3D11Resource** resources, uint count)
    {
        var release = (delegate* unmanaged[Stdcall]<IUnknown*, ID3D11Resource**, uint, void>)Vtbl[4];
        release((IUnknown*)_on12.Handle, resources, count);
    }

    private void** Vtbl => *(void***)_on12.Handle;

    // IID_ID3D11On12Device
    private static readonly Guid On12DeviceGuid =
        new(0x85611e73, 0x70a9, 0x490e, 0x96, 0x14, 0xa9, 0xe3, 0x02, 0x77, 0x79, 0x04);

    // D3D11_RESOURCE_FLAGS
    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11ResourceFlags
    {
        public uint BindFlags;
        public uint MiscFlags;
        public uint CPUAccessFlags;
        public uint StructureByteStride;
    }

    [LibraryImport("d3d11.dll", EntryPoint = "D3D11On12CreateDevice")]
    private static partial int D3D11On12CreateDevice(
        D12.ID3D12Device* device,
        uint flags,
        D3DFeatureLevel* featureLevels,
        uint featureLevelCount,
        void** commandQueues,
        uint queueCount,
        uint nodeMask,
        ID3D11Device** outDevice,
        ID3D11DeviceContext** outContext,
        D3DFeatureLevel* chosenLevel);

    // One generation's shared texture and the D3D11 alias of its present target.
    private sealed class BridgeSurface(EngineD3D11.D3D11Texture shared, ComPtr<ID3D11Resource> wrapped)
        : IDisposable
    {
        private ComPtr<ID3D11Resource> _wrapped = wrapped;

        internal EngineD3D11.D3D11Texture Shared { get; } = shared;

        internal ID3D11Resource* Wrapped => (ID3D11Resource*)_wrapped.Handle;

        public void Dispose()
        {
            ComOwnership.Release(ref _wrapped);
            Shared.Dispose();
        }
    }
}
