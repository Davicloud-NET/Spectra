using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.ConsoleSystem;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The console commands that switch parts of the sound simulation for every
/// sound, and set how strong Doppler is.
/// </summary>
public sealed class SoundSimulationConsoleCommandTests
{
    private const string Refusal = "Give placed, fades, walls, doppler or all, then on or off.";

    private readonly SpectraConsole _console = new();
    private readonly SoundSimulationSwitches _switches = new();

    public SoundSimulationConsoleCommandTests() =>
        SoundSimulationConsoleCommands.Register(_console.Commands, _switches);

    [Fact]
    public void Sound_simulate_alone_prints_the_four_as_on_or_off()
    {
        Run("sound_simulate").ShouldBe(["sound_simulate: placed on, fades on, walls on, doppler on."]);

        _switches.Enabled = SoundSimulation.Fades | SoundSimulation.Doppler;

        Run("sound_simulate").ShouldBe(["sound_simulate: placed off, fades on, walls off, doppler on."]);
    }

    [Theory]
    [InlineData("placed", SoundSimulation.Placed, "sound_simulate: placed off, fades on, walls on, doppler on.")]
    [InlineData("fades", SoundSimulation.Fades, "sound_simulate: placed on, fades off, walls on, doppler on.")]
    [InlineData("walls", SoundSimulation.Walls, "sound_simulate: placed on, fades on, walls off, doppler on.")]
    [InlineData("doppler", SoundSimulation.Doppler, "sound_simulate: placed on, fades on, walls on, doppler off.")]
    public void Sound_simulate_switches_one_part_and_leaves_the_others(string name, SoundSimulation part, string reply)
    {
        Run($"sound_simulate {name} off").ShouldBe([reply]);
        _switches.Enabled.ShouldBe(SoundSimulation.All & ~part);

        Run($"sound_simulate {name} on").ShouldBe(["sound_simulate: placed on, fades on, walls on, doppler on."]);
        _switches.Enabled.ShouldBe(SoundSimulation.All);
    }

    [Fact]
    public void Sound_simulate_all_switches_the_four()
    {
        Run("sound_simulate all off").ShouldBe(["sound_simulate: placed off, fades off, walls off, doppler off."]);
        _switches.Enabled.ShouldBe(SoundSimulation.None);

        Run("sound_simulate all on").ShouldBe(["sound_simulate: placed on, fades on, walls on, doppler on."]);
        _switches.Enabled.ShouldBe(SoundSimulation.All);
    }

    [Fact]
    public void Sound_simulate_reads_names_and_words_in_any_case()
    {
        Run("SOUND_SIMULATE Doppler OFF").ShouldBe(["sound_simulate: placed on, fades on, walls on, doppler off."]);
    }

    [Fact]
    public void Switching_a_part_to_what_it_is_changes_nothing()
    {
        Run("sound_simulate walls on").ShouldBe(["sound_simulate: placed on, fades on, walls on, doppler on."]);

        _switches.Enabled.ShouldBe(SoundSimulation.All);
    }

    [Theory]
    [InlineData("sound_simulate echo off", "sound_simulate: 'echo' is not a part. " + Refusal)]
    [InlineData("sound_simulate \"\" on", "sound_simulate: '' is not a part. " + Refusal)]
    [InlineData("sound_simulate walls maybe", "sound_simulate: 'maybe' is not on or off. " + Refusal)]
    [InlineData("sound_simulate walls 0", "sound_simulate: '0' is not on or off. " + Refusal)]
    [InlineData("sound_simulate walls", "sound_simulate: give placed, fades, walls, doppler or all, then on or off.")]
    [InlineData("sound_simulate off", "sound_simulate: give placed, fades, walls, doppler or all, then on or off.")]
    [InlineData(
        "sound_simulate walls fades off", "sound_simulate: give placed, fades, walls, doppler or all, then on or off.")]
    public void Sound_simulate_refuses_a_wrong_name_or_word_with_a_line_that_lists_the_names(
        string line, string refusal)
    {
        ConsoleLine reply = RunLines(line).ShouldHaveSingleItem();

        reply.Severity.ShouldBe(LogLevel.Error);
        reply.Text.ShouldBe(refusal);
        _switches.Enabled.ShouldBe(SoundSimulation.All);
    }

    [Fact]
    public void Sound_doppler_alone_prints_the_strength()
    {
        Run("sound_doppler").ShouldBe(["sound_doppler: 1. 1 is the real shift and 0 is none."]);
    }

    [Theory]
    [InlineData("sound_doppler 0", 0f, "sound_doppler: 0. 1 is the real shift and 0 is none.")]
    [InlineData("sound_doppler 2.5", 2.5f, "sound_doppler: 2.5. 1 is the real shift and 0 is none.")]
    [InlineData("sound_doppler 4", 4f, "sound_doppler: 4. 1 is the real shift and 0 is none.")]
    [InlineData("SOUND_DOPPLER 1e0", 1f, "sound_doppler: 1. 1 is the real shift and 0 is none.")]
    public void Sound_doppler_sets_the_strength_and_says_so(string line, float strength, string reply)
    {
        Run(line).ShouldBe([reply]);

        _switches.DopplerStrength.ShouldBe(strength);
    }

    [Theory]
    [InlineData("sound_doppler loud", "sound_doppler: 'loud' is not a number from 0 to 4.")]
    [InlineData("sound_doppler -1", "sound_doppler: '-1' is not a number from 0 to 4.")]
    [InlineData("sound_doppler 4.01", "sound_doppler: '4.01' is not a number from 0 to 4.")]
    [InlineData("sound_doppler NaN", "sound_doppler: 'NaN' is not a number from 0 to 4.")]
    [InlineData("sound_doppler 1,5", "sound_doppler: '1,5' is not a number from 0 to 4.")]
    [InlineData("sound_doppler 1 2", "sound_doppler: give one number from 0 to 4.")]
    public void Sound_doppler_refuses_what_is_not_a_number_from_0_to_4(string line, string refusal)
    {
        _switches.DopplerStrength = 2f;

        ConsoleLine reply = RunLines(line).ShouldHaveSingleItem();

        reply.Severity.ShouldBe(LogLevel.Error);
        reply.Text.ShouldBe(refusal);
        _switches.DopplerStrength.ShouldBe(2f);
    }

    [Fact]
    public void Sound_doppler_says_when_doppler_is_switched_off()
    {
        Run("sound_simulate doppler off");

        Run("sound_doppler 2").ShouldBe(
        [
            "sound_doppler: 2. 1 is the real shift and 0 is none.",
            "sound_doppler: Doppler is switched off. 'sound_simulate doppler on' switches it on.",
        ]);
    }

    [Fact]
    public void No_reply_speaks_of_saving()
    {
        string[] replies =
        [
            .. Run("sound_simulate"),
            .. Run("sound_simulate all off"),
            .. Run("sound_doppler"),
            .. Run("sound_doppler 3"),
        ];

        replies.ShouldAllBe(reply => !reply.Contains("sav", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Help_lists_the_two_commands()
    {
        string[] help = Run("help");

        help.ShouldContain(line => line.StartsWith("sound_simulate  Switches a part of the sound simulation"));
        help.ShouldContain(line => line.StartsWith("sound_doppler  Sets how strong Doppler is"));
        Run("help sound_simulate")[0].ShouldBe("sound_simulate [placed | fades | walls | doppler | all] [on | off]");
        Run("help sound_doppler")[0].ShouldBe("sound_doppler [strength]");
    }

    [Fact]
    public void The_commands_reach_the_presenter_they_were_registered_for()
    {
        using var rig = new SoundPresenterRig();
        var console = new SpectraConsole();
        SoundSimulationConsoleCommands.Register(console.Commands, rig.Presenter.Simulation);

        console.Execute("sound_simulate fades off; sound_doppler 0.5", new ConsoleFrame(rig.Scene, rig.World));

        rig.Presenter.Simulation.Enabled.ShouldBe(SoundSimulation.All & ~SoundSimulation.Fades);
        rig.Presenter.Simulation.DopplerStrength.ShouldBe(0.5f);
    }

    private string[] Run(string line) => [.. RunLines(line).Select(reply => reply.Text)];

    private IReadOnlyList<ConsoleLine> RunLines(string line)
    {
        _console.Execute(line, new ConsoleFrame(null, null));
        return _console.Output.Drain();
    }
}
