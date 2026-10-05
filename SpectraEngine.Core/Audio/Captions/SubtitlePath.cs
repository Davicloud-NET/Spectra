using SpectraEngine.Core.Projects;
using System;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Audio.Captions;

/// <summary>
/// Where a voice file's subtitles live: beside it, under its own name with
/// the language and <c>.vtt</c> in place of its extension.
/// <c>Sounds/vo/guard_hey.wav</c> has <c>Sounds/vo/guard_hey.en.vtt</c>.
/// </summary>
public static class SubtitlePath
{
    /// <summary>Extension of a subtitle file, including the dot.</summary>
    public const string FileExtension = ".vtt";

    /// <summary>Whether a content path names a subtitle file.</summary>
    public static bool IsSubtitle(string contentPath)
    {
        ArgumentNullException.ThrowIfNull(contentPath);
        return contentPath.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The content path of a sound's subtitle file in a language.</summary>
    public static string For(string soundPath, string language)
    {
        ArgumentNullException.ThrowIfNull(soundPath);
        ArgumentNullException.ThrowIfNull(language);

        return $"{WithoutExtension(soundPath)}.{language}{FileExtension}";
    }

    /// <summary>
    /// Splits a subtitle file's path into its sound's path without an
    /// extension and its language. False for a file whose name has no
    /// language in it.
    /// </summary>
    public static bool TryParse(
        string subtitlePath, [NotNullWhen(true)] out string? soundStem, [NotNullWhen(true)] out string? language)
    {
        soundStem = language = null;
        if (!IsSubtitle(subtitlePath))
            return false;

        ReadOnlySpan<char> named = subtitlePath.AsSpan(0, subtitlePath.Length - FileExtension.Length);
        ReadOnlySpan<char> stem = WithoutExtension(named);
        if (stem.Length == named.Length || stem.IsEmpty || stem[^1] == '/')
            return false;

        ReadOnlySpan<char> tag = named[(stem.Length + 1)..];
        if (!LanguageTag.IsValid(tag))
            return false;

        soundStem = stem.ToString();
        language = tag.ToString();
        return true;
    }

    // Only a dot in the file's own name counts.
    private static ReadOnlySpan<char> WithoutExtension(ReadOnlySpan<char> path)
    {
        int dot = path.LastIndexOf('.');
        return dot > path.LastIndexOf('/') ? path[..dot] : path;
    }
}
