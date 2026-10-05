using Spectra.Kitchen.Audio;
using Spectra.Kitchen.Cache;
using Spectra.Kitchen.Diagnostics;
using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Audio.Captions;
using System;
using System.Globalization;
using System.IO;

namespace Spectra.Kitchen.Rules;

/// <summary>
/// Checks a voice file's subtitles against the sound beside them and packs
/// the file as it is: the engine reads the same WebVTT loose and packed.
/// </summary>
public sealed class SubtitleRule : IRule
{
    /// <summary>
    /// How many seconds a cue may run past the end of its sound before the
    /// cook says so.
    /// </summary>
    // Subtitle tools pad a last cue so it can be read, and the engine keeps a
    // line up for its reading time anyway. Past this the file is likely for
    // another recording.
    public const double EndSlackSeconds = 1.0;

    // Must match what AudioRule cooks.
    private static readonly string[] SoundExtensions = [".wav", ".wave"];

    /// <inheritdoc/>
    public RuleKind Kind => RuleKind.Subtitle;

    /// <inheritdoc/>
    public int Version => 1;

    /// <inheritdoc/>
    public CookSettingKeys SettingsRead => CookSettingKeys.None;

    /// <summary>Whether <paramref name="contentPath"/> is a subtitle file.</summary>
    public static bool Handles(string contentPath) => SubtitlePath.IsSubtitle(contentPath);

    /// <inheritdoc/>
    public void Cook(IRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Packed even when it is refused. The diagnostic already fails the cook.
        byte[] source = context.Read(context.SourcePath);
        context.Emit(context.SourcePath, source);

        SubtitleFile file;
        try
        {
            file = SubtitleReader.Read(source, context.SourcePath);
        }
        catch (SubtitleFormatException refusal)
        {
            context.Report(CookDiagnostic.Error(
                CookDiagnosticCodes.SubtitleUnreadable,
                $"The engine cannot read these subtitles, so the sound would have none: {refusal.Reason}.",
                context.SourcePath,
                refusal.Line));
            return;
        }

        ReportUnreadParts(context, file);

        if (!SubtitlePath.TryParse(context.SourcePath, out string? soundStem, out _))
        {
            context.Report(CookDiagnostic.Warning(
                CookDiagnosticCodes.SubtitleNameHasNoLanguage,
                $"'{context.SourcePath}' has no language in its name, so the engine never reads it. Name a " +
                "subtitle file after its sound and a language, like guard_hey.en.vtt for guard_hey.wav.",
                context.SourcePath));
            return;
        }

        ReportSound(context, file, soundStem);
    }

    private static void ReportUnreadParts(IRuleContext context, SubtitleFile file)
    {
        foreach (SubtitleUnreadPart part in file.Unread)
        {
            context.Report(CookDiagnostic.Warning(
                CookDiagnosticCodes.SubtitlePartNotRead,
                $"The engine does not read {part.What}. The words around it are still shown.",
                context.SourcePath,
                part.Line));
        }

        if (file.Lines.Count == 0)
        {
            context.Report(CookDiagnostic.Warning(
                CookDiagnosticCodes.SubtitleHasNoCues,
                $"'{context.SourcePath}' has no cue with words in it, so the sound has no subtitles in this " +
                "language.",
                context.SourcePath));
        }
    }

    private static void ReportSound(IRuleContext context, SubtitleFile file, string soundStem)
    {
        string? sound = null;
        foreach (string extension in SoundExtensions)
        {
            if (context.Probe(soundStem + extension))
            {
                sound = soundStem + extension;
                break;
            }
        }

        if (sound is not null)
        {
            ReportCuesPastTheEnd(context, file, sound);
            return;
        }

        // Cooked by something else and put in the content as it is. Its
        // length is not looked at.
        if (context.Probe(AudioContentPath.CookedPathFor(soundStem + SoundExtensions[0])))
            return;

        context.Report(CookDiagnostic.Warning(
            CookDiagnosticCodes.CaptionSoundMissing,
            $"'{context.SourcePath}' holds the subtitles of '{soundStem}{SoundExtensions[0]}', and no such " +
            "sound is in the project.",
            context.SourcePath));
    }

    private static void ReportCuesPastTheEnd(IRuleContext context, SubtitleFile file, string sound)
    {
        double length;
        try
        {
            length = WaveDecoder.Decode(context.Read(sound), sound).Duration;
        }
        catch (InvalidDataException)
        {
            // The audio rule reports a sound that cannot be decoded.
            return;
        }

        for (int i = 0; i < file.Lines.Count; i++)
        {
            CaptionLine cue = file.Lines[i];
            if (cue.Start >= length)
            {
                context.Report(CookDiagnostic.Warning(
                    CookDiagnosticCodes.SubtitleStartsAfterSound,
                    $"This cue starts at {Seconds(cue.Start)} and '{sound}' is over after {Seconds(length)}, " +
                    "so the line never shows.",
                    context.SourcePath,
                    file.SourceLines[i]));
            }
            else if (cue.End > length + EndSlackSeconds)
            {
                context.Report(CookDiagnostic.Warning(
                    CookDiagnosticCodes.SubtitleRunsPastSound,
                    $"This cue runs until {Seconds(cue.End)} and '{sound}' is over after {Seconds(length)}. " +
                    "The line still shows. Check that these subtitles are for this recording.",
                    context.SourcePath,
                    file.SourceLines[i]));
            }
        }
    }

    private static string Seconds(double seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{seconds:0.###} s");
}
