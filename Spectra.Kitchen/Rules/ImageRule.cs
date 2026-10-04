using BCnEncoder.Encoder;
using Spectra.Kitchen.Cache;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Images;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Images;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace Spectra.Kitchen.Rules;

/// <summary>
/// Compresses an authored image into a <c>.simage</c>: a mip chain of BC blocks
/// in a restricted-profile KTX2 container. The source file is not also copied
/// into the pack.
/// </summary>
// Decodes through ImageDecoder so the cooked image has the same row flip as the
// loose one. Blocks cannot be flipped at load.
public sealed class ImageRule : IRule
{
    // Must match what ImageDecoder (StbImageSharp) reads.
    private static readonly string[] SourceExtensions = [".png", ".jpg", ".jpeg", ".tga", ".bmp"];

    /// <inheritdoc/>
    public RuleKind Kind => RuleKind.Image;

    /// <inheritdoc/>
    public int Version => 1;

    /// <inheritdoc/>
    // The profile picks the encoder quality. Targets are not read: a block
    // format is the same on every backend.
    public CookSettingKeys SettingsRead => CookSettingKeys.Profile;

    /// <summary>Whether <paramref name="contentPath"/> is an image this rule cooks.</summary>
    public static bool Handles(string contentPath)
    {
        ArgumentNullException.ThrowIfNull(contentPath);

        foreach (string extension in SourceExtensions)
        {
            if (contentPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void Cook(IRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        byte[] source = context.Read(context.SourcePath);

        DecodedImage image;
        try
        {
            image = ImageDecoder.Decode(source, context.SourcePath);
        }
        catch (InvalidDataException ex)
        {
            context.Report(CookDiagnostic.Error(
                CookDiagnosticCodes.ImageUndecodable,
                $"'{context.SourcePath}' could not be decoded: {ex.Message}",
                context.SourcePath));

            return;
        }

        TextureFormat format = ImageBlockEncoder.ChooseFormat(image);
        CompressionQuality quality = ImageBlockEncoder.QualityFor(context.Profile);

        byte[][] encoded = ImageBlockEncoder.Encode(image, format, quality);
        IReadOnlyList<byte[]> levels = TrimToHalvingChain(encoded, image.Width, image.Height, format);

        byte[] cooked;
        try
        {
            cooked = Ktx2Writer.Write(
                format,
                image.Width,
                image.Height,
                levels,
                // What ImageDecoder produced. KTX2 defaults to top-down, so the
                // file has to say so.
                SimageRowOrder.BottomUp,
                EngineInfo.TextureFormatVersion);
        }
        catch (ArgumentException ex)
        {
            // Report, don't throw, so the diagnostic names the asset.
            context.Report(CookDiagnostic.Error(
                CookDiagnosticCodes.ImageEncodeFailed,
                $"'{context.SourcePath}' produced a mip chain the container cannot hold: {ex.Message}",
                context.SourcePath));

            return;
        }

        context.Emit(ImageContentPath.CookedPathFor(context.SourcePath), cooked, PackEntryKind.Image);
    }

    // The uploader assumes each level is the previous one halved. Cut the chain
    // at the first level whose size disagrees; a shorter chain is fine.
    private static IReadOnlyList<byte[]> TrimToHalvingChain(
        byte[][] encoded, int width, int height, TextureFormat format)
    {
        var kept = new List<byte[]>(encoded.Length);
        for (int level = 0; level < encoded.Length; level++)
        {
            int levelWidth = Math.Max(1, width >> level);
            int levelHeight = Math.Max(1, height >> level);
            int expected = TextureFormatInfo.RowCount(format, levelHeight)
                * TextureFormatInfo.TightRowPitch(format, levelWidth);

            if (encoded[level].Length != expected) break;

            kept.Add(encoded[level]);
        }

        return kept;
    }
}
