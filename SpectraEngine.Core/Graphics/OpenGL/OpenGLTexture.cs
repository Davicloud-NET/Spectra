using Silk.NET.OpenGL;
using System;

namespace SpectraEngine.Core.Graphics.OpenGL;

internal sealed class OpenGLTexture : Texture
{
    private readonly GL _gl;
    private readonly TextureFilter _filter;

    // GL texture name. Survives ReallocateStorage.
    public uint Handle { get; }

    private bool _disposed;

    private OpenGLTexture(
        GL gl, uint handle, int width, int height, TextureFormat format,
        TextureColorSpace colorSpace, TextureFilter filter)
    {
        _gl = gl;
        Handle = handle;
        Width = width;
        Height = height;
        Format = format;
        ColorSpace = colorSpace;
        _filter = filter;
    }

    internal static unsafe OpenGLTexture Create(GL gl, in TextureUploadDesc desc, bool deferred = false)
    {
        TextureFormat format = desc.Format;
        TextureColorSpace resolved = TextureFormatInfo.Resolve(format, desc.ColorSpace);
        TextureFilter filter = desc.Filter;
        int width = desc.Width;
        int height = desc.Height;

        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, handle);

        (InternalFormat internalFormat, PixelFormat pixelFormat, PixelType pixelType) = GlFormats(format, resolved);
        bool compressed = TextureFormatInfo.IsBlockCompressed(format);

        // Rows are tightly packed; GL's default 4-byte unpack alignment skews
        // R8 and RGB8 at widths whose stride is not a multiple of 4.
        if (!compressed && pixelFormat != PixelFormat.Rgba)
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

        if (!deferred) UploadLevels(gl, desc, internalFormat, pixelFormat, pixelType, compressed);
        else
            for (int level = 0; level < desc.MipCount; level++)
            {
                var mip = desc.Mips[level];
                if (compressed)
                    gl.CompressedTexImage2D(TextureTarget.Texture2D, level, internalFormat,
                        (uint)mip.Width, (uint)mip.Height, 0, (uint)TextureUploadLayout.TightLevelSize(format, mip), null);
                else gl.TexImage2D(TextureTarget.Texture2D, level, internalFormat,
                    (uint)mip.Width, (uint)mip.Height, 0, pixelFormat, pixelType, null);
            }

        // Keep a supplied chain as cooked. GenerateMipmap can't re-encode blocks.
        bool wantsMipmaps = filter == TextureFilter.LinearMipmap;
        bool generate = wantsMipmaps && !desc.HasSuppliedMipChain && !compressed;
        if (generate && !deferred)
            gl.GenerateMipmap(TextureTarget.Texture2D);

        // A chain that stops short of 1x1 must set the max level, or the
        // texture is incomplete and samples black with no GL error.
        if (desc.HasSuppliedMipChain)
        {
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 0);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, desc.MipCount - 1);
        }

        bool hasChain = generate || desc.HasSuppliedMipChain;
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
            (int)MinFilter(filter, hasChain));
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
            (int)MagFilter(filter));

        int wrapMode = desc.Wrap == TextureWrap.Repeat ? (int)GLEnum.Repeat : (int)GLEnum.ClampToEdge;
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrapMode);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrapMode);

        gl.BindTexture(TextureTarget.Texture2D, 0);
        return new OpenGLTexture(gl, handle, width, height, format, resolved, filter);
    }

    private static unsafe void UploadLevels(
        GL gl,
        in TextureUploadDesc desc,
        InternalFormat internalFormat,
        PixelFormat pixelFormat,
        PixelType pixelType,
        bool compressed)
    {
        ReadOnlySpan<byte> payload = desc.Payload;
        TextureFormat format = desc.Format;

        for (int level = 0; level < desc.MipCount; level++)
        {
            TextureMipDesc mip = desc.Mips[level];
            ReadOnlySpan<byte> bytes = TextureUploadLayout.TightLevel(payload, format, mip, out byte[]? repacked);

            fixed (byte* p = bytes)
            {
                if (compressed)
                {
                    gl.CompressedTexImage2D(
                        TextureTarget.Texture2D, level, internalFormat,
                        (uint)mip.Width, (uint)mip.Height, 0, (uint)bytes.Length, p);
                }
                else
                {
                    gl.TexImage2D(
                        TextureTarget.Texture2D, level, internalFormat,
                        (uint)mip.Width, (uint)mip.Height, 0, pixelFormat, pixelType, p);
                }
            }

            // Keeps the repacked buffer alive across the fixed block.
            GC.KeepAlive(repacked);
        }
    }

    // Storage with no pixel data, for a render target attachment. No mip chain.
    internal static unsafe OpenGLTexture CreateEmpty(
        GL gl,
        int width,
        int height,
        TextureFormat format,
        TextureColorSpace colorSpace,
        TextureFilter filter,
        TextureWrap wrap)
    {
        TextureColorSpace resolved = TextureFormatInfo.Resolve(format, colorSpace);

        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, handle);

        (InternalFormat internalFormat, PixelFormat pixelFormat, PixelType pixelType) = GlFormats(format, resolved);
        gl.TexImage2D(TextureTarget.Texture2D, 0, internalFormat,
            (uint)width, (uint)height, 0, pixelFormat, pixelType, null);

        // No mip chain, so a mipmap min filter would read black.
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)MagFilter(filter));
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)MagFilter(filter));

        int wrapMode = wrap == TextureWrap.Repeat ? (int)GLEnum.Repeat : (int)GLEnum.ClampToEdge;
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrapMode);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrapMode);

        gl.BindTexture(TextureTarget.Texture2D, 0);
        return new OpenGLTexture(gl, handle, width, height, format, resolved, filter);
    }

    // Resizes in place. Materials hold this wrapper and GL name, so a render
    // target resize must not replace either.
    internal unsafe void ReallocateStorage(int width, int height)
    {
        (InternalFormat internalFormat, PixelFormat pixelFormat, PixelType pixelType) = GlFormats(Format, ColorSpace);

        _gl.BindTexture(TextureTarget.Texture2D, Handle);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, internalFormat,
            (uint)width, (uint)height, 0, pixelFormat, pixelType, null);
        _gl.BindTexture(TextureTarget.Texture2D, 0);

        Width = width;
        Height = height;
    }

    internal void Bind(int unit)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
    }

    internal override unsafe void WriteUploadRows(int level, TextureMipDesc mip, int firstRow, int rowCount, ReadOnlySpan<byte> bytes)
    {
        bool compressed = TextureFormatInfo.IsBlockCompressed(Format);
        int pitch = TextureFormatInfo.TightRowPitch(Format, mip.Width);
        byte[]? scratch = null;
        try
        {
            if (mip.RowPitch != pitch)
            {
                scratch = System.Buffers.ArrayPool<byte>.Shared.Rent(pitch * rowCount);
                for (int row = 0; row < rowCount; row++) bytes.Slice(row * mip.RowPitch, pitch).CopyTo(scratch.AsSpan(row * pitch));
                bytes = scratch.AsSpan(0, pitch * rowCount);
            }
            var formats = GlFormats(Format, ColorSpace);
            _gl.BindTexture(TextureTarget.Texture2D, Handle);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            int blockHeight = compressed ? 4 : 1;
            uint y = (uint)(firstRow * blockHeight);
            uint height = (uint)Math.Min(rowCount * blockHeight, mip.Height - (int)y);
            fixed (byte* source = bytes)
            {
                if (compressed) _gl.CompressedTexSubImage2D(TextureTarget.Texture2D, level, 0, (int)y,
                    (uint)mip.Width, height, formats.Internal, (uint)bytes.Length, source);
                else _gl.TexSubImage2D(TextureTarget.Texture2D, level, 0, (int)y,
                    (uint)mip.Width, height, formats.Pixel, formats.Type, source);
            }
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            _gl.BindTexture(TextureTarget.Texture2D, 0);
        }
        finally { if (scratch is not null) System.Buffers.ArrayPool<byte>.Shared.Return(scratch); }
    }

    internal override void FinishUpload(bool generateMips)
    {
        if (!generateMips) return;
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
        _gl.GenerateMipmap(TextureTarget.Texture2D);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    // Colour space lives in the internal format only. An sRGB one makes the
    // driver decode before filtering, GenerateMipmap included.
    private static (InternalFormat Internal, PixelFormat Pixel, PixelType Type) GlFormats(
        TextureFormat format, TextureColorSpace colorSpace)
    {
        bool srgb = colorSpace == TextureColorSpace.Srgb;
        return format switch
        {
            TextureFormat.Rgba8 =>
                (srgb ? InternalFormat.Srgb8Alpha8 : InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte),
            TextureFormat.Rgb8 =>
                (srgb ? InternalFormat.Srgb8 : InternalFormat.Rgb8, PixelFormat.Rgb, PixelType.UnsignedByte),
            // No sRGB R8 exists; TextureFormatInfo.Resolve forces linear.
            TextureFormat.R8 => (InternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte),
            // Type matters even with a null pointer: some drivers reject
            // UnsignedByte against RGBA16F.
            TextureFormat.Rgba16Float => (InternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float),
            // Compare mode stays GL_NONE, so sampler2D reads the depth in .r.
            TextureFormat.Depth32Float =>
                (InternalFormat.DepthComponent32f, PixelFormat.DepthComponent, PixelType.Float),

            // Compressed uploads ignore pixel format and type.
            // RGBA DXT1, not RGB: DXGI's BC1_UNORM carries the alpha bit.
            TextureFormat.Bc1 => (
                srgb ? InternalFormat.CompressedSrgbAlphaS3TCDxt1Ext : InternalFormat.CompressedRgbaS3TCDxt1Ext,
                PixelFormat.Rgba, PixelType.UnsignedByte),
            TextureFormat.Bc3 => (
                srgb ? InternalFormat.CompressedSrgbAlphaS3TCDxt5Ext : InternalFormat.CompressedRgbaS3TCDxt5Ext,
                PixelFormat.Rgba, PixelType.UnsignedByte),
            // RGTC has no sRGB form.
            TextureFormat.Bc4 => (InternalFormat.CompressedRedRgtc1, PixelFormat.Red, PixelType.UnsignedByte),
            TextureFormat.Bc5 => (InternalFormat.CompressedRGRgtc2, PixelFormat.RG, PixelType.UnsignedByte),
            // Unsigned BPTC float. The signed form would need its own TextureFormat.
            TextureFormat.Bc6H => (
                InternalFormat.CompressedRgbBptcUnsignedFloat, PixelFormat.Rgb, PixelType.HalfFloat),
            TextureFormat.Bc7 => (
                srgb ? InternalFormat.CompressedSrgbAlphaBptcUnorm : InternalFormat.CompressedRgbaBptcUnorm,
                PixelFormat.Rgba, PixelType.UnsignedByte),

            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    private static GLEnum MinFilter(TextureFilter filter, bool mipmaps) => filter switch
    {
        TextureFilter.Nearest => GLEnum.Nearest,
        TextureFilter.Linear => GLEnum.Linear,
        TextureFilter.LinearMipmap => mipmaps ? GLEnum.LinearMipmapLinear : GLEnum.Linear,
        _ => GLEnum.Linear,
    };

    private static GLEnum MagFilter(TextureFilter filter) => filter switch
    {
        TextureFilter.Nearest => GLEnum.Nearest,
        _ => GLEnum.Linear,
    };

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gl.DeleteTexture(Handle);
    }
}
