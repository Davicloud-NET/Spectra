using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.ConsoleSystem;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>The console command that prints what became of the level's sounds.</summary>
public sealed class SoundConsoleCommandTests
{
    [Fact]
    public void The_command_prints_how_many_sounds_play_and_how_many_have_a_source()
    {
        using var rig = new SoundPresenterRig(sources: 2);
        var console = ConsoleFor(rig.Presenter, rig.Audio);
        rig.Play(rig.Place("a", new Vector3(0, 0, -1)), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Play(rig.Place("b", new Vector3(0, 0, -2)), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Play(rig.Place("c", new Vector3(0, 0, -6)), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Play(rig.Place("d", new Vector3(0, 0, -90)), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.World.Sounds.Play(
            rig.Scene.Root,
            "Sounds/nothing.wav",
            new SoundDescription(SoundPresenterRig.Rate, SoundPresenterRig.Rate),
            SoundPresenterRig.Looped with { Gain = 2f });
        rig.Frame();

        console.Execute(SoundConsoleCommands.Stats, new ConsoleFrame(rig.Scene, rig.World, IsPlaying: true));

        IReadOnlyList<ConsoleLine> lines = console.Output.Drain();
        lines.Select(line => line.Text).ShouldBe(
        [
            "sound_stats: 5 playing, 2 with a source, 1 without, 1 silent, 1 not loaded.",
            "sound_stats: 2 sources, 0 starts refused.",
        ]);
        lines.ShouldAllBe(line => line.Severity == LogLevel.Information);
    }

    [Fact]
    public void With_no_level_running_the_command_says_so()
    {
        using var rig = new SoundPresenterRig();
        var console = ConsoleFor(rig.Presenter, rig.Audio);

        console.Execute(SoundConsoleCommands.Stats, new ConsoleFrame(rig.Scene, null));

        console.Output.Drain().ShouldHaveSingleItem().Text.ShouldBe(
            "sound_stats: the level is not running, so it plays nothing. 32 sources.");
    }

    [Fact]
    public void With_no_audio_device_the_command_says_why_nothing_is_heard()
    {
        using var rig = new SoundPresenterRig();
        var audio = new AudioManager(new CapturingLogger(), NoDevice);
        audio.Initialize();
        var presenter = new SoundPresenter(audio, rig.Assets, new DirectPropagation(), rig.Log);
        var console = ConsoleFor(presenter, audio);
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        presenter.Update(rig.World, SoundPresenterRig.TickSeconds);

        console.Execute(SoundConsoleCommands.Stats, new ConsoleFrame(rig.Scene, rig.World, IsPlaying: true));

        IReadOnlyList<ConsoleLine> lines = console.Output.Drain();
        lines[0].Severity.ShouldBe(LogLevel.Warning);
        lines[0].Text.ShouldBe("sound_stats: audio is off, so nothing is heard: the test has no audio device.");
        lines[1].Text.ShouldBe("sound_stats: 1 playing, 0 with a source, 1 without, 0 silent, 0 not loaded.");
    }

    [Fact]
    public void Help_lists_the_command()
    {
        using var rig = new SoundPresenterRig();
        var console = ConsoleFor(rig.Presenter, rig.Audio);

        console.Execute("help", default);

        console.Output.Drain().ShouldContain(line => line.Text.StartsWith(SoundConsoleCommands.Stats));
    }

    private static SpectraConsole ConsoleFor(SoundPresenter presenter, AudioManager audio)
    {
        var console = new SpectraConsole();
        SoundConsoleCommands.Register(console.Commands, presenter, audio);
        return console;
    }

    private static bool NoDevice(ILogger logger, [NotNullWhen(true)] out IAudioBackend? backend, out string reason)
    {
        backend = null;
        reason = "the test has no audio device";
        return false;
    }
}
