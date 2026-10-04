using SpectraEngine.Core.Graphics;
using System;

namespace SpectraEngine.Core.Assets.Images;

/// <summary>
/// Which end of the picture the first stored row is.
/// </summary>
// The engine samples v = 0 at the bottom, and BC blocks cannot be flipped at
// load, so the cook flips before compressing and declares it in KTXorientation.
public enum SimageRowOrder
{
    /// <summary><c>KTXorientation = "ru"</c>: the first stored row is the bottom of the picture.</summary>
    BottomUp,

    /// <summary>
    /// <c>KTXorientation = "rd"</c>: the first stored row is the top. The reader
    /// refuses these.
    /// </summary>
    TopDown,
}

/// <summary>
/// KTX2 container constants shared by the <c>.simage</c> writer and reader.
/// A <c>.simage</c> is a restricted profile of KTX2, readable by any KTX2 tool.
/// </summary>
// All numbers are little-endian per the KTX2 spec.
public static class SimageFormat
{
    /// <summary>The cooked extension, dot included.</summary>
    public const string FileExtension = ".simage";

    /// <summary>KTX2's 12-byte file identifier.</summary>
    public static ReadOnlySpan<byte> Identifier =>
        [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Bytes from the start of the file to <c>vkFormat</c>.</summary>
    public const int HeaderOffset = 12;

    /// <summary>Where the level index starts: 12 identifier + 36 header + 32 index.</summary>
    public const int LevelIndexOffset = 80;

    /// <summary>Bytes in one level-index entry: three <c>uint64</c>.</summary>
    public const int LevelIndexEntrySize = 24;

    /// <summary>No supercompression: level bytes are the payload.</summary>
    public const uint SupercompressionNone = 0;

    /// <summary>BasisLZ. Refused: it needs a transcoder.</summary>
    public const uint SupercompressionBasisLz = 1;

    /// <summary>Zstandard, per level.</summary>
    public const uint SupercompressionZstd = 2;

    /// <summary>ZLIB, per level.</summary>
    public const uint SupercompressionZlib = 3;

    /// <summary>The KTX2 standard key for row order.</summary>
    public const string OrientationKey = "KTXorientation";

    /// <summary>Value of <see cref="OrientationKey"/> for <see cref="SimageRowOrder.BottomUp"/>.</summary>
    public const string OrientationBottomUp = "ru";

    /// <summary>Value of <see cref="OrientationKey"/> for <see cref="SimageRowOrder.TopDown"/>.</summary>
    public const string OrientationTopDown = "rd";

    /// <summary>
    /// The key carrying <c>EngineInfo.TextureFormatVersion</c>, as a
    /// NUL-terminated ASCII decimal so KTX2 tools print it readably.
    /// </summary>
    public const string ProfileKey = "SpectraProfile";

    // Allowlist, not blocklist: guessing at an unknown vkFormat gives a wrong
    // row pitch and a read past the end of a mapped view.

    /// <summary>VK_FORMAT_R8_UNORM.</summary>
    public const uint VkFormatR8Unorm = 9;

    /// <summary>VK_FORMAT_R8G8B8A8_UNORM.</summary>
    public const uint VkFormatR8G8B8A8Unorm = 37;

    /// <summary>VK_FORMAT_R8G8B8A8_SRGB.</summary>
    public const uint VkFormatR8G8B8A8Srgb = 43;

    /// <summary>VK_FORMAT_BC1_RGB_UNORM_BLOCK.</summary>
    public const uint VkFormatBc1RgbUnormBlock = 131;

    /// <summary>VK_FORMAT_BC1_RGB_SRGB_BLOCK.</summary>
    public const uint VkFormatBc1RgbSrgbBlock = 132;

    /// <summary>VK_FORMAT_BC1_RGBA_UNORM_BLOCK.</summary>
    public const uint VkFormatBc1RgbaUnormBlock = 133;

    /// <summary>VK_FORMAT_BC1_RGBA_SRGB_BLOCK.</summary>
    public const uint VkFormatBc1RgbaSrgbBlock = 134;

    /// <summary>VK_FORMAT_BC3_UNORM_BLOCK.</summary>
    public const uint VkFormatBc3UnormBlock = 137;

    /// <summary>VK_FORMAT_BC3_SRGB_BLOCK.</summary>
    public const uint VkFormatBc3SrgbBlock = 138;

    /// <summary>VK_FORMAT_BC4_UNORM_BLOCK.</summary>
    public const uint VkFormatBc4UnormBlock = 139;

    /// <summary>VK_FORMAT_BC5_UNORM_BLOCK.</summary>
    public const uint VkFormatBc5UnormBlock = 141;

    /// <summary>VK_FORMAT_BC6H_UFLOAT_BLOCK.</summary>
    public const uint VkFormatBc6HUfloatBlock = 143;

    /// <summary>VK_FORMAT_BC7_UNORM_BLOCK.</summary>
    public const uint VkFormatBc7UnormBlock = 145;

    /// <summary>VK_FORMAT_BC7_SRGB_BLOCK.</summary>
    public const uint VkFormatBc7SrgbBlock = 146;

    /// <summary>
    /// Maps a <c>vkFormat</c> to the engine format and the colour space the file
    /// declares. False for anything not on the allowlist.
    /// </summary>
    // The declared colour space is for diagnostics only: colour vs data belongs
    // to the material slot, so the loader uploads with the caller's request.
    // The cooker writes UNORM; the sRGB forms are here for files from other tools.
    public static bool TryResolveVkFormat(uint vkFormat, out TextureFormat format, out TextureColorSpace declared)
    {
        switch (vkFormat)
        {
            case VkFormatR8Unorm: format = TextureFormat.R8; declared = TextureColorSpace.Linear; return true;
            case VkFormatR8G8B8A8Unorm: format = TextureFormat.Rgba8; declared = TextureColorSpace.Linear; return true;
            case VkFormatR8G8B8A8Srgb: format = TextureFormat.Rgba8; declared = TextureColorSpace.Srgb; return true;
            case VkFormatBc1RgbUnormBlock: format = TextureFormat.Bc1; declared = TextureColorSpace.Linear; return true;
            case VkFormatBc1RgbSrgbBlock: format = TextureFormat.Bc1; declared = TextureColorSpace.Srgb; return true;
            case VkFormatBc1RgbaUnormBlock: format = TextureFormat.Bc1; declared = TextureColorSpace.Linear; return true;
            case VkFormatBc1RgbaSrgbBlock: format = TextureFormat.Bc1; declared = TextureColorSpace.Srgb; return true;
            case VkFormatBc3UnormBlock: format = TextureFormat.Bc3; declared = TextureColorSpace.Linear; return true;
            case VkFormatBc3SrgbBlock: format = TextureFormat.Bc3; declared = TextureColorSpace.Srgb; return true;
            case VkFormatBc4UnormBlock: format = TextureFormat.Bc4; declared = TextureColorSpace.Linear; return true;
            case VkFormatBc5UnormBlock: format = TextureFormat.Bc5; declared = TextureColorSpace.Linear; return true;
            case VkFormatBc6HUfloatBlock: format = TextureFormat.Bc6H; declared = TextureColorSpace.Linear; return true;
            case VkFormatBc7UnormBlock: format = TextureFormat.Bc7; declared = TextureColorSpace.Linear; return true;
            case VkFormatBc7SrgbBlock: format = TextureFormat.Bc7; declared = TextureColorSpace.Srgb; return true;
            default:
                format = default;
                declared = default;
                return false;
        }
    }

    /// <summary>
    /// The <c>vkFormat</c> the cooker writes for <paramref name="format"/>,
    /// always the UNORM form.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The format is not in the profile: the render-target formats and
    /// <see cref="TextureFormat.Rgb8"/>.
    /// </exception>
    public static uint ToVkFormat(TextureFormat format) => format switch
    {
        TextureFormat.R8 => VkFormatR8Unorm,
        TextureFormat.Rgba8 => VkFormatR8G8B8A8Unorm,
        TextureFormat.Bc1 => VkFormatBc1RgbaUnormBlock,
        TextureFormat.Bc3 => VkFormatBc3UnormBlock,
        TextureFormat.Bc4 => VkFormatBc4UnormBlock,
        TextureFormat.Bc5 => VkFormatBc5UnormBlock,
        TextureFormat.Bc6H => VkFormatBc6HUfloatBlock,
        TextureFormat.Bc7 => VkFormatBc7UnormBlock,
        _ => throw new ArgumentOutOfRangeException(
            nameof(format), format, $"{format} is not a format the .simage profile carries."),
    };

    /// <summary>
    /// Every level's byte offset is a multiple of this, per the KTX2 mip-padding
    /// rule: the least common multiple of the texel block size and 4.
    /// </summary>
    public static int LevelAlignment(TextureFormat format)
    {
        int blockBytes = TextureFormatInfo.BytesPerBlock(format);
        return blockBytes % 4 == 0 ? blockBytes : blockBytes * 4;
    }
}
