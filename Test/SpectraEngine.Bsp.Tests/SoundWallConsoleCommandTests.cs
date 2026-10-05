using SpectraEngine.Core.Audio;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Diagnostics;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>The console command that prints what tracing walls cost the sound.</summary>
public sealed class SoundWallConsoleCommandTests
{
    [Fact]
    public void The_command_prints_the_sounds_in_earshot_the_lines_traced_and_the_sounds_that_wait()
    {
        using var rig = new WalledSoundRig();
        var console = ConsoleFor(rig, new FrameProfiler());
        for (int i = 0; i < 30; i++)
        {
            rig.Sound.Play(
                rig.Sound.Place($"near{i}", new Vector3(i * 0.1f, 0, -3)), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        }

        rig.Sound.Play(rig.Sound.Place("far", new Vector3(0, 0, -90)), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Step(2);

        console.Execute(SoundWallConsoleCommands.Walls, new ConsoleFrame(rig.Sound.Scene, rig.Sound.World, IsPlaying: true));

        console.Output.Drain().Select(line => line.Text).ShouldBe(
        [
            "sound_walls: 30 in earshot, 60 of 60 lines traced this frame, 11 waiting for their turn.",
            "sound_walls: not timed. Start with --profile to time it.",
        ]);
    }

    [Fact]
    public void With_the_profiler_on_the_command_prints_the_time_a_frame()
    {
        using var rig = new WalledSoundRig();
        var profiler = new FrameProfiler { Enabled = true };
        rig.Walls.Profiler = profiler;
        var console = ConsoleFor(rig, profiler);
        rig.Sound.Play(rig.Sound.Place("near", new Vector3(0, 0, -3)), SoundPresenterRig.Beep, SoundPresenterRig.Looped);

        profiler.BeginFrame();
        using (profiler.Measure(FramePhase.Audio))
            rig.Step();
        profiler.EndFrame();

        console.Execute(SoundWallConsoleCommands.Walls, new ConsoleFrame(rig.Sound.Scene, rig.Sound.World, IsPlaying: true));

        string[] lines = [.. console.Output.Drain().Select(line => line.Text)];
        lines[0].ShouldBe("sound_walls: 1 in earshot, 5 of 60 lines traced this frame, 0 waiting for their turn.");
        lines[1].ShouldMatch(@"^sound_walls: \d+\.\d{3} ms a frame\.$");
        profiler[FramePhase.SoundWalls].ShouldBeGreaterThan(0d);
    }

    [Fact]
    public void With_no_level_running_the_command_says_so()
    {
        using var rig = new WalledSoundRig();
        var console = ConsoleFor(rig, new FrameProfiler());

        console.Execute(SoundWallConsoleCommands.Walls, new ConsoleFrame(rig.Sound.Scene, null));

        console.Output.Drain().ShouldHaveSingleItem().Text.ShouldBe(
            "sound_walls: the level is not running, so nothing is traced.");
    }

    [Fact]
    public void Help_lists_the_command()
    {
        using var rig = new WalledSoundRig();
        var console = ConsoleFor(rig, new FrameProfiler());

        console.Execute("help", default);

        console.Output.Drain().ShouldContain(line => line.Text.StartsWith(SoundWallConsoleCommands.Walls));
    }

    private static SpectraConsole ConsoleFor(WalledSoundRig rig, FrameProfiler profiler)
    {
        var console = new SpectraConsole();
        SoundWallConsoleCommands.Register(console.Commands, rig.Walls, profiler);
        return console;
    }
}
