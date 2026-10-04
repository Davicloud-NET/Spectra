namespace SpectraEngine.Core.Graphics;

/// <summary>Pixel format for <see cref="Texture"/> data uploads.</summary>
public enum TextureFormat
{
    /// <summary>8 bits per channel, four channels (RGBA).</summary>
    Rgba8,

    /// <summary>8 bits per channel, three channels (RGB).</summary>
    Rgb8,

    /// <summary>8 bits, single channel (red / luminance / mask).</summary>
    R8,

    /// <summary>
    /// 16-bit float per channel, four channels, linear. A render-target format;
    /// it cannot be uploaded from the CPU.
    /// </summary>
    Rgba16Float,

    /// <summary>32-bit float depth. A render-target attachment only.</summary>
    // D3D refuses an SRV over a D32_FLOAT resource, so backends create it
    // R32_TYPELESS and put the depth format on the view.
    Depth32Float,

    /// <summary>
    /// BC1 (DXT1): 4x4 blocks, 8 bytes each, RGB plus one bit of alpha.
    /// Like every block-compressed format here, it only comes out of the cook.
    /// </summary>
    Bc1,

    /// <summary>
    /// BC3 (DXT5): 4x4 blocks, 16 bytes each, RGB plus a separately
    /// interpolated 8-bit alpha.
    /// </summary>
    Bc3,

    /// <summary>
    /// BC4 (RGTC1): 4x4 blocks, 8 bytes each, one interpolated channel. For masks
    /// and other single-channel data. No sRGB variant.
    /// </summary>
    Bc4,

    /// <summary>
    /// BC5 (RGTC2): 4x4 blocks, 16 bytes each, two interpolated channels. The
    /// normal-map format. No sRGB variant.
    /// </summary>
    Bc5,

    /// <summary>
    /// BC6H: 4x4 blocks, 16 bytes each, three half-float channels. For HDR
    /// sources. No sRGB variant.
    /// </summary>
    Bc6H,

    /// <summary>BC7: 4x4 blocks, 16 bytes each, RGBA. The default for cooked colour.</summary>
    Bc7,
}

/// <summary>
/// Whether a texture's bytes are sRGB-encoded colour or raw linear values.
/// The sampler does the decode, before filtering.
/// </summary>
public enum TextureColorSpace
{
    /// <summary>Raw values, uploaded and sampled untouched. Normal, roughness, metallic, AO, masks.</summary>
    Linear,

    /// <summary>sRGB-encoded colour, decoded to linear by the sampler. Albedo, emissive.</summary>
    Srgb,
}

/// <summary>Facts about a <see cref="TextureFormat"/> that all three backends must agree on.</summary>
public static class TextureFormatInfo
{
    /// <summary>
    /// Whether an sRGB variant of <paramref name="format"/> exists in hardware.
    /// Neither DXGI nor GL defines a one-channel sRGB format, and float formats are linear.
    /// </summary>
    public static bool SupportsSrgb(TextureFormat format) =>
        format is TextureFormat.Rgba8 or TextureFormat.Rgb8
            or TextureFormat.Bc1 or TextureFormat.Bc3 or TextureFormat.Bc7;

    /// <summary>
    /// Whether <paramref name="format"/> cannot be filled from a byte array.
    /// False for <see cref="TextureFormat.Bc6H"/>: its blocks are bytes.
    /// </summary>
    public static bool IsFloat(TextureFormat format) =>
        format is TextureFormat.Rgba16Float or TextureFormat.Depth32Float;

    /// <summary>Whether <paramref name="format"/> stores 4x4 blocks instead of texels.</summary>
    public static bool IsBlockCompressed(TextureFormat format) => format
        is TextureFormat.Bc1 or TextureFormat.Bc3 or TextureFormat.Bc4
        or TextureFormat.Bc5 or TextureFormat.Bc6H or TextureFormat.Bc7;

    /// <summary>Width in texels of one block: 4 for a block-compressed format, 1 otherwise.</summary>
    public static int BlockWidth(TextureFormat format) => IsBlockCompressed(format) ? 4 : 1;

    /// <summary>Height in texels of one block. See <see cref="BlockWidth"/>.</summary>
    public static int BlockHeight(TextureFormat format) => IsBlockCompressed(format) ? 4 : 1;

    /// <summary>
    /// Bytes in one block, or in one texel for an uncompressed format.
    /// Throws for the render-target-only formats.
    /// </summary>
    public static int BytesPerBlock(TextureFormat format) => format switch
    {
        TextureFormat.Rgba8 => 4,
        TextureFormat.Rgb8 => 3,
        TextureFormat.R8 => 1,
        TextureFormat.Bc1 or TextureFormat.Bc4 => 8,
        TextureFormat.Bc3 or TextureFormat.Bc5 or TextureFormat.Bc6H or TextureFormat.Bc7 => 16,
        _ => throw new ArgumentOutOfRangeException(
            nameof(format), $"{format} has no CPU-side block size; it is a render-target format."),
    };

    /// <summary>
    /// How many rows a mip of <paramref name="height"/> texels occupies: texel
    /// rows for an uncompressed format, block rows for a compressed one.
    /// </summary>
    public static int RowCount(TextureFormat format, int height)
    {
        int blockHeight = BlockHeight(format);
        return (height + blockHeight - 1) / blockHeight;
    }

    /// <summary>
    /// The unpadded row pitch of a mip of <paramref name="width"/> texels.
    /// A floor for validation; a file's declared pitch may be larger.
    /// </summary>
    public static int TightRowPitch(TextureFormat format, int width)
    {
        int blockWidth = BlockWidth(format);
        return (width + blockWidth - 1) / blockWidth * BytesPerBlock(format);
    }

    public static bool IsDepth(TextureFormat format) => format is TextureFormat.Depth32Float;

    /// <summary>
    /// The colour space a texture will get: the requested one, or linear when
    /// the format has no sRGB variant.
    /// </summary>
    public static TextureColorSpace Resolve(TextureFormat format, TextureColorSpace requested) =>
        requested == TextureColorSpace.Srgb && SupportsSrgb(format)
            ? TextureColorSpace.Srgb
            : TextureColorSpace.Linear;
}

/// <summary>Magnification and minification filtering applied when sampling.</summary>
public enum TextureFilter
{
    /// <summary>Point sampling. Sharp pixels, no blending.</summary>
    Nearest,

    /// <summary>Bilinear interpolation between the four nearest texels.</summary>
    Linear,

    /// <summary>Linear with trilinear mipmap interpolation; requires mipmaps.</summary>
    LinearMipmap,
}

/// <summary>Wrap behaviour when UV coordinates fall outside [0,1].</summary>
public enum TextureWrap
{
    /// <summary>Tile the texture infinitely.</summary>
    Repeat,

    /// <summary>Clamp to the nearest edge pixel.</summary>
    Clamp,
}
