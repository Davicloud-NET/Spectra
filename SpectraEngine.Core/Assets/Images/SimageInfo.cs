using SpectraEngine.Core.Graphics;
using System;

namespace SpectraEngine.Core.Assets.Images;

/// <summary>
/// What <see cref="SimageReader"/> found in a <c>.simage</c>: format, shape and
/// per-mip layout. Holds no bytes, so it may outlive the span it describes.
/// </summary>
public sealed class SimageInfo
{
    internal SimageInfo(
        TextureFormat format,
        TextureColorSpace declaredColorSpace,
        SimageRowOrder rowOrder,
        int profileVersion,
        TextureMipDesc[] mips,
        int payloadBytes)
    {
        Format = format;
        DeclaredColorSpace = declaredColorSpace;
        RowOrder = rowOrder;
        ProfileVersion = profileVersion;
        Mips = mips;
        PayloadBytes = payloadBytes;
    }

    /// <summary>The block or pixel format every level is stored in.</summary>
    public TextureFormat Format { get; }

    /// <summary>
    /// The colour space the file's <c>vkFormat</c> declares. The texture gets
    /// the one its caller asks for instead.
    /// </summary>
    public TextureColorSpace DeclaredColorSpace { get; }

    /// <summary>Which end of the picture the first stored row is.</summary>
    public SimageRowOrder RowOrder { get; }

    /// <summary>The <c>SpectraProfile</c> version this file was cooked under.</summary>
    public int ProfileVersion { get; }

    /// <summary>
    /// Every level, most detailed first. Offsets are into the whole file, so an
    /// upload can be built straight over the file span.
    /// </summary>
    public TextureMipDesc[] Mips { get; }

    /// <summary>How many bytes of the file are level data.</summary>
    public int PayloadBytes { get; }

    /// <summary>Width of the base level in texels.</summary>
    public int Width => Mips[0].Width;

    /// <summary>Height of the base level in texels.</summary>
    public int Height => Mips[0].Height;

    /// <summary>How many levels the file supplies.</summary>
    public int MipCount => Mips.Length;
}
