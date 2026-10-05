using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.ConsoleSystem;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The console commands that play one sound by itself, stop it, and set the
/// volume of everything heard.
/// </summary>
public sealed class SoundPreviewConsoleCommandTests
{
    [Fact]
    public void Sound_play_plays_the_file_and_says_so()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        SpectraConsole console = SoundConsoleCommandTests.ConsoleFor(rig, preview: preview);

        console.Execute("sound_play Sounds/beep.wav", new ConsoleFrame(rig.Scene, null));

        ConsoleLine line = console.Output.Drain().ShouldHaveSingleItem();
        line.Text.ShouldBe("sound_play: playing Sounds/beep.wav.");
        line.Severity.ShouldBe(LogLevel.Information);
        preview.Path.ShouldBe(SoundPresenterRig.Beep);
        rig.OnlyVoice().Relative.ShouldBeTrue();
    }

    [Fact]
    public void Sound_play_takes_a_quoted_path_with_a_space_in_it()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        rig.Cook("Sounds/big door.wav", HandBuiltSaudio.Resident(frames: 600));
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        SpectraConsole console = SoundConsoleCommandTests.ConsoleFor(rig, preview: preview);

        console.Execute("sound_play \"Sounds/big door.wav\"", new ConsoleFrame(rig.Scene, null));

        console.Output.Drain().ShouldHaveSingleItem().Text.ShouldBe("sound_play: playing Sounds/big door.wav.");
        preview.Path.ShouldBe("Sounds/big door.wav");
    }

    [Fact]
    public void Sound_play_plays_while_a_level_runs_too()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        SpectraConsole console = SoundConsoleCommandTests.ConsoleFor(rig, preview: preview);
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Frame();

        console.Execute("sound_play Sounds/speech.wav", new ConsoleFrame(rig.Scene, rig.World, IsPlaying: true));
        rig.Frame();

        console.Output.Drain().ShouldHaveSingleItem().Text.ShouldBe("sound_play: playing Sounds/speech.wav.");
        rig.Backend.PlayingSources().Length.ShouldBe(2);
        rig.Stats.WithSource.ShouldBe(1);
    }

    [Fact]
    public void Sound_play_of_a_file_that_does_not_load_is_an_error_with_the_reason()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        SpectraConsole console = SoundConsoleCommandTests.ConsoleFor(rig, preview: preview);

        console.Execute("sound_play Sounds/nothing.wav", new ConsoleFrame(rig.Scene, null));

        ConsoleLine line = console.Output.Drain().ShouldHaveSingleItem();
        line.Severity.ShouldBe(LogLevel.Error);
        line.Text.ShouldStartWith("sound_play: Sounds/nothing.wav was not played: ");
        line.Text.Length.ShouldBeGreaterThan("sound_play: Sounds/nothing.wav was not played: ".Length);
        preview.Path.ShouldBeEmpty();
        rig.Backend.PlayingSources().ShouldBeEmpty();
    }

    [Fact]
    public void Sound_play_with_no_file_asks_for_one()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SpectraConsole console = SoundConsoleCommandTests.ConsoleFor(rig);

        console.Execute("sound_play", new ConsoleFrame(rig.Scene, null));

        ConsoleLine line = console.Output.Drain().ShouldHaveSingleItem();
        line.Severity.ShouldBe(LogLevel.Error);
        line.Text.ShouldBe("sound_play: name a sound file, such as sound_play Sounds/door_open.wav.");
    }

    [Fact]
    public void Sound_stop_stops_the_preview_and_names_it()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        SpectraConsole console = SoundConsoleCommandTests.ConsoleFor(rig, preview: preview);
        preview.Play(SoundPresenterRig.Beep, out _).ShouldBeTrue();

        console.Execute("sound_stop", new ConsoleFrame(rig.Scene, null));

        console.Output.Drain().ShouldHaveSingleItem().Text.ShouldBe("sound_stop: stopped Sounds/beep.wav.");
        preview.Path.ShouldBeEmpty();
        rig.Backend.PlayingSources().ShouldBeEmpty();
    }

    [Fact]
    public void Sound_stop_with_nothing_playing_says_so()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SpectraConsole console = SoundConsoleCommandTests.ConsoleFor(rig);

        console.Execute("sound_stop", new ConsoleFrame(rig.Scene, null));

        console.Output.Drain().ShouldHaveSingleItem().Text.ShouldBe("sound_stop: no preview is playing.");
    }

    [Fact]
    public void Sound_volume_alone_prints_the_volume()
    {
        using var rig = new SoundPresenterRig();
        SpectraConsole console = SoundConsoleCommandTests.ConsoleFor(rig);

        console.Execute("sound_volume", new ConsoleFrame(rig.Scene, null));

        console.Output.Drain().ShouldHaveSingleItem().Text.ShouldBe("sound_volume: 1.");
    }

    [Fact]
    public void Sound_volume_reaches_the_listeners_gain()
    {
        using var rig = new SoundPresenterRig();
        SpectraConsole console = SoundConsoleCommandTests.ConsoleFor(rig);

        console.Execute("sound_volume 0.25", new ConsoleFrame(rig.Scene, null));

        console.Output.Drain().ShouldHaveSingleItem().Text.ShouldBe("sound_volume: 0.25.");
        rig.Backend.ListenerGain.ShouldBe(0.25f);
        rig.Audio.MasterGain.ShouldBe(0.25f);
    }

    [Theory]
    [InlineData("3", 1f, "sound_volume: 1. It goes from 0 to 1.")]
    [InlineData("-2", 0f, "sound_volume: 0. It goes from 0 to 1.")]
    public void Sound_volume_keeps_within_nothing_and_full(string typed, float gain, string reply)
    {
        using var rig = new SoundPresenterRig();
        SpectraConsole console = SoundConsoleCommandTests.ConsoleFor(rig);
        rig.Audio.MasterGain = 0.5f;

        console.Execute("sound_volume " + typed, new ConsoleFrame(rig.Scene, null));

        console.Output.Drain().ShouldHaveSingleItem().Text.ShouldBe(reply);
        rig.Backend.ListenerGain.ShouldBe(gain);
    }

    [Theory]
    [InlineData("loud")]
    [InlineData("NaN")]
    [InlineData("0,5")]
    public void Sound_volume_refuses_what_is_not_a_number(string typed)
    {
        using var rig = new SoundPresenterRig();
        SpectraConsole console = SoundConsoleCommandTests.ConsoleFor(rig);
        rig.Audio.MasterGain = 0.5f;

        console.Execute("sound_volume " + typed, new ConsoleFrame(rig.Scene, null));

        ConsoleLine line = console.Output.Drain().ShouldHaveSingleItem();
        line.Severity.ShouldBe(LogLevel.Error);
        line.Text.ShouldBe($"sound_volume: '{typed}' is not a number from 0 to 1.");
        rig.Backend.ListenerGain.ShouldBe(0.5f);
    }

    [Fact]
    public void A_volume_set_before_a_level_plays_holds_for_its_sounds_and_a_preview()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        SpectraConsole console = SoundConsoleCommandTests.ConsoleFor(rig, preview: preview);

        console.Execute("sound_volume 0", new ConsoleFrame(rig.Scene, null));
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Frame();
        preview.Play(SoundPresenterRig.Speech, out _).ShouldBeTrue();
        rig.Frame();

        // The listener scales every source, so the sources keep their own gain.
        rig.Backend.ListenerGain.ShouldBe(0f);
        rig.Backend.PlayingSources().Length.ShouldBe(2);
    }
}
