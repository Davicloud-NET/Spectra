using SpectraEngine.Core.Graphics;
using System;

namespace SpectraEngine.Core.Assets;

/// <summary>
/// A decoded image ready for <see cref="Graphics.Renderer.CreateTexture"/>.
/// Rows are bottom-up (row 0 is the bottom of the picture). Immutable.
/// </summary>
public sealed class DecodedImage
{
    private readonly byte[] _pixels;

    /// <summary>
    /// Wraps a bottom-up, tightly packed pixel buffer. Not copied: the caller
    /// must not mutate it afterwards.
    /// </summary>
    public DecodedImage(byte[] pixels, int width, int height, int channels, TextureFormat format)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        int expected = checked(width * height * channels);
        if (pixels.Length < expected)
            throw new ArgumentException(
                $"Pixel buffer holds {pixels.Length} bytes but {width}x{height}x{channels} needs {expected}.",
                nameof(pixels));

        _pixels = pixels;
        Width = width;
        Height = height;
        Channels = channels;
        Format = format;
    }

    /// <summary>Width in texels.</summary>
    public int Width { get; }

    /// <summary>Height in texels.</summary>
    public int Height { get; }

    /// <summary>Bytes per texel: 1 (R8), 3 (RGB8) or 4 (RGBA8).</summary>
    public int Channels { get; }

    /// <summary>GPU format the buffer should be uploaded as.</summary>
    public TextureFormat Format { get; }

    /// <summary>Row stride in bytes. Rows are tightly packed.</summary>
    public int Stride => Width * Channels;

    /// <summary>The bottom-up, tightly packed pixel bytes.</summary>
    public ReadOnlySpan<byte> Pixels => _pixels;

    /// <summary>
    /// The bytes of one texel. <paramref name="y"/> counts from the bottom edge,
    /// so (0, 0) is the texel at UV (0, 0).
    /// </summary>
    public ReadOnlySpan<byte> GetPixel(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        return _pixels.AsSpan((y * Width + x) * Channels, Channels);
    }
}
