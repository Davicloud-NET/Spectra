using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Captions;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A caption shows while its sound can be heard at the listener, by the
/// presenter's own loudness, and long enough to be read.
/// </summary>
public sealed class CaptionFeedHearingTests
{
    private const string Beep = SoundPresenterRig.Beep;
    private const string Speech = SoundPresenterRig.Speech;
    private const string Click = CaptionFeedRig.Click;

    [Fact]
    public void A_sound_that_is_heard_shows_its_caption()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Beep} = Beep sounds");
        rig.Play(Beep);

        rig.Step();

        Caption caption = rig.Shown.ShouldHaveSingleItem();
        caption.Id.ShouldBe(1L);
        caption.Kind.ShouldBe(CaptionKind.Sound);
        caption.Text.ShouldBe("Beep sounds");
        caption.Speaker.ShouldBeNull();
        caption.Position.ShouldBe(CaptionFeedRig.Near);
        caption.Audibility.ShouldBe(1f);
        caption.StartedAt.ShouldBe(rig.Feed.Now);
        caption.EarliestEnd.ShouldBe(caption.StartedAt + CaptionTiming.ReadingSeconds("Beep sounds"), 1e-9);
    }

    [Fact]
    public void A_sound_with_no_caption_shows_nothing()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Click} = Click");
        rig.Play(Beep);

        rig.Step();

        rig.Shown.ShouldBeEmpty();
    }

    [Fact]
    public void What_decides_is_whether_the_sound_is_heard_and_not_how_far_away_it_is()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Beep} = Whisper\n{Speech} = Siren");

        // A whisper ten metres away that reaches five, and a siren a hundred
        // metres away that reaches two hundred.
        rig.Play(Beep, rig.Place(new Vector3(0, 0, -10)), new SoundEmitterSettings(1f, 1f, 1f, 5f, IsLooped: true));
        rig.Play(Speech, rig.Place(new Vector3(0, 0, -100)), new SoundEmitterSettings(1f, 1f, 20f, 200f, IsLooped: true));

        rig.Step();

        rig.Shown.ShouldHaveSingleItem().Text.ShouldBe("Siren");
    }

    // The rig's sounds are at full volume up to 2 units and silent from 30.
    // At 27.5 units one is just loud enough for a caption, at 28.6 it is
    // between the two numbers, and at 29.3 it has a voice and is too faint
    // to count.
    [Fact]
    public void A_sound_too_faint_to_hear_has_a_voice_and_no_caption()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Beep} = Beep sounds");
        rig.Play(Beep, new Vector3(0, 0, -29.3f), looped: true);

        rig.Step(5);

        rig.Sound.OnlyVoice().Gain.ShouldBeInRange(SoundPresenter.SilenceGain, CaptionTracker.HeardDownTo);
        rig.Shown.ShouldBeEmpty();
    }

    [Fact]
    public void A_sound_that_wavers_at_the_edge_of_hearing_shows_its_caption_once()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Beep} = Beep sounds");
        rig.Play(Beep, new Vector3(0, 0, -27.5f), looped: true);
        rig.Step();
        long first = rig.Shown.ShouldHaveSingleItem().Id;

        for (int i = 0; i < 20; i++)
        {
            rig.Sound.Listen(new Vector3(0, 0, i % 2 == 0 ? 1.1f : 0f));
            rig.Step(30);
        }

        rig.Feed.LastId.ShouldBe(first);
    }

    [Fact]
    public void A_sound_that_left_hearing_shows_its_caption_again_when_it_comes_back()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Beep} = Beep sounds");
        rig.Play(Beep, new Vector3(0, 0, -27.5f), looped: true);
        rig.Step();
        long first = rig.Shown.ShouldHaveSingleItem().Id;

        rig.Sound.Listen(new Vector3(0, 0, 1.8f));
        rig.Step(120);
        rig.Shown.ShouldBeEmpty();

        rig.Sound.Listen(Vector3.Zero);
        rig.Step(60);

        rig.Feed.LastId.ShouldBeGreaterThan(first);
    }

    [Fact]
    public void A_sound_at_no_volume_has_no_caption()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Beep} = Beep sounds");
        rig.Play(Beep, rig.Place(CaptionFeedRig.Near), SoundPresenterRig.Looped with { Gain = 0f });

        rig.Step(5);

        rig.Shown.ShouldBeEmpty();
    }

    [Fact]
    public void A_caption_shows_when_its_sound_comes_into_hearing()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Beep} = Beep sounds");
        rig.Play(Beep, CaptionFeedRig.OutOfHearing, looped: true);
        rig.Step(30);
        rig.Shown.ShouldBeEmpty();

        rig.Sound.Listen(CaptionFeedRig.OutOfHearing + Vector3.UnitZ);
        rig.Step();

        Caption caption = rig.Shown.ShouldHaveSingleItem();
        caption.Text.ShouldBe("Beep sounds");
        caption.StartedAt.ShouldBe(rig.Feed.Now);
    }

    [Fact]
    public void How_audible_a_caption_is_is_how_loud_its_sound_is_played()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Beep} = Beep sounds");
        rig.Play(Beep, new Vector3(0, 0, -10), looped: true);

        rig.Step();

        float audibility = rig.Shown.ShouldHaveSingleItem().Audibility;
        audibility.ShouldBe(rig.Sound.OnlyVoice().Gain);
        audibility.ShouldBeInRange(0.1f, 0.2f);
    }

    [Fact]
    public void A_caption_is_where_its_sound_is_and_follows_it()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Beep} = Beep sounds");
        SceneNode door = rig.Place(new Vector3(3, 0, -1));
        rig.Play(Beep, door, SoundPresenterRig.Looped);
        rig.Step();
        rig.Shown.ShouldHaveSingleItem().Position.ShouldBe(new Vector3(3, 0, -1));

        door.LocalPosition = new Vector3(4, 1, -1);
        rig.Step();

        rig.Shown.ShouldHaveSingleItem().Position.ShouldBe(new Vector3(4, 1, -1));
    }

    [Fact]
    public void A_stereo_sound_plays_at_the_listener_so_its_caption_has_no_place()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{SoundPresenterRig.Music} = Music plays");
        rig.Play(SoundPresenterRig.Music, new Vector3(3, 0, -1), looped: true);

        rig.Step();

        Caption caption = rig.Shown.ShouldHaveSingleItem();
        caption.Position.ShouldBeNull();
        caption.Audibility.ShouldBeGreaterThan(0f);
    }

    [Fact]
    public void Several_copies_of_a_sound_share_one_caption_at_the_loudest_of_them()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Beep} = Beep sounds");
        rig.Play(Beep, new Vector3(0, 0, -12), looped: true);
        rig.Play(Beep, new Vector3(2, 0, -1), looped: true);
        rig.Play(Beep, new Vector3(0, 0, -20), looped: true);

        rig.Step();

        FakeAudioBackend device = rig.Sound.Backend;
        Caption caption = rig.Shown.ShouldHaveSingleItem();
        caption.Position.ShouldBe(new Vector3(2, 0, -1));
        caption.Audibility.ShouldBe(device.PlayingSources().Max(source => device.SettingsOf(source).Gain));
    }

    [Fact]
    public void A_sound_that_plays_once_keeps_its_caption_for_as_long_as_it_plays()
    {
        // Two seconds of sound and a word that is read in one.
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Speech} = Hum");
        rig.Play(Speech);

        rig.RunTo(1.9);
        Caption playing = rig.Shown.ShouldHaveSingleItem();
        rig.RunTo(2.1);

        playing.Text.ShouldBe("Hum");
        playing.EarliestEnd.ShouldBeLessThan(1.1);
        rig.Shown.ShouldBeEmpty();
    }

    [Fact]
    public void A_looped_sound_shows_its_caption_for_its_reading_time_and_not_for_as_long_as_it_plays()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Beep} = Hum");
        rig.Play(Beep, CaptionFeedRig.Near, looped: true);

        rig.RunTo(0.9);
        rig.Shown.ShouldHaveSingleItem().Text.ShouldBe("Hum");
        rig.RunTo(1.1);

        rig.Shown.ShouldBeEmpty();
        rig.Sound.Stats.WithSource.ShouldBe(1);
    }

    [Fact]
    public void A_looped_sound_shows_its_caption_again_each_time_it_comes_into_hearing()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Beep} = Hum");
        rig.Play(Beep, CaptionFeedRig.Near, looped: true);
        rig.RunTo(0.5);
        long first = rig.Shown.ShouldHaveSingleItem().Id;
        rig.RunTo(3);
        rig.Shown.ShouldBeEmpty();

        rig.Sound.Listen(CaptionFeedRig.OutOfHearing * 2);
        rig.Step(30);
        rig.Shown.ShouldBeEmpty();
        rig.Sound.Listen(Vector3.Zero);
        rig.Step();

        Caption again = rig.Shown.ShouldHaveSingleItem();
        again.Text.ShouldBe("Hum");
        again.Id.ShouldBeGreaterThan(first);
        again.StartedAt.ShouldBe(rig.Feed.Now);
    }

    [Fact]
    public void A_caption_stays_at_least_a_second_however_short_its_sound_is()
    {
        // A tenth of a second of sound.
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Click} = Click");
        rig.Play(Click);

        rig.RunTo(0.95);
        Caption late = rig.Shown.ShouldHaveSingleItem();
        rig.RunTo(1.05);

        late.Text.ShouldBe("Click");
        rig.Shown.ShouldBeEmpty();
    }

    [Fact]
    public void A_caption_with_more_to_read_stays_longer()
    {
        const string Words = "A heavy door grinds open somewhere below";
        double reading = CaptionTiming.ReadingSeconds(Words);
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Click} = {Words}");
        rig.Play(Click);

        rig.RunTo(reading - 0.05);
        int before = rig.Shown.Count;
        rig.RunTo(reading + 0.1);

        reading.ShouldBeGreaterThan(2.5);
        before.ShouldBe(1);
        rig.Shown.ShouldBeEmpty();
    }

    [Fact]
    public void A_caption_that_outlasts_its_sound_is_not_heard_and_stays_where_the_sound_was()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Click} = Click");
        rig.Play(Click, new Vector3(1, 0, -1));
        rig.Step();
        rig.Shown.ShouldHaveSingleItem().Audibility.ShouldBe(1f);

        rig.RunTo(0.5);

        Caption caption = rig.Shown.ShouldHaveSingleItem();
        caption.Audibility.ShouldBe(0f);
        caption.Position.ShouldBe(new Vector3(1, 0, -1));
    }

    [Fact]
    public void A_sound_that_goes_out_of_hearing_loses_its_caption_once_it_has_been_read()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Speech} = Hum");
        rig.Play(Speech);
        rig.RunTo(0.5);

        rig.Sound.Listen(CaptionFeedRig.OutOfHearing * 2);
        rig.RunTo(0.9);
        Caption unheard = rig.Shown.ShouldHaveSingleItem();
        rig.RunTo(1.1);

        unheard.Audibility.ShouldBe(0f);
        rig.Shown.ShouldBeEmpty();
    }

    [Fact]
    public void A_sound_is_captioned_with_no_audio_device()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Beep} = Beep sounds\n{SoundPresenterRig.Music} = Music plays");
        var audio = new AudioManager(new CapturingLogger(), NoDevice);
        audio.Initialize();
        CaptionFeed feed = rig.Sound.NewCaptionFeed();
        feed.Mode = CaptionMode.All;
        var presenter = new SoundPresenter(audio, rig.Sound.Assets, new DirectPropagation(), feed, rig.Sound.Log);
        rig.Play(Beep, CaptionFeedRig.Near, looped: true);
        rig.Play(SoundPresenterRig.Music, new Vector3(0, 0, -5), looped: true);

        presenter.Update(rig.Sound.World, SoundPresenterRig.TickSeconds);

        presenter.Stats.WithSource.ShouldBe(0);
        feed.Captions.Select(caption => caption.Text).ShouldBe(["Beep sounds", "Music plays"]);
        feed.Captions[0].Position.ShouldBe(CaptionFeedRig.Near);
        feed.Captions[1].Position.ShouldBeNull();
    }

    private static bool NoDevice(ILogger logger, [NotNullWhen(true)] out IAudioBackend? backend, out string reason)
    {
        backend = null;
        reason = "the test has no audio device";
        return false;
    }
}
