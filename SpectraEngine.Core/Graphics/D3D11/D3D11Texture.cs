using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Graphics.D3D11;

internal sealed unsafe partial class D3D11Texture : Texture
{
    private ComPtr<ID3D11Texture2D> _texture;
    private ComPtr<ID3D11ShaderResourceView> _srv;
    private ComPtr<ID3D11SamplerState> _sampler;
    private ComPtr<IDXGIKeyedMutex> _keyedMutex;
    private nint _sharedHandle;
    private bool _disposed;

    public ComPtr<ID3D11ShaderResourceView> Srv => _srv;
    public ComPtr<ID3D11SamplerState> Sampler => _sampler;

    internal ID3D11Resource* Resource => (ID3D11Resource*)_texture.Handle;

    // The resource's format. The SRV must match it.
    internal Silk.NET.DXGI.Format DxgiFormat { get; private set; }

    // Differs from DxgiFormat only on a shared target: UNORM resource, sRGB view.
    internal Silk.NET.DXGI.Format RtvFormat { get; private set; }

    // NT handle, zero when not shared.
    internal nint SharedHandle => _sharedHandle;

    internal bool IsShared => _sharedHandle != 0;

    // Null when not shared.
    internal IDXGIKeyedMutex* KeyedMutex => (IDXGIKeyedMutex*)_keyedMutex.Handle;

    private D3D11Texture(
        ComPtr<ID3D11Texture2D> texture,
        ComPtr<ID3D11ShaderResourceView> srv,
        ComPtr<ID3D11SamplerState> sampler,
        int width, int height, TextureFormat format, TextureColorSpace colorSpace,
        Silk.NET.DXGI.Format dxgiFormat,
        Silk.NET.DXGI.Format? rtvFormat = null)
    {
        _texture = texture;
        _srv = srv;
        _sampler = sampler;
        Width = width;
        Height = height;
        Format = format;
        ColorSpace = colorSpace;
        DxgiFormat = dxgiFormat;
        RtvFormat = rtvFormat ?? dxgiFormat;
    }

    internal static D3D11Texture Create(ComPtr<ID3D11Device> device, in TextureUploadDesc desc, bool deferred = false)
    {
        TextureFormat format = desc.Format;
        TextureColorSpace resolved = TextureFormatInfo.Resolve(format, desc.ColorSpace);
        TextureFilter filter = desc.Filter;
        int width = desc.Width;
        int height = desc.Height;

        Silk.NET.DXGI.Format dxgiFormat = UploadDxgiFormat(format, resolved);

        // No 24-bit RGB format in DXGI, so Rgb8 is expanded to RGBA8.
        ReadOnlySpan<byte> payload = desc.Payload;
        ReadOnlySpan<TextureMipDesc> mips = desc.Mips;
        byte[]? expanded = null;
        if (format == TextureFormat.Rgb8 && !deferred)
        {
            expanded = TextureUploadLayout.ExpandRgbToRgba(payload, mips, out TextureMipDesc[] expandedMips);
            payload = expanded;
            mips = expandedMips;
        }

        // GenerateMips needs the RenderTarget bind flag, which BC formats
        // cannot have. A supplied chain is used as is.
        bool wantsMipmaps = filter == TextureFilter.LinearMipmap;
        bool generateMips = wantsMipmaps
            && !desc.HasSuppliedMipChain
            && !TextureFormatInfo.IsBlockCompressed(format);

        uint mipLevels = generateMips ? 0u : (uint)mips.Length; // 0 = full chain
        uint bindFlags = (uint)BindFlag.ShaderResource;
        uint miscFlags = 0u;
        if (generateMips)
        {
            // sRGB is fine: GenerateMips filters through the view, so it averages linear light.
            bindFlags |= (uint)BindFlag.RenderTarget;
            miscFlags |= (uint)ResourceMiscFlag.GenerateMips;
        }

        var textureDesc = new Texture2DDesc
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = mipLevels,
            ArraySize = 1,
            Format = dxgiFormat,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = bindFlags,
            CPUAccessFlags = 0,
            MiscFlags = miscFlags,
        };

        var dev = (ID3D11Device*)device.Handle;
        ID3D11Texture2D* texPtr = null;
        if (generateMips || deferred)
        {
            // No initial data: mip 0 is uploaded afterwards.
            SilkMarshal.ThrowHResult(dev->CreateTexture2D(&textureDesc, null, &texPtr));
        }
        else
        {
            // D3D11 takes a source pitch, so levels point straight into the payload.
            Span<SubresourceData> initial = mips.Length <= 16
                ? stackalloc SubresourceData[mips.Length]
                : new SubresourceData[mips.Length];

            fixed (byte* p = payload)
            {
                for (int level = 0; level < mips.Length; level++)
                {
                    initial[level] = new SubresourceData
                    {
                        PSysMem = p + mips[level].Offset,
                        SysMemPitch = (uint)mips[level].RowPitch,
                        SysMemSlicePitch = 0,
                    };
                }

                fixed (SubresourceData* init = initial)
                {
                    SilkMarshal.ThrowHResult(dev->CreateTexture2D(&textureDesc, init, &texPtr));
                }
            }
        }

        var srvDesc = new ShaderResourceViewDesc
        {
            Format = dxgiFormat,
            ViewDimension = Silk.NET.Core.Native.D3DSrvDimension.D3DSrvDimensionTexture2D,
        };
        srvDesc.Anonymous.Texture2D = new Tex2DSrv
        {
            MostDetailedMip = 0,
            MipLevels = unchecked((uint)-1),
        };

        ID3D11ShaderResourceView* srvPtr = null;
        SilkMarshal.ThrowHResult(dev->CreateShaderResourceView((ID3D11Resource*)texPtr, &srvDesc, &srvPtr));

        if (generateMips && !deferred)
        {
            ID3D11DeviceContext* ctxPtr = null;
            dev->GetImmediateContext(&ctxPtr);
            fixed (byte* p = payload)
            {
                ctxPtr->UpdateSubresource(
                    (ID3D11Resource*)texPtr, 0, null, p + mips[0].Offset, (uint)mips[0].RowPitch, 0u);
            }
            ctxPtr->GenerateMips(srvPtr);
            ctxPtr->Release();
        }

        ID3D11SamplerState* samplerPtr = CreateSampler(dev, filter, desc.Wrap);

        GC.KeepAlive(expanded);

        return new D3D11Texture(
            ComOwnership.Own(texPtr),
            ComOwnership.Own(srvPtr),
            ComOwnership.Own(samplerPtr),
            width, height, format, resolved, dxgiFormat);
    }

    // Rgb8 maps to RGBA (the payload is expanded). R8, BC4, BC5 and BC6H have
    // no sRGB form in DXGI; TextureFormatInfo.Resolve already forced linear.
    private static Silk.NET.DXGI.Format UploadDxgiFormat(
        TextureFormat format, TextureColorSpace resolved)
    {
        bool srgb = resolved == TextureColorSpace.Srgb;
        return format switch
        {
            TextureFormat.Rgba8 or TextureFormat.Rgb8 => srgb
                ? Silk.NET.DXGI.Format.FormatR8G8B8A8UnormSrgb
                : Silk.NET.DXGI.Format.FormatR8G8B8A8Unorm,
            TextureFormat.R8 => Silk.NET.DXGI.Format.FormatR8Unorm,
            TextureFormat.Bc1 => srgb
                ? Silk.NET.DXGI.Format.FormatBC1UnormSrgb
                : Silk.NET.DXGI.Format.FormatBC1Unorm,
            TextureFormat.Bc3 => srgb
                ? Silk.NET.DXGI.Format.FormatBC3UnormSrgb
                : Silk.NET.DXGI.Format.FormatBC3Unorm,
            TextureFormat.Bc4 => Silk.NET.DXGI.Format.FormatBC4Unorm,
            TextureFormat.Bc5 => Silk.NET.DXGI.Format.FormatBC5Unorm,
            TextureFormat.Bc6H => Silk.NET.DXGI.Format.FormatBC6HUF16,
            TextureFormat.Bc7 => srgb
                ? Silk.NET.DXGI.Format.FormatBC7UnormSrgb
                : Silk.NET.DXGI.Format.FormatBC7Unorm,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    // The colour attachment of a render target.
    //
    // A shared one is a UNORM resource with an _SRGB RTV. The RTV encodes on
    // write; an sRGB-typed resource would make the importer decode a second
    // time and wash the picture out. The SRV stays UNORM because D3D11 refuses
    // an _SRGB SRV over a _UNORM resource (E_INVALIDARG), so sampling a shared
    // target inside the engine reads encoded values.
    //
    // SHARED_NTHANDLE is only legal together with SHARED or SHARED_KEYEDMUTEX.
    internal static D3D11Texture CreateRenderTargetTexture(
        ComPtr<ID3D11Device> device,
        int width,
        int height,
        TextureFormat format,
        TextureColorSpace colorSpace,
        TextureFilter filter,
        TextureWrap wrap,
        RenderTargetSharing sharing = RenderTargetSharing.None)
    {
        TextureColorSpace resolved = TextureFormatInfo.Resolve(format, colorSpace);
        Silk.NET.DXGI.Format viewFormat = RenderTargetDxgiFormat(format, resolved);

        bool shared = sharing != RenderTargetSharing.None;
        Silk.NET.DXGI.Format resourceFormat = shared
            ? RenderTargetDxgiFormat(format, TextureColorSpace.Linear)
            : viewFormat;

        uint misc = shared
            ? (uint)(ResourceMiscFlag.SharedKeyedmutex | ResourceMiscFlag.SharedNthandle)
            : 0u;

        var dev = (ID3D11Device*)device.Handle;
        ID3D11Texture2D* texPtr = CreateRenderTargetResource(dev, width, height, resourceFormat, misc);
        ID3D11ShaderResourceView* srvPtr = CreateSrv(dev, texPtr, resourceFormat, mipLevels: 1);
        ID3D11SamplerState* samplerPtr = CreateSampler(dev, filter, wrap);

        var texture = new D3D11Texture(
            ComOwnership.Own(texPtr),
            ComOwnership.Own(srvPtr),
            ComOwnership.Own(samplerPtr),
            width, height, format, resolved, resourceFormat, viewFormat);

        if (shared)
        {
            try
            {
                texture.AcquireSharing();
            }
            catch
            {
                texture.Dispose();
                throw;
            }
        }

        return texture;
    }

    private void AcquireSharing()
    {
        IDXGIResource1* resourcePtr = null;
        Guid resourceGuid = IDXGIResource1.Guid;
        SilkMarshal.ThrowHResult(((ID3D11Texture2D*)_texture.Handle)->QueryInterface(
            &resourceGuid, (void**)&resourcePtr));
        ComPtr<IDXGIResource1> resource = ComOwnership.Own(resourcePtr);
        try
        {
            void* handle = null;
            SilkMarshal.ThrowHResult(((IDXGIResource1*)resource.Handle)->CreateSharedHandle(
                (SecurityAttributes*)null, SharedResourceRead | SharedResourceWrite, (char*)null, &handle));
            _sharedHandle = (nint)handle;
        }
        finally
        {
            ComOwnership.Release(ref resource);
        }

        IDXGIKeyedMutex* mutexPtr = null;
        Guid mutexGuid = IDXGIKeyedMutex.Guid;
        SilkMarshal.ThrowHResult(((ID3D11Texture2D*)_texture.Handle)->QueryInterface(
            &mutexGuid, (void**)&mutexPtr));
        _keyedMutex = ComOwnership.Own(mutexPtr);
    }

    // A depth texture that can also be sampled. D3D refuses an SRV over a
    // D32_FLOAT resource, so the resource is R32_TYPELESS with a D32_FLOAT DSV
    // and an R32_FLOAT SRV.
    internal static D3D11Texture CreateDepthTexture(
        ComPtr<ID3D11Device> device, int width, int height)
    {
        var dev = (ID3D11Device*)device.Handle;

        var desc = new Texture2DDesc
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Silk.NET.DXGI.Format.FormatR32Typeless,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)(BindFlag.DepthStencil | BindFlag.ShaderResource),
        };

        ID3D11Texture2D* texPtr = null;
        SilkMarshal.ThrowHResult(dev->CreateTexture2D(&desc, null, &texPtr));

        ID3D11ShaderResourceView* srvPtr = CreateSrv(
            dev, texPtr, Silk.NET.DXGI.Format.FormatR32Float, mipLevels: 1);
        // Nearest: interpolated depth lies on neither surface.
        ID3D11SamplerState* samplerPtr = CreateSampler(dev, TextureFilter.Nearest, TextureWrap.Clamp);

        return new D3D11Texture(
            ComOwnership.Own(texPtr),
            ComOwnership.Own(srvPtr),
            ComOwnership.Own(samplerPtr),
            width, height, TextureFormat.Depth32Float, TextureColorSpace.Linear,
            Silk.NET.DXGI.Format.FormatR32Typeless);
    }

    // Resizes in place; the wrapper object stays the same.
    internal void ReplaceDepthStorage(ComPtr<ID3D11Device> device, int width, int height)
    {
        D3D11Texture replacement = CreateDepthTexture(device, width, height);

        ComOwnership.Release(ref _srv);
        ComOwnership.Release(ref _texture);

        _texture = replacement._texture;
        _srv = replacement._srv;
        replacement._texture = default;
        replacement._srv = default;
        replacement.Dispose();

        Width = width;
        Height = height;
    }

    // Resizes in place; the wrapper object stays the same, so materials
    // sampling it stay valid.
    internal void ReplaceStorage(ComPtr<ID3D11Device> device, int width, int height)
    {
        // The consumer imported the old NT handle, which cannot be swapped.
        if (IsShared)
        {
            throw new InvalidOperationException(
                "A shared render target cannot be resized in place: the consumer imported its NT handle, and a " +
                "handle cannot be swapped inside the wrapper the way a plain GPU resource can. Recreate the " +
                "target under a new generation and retire the old one.");
        }

        var dev = (ID3D11Device*)device.Handle;
        ID3D11Texture2D* texPtr = CreateRenderTargetResource(dev, width, height, DxgiFormat);
        ID3D11ShaderResourceView* srvPtr = CreateSrv(dev, texPtr, DxgiFormat, mipLevels: 1);

        ComOwnership.Release(ref _srv);
        ComOwnership.Release(ref _texture);

        _texture = ComOwnership.Own(texPtr);
        _srv = ComOwnership.Own(srvPtr);
        Width = width;
        Height = height;
    }

    private static Silk.NET.DXGI.Format RenderTargetDxgiFormat(
        TextureFormat format, TextureColorSpace resolved) => format switch
    {
        TextureFormat.Rgba8 => resolved == TextureColorSpace.Srgb
            ? Silk.NET.DXGI.Format.FormatR8G8B8A8UnormSrgb
            : Silk.NET.DXGI.Format.FormatR8G8B8A8Unorm,
        TextureFormat.Rgba16Float => Silk.NET.DXGI.Format.FormatR16G16B16A16Float,
        _ => throw new ArgumentOutOfRangeException(
            nameof(format), $"{format} is not a render-target format."),
    };

    private static ID3D11Texture2D* CreateRenderTargetResource(
        ID3D11Device* dev, int width, int height, Silk.NET.DXGI.Format dxgiFormat, uint miscFlags = 0)
    {
        var desc = new Texture2DDesc
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = dxgiFormat,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)(BindFlag.ShaderResource | BindFlag.RenderTarget),
            MiscFlags = miscFlags,
        };

        ID3D11Texture2D* texPtr = null;
        SilkMarshal.ThrowHResult(dev->CreateTexture2D(&desc, null, &texPtr));
        return texPtr;
    }

    private static ID3D11ShaderResourceView* CreateSrv(
        ID3D11Device* dev, ID3D11Texture2D* texture, Silk.NET.DXGI.Format dxgiFormat, uint mipLevels)
    {
        var srvDesc = new ShaderResourceViewDesc
        {
            Format = dxgiFormat,
            ViewDimension = Silk.NET.Core.Native.D3DSrvDimension.D3DSrvDimensionTexture2D,
        };
        srvDesc.Anonymous.Texture2D = new Tex2DSrv { MostDetailedMip = 0, MipLevels = mipLevels };

        ID3D11ShaderResourceView* srvPtr = null;
        SilkMarshal.ThrowHResult(dev->CreateShaderResourceView((ID3D11Resource*)texture, &srvDesc, &srvPtr));
        return srvPtr;
    }

    private static ID3D11SamplerState* CreateSampler(
        ID3D11Device* dev, TextureFilter filter, TextureWrap wrap)
    {
        Silk.NET.Direct3D11.Filter samplerFilter = filter switch
        {
            TextureFilter.Nearest => Silk.NET.Direct3D11.Filter.MinMagMipPoint,
            _ => Silk.NET.Direct3D11.Filter.MinMagMipLinear,
        };
        var addrMode = wrap == TextureWrap.Repeat ? TextureAddressMode.Wrap : TextureAddressMode.Clamp;

        var samplerDesc = new SamplerDesc
        {
            Filter = samplerFilter,
            AddressU = addrMode,
            AddressV = addrMode,
            AddressW = addrMode,
            MipLODBias = 0f,
            MaxAnisotropy = 1,
            ComparisonFunc = ComparisonFunc.Always,
            MinLOD = 0f,
            MaxLOD = float.MaxValue,
        };

        ID3D11SamplerState* samplerPtr = null;
        SilkMarshal.ThrowHResult(dev->CreateSamplerState(&samplerDesc, &samplerPtr));
        return samplerPtr;
    }

    internal override void WriteUploadRows(int level, TextureMipDesc mip, int firstRow, int rowCount, ReadOnlySpan<byte> bytes)
    {
        ID3D11Device* device = null;
        Resource->GetDevice(&device);
        ID3D11DeviceContext* context = null;
        device->GetImmediateContext(&context);
        device->Release();
        byte[]? expanded = null;
        try
        {
            int pitch = mip.RowPitch;
            if (Format == TextureFormat.Rgb8)
            {
                pitch = checked(mip.Width * 4);
                expanded = ArrayPool<byte>.Shared.Rent(pitch * rowCount);
                TextureUploadLayout.CopyRows(bytes, mip.RowPitch, expanded, pitch, mip.Width, rowCount, Format);
                bytes = expanded.AsSpan(0, pitch * rowCount);
            }
            (uint top, uint right, uint bottom) = TextureUploadLayout.RowBox(Format, in mip, firstRow, rowCount);
            var box = new Box(0, top, 0, right, bottom, 1);
            fixed (byte* source = bytes)
                context->UpdateSubresource(Resource, (uint)level, &box, source, (uint)pitch, 0);
        }
        finally
        {
            if (expanded is not null) ArrayPool<byte>.Shared.Return(expanded);
            context->Release();
        }
    }

    internal override int UploadRowPitch(TextureMipDesc mip) => Format == TextureFormat.Rgb8 ? checked(mip.Width * 4) : base.UploadRowPitch(mip);

    internal override void FinishUpload(bool generateMips)
    {
        if (!generateMips) return;
        ID3D11Device* device = null;
        Resource->GetDevice(&device);
        ID3D11DeviceContext* context = null;
        device->GetImmediateContext(&context);
        device->Release();
        context->GenerateMips((ID3D11ShaderResourceView*)_srv.Handle);
        context->Release();
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ComOwnership.Release(ref _keyedMutex);
        ComOwnership.Release(ref _sampler);
        ComOwnership.Release(ref _srv);
        ComOwnership.Release(ref _texture);

        // The NT handle holds its own reference on the resource, so it must be
        // closed too. Zeroed first: a closed handle value can be reused, and a
        // second close would hit somebody else's.
        if (_sharedHandle != 0)
        {
            nint handle = _sharedHandle;
            _sharedHandle = 0;
            _ = Kernel32.CloseHandle(handle);
        }
    }

    // DXGI_SHARED_RESOURCE_READ
    private const uint SharedResourceRead = 0x80000000u;

    // DXGI_SHARED_RESOURCE_WRITE
    private const uint SharedResourceWrite = 0x00000001u;

    // Silk.NET does not bind CloseHandle.
    private static partial class Kernel32
    {
        // Raw BOOL as int: Silk.NET.Core.Native also defines UnmanagedType,
        // which makes the marshalling attribute ambiguous here.
        [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
        internal static partial int CloseHandle(nint handle);
    }
}
