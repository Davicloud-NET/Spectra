using SpectraEngine.Core.Audio.Captions;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The list a view reads: a caption keeps its id while it shows, a sound that
/// plays again refreshes its caption, the order is stable, and nothing is
/// made new while nothing changes.
/// </summary>
public sealed class CaptionFeedListTests
{
    private const string Beep = SoundPresenterRig.Beep;
    private const string Speech = SoundPresenterRig.Speech;
    private const string Click = CaptionFeedRig.Click;

    [Fact]
    public void A_caption_keeps_its_id_for_as_long_as_it_shows()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Speech} = Engine runs");
        rig.Play(Speech);
        rig.Step();
        Caption first = rig.Shown.ShouldHaveSingleItem();

        rig.RunTo(1.5);

        Caption later = rig.Shown.ShouldHaveSingleItem();
        later.Id.ShouldBe(first.Id);
        later.StartedAt.ShouldBe(first.StartedAt);
        rig.Feed.LastId.ShouldBe(first.Id);
    }

    [Fact]
    public void A_caption_that_shows_again_later_has_a_new_id()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Click} = Click");
        rig.Play(Click);
        rig.Step();
        long first = rig.Shown.ShouldHaveSingleItem().Id;
        rig.RunTo(2);
        rig.Shown.ShouldBeEmpty();

        rig.Play(Click);
        rig.Step();

        rig.Shown.ShouldHaveSingleItem().Id.ShouldBe(first + 1);
        rig.Feed.LastId.ShouldBe(first + 1);
    }

    [Fact]
    public void A_sound_that_plays_again_while_its_caption_shows_refreshes_it_and_adds_none()
    {
        // Ten footsteps, one every fifth of a second.
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Click} = Footsteps");
        SceneNode walker = rig.Place(CaptionFeedRig.Near);
        rig.Play(Click, walker, SoundPresenterRig.Once);
        rig.Step();
        Caption first = rig.Shown.ShouldHaveSingleItem();

        for (int step = 1; step < 10; step++)
        {
            rig.RunTo(step * 0.2);
            rig.Play(Click, walker, SoundPresenterRig.Once);
            rig.Step();
            rig.Shown.Count.ShouldBe(1);
        }

        Caption last = rig.Shown.ShouldHaveSingleItem();
        last.Id.ShouldBe(first.Id);
        last.StartedAt.ShouldBe(first.StartedAt);
        last.EarliestEnd.ShouldBe(rig.Feed.Now + CaptionTiming.ReadingSeconds("Footsteps"), 1e-9);
        rig.Feed.LastId.ShouldBe(1L);
    }

    [Fact]
    public void A_refreshed_caption_goes_once_its_last_sound_has_been_read()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Click} = Click");
        rig.Play(Click);
        rig.RunTo(0.8);
        rig.Play(Click);

        rig.RunTo(1.7);
        int afterTheFirstWouldHaveGone = rig.Shown.Count;
        rig.RunTo(1.9);

        afterTheFirstWouldHaveGone.ShouldBe(1);
        rig.Shown.ShouldBeEmpty();
    }

    [Fact]
    public void Captions_are_listed_by_when_they_started_and_then_by_id()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Beep} = Beep sounds\n{Speech} = Engine runs\n{Click} = Click");
        rig.Play(Speech);
        rig.Play(Beep);
        rig.Step();
        rig.RunTo(0.5);
        rig.Play(Click);
        rig.Step();

        IReadOnlyList<Caption> shown = rig.Shown;

        shown.Select(caption => caption.Text).ShouldBe(["Engine runs", "Beep sounds", "Click"]);
        shown.Select(caption => caption.Id).ShouldBe([1L, 2L, 3L]);
        shown[0].StartedAt.ShouldBe(shown[1].StartedAt);
        shown[2].StartedAt.ShouldBeGreaterThan(shown[1].StartedAt);
    }

    [Fact]
    public void The_order_holds_when_an_earlier_caption_goes()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Speech} = Engine runs\n{Click} = Click\n{Beep} = A long low beep sounds");
        rig.Play(Click);
        rig.Play(Speech);
        rig.Play(Beep);

        rig.RunTo(1.5);

        rig.Shown.Select(caption => caption.Text).ShouldBe(["Engine runs", "A long low beep sounds"]);
    }

    [Fact]
    public void The_list_is_the_same_instance_while_nothing_changes()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Speech} = Engine runs");
        IReadOnlyList<Caption> empty = rig.Shown;
        rig.Step(5);
        rig.Shown.ShouldBeSameAs(empty);

        rig.Play(Speech);
        rig.Step();
        IReadOnlyList<Caption> shown = rig.Shown;
        rig.Step(60);

        shown.ShouldNotBeSameAs(empty);
        shown.Count.ShouldBe(1);
        rig.Shown.ShouldBeSameAs(shown);
    }

    [Fact]
    public void A_change_gives_a_new_list_and_leaves_the_one_handed_out_as_it_was()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Speech} = Engine runs");
        SceneNode engine = rig.Place(CaptionFeedRig.Near);
        rig.Play(Speech, engine, SoundPresenterRig.Looped);
        rig.Step();
        IReadOnlyList<Caption> before = rig.Shown;

        engine.LocalPosition = new Vector3(0, 0, -6);
        rig.Step();

        rig.Shown.ShouldNotBeSameAs(before);
        before.ShouldHaveSingleItem().Position.ShouldBe(CaptionFeedRig.Near);
        before[0].Audibility.ShouldBe(1f);
        rig.Shown.ShouldHaveSingleItem().Position.ShouldBe(new Vector3(0, 0, -6));
        rig.Shown[0].Audibility.ShouldBeLessThan(1f);
    }

    [Fact]
    public void With_nothing_on_show_the_list_is_one_empty_list()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Click} = Click");
        IReadOnlyList<Caption> empty = rig.Shown;
        rig.Play(Click);
        rig.Step();

        rig.RunTo(2);

        rig.Shown.ShouldBeEmpty();
        rig.Shown.ShouldBeSameAs(empty);
    }

    [Fact]
    public void Following_captions_allocates_nothing_a_frame_while_nothing_changes()
    {
        // Looped sounds whose captions have been read and gone, and a speech
        // of half a minute whose one line is said all the way through.
        const string Monologue = "Sounds/monologue.wav";
        using var rig = new CaptionFeedRig();
        rig.Sound.Cook(Monologue, HandBuiltSaudio.Streaming(frames: 30 * SoundPresenterRig.Rate, framesPerEntry: 4096));
        rig.Captions("en", $"{Beep} = Beep sounds\n{SoundPresenterRig.Music} = Music plays");
        rig.Subtitles(Monologue, "en", "WEBVTT\n\n00:00.000 --> 00:30.000\n<v Guard>Hey! You there!");
        for (int i = 0; i < 60; i++)
        {
            string sound = (i % 3) switch { 0 => Beep, 1 => SoundPresenterRig.Music, _ => Monologue };
            rig.Play(sound, new Vector3((i % 10) - 5, 0, -(i / 10) - 1), looped: i % 3 != 2);
        }

        rig.Sound.Backend.KeepsUploads = false;
        RunFrames(rig, 120);
        rig.Shown.Count.ShouldBe(1);

        // The least of several rounds: a one-off from the runtime is not a
        // cost per frame.
        long least = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            RunFrames(rig, 100);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        least.ShouldBe(0L);
        rig.Shown.ShouldHaveSingleItem().Text.ShouldBe("Hey! You there!");
    }

    // A tick and a frame each, the list read as a view reads it, and every
    // stream's queue refilled.
    private static void RunFrames(CaptionFeedRig rig, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            rig.Sound.Backend.ConsumeOneEverywhere();
            rig.Step();
            _ = rig.Shown.Count;
        }
    }
}
