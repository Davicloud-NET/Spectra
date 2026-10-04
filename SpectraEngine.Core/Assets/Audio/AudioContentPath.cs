using SpectraEngine.Core.Assets.Sources;
using System;
using System.IO;

namespace SpectraEngine.Core.Assets.Audio;

/// <summary>
/// Maps a sound's authored path to where its bytes live: the cooked
/// <c>.saudio</c> beside it if a mounted source has one, otherwise the authored
/// file. Content always names the authored path.
/// </summary>
public static class AudioContentPath
{
    /// <summary>Whether <paramref name="contentPath"/> already names a cooked sound.</summary>
    public static bool IsCooked(string contentPath) =>
        contentPath.EndsWith(SaudioFormat.FileExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The cooked path for an authored one: the same path with the
    /// <c>.saudio</c> extension.
    /// </summary>
    public static string CookedPathFor(string contentPath) =>
        IsCooked(contentPath)
            ? contentPath
            : ContentRoot.NormalizeRelativePath(Path.ChangeExtension(contentPath, SaudioFormat.FileExtension));

    /// <summary>
    /// The path <paramref name="source"/> should actually be asked for when a
    /// caller wants the sound at <paramref name="contentPath"/>. On a miss this
    /// is the authored path, so an error names the file that was looked for.
    /// </summary>
    public static string Resolve(IContentSource source, string contentPath)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (IsCooked(contentPath)) return contentPath;

        string cooked = CookedPathFor(contentPath);
        return source.Exists(cooked) ? cooked : contentPath;
    }
}
