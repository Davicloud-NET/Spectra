using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Diagnostics;
using System;
using System.Globalization;

namespace SpectraEngine.Core.Audio;

/// <summary>The console command for what walls cost the sound.</summary>
public static class SoundWallConsoleCommands
{
    /// <summary>The command that prints how many lines were traced for the level's sounds.</summary>
    public const string Walls = "sound_walls";

    /// <summary>Adds the command to <paramref name="table"/>.</summary>
    /// <param name="table">The console's commands.</param>
    /// <param name="walls">Whose numbers <c>sound_walls</c> prints.</param>
    /// <param name="audio">The device the sounds play on. Without one nothing is traced.</param>
    /// <param name="profiler">Where the time under <see cref="FramePhase.SoundWalls"/> is read from.</param>
    public static void Register(
        ConCommandTable table, WallPropagation walls, AudioManager audio, FrameProfiler profiler)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(walls);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(profiler);

        table.Add(new ConCommand(
            Walls,
            Walls,
            "Prints how many lines were traced this frame to find what stands between the sounds and " +
            "the listener, how many sounds wait for their turn and the time it takes.",
            (in ConArgs args) => Print(walls, audio, profiler, in args)));
    }

    private static void Print(WallPropagation walls, AudioManager audio, FrameProfiler profiler, in ConArgs args)
    {
        // sound_stats says why it is off.
        if (!audio.IsEnabled)
        {
            args.Out.Warn($"{Walls}: audio is off, so nothing is traced. Captions go by distance alone.");
            return;
        }

        if (args.Entities is null)
        {
            args.Out.Print($"{Walls}: the level is not running, so nothing is traced.");
            return;
        }

        WallPropagationStats stats = walls.Stats;
        args.Out.Print(
            $"{Walls}: {stats.Sounds} in earshot, {stats.Traces} of {walls.Settings.TracesPerFrame} " +
            $"lines traced this frame, {stats.Waiting} waiting for their turn.");

        if (!profiler.Enabled)
        {
            args.Out.Print($"{Walls}: not timed. Start with --profile to time it.");
            return;
        }

        string milliseconds = profiler[FramePhase.SoundWalls].ToString("0.000", CultureInfo.InvariantCulture);
        args.Out.Print($"{Walls}: {milliseconds} ms a frame.");
    }
}
