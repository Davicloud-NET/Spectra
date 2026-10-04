using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using Spectra.Kitchen.Cooking;
using SpectraEngine.Core.Assets;
using System;
using EnginePixelFormat = SpectraEngine.Core.Graphics.TextureFormat;

namespace Spectra.Kitchen.Images;

/// <summary>
/// Turns a decoded image into block-compressed levels: the one place the cooker
/// touches an encoder.
/// </summary>
// Rows arrive bottom-up from ImageDecoder and are compressed as they are. A
// block-compressed payload cannot be flipped later.
public static class ImageBlockEncoder
{
    /// <summary>
    /// The block format a decoded image cooks to: BC4 for one channel, BC7 for
    /// everything else.
    /// </summary>
    // No automatic BC1: texture quality would depend on whether the author
    // happened to save an alpha channel.
    public static EnginePixelFormat ChooseFormat(DecodedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return image.Channels == 1 ? EnginePixelFormat.Bc4 : EnginePixelFormat.Bc7;
    }

    /// <summary>
    /// How hard the encoder searches, per cook profile.
    /// </summary>
    public static CompressionQuality QualityFor(CookProfile profile) => profile switch
    {
        CookProfile.Ship => CompressionQuality.Balanced,
        _ => CompressionQuality.Fast,
    };

    /// <summary>
    /// Encodes <paramref name="image"/> and its mip chain, most detailed first.
    /// </summary>
    /// <param name="parallel">
    /// Whether the encoder may split one image across tasks. The cook passes
    /// false, since it already runs one rule per worker.
    /// </param>
    public static byte[][] Encode(
        DecodedImage image, EnginePixelFormat format, CompressionQuality quality, bool parallel = false)
    {
        ArgumentNullException.ThrowIfNull(image);

        var encoder = new BcEncoder
        {
            OutputOptions =
            {
                GenerateMipMaps = true,
                Quality = quality,
                Format = ToCompressionFormat(format),
            },
            Options = { IsParallel = parallel },
            // Red: the channel an R8 texture samples in a shader.
            InputOptions = { Bc4Component = ColorComponent.R },
        };

        // The encoder has no single-channel input format.
        if (image.Channels == 1)
        {
            byte[] widened = WidenGreyToRgba(image);
            return encoder.EncodeToRawBytes(widened, image.Width, image.Height, PixelFormat.Rgba32);
        }

        PixelFormat input = image.Channels switch
        {
            3 => PixelFormat.Rgb24,
            4 => PixelFormat.Rgba32,
            _ => throw new ArgumentOutOfRangeException(
                nameof(image),
                image.Channels,
                "A decoded image has 1, 3 or 4 channels; ImageDecoder produces no others."),
        };

        return encoder.EncodeToRawBytes(image.Pixels, image.Width, image.Height, input);
    }

    private static byte[] WidenGreyToRgba(DecodedImage image)
    {
        ReadOnlySpan<byte> grey = image.Pixels;
        var rgba = new byte[image.Width * image.Height * 4];
        for (int i = 0, o = 0; i < image.Width * image.Height; i++, o += 4)
        {
            rgba[o] = grey[i];
            rgba[o + 1] = grey[i];
            rgba[o + 2] = grey[i];
            rgba[o + 3] = 255;
        }

        return rgba;
    }

    private static CompressionFormat ToCompressionFormat(EnginePixelFormat format) => format switch
    {
        EnginePixelFormat.Bc1 => CompressionFormat.Bc1,
        EnginePixelFormat.Bc3 => CompressionFormat.Bc3,
        EnginePixelFormat.Bc4 => CompressionFormat.Bc4,
        EnginePixelFormat.Bc5 => CompressionFormat.Bc5,
        EnginePixelFormat.Bc7 => CompressionFormat.Bc7,
        _ => throw new ArgumentOutOfRangeException(
            nameof(format), format, $"{format} is not a format this cooker encodes to."),
    };
}
