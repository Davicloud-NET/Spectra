using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Projects;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Audio.Captions;

/// <summary>
/// Answers what captions a sound has in a language. A sound with a subtitle
/// file beside it is speech. A sound with a line in the language's caption
/// file is a sound caption. A sound with nothing in the language asked for
/// gets the captions of the project's language. Each file is read once and
/// kept. Render thread only.
/// </summary>
public sealed class CaptionLibrary
{
    private readonly IContentSource _content;
    private readonly ILogger _logger;
    private readonly Dictionary<string, LanguageCaptions> _languages = new(StringComparer.Ordinal);

    // Files the log has named as missing or refused. Each is named once.
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);

    private string _projectLanguage;

    /// <param name="content">Where the caption and subtitle files are read from.</param>
    /// <param name="projectLanguage">The language the project names as its own.</param>
    /// <param name="logger">Told once about a file that is missing or cannot be read.</param>
    public CaptionLibrary(IContentSource content, string projectLanguage, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(logger);
        ThrowIfNotALanguage(projectLanguage);

        _content = content;
        _projectLanguage = projectLanguage;
        _logger = logger;
    }

    /// <summary>The language a sound's captions come from when the one asked for has none.</summary>
    /// <exception cref="ArgumentException">The value is not a <see cref="LanguageTag"/>.</exception>
    public string ProjectLanguage
    {
        get => _projectLanguage;
        set
        {
            ThrowIfNotALanguage(value);
            _projectLanguage = value;
        }
    }

    /// <summary>
    /// The captions of a sound in a language, or in the project's language
    /// when that one has none. Null when neither has any.
    /// </summary>
    /// <param name="soundPath">The authored sound's content path, such as <c>Sounds/door_open.wav</c>.</param>
    /// <param name="language">The language to look in first, as a <see cref="LanguageTag"/>.</param>
    public SoundCaptions? Find(string soundPath, string language)
    {
        ArgumentNullException.ThrowIfNull(soundPath);
        ArgumentNullException.ThrowIfNull(language);

        SoundCaptions? found = FindIn(soundPath, language);
        if (found is null && !string.Equals(language, _projectLanguage, StringComparison.Ordinal))
            found = FindIn(soundPath, _projectLanguage);

        return found;
    }

    /// <summary>Whether the project's content has a caption file for a language.</summary>
    public bool HasCaptionFile(string language)
    {
        ArgumentNullException.ThrowIfNull(language);
        return _content.Exists(CaptionFile.PathFor(language));
    }

    /// <summary>Forgets what was read, so a file that has changed since is read again.</summary>
    public void Reload() => _languages.Clear();

    private SoundCaptions? FindIn(string soundPath, string language)
    {
        if (!_languages.TryGetValue(language, out LanguageCaptions? known))
        {
            known = new LanguageCaptions(ReadCaptionFile(language));
            _languages.Add(language, known);
        }

        // Kept under the path as it was asked too, so asking again normalizes nothing.
        if (known.BySound.TryGetValue(soundPath, out SoundCaptions? captions))
            return captions;

        // One answer for a sound however its path is spelled: a caption that
        // shows is told from another by the answer it came from.
        if (TryNormalize(soundPath, out string sound) && !known.BySound.TryGetValue(sound, out captions))
        {
            captions = Load(sound, language, known.File);
            known.BySound[sound] = captions;
        }

        known.BySound[soundPath] = captions;
        return captions;
    }

    private static bool TryNormalize(string soundPath, out string sound)
    {
        try
        {
            sound = ContentRoot.NormalizeRelativePath(soundPath);
            return true;
        }
        catch (ArgumentException)
        {
            sound = "";
            return false;
        }
    }

    private SoundCaptions? Load(string sound, string language, CaptionFile? file)
    {
        // The subtitle file wins over a line in the caption file.
        if (ReadSubtitles(SubtitlePath.For(sound, language)) is { Count: > 0 } cues)
            return new SoundCaptions(CaptionKind.Voice, language, cues);

        if (file is null || !file.TryGetText(sound, out string? words))
            return null;

        return new SoundCaptions(
            CaptionKind.Sound, language, [new CaptionLine(0d, double.PositiveInfinity, null, words)]);
    }

    private IReadOnlyList<CaptionLine>? ReadSubtitles(string path)
    {
        if (!_content.Exists(path) || !_content.TryOpen(path, out ContentBlob? blob))
            return null;

        try
        {
            return SubtitleReader.Read(blob.Span, path).Lines;
        }
        catch (SubtitleFormatException refusal)
        {
            if (_reported.Add(path))
                _logger.LogWarning("Subtitles will not show: {Reason}", refusal.Message);

            return null;
        }
        finally
        {
            blob.Dispose();
        }
    }

    private CaptionFile? ReadCaptionFile(string language)
    {
        string path = CaptionFile.PathFor(language);
        if (!_content.Exists(path) || !_content.TryOpen(path, out ContentBlob? blob))
        {
            ReportMissing(path, language);
            return null;
        }

        using (blob)
        {
            CaptionFile file = CaptionFileReader.Read(blob.Span);
            foreach (CaptionFileProblem problem in file.Problems)
                _logger.LogWarning("Captions {Path}({Line}): {Problem}", path, problem.Line, problem.Message);

            return file;
        }
    }

    private void ReportMissing(string path, string language)
    {
        if (!_reported.Add(path))
            return;

        if (string.Equals(language, _projectLanguage, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Captions: the project has no {Path}, so a sound with no subtitle file has no caption", path);
            return;
        }

        _logger.LogWarning(
            "Captions: the project has no {Path}. A sound with no subtitle file in {Language} gets its " +
            "caption in {ProjectLanguage}, the project's language",
            path,
            language,
            _projectLanguage);
    }

    private static void ThrowIfNotALanguage(string language)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (!LanguageTag.IsValid(language))
        {
            throw new ArgumentException(
                $"'{language}' is not a language tag such as en, de or pt-br.", nameof(language));
        }
    }

    // What one language's files have said so far.
    private sealed class LanguageCaptions(CaptionFile? file)
    {
        // Null when the language has no caption file.
        public CaptionFile? File { get; } = file;

        // By sound path as it was asked for. Null for a sound with nothing in this language.
        public Dictionary<string, SoundCaptions?> BySound { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
