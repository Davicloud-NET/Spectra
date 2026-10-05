using SpectraEngine.Core.ConsoleSystem;
using System;

namespace SpectraEngine.Core.Audio;

/// <summary>The console commands for sound.</summary>
public static class SoundConsoleCommands
{
    /// <summary>The command that prints what became of the level's sounds.</summary>
    public const string Stats = "sound_stats";

    /// <summary>Adds the commands to <paramref name="table"/>.</summary>
    /// <param name="table">The console's commands.</param>
    /// <param name="presenter">Whose numbers <c>sound_stats</c> prints.</param>
    /// <param name="audio">The device the presenter plays on.</param>
    public static void Register(ConCommandTable table, SoundPresenter presenter, AudioManager audio)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(audio);

        table.Add(new ConCommand(
            Stats,
            Stats,
            "Prints how many sounds the level is playing, how many have a source on the audio device " +
            "and how many go without.",
            (in ConArgs args) => PrintStats(presenter, audio, in args)));
    }

    private static void PrintStats(SoundPresenter presenter, AudioManager audio, in ConArgs args)
    {
        if (!audio.IsEnabled)
        {
            args.Out.Warn(audio.DisabledReason.Length == 0
                ? $"{Stats}: audio is off, so nothing is heard."
                : $"{Stats}: audio is off, so nothing is heard: {audio.DisabledReason}.");
        }

        if (args.Entities is null)
        {
            args.Out.Print($"{Stats}: the level is not running, so it plays nothing. {audio.SourceCount} sources.");
            return;
        }

        SoundStats stats = presenter.Stats;
        args.Out.Print(
            $"{Stats}: {stats.Playing} playing, {stats.WithSource} with a source, " +
            $"{stats.WithoutSource} without, {stats.Silent} silent, {stats.Unplayable} not loaded.");
        args.Out.Print($"{Stats}: {audio.SourceCount} sources, {stats.RefusedStarts} starts refused.");
    }
}
