using Spectra.Kitchen.Cache;
using Spectra.Kitchen.Diagnostics;
using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Audio.Captions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Spectra.Kitchen.Rules;

/// <summary>
/// Checks a language's caption file against the project and packs it as it
/// is: the engine reads the same text loose and packed.
/// </summary>
// It says something on every run, and a run that says anything is never
// served from the cook cache. This rule needs that: what it says depends on
// the project's language, and that is in no cache key.
public sealed class CaptionFileRule : IRule
{
    /// <summary>The most sounds a message names before it counts the rest.</summary>
    public const int MaxNamed = 5;

    private readonly string _projectLanguage;

    /// <param name="projectLanguage">The language every other caption file is compared with.</param>
    public CaptionFileRule(string projectLanguage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectLanguage);
        _projectLanguage = projectLanguage;
    }

    /// <inheritdoc/>
    public RuleKind Kind => RuleKind.CaptionFile;

    /// <inheritdoc/>
    public int Version => 1;

    /// <inheritdoc/>
    public CookSettingKeys SettingsRead => CookSettingKeys.None;

    /// <summary>Whether <paramref name="contentPath"/> is a text file in the captions folder.</summary>
    public static bool Handles(string contentPath) => CaptionFile.IsInCaptionFolder(contentPath);

    /// <inheritdoc/>
    public void Cook(IRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        byte[] source = context.Read(context.SourcePath);
        context.Emit(context.SourcePath, source);

        if (!CaptionFile.TryGetLanguage(context.SourcePath, out string? language))
        {
            context.Report(CookDiagnostic.Warning(
                CookDiagnosticCodes.CaptionFileNotALanguage,
                $"'{context.SourcePath}' is in the captions folder and is not named after a language, so " +
                "the engine never reads it. Name a caption file after a short lowercase tag, like " +
                $"{CaptionFile.PathFor("en")} or {CaptionFile.PathFor("pt-br")}.",
                context.SourcePath));
            return;
        }

        CaptionFile file = CaptionFileReader.Read(source);
        ReportProblems(context, file);
        ReportSounds(context, file, language);
        ReportCoverage(context, file, language);
    }

    private static void ReportProblems(IRuleContext context, CaptionFile file)
    {
        foreach (CaptionFileProblem problem in file.Problems)
        {
            context.Report(CookDiagnostic.Warning(
                problem.Kind == CaptionFileProblemKind.Repeated
                    ? CookDiagnosticCodes.CaptionSoundRepeated
                    : CookDiagnosticCodes.CaptionLineUnreadable,
                string.Concat(problem.Message[..1].ToUpperInvariant(), problem.Message.AsSpan(1), "."),
                context.SourcePath,
                problem.Line));
        }
    }

    private static void ReportSounds(IRuleContext context, CaptionFile file, string language)
    {
        var content = new RuleContentSource(context);

        foreach (CaptionFileEntry entry in file.Entries)
        {
            if (!content.Exists(AudioContentPath.Resolve(content, entry.Sound)))
            {
                context.Report(CookDiagnostic.Warning(
                    CookDiagnosticCodes.CaptionSoundMissing,
                    $"'{entry.Sound}' has a caption here and no such sound is in the project.",
                    context.SourcePath,
                    entry.Line));
            }

            string subtitles = SubtitlePath.For(entry.Sound, language);
            if (context.Probe(subtitles))
            {
                context.Report(CookDiagnostic.Warning(
                    CookDiagnosticCodes.CaptionHiddenBySubtitles,
                    $"'{entry.Sound}' has a caption here and subtitles in '{subtitles}'. The subtitles are " +
                    "shown and this line is not.",
                    context.SourcePath,
                    entry.Line));
            }
        }
    }

    // Says how this language's caption file stands against the project's.
    // SubtitleCoverage does the same for the subtitle files.
    private void ReportCoverage(IRuleContext context, CaptionFile file, string language)
    {
        if (string.Equals(language, _projectLanguage, StringComparison.Ordinal))
        {
            Summarize(context, $"'{language}' is the project's language. {Sounds(file.Entries.Count)} a caption here.");
            return;
        }

        string projectPath = CaptionFile.PathFor(_projectLanguage);
        if (!context.Probe(projectPath))
        {
            Summarize(
                context,
                $"The project's language is '{_projectLanguage}' and there is no '{projectPath}', so " +
                $"'{language}' has nothing to be compared with.");
            return;
        }

        CaptionFile project = CaptionFileReader.Read(context.Read(projectPath));
        var missing = new List<string>();
        foreach (CaptionFileEntry entry in project.Entries)
        {
            if (!file.TryGetText(entry.Sound, out _) && !context.Probe(SubtitlePath.For(entry.Sound, language)))
                missing.Add(entry.Sound);
        }

        if (missing.Count == 0)
        {
            Summarize(
                context,
                $"'{language}' has a caption for every sound that has one in '{_projectLanguage}', the " +
                $"project's language ({project.Entries.Count} of {project.Entries.Count}).");
            return;
        }

        string captioned = project.Entries.Count == 1 ? "sound that has" : "sounds that have";
        string shown = missing.Count == 1 ? "It shows" : "They show";

        context.Report(CookDiagnostic.Warning(
            CookDiagnosticCodes.CaptionLanguageIncomplete,
            $"'{language}' has no caption for {missing.Count} of the {project.Entries.Count} {captioned} " +
            $"one in '{_projectLanguage}', the project's language: {Name(missing)}. {shown} in " +
            $"'{_projectLanguage}'.",
            context.SourcePath));
    }

    private static void Summarize(IRuleContext context, string message) =>
        context.Report(CookDiagnostic.Info(CookDiagnosticCodes.CaptionLanguageSummary, message, context.SourcePath));

    private static string Sounds(int count) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? "sound has" : "sounds have")}");

    private static string Name(List<string> sounds)
    {
        var names = new StringBuilder();
        for (int i = 0; i < Math.Min(sounds.Count, MaxNamed); i++)
        {
            if (i > 0) names.Append(", ");
            names.Append(sounds[i]);
        }

        if (sounds.Count > MaxNamed)
            names.Append(CultureInfo.InvariantCulture, $" and {sounds.Count - MaxNamed} more");

        return names.ToString();
    }
}
