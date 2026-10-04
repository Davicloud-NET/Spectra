using System;

namespace SpectraEngine.Core.Graphics;

/// <summary>One mip level of a <see cref="TextureUploadDesc"/>.</summary>
/// <param name="Width">Width of this level in texels, not blocks.</param>
/// <param name="Height">Height of this level in texels, not blocks.</param>
/// <param name="Offset">
/// Byte offset of this level's first row within the payload. Levels need not be contiguous.
/// </param>
/// <param name="RowPitch">
/// Bytes from the start of one row to the start of the next, as the file
/// declares it. A row is a row of blocks for a compressed format.
/// </param>
public readonly record struct TextureMipDesc(int Width, int Height, int Offset, int RowPitch);

/// <summary>
/// One texture upload: a format, a colour space, a payload, and a per-mip layout
/// over that payload.
/// </summary>
// A ref struct so the payload can be a span into a mapped pack view, uncopied.
public readonly ref struct TextureUploadDesc
{
    /// <summary>Builds a descriptor. Nothing is validated until <see cref="Validate"/> runs.</summary>
    public TextureUploadDesc(
        TextureFormat format,
        TextureColorSpace colorSpace,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<TextureMipDesc> mips,
        TextureFilter filter = TextureFilter.Linear,
        TextureWrap wrap = TextureWrap.Repeat)
    {
        Format = format;
        ColorSpace = colorSpace;
        Payload = payload;
        Mips = mips;
        Filter = filter;
        Wrap = wrap;
    }

    public TextureFormat Format { get; }

    /// <summary>
    /// The requested colour space. A format with no sRGB variant resolves to
    /// linear through <see cref="TextureFormatInfo.Resolve"/>.
    /// </summary>
    public TextureColorSpace ColorSpace { get; }

    /// <summary>Every level's bytes, addressed through <see cref="Mips"/>.</summary>
    public ReadOnlySpan<byte> Payload { get; }

    /// <summary>The levels, most detailed first, each half the previous one.</summary>
    public ReadOnlySpan<TextureMipDesc> Mips { get; }

    public TextureFilter Filter { get; }

    public TextureWrap Wrap { get; }

    /// <summary>Width of the most detailed level.</summary>
    public int Width => Mips[0].Width;

    /// <summary>Height of the most detailed level.</summary>
    public int Height => Mips[0].Height;

    public int MipCount => Mips.Length;

    /// <summary>
    /// True when more than one level is supplied, so a backend should not build
    /// a chain of its own.
    /// </summary>
    public bool HasSuppliedMipChain => Mips.Length > 1;

    /// <summary>A descriptor over one tightly packed level.</summary>
    public static TextureUploadDesc SingleLevel(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        TextureFormat format,
        TextureColorSpace colorSpace,
        TextureFilter filter = TextureFilter.Linear,
        TextureWrap wrap = TextureWrap.Repeat)
    {
        // An array, not a stackalloc: the levels have to outlive this method.
        var mips = new TextureMipDesc[]
        {
            new(width, height, 0, TextureFormatInfo.IsFloat(format)
                // TightRowPitch throws for a float format. Let Validate refuse it.
                ? 0
                : TextureFormatInfo.TightRowPitch(format, width)),
        };
        return new TextureUploadDesc(format, colorSpace, pixels, mips, filter, wrap);
    }

    /// <summary>
    /// Throws for a descriptor that cannot be uploaded, naming the mip that is wrong.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The format cannot be filled from bytes at all.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A level is degenerate, is not the previous level halved, declares a pitch
    /// too small for its width, or runs past the end of the payload.
    /// </exception>
    public void Validate()
    {
        // Callers test for ArgumentOutOfRangeException here.
        if (TextureFormatInfo.IsFloat(Format))
        {
            throw new ArgumentOutOfRangeException(
                nameof(Format),
                $"{Format} cannot be uploaded from bytes; it is a render-target format.");
        }

        if (Mips.Length == 0)
            throw new ArgumentException("A texture upload needs at least one mip level.", nameof(Mips));

        for (int level = 0; level < Mips.Length; level++)
        {
            TextureMipDesc mip = Mips[level];

            if (mip.Width <= 0 || mip.Height <= 0)
            {
                throw new ArgumentException(
                    $"Mip {level} is {mip.Width}x{mip.Height}; every level must have a positive size.",
                    nameof(Mips));
            }

            if (level > 0)
            {
                // The API derives level sizes from the base size, so the chain has to match.
                int expectedWidth = Math.Max(1, Mips[level - 1].Width / 2);
                int expectedHeight = Math.Max(1, Mips[level - 1].Height / 2);
                if (mip.Width != expectedWidth || mip.Height != expectedHeight)
                {
                    throw new ArgumentException(
                        $"Mip {level} is {mip.Width}x{mip.Height}, but halving mip {level - 1} " +
                        $"({Mips[level - 1].Width}x{Mips[level - 1].Height}) gives " +
                        $"{expectedWidth}x{expectedHeight}.",
                        nameof(Mips));
                }
            }

            int tightPitch = TextureFormatInfo.TightRowPitch(Format, mip.Width);
            if (mip.RowPitch < tightPitch)
            {
                throw new ArgumentException(
                    $"Mip {level} ({mip.Width}x{mip.Height}) declares a row pitch of {mip.RowPitch} bytes, " +
                    $"which is less than the {tightPitch} bytes one row of {Format} occupies.",
                    nameof(Mips));
            }

            if (mip.Offset < 0)
            {
                throw new ArgumentException(
                    $"Mip {level} declares a negative payload offset of {mip.Offset}.", nameof(Mips));
            }

            // The last row is measured tight: a writer may stop after its real bytes.
            int rows = TextureFormatInfo.RowCount(Format, mip.Height);
            long required = (long)mip.Offset + (long)(rows - 1) * mip.RowPitch + tightPitch;
            if (required > Payload.Length)
            {
                throw new ArgumentException(
                    $"Mip {level} ({mip.Width}x{mip.Height}) needs {required} bytes at offset {mip.Offset} " +
                    $"over {rows} rows of pitch {mip.RowPitch}, but the payload is " +
                    $"{Payload.Length} bytes.",
                    nameof(Mips));
            }
        }
    }
}
