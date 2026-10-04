using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using System;
// Aliased: inside a Texture subclass the bare name is the inherited property.
using ColorMath = SpectraEngine.Core.Graphics.ColorSpace;

namespace SpectraEngine.Core.Graphics.D3D12;

// SRV and sampler live in small CPU-only heaps and are copied into the
// renderer's shader-visible rings at draw time.
internal sealed unsafe class D3D12Texture : Texture
{
    private ComPtr<ID3D12Resource> _texture;
    private ComPtr<ID3D12DescriptorHeap> _srvHeap;      // 1 slot, non-shader-visible
    private ComPtr<ID3D12DescriptorHeap> _samplerHeap;  // 1 slot, non-shader-visible
    private bool _disposed;
    private readonly D3D12Renderer _renderer;

    internal CpuDescriptorHandle SrvCpu { get; private set; }
    internal CpuDescriptorHandle SamplerCpu { get; private set; }

    internal ID3D12Resource* Resource => (ID3D12Resource*)_texture.Handle;

    internal Silk.NET.DXGI.Format DxgiFormat { get; private set; }

    // Only set for render-target textures, whose storage can be replaced.
    private TextureFilter _filter;
    private TextureWrap _wrap;

    private D3D12Texture(D3D12Renderer renderer, int width, int height,
        TextureFormat format, TextureColorSpace colorSpace, Silk.NET.DXGI.Format dxgiFormat,
        TextureFilter filter, TextureWrap wrap)
    {
        _renderer = renderer;
        Width = width;
        Height = height;
        Format = format;
        ColorSpace = colorSpace;
        DxgiFormat = dxgiFormat;
        _filter = filter;
        _wrap = wrap;

        _srvHeap = renderer.CreateDescriptorHeap(DescriptorHeapType.CbvSrvUav, 1, shaderVisible: false);
        _samplerHeap = renderer.CreateDescriptorHeap(DescriptorHeapType.Sampler, 1, shaderVisible: false);
        SrvCpu = ((ID3D12DescriptorHeap*)_srvHeap.Handle)->GetCPUDescriptorHandleForHeapStart();
        SamplerCpu = ((ID3D12DescriptorHeap*)_samplerHeap.Handle)->GetCPUDescriptorHandleForHeapStart();

        CreateSampler(renderer, filter, wrap);
    }

    // Starts in PixelShaderResource, which D3D12RenderTarget's state tracking assumes.
    internal static D3D12Texture CreateRenderTargetTexture(
        D3D12Renderer renderer, int width, int height, TextureFormat format,
        TextureColorSpace colorSpace, TextureFilter filter, TextureWrap wrap)
    {
        TextureColorSpace resolved = TextureFormatInfo.Resolve(format, colorSpace);
        Silk.NET.DXGI.Format dxgiFormat = RenderTargetDxgiFormat(format, resolved);

        var texture = new D3D12Texture(renderer, width, height, format, resolved, dxgiFormat, filter, wrap);
        texture.AllocateRenderTargetStorage(renderer, width, height);
        return texture;
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

    // D3D refuses an SRV over a D32_FLOAT resource, so the resource is
    // R32_TYPELESS with a D32_FLOAT DSV and an R32_FLOAT SRV.
    internal static D3D12Texture CreateDepthTexture(D3D12Renderer renderer, int width, int height)
    {
        var texture = new D3D12Texture(
            renderer, width, height, TextureFormat.Depth32Float, TextureColorSpace.Linear,
            Silk.NET.DXGI.Format.FormatR32Typeless, TextureFilter.Nearest, TextureWrap.Clamp);
        texture.AllocateDepthStorage(renderer, width, height);
        return texture;
    }

    internal void ReplaceDepthStorage(D3D12Renderer renderer, int width, int height)
    {
        _renderer.Retire(ref _texture);
        AllocateDepthStorage(renderer, width, height);
        Width = width;
        Height = height;
    }

    private void AllocateDepthStorage(D3D12Renderer renderer, int width, int height)
    {
        _texture = renderer.CreateDepthResource((uint)width, (uint)height);

        var srvDesc = new ShaderResourceViewDesc
        {
            // A typeless SRV is rejected.
            Format = Silk.NET.DXGI.Format.FormatR32Float,
            ViewDimension = SrvDimension.Texture2D,
            Shader4ComponentMapping = D3D12Renderer.DefaultComponentMapping,
        };
        srvDesc.Anonymous.Texture2D = new Tex2DSrv
        {
            MostDetailedMip = 0,
            MipLevels = 1,
            PlaneSlice = 0,
            ResourceMinLODClamp = 0f,
        };
        renderer.DevicePtr->CreateShaderResourceView(Resource, &srvDesc, SrvCpu);
    }

    // Same wrapper and SRV slot, so materials survive a render-target resize.
    internal void ReplaceStorage(D3D12Renderer renderer, int width, int height)
    {
        _renderer.Retire(ref _texture);
        AllocateRenderTargetStorage(renderer, width, height);
        Width = width;
        Height = height;
    }

    private void AllocateRenderTargetStorage(D3D12Renderer renderer, int width, int height)
    {
        _texture = renderer.CreateRenderTargetResource((uint)width, (uint)height, DxgiFormat);

        var srvDesc = new ShaderResourceViewDesc
        {
            Format = DxgiFormat,
            ViewDimension = SrvDimension.Texture2D,
            Shader4ComponentMapping = D3D12Renderer.DefaultComponentMapping,
        };
        srvDesc.Anonymous.Texture2D = new Tex2DSrv
        {
            MostDetailedMip = 0,
            MipLevels = 1,
            PlaneSlice = 0,
            ResourceMinLODClamp = 0f,
        };
        renderer.DevicePtr->CreateShaderResourceView(Resource, &srvDesc, SrvCpu);
    }

    private void CreateSampler(D3D12Renderer renderer, TextureFilter filter, TextureWrap wrap)
    {
        Filter samplerFilter = filter == TextureFilter.Nearest ? Filter.MinMagMipPoint : Filter.MinMagMipLinear;
        var addr = wrap == TextureWrap.Repeat ? TextureAddressMode.Wrap : TextureAddressMode.Clamp;
        var samplerDesc = new SamplerDesc
        {
            Filter = samplerFilter,
            AddressU = addr,
            AddressV = addr,
            AddressW = addr,
            MipLODBias = 0f,
            MaxAnisotropy = 1,
            ComparisonFunc = ComparisonFunc.None,
            MinLOD = 0f,
            MaxLOD = float.MaxValue,
        };
        renderer.DevicePtr->CreateSampler(&samplerDesc, SamplerCpu);
    }

    internal D3D12Texture(D3D12Renderer renderer, in TextureUploadDesc desc, bool deferred = false)
    {
        _renderer = renderer;
        TextureFormat format = desc.Format;
        TextureColorSpace resolved = TextureFormatInfo.Resolve(format, desc.ColorSpace);
        bool srgb = resolved == TextureColorSpace.Srgb;
        TextureFilter filter = desc.Filter;

        Width = desc.Width;
        Height = desc.Height;
        Format = format;
        ColorSpace = resolved;

        Silk.NET.DXGI.Format dxgiFormat = UploadDxgiFormat(format, resolved);

        // No 24-bit DXGI format, so Rgb8 is expanded to RGBA8. Everything below
        // must use uploadFormat, or the row pitch is wrong and the picture shears.
        TextureFormat uploadFormat = format == TextureFormat.Rgb8 ? TextureFormat.Rgba8 : format;
        ReadOnlySpan<byte> payload = desc.Payload;
        ReadOnlySpan<TextureMipDesc> mips = desc.Mips;
        byte[]? owned = null;
        if (format == TextureFormat.Rgb8 && !deferred)
        {
            owned = TextureUploadLayout.ExpandRgbToRgba(payload, mips, out TextureMipDesc[] expandedMips);
            payload = owned;
            mips = expandedMips;
        }

        // D3D12 has no GenerateMips, so the chain is built on the CPU. Not for a
        // supplied chain, and not for BC: the engine has no block encoder.
        bool wantsMips = filter == TextureFilter.LinearMipmap;
        if (!deferred && wantsMips && !desc.HasSuppliedMipChain && !TextureFormatInfo.IsBlockCompressed(uploadFormat))
        {
            var levels = BuildMipChain(
                TextureUploadLayout.TightLevel(payload, uploadFormat, mips[0], out _).ToArray(),
                mips[0].Width, mips[0].Height, TextureFormatInfo.BytesPerBlock(uploadFormat), srgb);
            owned = TextureUploadLayout.Flatten(uploadFormat, levels, out TextureMipDesc[] generatedMips);
            payload = owned;
            mips = generatedMips;
        }

        DxgiFormat = dxgiFormat;
        _texture = renderer.CreateTexture2D((uint)Width, (uint)Height, (ushort)mips.Length, dxgiFormat);
        if (!deferred) renderer.UploadTexture(_texture, payload, mips);

        GC.KeepAlive(owned);

        _srvHeap = renderer.CreateDescriptorHeap(DescriptorHeapType.CbvSrvUav, 1, shaderVisible: false);
        _samplerHeap = renderer.CreateDescriptorHeap(DescriptorHeapType.Sampler, 1, shaderVisible: false);
        SrvCpu = ((ID3D12DescriptorHeap*)_srvHeap.Handle)->GetCPUDescriptorHandleForHeapStart();
        SamplerCpu = ((ID3D12DescriptorHeap*)_samplerHeap.Handle)->GetCPUDescriptorHandleForHeapStart();

        var srvDesc = new ShaderResourceViewDesc
        {
            Format = dxgiFormat,
            ViewDimension = SrvDimension.Texture2D,
            Shader4ComponentMapping = D3D12Renderer.DefaultComponentMapping,
        };
        srvDesc.Anonymous.Texture2D = new Tex2DSrv
        {
            MostDetailedMip = 0,
            MipLevels = (uint)mips.Length,
            PlaneSlice = 0,
            ResourceMinLODClamp = 0f,
        };
        renderer.DevicePtr->CreateShaderResourceView((ID3D12Resource*)_texture.Handle, &srvDesc, SrvCpu);

        Filter samplerFilter = filter switch
        {
            TextureFilter.Nearest => Filter.MinMagMipPoint,
            TextureFilter.Linear => Filter.MinMagMipLinear,
            TextureFilter.LinearMipmap => Filter.MinMagMipLinear,
            _ => Filter.MinMagMipLinear,
        };
        var addr = desc.Wrap == TextureWrap.Repeat ? TextureAddressMode.Wrap : TextureAddressMode.Clamp;
        var samplerDesc = new SamplerDesc
        {
            Filter = samplerFilter,
            AddressU = addr,
            AddressV = addr,
            AddressW = addr,
            MipLODBias = 0f,
            MaxAnisotropy = 1,
            // None, not Always: the debug layer warns about a func on a non-comparison filter.
            ComparisonFunc = ComparisonFunc.None,
            MinLOD = 0f,
            MaxLOD = float.MaxValue,
        };
        renderer.DevicePtr->CreateSampler(&samplerDesc, SamplerCpu);
    }

    // Must agree with D3D11's table.
    private static Silk.NET.DXGI.Format UploadDxgiFormat(
        TextureFormat format, TextureColorSpace resolved)
    {
        bool srgb = resolved == TextureColorSpace.Srgb;
        return format switch
        {
            TextureFormat.Rgba8 or TextureFormat.Rgb8 => srgb
                ? Silk.NET.DXGI.Format.FormatR8G8B8A8UnormSrgb
                : Silk.NET.DXGI.Format.FormatR8G8B8A8Unorm,
            // No R8_UNORM_SRGB exists; Resolve already forced linear.
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

    // Box-filtered chain down to 1x1. An sRGB chain averages in linear light,
    // as the hardware does on D3D11 and GL; averaging the bytes darkens each level.
    internal static List<(byte[] Pixels, int Width, int Height)> BuildMipChain(
        byte[] level0, int width, int height, int bpp, bool srgb)
    {
        var mips = new List<(byte[], int, int)> { (level0, width, height) };

        float[]? toLinear = srgb ? SrgbDecodeTable : null;

        byte[] prev = level0;
        uint w = (uint)width, h = (uint)height;
        while (w > 1 || h > 1)
        {
            uint nw = Math.Max(1, w / 2);
            uint nh = Math.Max(1, h / 2);
            var next = new byte[nw * nh * bpp];

            for (uint y = 0; y < nh; y++)
            {
                // Clamp the source window so odd sizes stay in bounds.
                uint sy0 = Math.Min(y * 2, h - 1);
                uint sy1 = Math.Min(y * 2 + 1, h - 1);
                for (uint x = 0; x < nw; x++)
                {
                    uint sx0 = Math.Min(x * 2, w - 1);
                    uint sx1 = Math.Min(x * 2 + 1, w - 1);
                    for (int c = 0; c < bpp; c++)
                    {
                        byte t0 = prev[(sy0 * w + sx0) * bpp + c];
                        byte t1 = prev[(sy0 * w + sx1) * bpp + c];
                        byte t2 = prev[(sy1 * w + sx0) * bpp + c];
                        byte t3 = prev[(sy1 * w + sx1) * bpp + c];

                        // Alpha (c == 3) is linear even in an sRGB format.
                        next[(y * nw + x) * bpp + c] = toLinear is null || c == 3
                            ? (byte)((t0 + t1 + t2 + t3) / 4)
                            : EncodeSrgb(
                                (toLinear[t0] + toLinear[t1] + toLinear[t2] + toLinear[t3]) * 0.25f);
                    }
                }
            }

            mips.Add((next, (int)nw, (int)nh));
            prev = next;
            w = nw;
            h = nh;
        }
        return mips;
    }

    private static readonly float[] SrgbDecodeTable = BuildSrgbDecodeTable();

    private static float[] BuildSrgbDecodeTable()
    {
        var table = new float[256];
        for (int i = 0; i < table.Length; i++)
            table[i] = ColorMath.SrgbToLinear(i / 255f);
        return table;
    }

    private static byte EncodeSrgb(float linear)
    {
        float encoded = ColorMath.LinearToSrgb(linear);
        // Round to nearest: truncating darkens every level by about half a code.
        return (byte)Math.Clamp((int)(encoded * 255f + 0.5f), 0, 255);
    }

    internal override void WriteUploadRows(int level, TextureMipDesc mip, int firstRow, int rowCount, ReadOnlySpan<byte> bytes) =>
        _renderer.UploadTextureRows(this, level, mip, firstRow, rowCount, bytes);

    internal override int UploadRowPitch(TextureMipDesc mip)
    {
        long pitch = Format == TextureFormat.Rgb8 ? (long)mip.Width * 4 : base.UploadRowPitch(mip);
        return checked((int)((pitch + 255) / 256 * 256));
    }

    internal override void FinishUpload(bool generateMips) => _renderer.FinishTextureUpload(this);

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _renderer.Retire(ref _samplerHeap);
        _renderer.Retire(ref _srvHeap);
        _renderer.Retire(ref _texture);
    }
}
