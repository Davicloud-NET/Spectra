using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Graphics;
using StbImageSharp;
using System;
using System.Buffers;
using System.IO;

namespace SpectraEngine.Core.Assets;

/// <summary>
/// Decodes image files (PNG/JPG/TGA/BMP) into <see cref="DecodedImage"/>.
/// Pure CPU, callable from any thread.
/// </summary>
public static class ImageDecoder
{
    /// <summary>
    /// Reads and decodes one file straight off the filesystem. For tools and
    /// tests; engine content goes through <see cref="IContentSource"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">The bytes are not a supported image.</exception>
    public static DecodedImage DecodeFile(string absolutePath)
    {
        ArgumentNullException.ThrowIfNull(absolutePath);

        using ContentBlob blob = FileContent.Read(absolutePath);
        return Decode(blob.Span, absolutePath);
    }

    /// <summary>Decodes an image file held in a span.</summary>
    /// <exception cref="InvalidDataException">The bytes are not a supported image.</exception>
    // Stb wants a byte[], and a pooled blob buffer is longer than its content, so copy.
    public static DecodedImage Decode(ReadOnlySpan<byte> fileBytes, string originForErrors = "<memory>")
        => Decode(fileBytes.ToArray(), originForErrors);

    /// <summary>
    /// Decodes an in-memory image file. <paramref name="originForErrors"/> only
    /// labels exception messages.
    /// </summary>
    /// <exception cref="InvalidDataException">The bytes are not a supported image.</exception>
    public static DecodedImage Decode(byte[] fileBytes, string originForErrors = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(fileBytes);

        // Default keeps the file's channel count, so a mask stays R8.
        ImageResult result;
        try
        {
            result = ImageResult.FromMemory(fileBytes, ColorComponents.Default);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Could not decode image '{originForErrors}': {ex.Message}", ex);
        }

        if (result is null || result.Data is null)
            throw new InvalidDataException($"Could not decode image '{originForErrors}': no pixel data.");

        int channels = (int)result.Comp;
        TextureFormat format;
        switch (result.Comp)
        {
            case ColorComponents.Grey:
                format = TextureFormat.R8;
                break;
            case ColorComponents.RedGreenBlue:
                format = TextureFormat.Rgb8;
                break;
            case ColorComponents.RedGreenBlueAlpha:
                format = TextureFormat.Rgba8;
                break;
            default:
                // Grey+alpha has no TextureFormat; re-decode as RGBA to keep the alpha.
                result = ImageResult.FromMemory(fileBytes, ColorComponents.RedGreenBlueAlpha);
                channels = 4;
                format = TextureFormat.Rgba8;
                break;
        }

        byte[] pixels = result.Data;
        // Files are top-down, uploads are bottom-up. Not stb's flip-on-load:
        // that is process-global and races between background decodes.
        FlipRowsInPlace(pixels, result.Width, result.Height, channels);

        return new DecodedImage(pixels, result.Width, result.Height, channels, format);
    }
    private static void FlipRowsInPlace(byte[] pixels, int width, int height, int channels)
    {
        int stride = width * channels;
        if (stride == 0 || height < 2) return;

        byte[] scratch = ArrayPool<byte>.Shared.Rent(stride);
        try
        {
            Span<byte> row = scratch.AsSpan(0, stride);
            for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
            {
                Span<byte> topRow = pixels.AsSpan(top * stride, stride);
                Span<byte> bottomRow = pixels.AsSpan(bottom * stride, stride);
                topRow.CopyTo(row);
                bottomRow.CopyTo(topRow);
                row.CopyTo(bottomRow);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }
}
