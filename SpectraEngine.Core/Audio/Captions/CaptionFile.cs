using SpectraEngine.Core.Projects;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Audio.Captions;

/// <summary>
/// One language's sound captions: the text file
/// <c>Captions/&lt;language&gt;.txt</c> in a project's content, as
/// <see cref="CaptionFileReader"/> read it.
/// </summary>
public sealed class CaptionFile
{
    /// <summary>The content folder the caption files are in.</summary>
    public const string Folder = "Captions";

    /// <summary>Extension of a caption file, including the dot.</summary>
    public const string FileExtension = ".txt";

    // By sound path, to an index into Entries.
    private readonly Dictionary<string, int> _bySound;

    internal CaptionFile(
        List<CaptionFileEntry> entries, Dictionary<string, int> bySound, List<CaptionFileProblem> problems)
    {
        Entries = entries;
        _bySound = bySound;
        Problems = problems;
    }

    /// <summary>The captions, one for each sound, in the order the file first names them.</summary>
    public IReadOnlyList<CaptionFileEntry> Entries { get; }

    /// <summary>The lines that were not read as their writer meant, in file order.</summary>
    public IReadOnlyList<CaptionFileProblem> Problems { get; }

    /// <summary>The caption of the sound at a normalized content path.</summary>
    public bool TryGetText(string soundPath, [NotNullWhen(true)] out string? text)
    {
        ArgumentNullException.ThrowIfNull(soundPath);

        if (_bySound.TryGetValue(soundPath, out int at))
        {
            text = Entries[at].Text;
            return true;
        }

        text = null;
        return false;
    }

    /// <summary>The content path of a language's caption file: <c>Captions/en.txt</c> for <c>en</c>.</summary>
    public static string PathFor(string language)
    {
        ArgumentNullException.ThrowIfNull(language);
        return $"{Folder}/{language}{FileExtension}";
    }

    /// <summary>
    /// Whether a normalized content path is a text file directly in the
    /// captions folder, whatever it is called.
    /// </summary>
    public static bool IsInCaptionFolder(string contentPath)
    {
        ArgumentNullException.ThrowIfNull(contentPath);

        int nameAt = Folder.Length + 1;
        return contentPath.Length > nameAt + FileExtension.Length
            && contentPath.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)
            && contentPath[Folder.Length] == '/'
            && contentPath.IndexOf('/', nameAt) < 0
            && contentPath.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The language a caption file is for, from its path. False for a file
    /// that is not in the captions folder or is not named after a language.
    /// </summary>
    public static bool TryGetLanguage(string contentPath, [NotNullWhen(true)] out string? language)
    {
        language = null;
        if (!IsInCaptionFolder(contentPath))
            return false;

        ReadOnlySpan<char> name = contentPath.AsSpan(Folder.Length + 1);
        name = name[..^FileExtension.Length];
        if (!LanguageTag.IsValid(name))
            return false;

        language = name.ToString();
        return true;
    }
}
