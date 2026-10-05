using SpectraEngine.Core.Audio.Captions;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A voice line shows when playback reaches its start, never before, and
/// goes at its end once it has been read.
/// </summary>
public sealed class CaptionFeedVoiceTests
{
    // Two seconds long.
    private const string Speech = SoundPresenterRig.Speech;

    // One second long.
    private const string Beep = SoundPresenterRig.Beep;

    private const string Announcement = "Sounds/announcement.wav";

    private const string EarlyAndLate = "WEBVTT\n\n00:00.000 --> 00:00.300\nOne\n\n00:00.700 --> 00:00.900\nTwo";

    private const string TwoLines =
        """
        WEBVTT

        00:00.000 --> 00:00.800
        <v Guard>Hey! You there!

        00:01.000 --> 00:01.800
        <v Guard>Stop right where you are.
        """;

    [Fact]
    public void A_line_shows_when_playback_reaches_its_start_and_never_before()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", TwoLines);
        rig.Play(Speech);

        rig.Step();
        string[] atTheStart = Texts(rig);
        rig.Step(58);
        string[] justBefore = Texts(rig);
        rig.Step();

        atTheStart.ShouldBe(["Hey! You there!"]);
        justBefore.ShouldBe(["Hey! You there!"]);
        rig.Shown.Select(caption => caption.Text).ShouldBe(["Hey! You there!", "Stop right where you are."]);
        rig.Shown[1].StartedAt.ShouldBe(rig.Feed.Now);
        rig.Feed.Now.ShouldBe(1.0, 1e-6);
    }

    [Fact]
    public void A_line_is_speech_and_names_its_speaker()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", TwoLines);
        rig.Play(Speech);

        rig.Step();

        Caption line = rig.Shown.ShouldHaveSingleItem();
        line.Kind.ShouldBe(CaptionKind.Voice);
        line.Speaker.ShouldBe("Guard");
        line.Position.ShouldBe(CaptionFeedRig.Near);
        line.Audibility.ShouldBe(1f);
    }

    [Fact]
    public void A_line_goes_at_its_end_once_it_has_been_read()
    {
        // A word read in a second, said for a second and a half.
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", "WEBVTT\n\n00:00.000 --> 00:01.500\nHi");
        rig.Play(Speech);

        rig.RunTo(1.45);
        string[] whileSaid = Texts(rig);
        rig.RunTo(1.55);

        whileSaid.ShouldBe(["Hi"]);
        rig.Shown.ShouldBeEmpty();
        rig.Sound.Stats.WithSource.ShouldBe(1);
    }

    [Fact]
    public void A_line_that_is_over_before_it_can_be_read_stays_until_it_has_been()
    {
        const string Words = "Stop right where you are.";
        double reading = CaptionTiming.ReadingSeconds(Words);
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", $"WEBVTT\n\n00:00.000 --> 00:00.300\n{Words}");
        rig.Play(Speech);

        rig.RunTo(reading - 0.05);
        string[] afterItsEnd = Texts(rig);
        rig.RunTo(reading + 0.1);

        reading.ShouldBeGreaterThan(1.5);
        afterItsEnd.ShouldBe([Words]);
        rig.Shown.ShouldBeEmpty();
    }

    [Fact]
    public void A_sound_that_comes_into_hearing_in_the_middle_of_a_line_shows_that_line_only()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", TwoLines);
        rig.Play(Speech, CaptionFeedRig.OutOfHearing);
        rig.RunTo(1.2);
        rig.Shown.ShouldBeEmpty();

        rig.Sound.Listen(CaptionFeedRig.OutOfHearing + Vector3.UnitZ);
        rig.Step();

        Texts(rig).ShouldBe(["Stop right where you are."]);
    }

    [Fact]
    public void A_sound_that_comes_into_hearing_between_two_lines_shows_neither()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", TwoLines);
        rig.Play(Speech, CaptionFeedRig.OutOfHearing);
        rig.RunTo(0.9);

        rig.Sound.Listen(CaptionFeedRig.OutOfHearing + Vector3.UnitZ);
        rig.Step();
        string[] between = Texts(rig);
        rig.RunTo(1.0);

        between.ShouldBeEmpty();
        Texts(rig).ShouldBe(["Stop right where you are."]);
    }

    [Fact]
    public void A_long_frame_that_passes_a_short_line_whole_still_shows_it()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", "WEBVTT\n\n00:00.500 --> 00:00.600\nDuck!");
        rig.Play(Speech);
        rig.RunTo(0.4);
        rig.Shown.ShouldBeEmpty();

        // Eighteen ticks in one frame: from 0.4 to 0.7 seconds.
        rig.Sound.Tick(18);
        rig.Sound.Frame();

        Texts(rig).ShouldBe(["Duck!"]);
    }

    [Fact]
    public void A_voice_that_played_on_through_a_stalled_frame_shows_the_line_it_reached()
    {
        // One second on one buffer: the device plays it to the end by itself.
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Beep, "en", EarlyAndLate);
        rig.Play(Beep);
        rig.RunTo(0.2);
        string[] before = Texts(rig);

        rig.Stall(0.6f);

        before.ShouldBe(["One"]);
        Texts(rig).ShouldBe(["One", "Two"]);
        rig.Written.ShouldBe(["Caption: One", "Caption: Two"]);
    }

    [Fact]
    public void A_line_a_stalled_frame_brought_forward_does_not_show_again_when_the_ticks_reach_it()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Beep, "en", EarlyAndLate);
        rig.Play(Beep);
        rig.RunTo(0.2);
        rig.Stall(0.6f);

        rig.RunTo(0.95);

        rig.Feed.LastId.ShouldBe(2L);
        rig.Written.Count.ShouldBe(2);
    }

    [Fact]
    public void A_voice_the_device_played_out_in_a_stalled_frame_still_shows_the_line_it_said()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Beep, "en", EarlyAndLate);
        rig.Play(Beep);
        rig.RunTo(0.2);

        // The frame hangs for two seconds, and the device plays the sound out meanwhile.
        rig.Sound.Backend.Finish(rig.Sound.Backend.PlayingSources().ShouldHaveSingleItem());
        rig.Stall(2f);

        Texts(rig).ShouldBe(["One", "Two"]);
        rig.Shown[1].Audibility.ShouldBe(0f);
        rig.Shown[1].Position.ShouldBe(CaptionFeedRig.Near);
        rig.Sound.Stats.WithSource.ShouldBe(0);
    }

    [Fact]
    public void A_voice_that_loses_its_source_with_no_stalled_frame_shows_no_line_it_did_not_reach()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Beep, "en", EarlyAndLate);
        rig.Play(Beep);
        rig.RunTo(0.2);

        rig.Sound.Backend.Finish(rig.Sound.Backend.PlayingSources().ShouldHaveSingleItem());
        rig.RunTo(0.95);

        rig.Written.ShouldBe(["Caption: One"]);
    }

    [Fact]
    public void A_voice_that_loses_its_source_after_a_stalled_frame_shows_no_line_it_did_not_reach()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Beep, "en", EarlyAndLate);
        rig.Play(Beep);
        rig.RunTo(0.1);
        rig.Stall(0.2f);

        // Another sound takes the source a third of a second in.
        rig.Sound.Backend.Finish(rig.Sound.Backend.PlayingSources().ShouldHaveSingleItem());
        rig.RunTo(0.95);

        rig.Written.ShouldBe(["Caption: One"]);
    }

    [Fact]
    public void A_stream_runs_dry_in_a_stalled_frame_so_its_lines_keep_to_the_ticks()
    {
        // Two seconds, played as a stream.
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", EarlyAndLate);
        rig.Play(Speech);
        rig.RunTo(0.2);

        rig.Stall(0.6f);
        string[] afterTheStall = Texts(rig);
        rig.RunTo(0.75);

        afterTheStall.ShouldBe(["One"]);
        Texts(rig).ShouldBe(["One", "Two"]);
    }

    [Fact]
    public void A_line_plays_sooner_when_its_sound_plays_faster()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", TwoLines);
        rig.Play(Speech, rig.Place(CaptionFeedRig.Near), SoundPresenterRig.Once with { Pitch = 2f });

        rig.RunTo(0.48);
        string[] before = Texts(rig);
        rig.RunTo(0.52);

        before.ShouldBe(["Hey! You there!"]);
        Texts(rig).ShouldBe(["Hey! You there!", "Stop right where you are."]);
    }

    [Fact]
    public void A_looped_voice_shows_its_lines_again_on_every_pass()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", "WEBVTT\n\n00:00.100 --> 00:00.500\nHi");
        rig.Play(Speech, CaptionFeedRig.Near, looped: true);
        rig.RunTo(0.2);
        long first = rig.Shown.ShouldHaveSingleItem().Id;
        rig.RunTo(1.9);
        rig.Shown.ShouldBeEmpty();

        rig.RunTo(2.2);

        Caption again = rig.Shown.ShouldHaveSingleItem();
        again.Text.ShouldBe("Hi");
        again.Id.ShouldBeGreaterThan(first);
    }

    [Fact]
    public void A_voice_that_loops_part_of_its_file_does_not_show_the_lines_before_that_part_again()
    {
        using var rig = RigWithAnnouncement();
        rig.Play(Announcement, CaptionFeedRig.Near, looped: true);

        // Four seconds, then the last two of them twice more.
        rig.RunTo(7);

        rig.Written.ShouldBe(
        [
            "Caption: Attention.",
            "Caption: Mind the gap.",
            "Caption: Stand clear.",
            "Caption: Mind the gap.",
            "Caption: Stand clear.",
            "Caption: Mind the gap.",
        ]);
    }

    [Fact]
    public void A_long_frame_in_which_a_loop_turns_round_shows_the_lines_on_both_sides_of_the_turn()
    {
        using var rig = RigWithAnnouncement();
        rig.Play(Announcement, CaptionFeedRig.Near, looped: true);
        rig.RunTo(3.8);
        int before = rig.Written.Count;

        // Half a second in one frame: on from 3.8 seconds, round at 4, to 2.3.
        rig.Sound.Tick(29);
        rig.Step();

        before.ShouldBe(2);
        rig.Written.Skip(before).ShouldBe(["Caption: Mind the gap.", "Caption: Stand clear."], ignoreOrder: true);
    }

    [Fact]
    public void A_voice_that_is_stopped_in_the_middle_of_a_line_keeps_the_line_up_until_it_has_been_read()
    {
        const string Words = "Stop right where you are.";
        double reading = CaptionTiming.ReadingSeconds(Words);
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", $"WEBVTT\n\n00:00.000 --> 00:01.900\n{Words}");
        int playing = rig.Play(Speech);
        rig.RunTo(0.5);

        rig.Sound.World.Sounds.Stop(playing);
        rig.RunTo(reading - 0.05);
        Caption afterTheStop = rig.Shown.ShouldHaveSingleItem();
        rig.RunTo(reading + 0.05);

        afterTheStop.Text.ShouldBe(Words);
        afterTheStop.Audibility.ShouldBe(0f);
        rig.Shown.ShouldBeEmpty();
    }

    [Fact]
    public void A_voice_that_is_played_again_in_the_middle_of_a_line_refreshes_the_line_and_adds_none()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", TwoLines);
        SceneNode guard = rig.Place(CaptionFeedRig.Near);
        int first = rig.Play(Speech, guard, SoundPresenterRig.Once);
        rig.RunTo(0.5);
        Caption before = rig.Shown.ShouldHaveSingleItem();

        rig.Sound.World.Sounds.Stop(first);
        rig.Play(Speech, guard, SoundPresenterRig.Once);
        rig.Step();

        Caption after = rig.Shown.ShouldHaveSingleItem();
        after.Id.ShouldBe(before.Id);
        after.Text.ShouldBe("Hey! You there!");
        after.EarliestEnd.ShouldBeGreaterThan(before.EarliestEnd);
        rig.Written.ShouldBe(["Caption: Guard: Hey! You there!"]);
    }

    [Fact]
    public void Another_language_switches_the_words_of_the_line_that_is_being_said()
    {
        const string InGerman = "WEBVTT\n\n00:00.000 --> 00:00.800\nHe! Sie da!\n\n00:01.000 --> 00:01.800\nHalt.";
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", TwoLines);
        rig.Subtitles(Speech, "de", InGerman);
        rig.Play(Speech);
        rig.RunTo(0.5);
        Caption english = rig.Shown.ShouldHaveSingleItem();

        rig.Feed.Language = "de";
        rig.Step();
        Caption german = rig.Shown.ShouldHaveSingleItem();
        rig.RunTo(1.1);

        english.Text.ShouldBe("Hey! You there!");
        german.Text.ShouldBe("He! Sie da!");
        german.Id.ShouldBeGreaterThan(english.Id);
        rig.Shown.Select(caption => caption.Text).ShouldBe(["He! Sie da!", "Halt."]);
    }

    [Fact]
    public void Two_copies_of_a_voice_share_each_line()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", TwoLines);
        rig.Play(Speech);
        rig.Play(Speech, new Vector3(1, 0, -1));

        rig.Step();

        Texts(rig).ShouldBe(["Hey! You there!"]);
    }

    // Four seconds of which the last two repeat. One line is said before
    // that part, one early in it and one just before its end.
    private static CaptionFeedRig RigWithAnnouncement()
    {
        var rig = new CaptionFeedRig();
        rig.Sound.Cook(
            Announcement,
            HandBuiltSaudio.Resident(
                frames: 4 * SoundPresenterRig.Rate, loopStart: 2 * SoundPresenterRig.Rate, loopEnd: 4 * SoundPresenterRig.Rate));
        rig.Subtitles(
            Announcement,
            "en",
            """
            WEBVTT

            00:00.200 --> 00:00.800
            Attention.

            00:02.200 --> 00:02.800
            Mind the gap.

            00:03.900 --> 00:03.950
            Stand clear.
            """);

        return rig;
    }

    private static string[] Texts(CaptionFeedRig rig) => [.. rig.Shown.Select(caption => caption.Text)];
}
