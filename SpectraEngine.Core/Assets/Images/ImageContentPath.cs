using SpectraEngine.Core.Assets.Sources;
using System;
using System.IO;

namespace SpectraEngine.Core.Assets.Images;

/// <summary>
/// Where an image's bytes live: the cooked <c>.simage</c> beside it when a
/// mounted source has one, otherwise the authored file.
/// </summary>
// Materials and models keep naming the source path (Textures/x.png) after a cook.
// The engine and the cook's verifier must both resolve through here, or an
// existence check and the open beside it disagree and packed materials go magenta.
public static class ImageContentPath
{
    /// <summary>Whether <paramref name="contentPath"/> already names a cooked image.</summary>
    public static bool IsCooked(string contentPath) =>
        contentPath.EndsWith(SimageFormat.FileExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The cooked path for an authored one: same path, <c>.simage</c> extension.</summary>
    public static string CookedPathFor(string contentPath) =>
        IsCooked(contentPath)
            ? contentPath
            : ContentRoot.NormalizeRelativePath(Path.ChangeExtension(contentPath, SimageFormat.FileExtension));

    /// <summary>
    /// The path to ask <paramref name="source"/> for when a caller wants the
    /// image at <paramref name="contentPath"/>. Falls back to the authored path.
    /// </summary>
    public static string Resolve(IContentSource source, string contentPath)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (IsCooked(contentPath)) return contentPath;

        string cooked = CookedPathFor(contentPath);
        return source.Exists(cooked) ? cooked : contentPath;
    }
}
