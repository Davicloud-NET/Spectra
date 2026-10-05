using SpectraEngine.Core.ConsoleSystem;
using System;
using System.Globalization;

namespace SpectraEngine.Core.Audio;

/// <summary>The console commands for sound.</summary>
public static class SoundConsoleCommands
{
    /// <summary>The command that prints what became of the level's sounds.</summary>
    public const string Stats = "sound_stats";

    /// <summary>The command that plays one sound file by itself.</summary>
    public const string Play = "sound_play";

    /// <summary>The command that stops the sound <see cref="Play"/> started.</summary>
    public const string Stop = "sound_stop";

    /// <summary>The command that prints or sets the volume of everything heard.</summary>
    public const string Volume = "sound_volume";

    /// <summary>Adds the commands to <paramref name="table"/>.</summary>
    /// <param name="table">The console's commands.</param>
    /// <param name="presenter">Whose numbers <c>sound_stats</c> prints.</param>
    /// <param name="audio">The device the presenter plays on.</param>
    /// <param name="preview">What <c>sound_play</c> plays through.</param>
    public static void Register(
        ConCommandTable table, SoundPresenter presenter, AudioManager audio, SoundPreview preview)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(preview);

        table.Add(new ConCommand(
            Stats,
            Stats,
            "Prints how many sounds the level is playing, how many have a source on the audio device " +
            "and how many go without.",
            (in ConArgs args) => PrintStats(presenter, audio, preview, in args)));

        table.Add(new ConCommand(
            Play,
            Play + " <path>",
            "Plays one sound file by itself, once from its start. It needs no level running.",
            (in ConArgs args) => PlayPreview(preview, in args)));

        table.Add(new ConCommand(
            Stop,
            Stop,
            "Stops the sound that sound_play started.",
            (in ConArgs args) => StopPreview(preview, in args)));

        table.Add(new ConCommand(
            Volume,
            Volume + " [0..1]",
            "Prints the volume of everything heard, or sets it. 1 is full and 0 is silent.",
            (in ConArgs args) => SetVolume(audio, in args)));
    }

    private static void PrintStats(SoundPresenter presenter, AudioManager audio, SoundPreview preview, in ConArgs args)
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
        }
        else
        {
            SoundStats stats = presenter.Stats;
            args.Out.Print(
                $"{Stats}: {stats.Playing} playing, {stats.WithSource} with a source, " +
                $"{stats.WithoutSource} without, {stats.Silent} silent, {stats.Unplayable} not loaded.");
            args.Out.Print($"{Stats}: {audio.SourceCount} sources, {stats.RefusedStarts} starts refused.");
        }

        args.Out.Print(
            $"{Stats}: preview {(preview.IsPlaying ? preview.Path : "none")}, volume {Number(audio.MasterGain)}.");
    }

    private static void PlayPreview(SoundPreview preview, in ConArgs args)
    {
        if (args.Count == 0)
        {
            args.Out.Error($"{Play}: name a sound file, such as {Play} Sounds/door_open.wav.");
            return;
        }

        string path = args[0].ToString();
        if (preview.Play(path, out string refusal))
            args.Out.Print($"{Play}: playing {preview.Path}.");
        else
            args.Out.Error($"{Play}: {path} was not played: {refusal}");
    }

    private static void StopPreview(SoundPreview preview, in ConArgs args)
    {
        string path = preview.Path;
        args.Out.Print(preview.Stop() ? $"{Stop}: stopped {path}." : $"{Stop}: no preview is playing.");
    }

    private static void SetVolume(AudioManager audio, in ConArgs args)
    {
        if (args.Count == 0)
        {
            args.Out.Print($"{Volume}: {Number(audio.MasterGain)}.");
            return;
        }

        if (!args.TryGetFloat(0, out float wanted))
        {
            args.Out.Error($"{Volume}: '{args[0]}' is not a number from 0 to 1.");
            return;
        }

        audio.MasterGain = Math.Clamp(wanted, 0f, 1f);

        args.Out.Print(wanted is < 0f or > 1f
            ? $"{Volume}: {Number(audio.MasterGain)}. It goes from 0 to 1."
            : $"{Volume}: {Number(audio.MasterGain)}.");
    }

    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
