using SpectraEngine.Core.Hosting;
using SpectraEngine.Editor.Shell;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The play buttons beside sound files: their state is what the engine's
/// snapshots say, and a press only asks.
/// </summary>
public sealed class SoundPreviewModelTests
{
    private const string Open = "Sounds/door_open.wav";
    private const string Close = "Sounds/door_close.wav";

    private static FrameSnapshot Previewing(string path) => new() { PreviewingSound = path };

    [Fact]
    public void With_no_snapshot_no_file_shows_as_playing()
    {
        var shell = new ShellModel();

        shell.SoundPreview.Playing.ShouldBeEmpty();
        shell.SoundPreview.IsPlaying(Open).ShouldBeFalse();
    }

    [Fact]
    public void A_file_shows_stop_once_a_snapshot_says_it_plays()
    {
        var shell = new ShellModel();

        shell.ApplySnapshot(Previewing(Open));

        shell.SoundPreview.IsPlaying(Open).ShouldBeTrue();
        shell.SoundPreview.IsPlaying(Close).ShouldBeFalse();
    }

    [Fact]
    public void A_file_goes_back_to_play_when_a_snapshot_says_nothing_plays()
    {
        var shell = new ShellModel();
        shell.ApplySnapshot(Previewing(Open));

        shell.ApplySnapshot(Previewing(string.Empty));

        shell.SoundPreview.IsPlaying(Open).ShouldBeFalse();
        shell.SoundPreview.Playing.ShouldBeEmpty();
    }

    [Fact]
    public void Another_file_takes_the_stop_button_over()
    {
        var shell = new ShellModel();
        shell.ApplySnapshot(Previewing(Open));

        shell.ApplySnapshot(Previewing(Close));

        shell.SoundPreview.IsPlaying(Open).ShouldBeFalse();
        shell.SoundPreview.IsPlaying(Close).ShouldBeTrue();
    }

    [Fact]
    public void The_session_ending_leaves_no_file_showing_stop()
    {
        var shell = new ShellModel();
        shell.ApplySnapshot(Previewing(Open));

        shell.SoundPreview.EndSession();

        shell.SoundPreview.IsPlaying(Open).ShouldBeFalse();
    }

    [Fact]
    public void The_empty_snapshot_a_closed_session_leaves_shows_no_file_as_playing()
    {
        var shell = new ShellModel();
        shell.ApplySnapshot(Previewing(Open));

        shell.ApplySnapshot(FrameSnapshot.Empty);

        shell.SoundPreview.IsPlaying(Open).ShouldBeFalse();
    }

    [Fact]
    public void A_press_asks_for_the_file_and_shows_nothing_until_the_engine_answers()
    {
        var preview = new SoundPreviewModel();
        var asked = new List<string>();
        preview.Send = Taking(asked);

        preview.Press(Open);

        asked.ShouldBe([Open]);
        preview.IsPlaying(Open).ShouldBeFalse("a file that fails to load must not show stop");
    }

    [Fact]
    public void A_press_on_the_file_that_plays_asks_for_a_stop()
    {
        var preview = new SoundPreviewModel();
        var asked = new List<string>();
        preview.Send = Taking(asked);
        preview.Apply(Open);

        preview.Press(Open);

        asked.ShouldBe([string.Empty]);
        preview.IsPlaying(Open).ShouldBeTrue("it shows stop until the engine says it stopped");
    }

    [Fact]
    public void A_press_on_another_file_asks_for_that_file()
    {
        var preview = new SoundPreviewModel();
        var asked = new List<string>();
        preview.Send = Taking(asked);
        preview.Apply(Open);

        preview.Press(Close);

        asked.ShouldBe([Close]);
    }

    [Fact]
    public void A_press_with_no_file_asks_for_nothing()
    {
        var preview = new SoundPreviewModel();
        var asked = new List<string>();
        preview.Send = Taking(asked);

        preview.Press(string.Empty);
        preview.Press(null);

        asked.ShouldBeEmpty();
    }

    [Fact]
    public void A_path_matches_whatever_its_case()
    {
        var preview = new SoundPreviewModel();

        preview.Apply("sounds/DOOR_OPEN.wav");

        preview.IsPlaying(Open).ShouldBeTrue();
    }

    [Fact]
    public void The_buttons_are_told_only_when_what_plays_changes()
    {
        var preview = new SoundPreviewModel();
        int changes = 0;
        preview.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(SoundPreviewModel.Playing))
                changes++;
        };

        preview.Apply(Open);
        preview.Apply(Open);
        preview.Apply(Open);
        preview.Apply(string.Empty);

        changes.ShouldBe(2);
    }

    [Fact]
    public void A_button_that_asks_as_it_is_told_gets_the_new_answer()
    {
        var preview = new SoundPreviewModel();
        var answers = new List<bool>();
        preview.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(SoundPreviewModel.Playing))
                answers.Add(preview.IsPlaying(Open));
        };

        preview.Apply(Open);
        preview.Apply(Close);

        answers.ShouldBe([true, false]);
    }

    [Theory]
    [InlineData("Sounds/door_open")]
    [InlineData("Sounds/door_open.saudio")]
    [InlineData("Sounds/door_open.wave")]
    [InlineData(@"Sounds\door_open.wav")]
    [InlineData("/Sounds/door_open.wav")]
    [InlineData("Sounds//door_open.wav")]
    public void A_file_plays_under_any_spelling_of_its_path(string spelling)
    {
        var preview = new SoundPreviewModel();

        // What the console was given.
        preview.Apply(spelling);
        preview.IsPlaying(Open).ShouldBeTrue();

        // What a level stores.
        preview.Apply(Open);
        preview.IsPlaying(spelling).ShouldBeTrue();
    }

    [Fact]
    public void A_press_on_a_row_that_spells_the_playing_file_another_way_asks_for_a_stop()
    {
        var preview = new SoundPreviewModel();
        var asked = new List<string>();
        preview.Send = Taking(asked);
        preview.Apply(Open);

        preview.Press(@"\Sounds\door_open.wav");

        asked.ShouldBe([string.Empty]);
    }

    [Theory]
    [InlineData("Music/door_open.wav")]
    [InlineData("Sounds/door_open_far.wav")]
    [InlineData("../door_open.wav")]
    [InlineData("C:/Sounds/door_open.wav")]
    public void Another_file_is_not_the_one_playing(string other)
    {
        var preview = new SoundPreviewModel();

        preview.Apply(Open);

        preview.IsPlaying(other).ShouldBeFalse();
    }

    [Fact]
    public void A_press_with_no_engine_to_ask_is_reported_and_not_lost()
    {
        var preview = new SoundPreviewModel();
        int notSent = 0;
        preview.NotSent += () => notSent++;

        preview.Press(Open);
        notSent.ShouldBe(1, "nothing takes requests yet");

        preview.Send = _ => false;
        preview.Press(Open);
        notSent.ShouldBe(2, "the viewport is stopped");

        preview.Send = _ => true;
        preview.Press(Open);
        notSent.ShouldBe(2);
    }

    [Fact]
    public void Stop_asks_for_a_stop_whichever_file_plays()
    {
        var preview = new SoundPreviewModel();
        var asked = new List<string>();
        preview.Send = Taking(asked);

        preview.Stop();
        asked.ShouldBeEmpty("nothing plays");

        preview.Apply(Close);
        preview.Stop();

        asked.ShouldBe([string.Empty]);
    }

    [Fact]
    public void What_plays_is_named_by_its_file_alone()
    {
        var preview = new SoundPreviewModel();
        var raised = new List<string?>();
        preview.PropertyChanged += (_, change) => raised.Add(change.PropertyName);
        preview.HasPlaying.ShouldBeFalse();
        preview.PlayingName.ShouldBeEmpty();

        preview.Apply("Sounds/ambience/wind.wav");

        preview.HasPlaying.ShouldBeTrue();
        preview.PlayingName.ShouldBe("wind.wav");
        raised.ShouldBe(
            [nameof(SoundPreviewModel.Playing), nameof(SoundPreviewModel.PlayingName), nameof(SoundPreviewModel.HasPlaying)]);
    }

    // An engine that takes every request.
    private static Func<string, bool> Taking(List<string> asked) => path =>
    {
        asked.Add(path);
        return true;
    };
}
