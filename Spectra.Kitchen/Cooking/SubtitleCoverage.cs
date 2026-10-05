using Spectra.Kitchen.Diagnostics;
using SpectraEngine.Core.Audio.Captions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Spectra.Kitchen.Cooking;

/// <summary>
/// Says, for each other language the project has, which sounds have subtitles
/// in the project's language and none in that one.
/// </summary>
// Not a rule: a rule sees one file, and a subtitle file that is missing has
// none to run for. It reads only the names of the content, once a cook.
public static class SubtitleCoverage
{
    /// <summary>The most missing files a message names before it counts the rest.</summary>
    public const int MaxNamed = 5;

    /// <summary>One warning for each language that lacks subtitles, in the languages' ordinal order.</summary>
    /// <param name="content">The project's content, in walk order.</param>
    /// <param name="projectLanguage">The language every other one is compared with.</param>
    public static IReadOnlyList<CookDiagnostic> Check(IReadOnlyList<ContentFile> content, string projectLanguage)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectLanguage);

        // By language, the sounds with subtitles in it, each as its path
        // with no extension. A language with only a caption file is here
        // too, with none: that is the plainest gap.
        var subtitled = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var inProjectLanguage = new List<string>();

        foreach (ContentFile file in content)
        {
            if (SubtitlePath.TryParse(file.ContentPath, out string? sound, out string? language))
            {
                SoundsIn(subtitled, language).Add(sound);
                if (string.Equals(language, projectLanguage, StringComparison.Ordinal))
                    inProjectLanguage.Add(sound);
            }
            else if (CaptionFile.TryGetLanguage(file.ContentPath, out string? captioned))
            {
                SoundsIn(subtitled, captioned);
            }
        }

        var reports = new List<CookDiagnostic>();
        foreach ((string language, HashSet<string> sounds) in subtitled)
        {
            if (string.Equals(language, projectLanguage, StringComparison.Ordinal))
                continue;

            var missing = new List<string>();
            foreach (string sound in inProjectLanguage)
            {
                if (!sounds.Contains(sound))
                    missing.Add($"{sound}.{language}{SubtitlePath.FileExtension}");
            }

            if (missing.Count > 0)
                reports.Add(Incomplete(language, projectLanguage, missing, inProjectLanguage.Count));
        }

        return reports;
    }

    private static HashSet<string> SoundsIn(SortedDictionary<string, HashSet<string>> subtitled, string language)
    {
        if (!subtitled.TryGetValue(language, out HashSet<string>? sounds))
        {
            sounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            subtitled.Add(language, sounds);
        }

        return sounds;
    }

    private static CookDiagnostic Incomplete(string language, string projectLanguage, List<string> missing, int total)
    {
        var message = new StringBuilder();
        message.Append(
            CultureInfo.InvariantCulture,
            $"'{language}' has no subtitles for {missing.Count} of the {total} ");
        message.Append(total == 1 ? "sound that has" : "sounds that have");
        message.Append($" them in '{projectLanguage}', the project's language. There is no ");

        for (int i = 0; i < Math.Min(missing.Count, MaxNamed); i++)
        {
            if (i > 0) message.Append(", ");
            message.Append(missing[i]);
        }

        if (missing.Count > MaxNamed)
            message.Append(CultureInfo.InvariantCulture, $" and {missing.Count - MaxNamed} more");

        message.Append(missing.Count == 1 ? ". Its lines show in '" : ". Their lines show in '");
        message.Append(projectLanguage).Append("'.");

        return CookDiagnostic.Warning(CookDiagnosticCodes.SubtitleLanguageIncomplete, message.ToString());
    }
}
