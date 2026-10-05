using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Projects;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Audio.Captions;

/// <summary>The console commands for captions.</summary>
public static class CaptionConsoleCommands
{
    /// <summary>The command that sets which captions show.</summary>
    public const string Mode = "captions";

    /// <summary>The command that sets the language captions are looked up in.</summary>
    public const string Language = "caption_language";

    /// <summary>The command that lists the level's sounds that have no caption.</summary>
    public const string Missing = "captions_missing";

    /// <summary>The most sounds <c>captions_missing</c> lists.</summary>
    public const int MaxListed = 200;

    /// <summary>Adds the commands to <paramref name="table"/>.</summary>
    /// <param name="table">The console's commands.</param>
    /// <param name="feed">The captions the commands set and ask about.</param>
    public static void Register(ConCommandTable table, CaptionFeed feed)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(feed);

        table.Add(new ConCommand(
            Mode,
            $"{Mode} [off | voice | all]",
            "Sets which captions show: none, speech only, or speech and other sounds. " +
            "Alone it prints the setting.",
            (in ConArgs args) => SetMode(feed, in args)));
        table.Add(new ConCommand(
            Language,
            $"{Language} [language]",
            "Sets the language captions are looked up in, such as en or de. Alone it prints the language.",
            (in ConArgs args) => SetLanguage(feed, in args)));
        table.Add(new ConCommand(
            Missing,
            Missing,
            "Lists the sounds the level's entities play that have no caption in the caption language.",
            (in ConArgs args) => ListMissing(feed, in args)));
    }

    private static void SetMode(CaptionFeed feed, in ConArgs args)
    {
        if (args.Count > 1)
        {
            args.Out.Error($"{Mode}: give one of off, voice or all.");
            return;
        }

        if (args.Count == 1)
        {
            if (!TryReadMode(args[0], out CaptionMode mode))
            {
                args.Out.Error($"{Mode}: '{args[0]}' is not a setting. Give off, voice or all.");
                return;
            }

            feed.Mode = mode;
        }

        args.Out.Print(feed.Mode switch
        {
            CaptionMode.Off => $"{Mode}: off. No captions show.",
            CaptionMode.Voice => $"{Mode}: voice. Speech shows. Other sounds do not.",
            _ => $"{Mode}: all. Speech and other sounds show.",
        });
    }

    private static bool TryReadMode(ReadOnlySpan<char> word, out CaptionMode mode)
    {
        mode = CaptionMode.Off;
        if (word.Equals("off", StringComparison.OrdinalIgnoreCase))
            return true;

        mode = CaptionMode.Voice;
        if (word.Equals("voice", StringComparison.OrdinalIgnoreCase))
            return true;

        mode = CaptionMode.All;
        return word.Equals("all", StringComparison.OrdinalIgnoreCase);
    }

    private static void SetLanguage(CaptionFeed feed, in ConArgs args)
    {
        if (args.Count > 1)
        {
            args.Out.Error($"{Language}: give one language, such as en or de.");
            return;
        }

        if (args.Count == 1)
        {
            if (!LanguageTag.TryNormalize(args[0], out string? tag))
            {
                args.Out.Error(
                    $"{Language}: '{args[0]}' is not a language. Give a short tag such as en, de or pt-br.");
                return;
            }

            feed.Language = tag;
        }

        string language = feed.Language;
        string project = feed.Library.ProjectLanguage;

        args.Out.Print(string.Equals(language, project, StringComparison.Ordinal)
            ? $"{Language}: {language}, the project's language."
            : $"{Language}: {language}. A sound with no caption in {language} gets the one in {project}, " +
              "the project's language.");

        if (!feed.Library.HasCaptionFile(language))
            args.Out.Warn($"{Language}: the project has no {CaptionFile.PathFor(language)}.");
    }

    private static void ListMissing(CaptionFeed feed, in ConArgs args)
    {
        if (args.Count > 0)
        {
            args.Out.Error($"{Missing}: it takes nothing after its name. {Language} sets the language it checks.");
            return;
        }

        bool isRunning = args.Entities is { IsActive: true };
        if ((isRunning ? args.Entities?.Scene : args.Scene) is not { } scene)
        {
            args.Out.Error($"{Missing}: no level is open.");
            return;
        }

        if (scene.EntitySchemas is not { } schemas)
        {
            args.Out.Error($"{Missing}: the level's entity classes are not known, so neither are its sounds.");
            return;
        }

        // A running level's captions come from the files as they were when it
        // started. Otherwise read them again, so an edit shows here.
        if (!isRunning)
            feed.Library.Reload();

        var sounds = new List<string>();
        LevelSounds.Collect(scene, schemas, sounds);

        var gaps = new List<string>();
        foreach (string sound in sounds)
        {
            if (DescribeGap(feed, sound) is { } gap)
                gaps.Add(gap);
        }

        PrintGaps(feed.Language, sounds.Count, gaps, args.Out);
    }

    // Null for a sound that has a caption in the language.
    private static string? DescribeGap(CaptionFeed feed, string sound)
    {
        string language = feed.Language;
        SoundCaptions? found = feed.Library.Find(sound, language);

        if (found is null)
            return $"{sound}  no caption";

        return string.Equals(found.Language, language, StringComparison.Ordinal)
            ? null
            : $"{sound}  none in {language}, shows the one in {found.Language}";
    }

    private static void PrintGaps(string language, int soundCount, List<string> gaps, IConsoleOutput output)
    {
        string sounds = EntityConsoleText.Count(soundCount, "sound", "sounds");

        if (soundCount == 0)
        {
            output.Print($"{Missing}: no entity in the level is set to play a sound.");
            return;
        }

        if (gaps.Count == 0)
        {
            output.Print($"{Missing}: none. Every sound the level plays has a caption in {language} ({sounds}).");
            return;
        }

        output.Print($"{Missing}: no caption in {language} for {gaps.Count} of {sounds} the level plays.");

        int shown = Math.Min(gaps.Count, MaxListed);
        for (int i = 0; i < shown; i++)
            output.Print(gaps[i]);

        if (gaps.Count > shown)
            output.Print($"{Missing}: {gaps.Count - shown} more not shown.");
    }
}
