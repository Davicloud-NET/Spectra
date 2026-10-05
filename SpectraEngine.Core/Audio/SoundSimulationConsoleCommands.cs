using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.ConsoleSystem;
using System;
using System.Globalization;

namespace SpectraEngine.Core.Audio;

/// <summary>
/// The console commands that switch parts of the sound simulation on and off
/// for every sound, to hear what each part does.
/// </summary>
public static class SoundSimulationConsoleCommands
{
    /// <summary>The command that switches a part on or off for every sound.</summary>
    public const string Simulate = "sound_simulate";

    /// <summary>The command that sets how strong Doppler is.</summary>
    public const string Doppler = "sound_doppler";

    private const string Names = "placed, fades, walls, doppler or all";

    /// <summary>Adds the commands to <paramref name="table"/>.</summary>
    /// <param name="table">The console's commands.</param>
    /// <param name="switches">What the commands set: a presenter's <see cref="SoundPresenter.Simulation"/>.</param>
    public static void Register(ConCommandTable table, SoundSimulationSwitches switches)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(switches);

        table.Add(new ConCommand(
            Simulate,
            $"{Simulate} [placed | fades | walls | doppler | all] [on | off]",
            "Switches a part of the sound simulation on or off for every sound. Alone it prints the four.",
            (in ConArgs args) => SetPart(switches, in args)));
        table.Add(new ConCommand(
            Doppler,
            $"{Doppler} [strength]",
            "Sets how strong Doppler is, from 0 to 4. 1 is real and 0 is none. Alone it prints the strength.",
            (in ConArgs args) => SetDoppler(switches, in args)));
    }

    private static void SetPart(SoundSimulationSwitches switches, in ConArgs args)
    {
        if (args.Count == 2)
        {
            if (!TryReadPart(args[0], out SoundSimulation part))
            {
                args.Out.Error($"{Simulate}: '{args[0]}' is not a part. Give {Names}, then on or off.");
                return;
            }

            if (!TryReadOnOff(args[1], out bool isOn))
            {
                args.Out.Error($"{Simulate}: '{args[1]}' is not on or off. Give {Names}, then on or off.");
                return;
            }

            switches.Set(part, isOn);
        }
        else if (args.Count != 0)
        {
            args.Out.Error($"{Simulate}: give {Names}, then on or off.");
            return;
        }

        SoundSimulation enabled = switches.Enabled;
        args.Out.Print(
            $"{Simulate}: placed {OnOff(enabled, SoundSimulation.Placed)}, " +
            $"fades {OnOff(enabled, SoundSimulation.Fades)}, " +
            $"walls {OnOff(enabled, SoundSimulation.Walls)}, " +
            $"doppler {OnOff(enabled, SoundSimulation.Doppler)}.");
    }

    private static void SetDoppler(SoundSimulationSwitches switches, in ConArgs args)
    {
        if (args.Count > 1)
        {
            args.Out.Error($"{Doppler}: give one number from 0 to {Number(SoundSimulationSwitches.MaxDopplerStrength)}.");
            return;
        }

        if (args.Count == 1)
        {
            if (!args.TryGetFloat(0, out float strength)
                || strength < 0f
                || strength > SoundSimulationSwitches.MaxDopplerStrength)
            {
                args.Out.Error(
                    $"{Doppler}: '{args[0]}' is not a number from 0 to " +
                    $"{Number(SoundSimulationSwitches.MaxDopplerStrength)}.");
                return;
            }

            switches.DopplerStrength = strength;
        }

        args.Out.Print($"{Doppler}: {Number(switches.DopplerStrength)}. 1 is the real shift and 0 is none.");

        if ((switches.Enabled & SoundSimulation.Doppler) == 0)
            args.Out.Print($"{Doppler}: Doppler is switched off. '{Simulate} doppler on' switches it on.");
    }

    private static bool TryReadPart(ReadOnlySpan<char> word, out SoundSimulation part)
    {
        part = SoundSimulation.Placed;
        if (word.Equals("placed", StringComparison.OrdinalIgnoreCase))
            return true;

        part = SoundSimulation.Fades;
        if (word.Equals("fades", StringComparison.OrdinalIgnoreCase))
            return true;

        part = SoundSimulation.Walls;
        if (word.Equals("walls", StringComparison.OrdinalIgnoreCase))
            return true;

        part = SoundSimulation.Doppler;
        if (word.Equals("doppler", StringComparison.OrdinalIgnoreCase))
            return true;

        part = SoundSimulation.All;
        return word.Equals("all", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadOnOff(ReadOnlySpan<char> word, out bool isOn)
    {
        isOn = word.Equals("on", StringComparison.OrdinalIgnoreCase);
        return isOn || word.Equals("off", StringComparison.OrdinalIgnoreCase);
    }

    private static string OnOff(SoundSimulation enabled, SoundSimulation part) =>
        (enabled & part) != 0 ? "on" : "off";

    private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
