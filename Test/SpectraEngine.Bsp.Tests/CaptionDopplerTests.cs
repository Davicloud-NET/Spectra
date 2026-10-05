using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The lines of a voice whose pitch Doppler bends. While the listener closes
/// in the device is ahead of the level's count, and the lines follow what is
/// heard. A voice that reaches its end early has lost none of them.
/// </summary>
public sealed class CaptionDopplerTests
{
    // One second long, on one buffer.
    private const string Beep = SoundPresenterRig.Beep;

    // Two seconds long, played as a stream.
    private const string Speech = SoundPresenterRig.Speech;

    private const string EarlyAndLate = "WEBVTT\n\n00:00.000 --> 00:00.300\nOne\n\n00:00.920 --> 00:00.990\nTwo";

    // A tenth of the speed of sound. A voice that closes in plays a ninth
    // faster, so a second of sound is over on the device after 0.9 seconds.
    private const float Speed = 34.3f;

    // Loud enough to be heard from two hundred units, as a caption needs.
    private static readonly SoundEmitterSettings FromAfar =
        SoundPresenterRig.Once with { MinDistance = 50f, MaxDistance = 100_000f };

    [Fact]
    public void While_the_listener_closes_in_a_line_shows_when_it_is_heard_and_not_when_the_level_counts_it()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Beep, "en", EarlyAndLate);
        SceneNode speaker = rig.Place(new Vector3(0, 0, -200));
        rig.Play(Beep, speaker, FromAfar);

        RunTo(rig, speaker, 0.80, Speed);
        string[] before = [.. rig.Written];
        RunTo(rig, speaker, 0.87, Speed);

        before.ShouldBe(["Caption: One"]);
        rig.Written.ShouldBe(["Caption: One", "Caption: Two"]);
    }

    [Fact]
    public void A_voice_that_is_over_early_while_the_listener_closes_in_has_shown_its_last_line_once()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Beep, "en", EarlyAndLate);
        SceneNode speaker = rig.Place(new Vector3(0, 0, -200));
        rig.Play(Beep, speaker, FromAfar);

        // The device reaches the end while the level still counts 0.9 seconds.
        RunTo(rig, speaker, 0.90, Speed);
        rig.Sound.Backend.Finish(rig.Sound.Backend.PlayingSources().ShouldHaveSingleItem());
        RunTo(rig, speaker, 1.1, Speed);

        rig.Written.ShouldBe(["Caption: One", "Caption: Two"]);
        rig.Sound.Stats.WithSource.ShouldBe(0);
    }

    [Fact]
    public void A_voice_cut_off_while_the_listener_closes_in_shows_the_line_it_had_reached_and_none_after()
    {
        const string ThreeLines =
            "WEBVTT\n\n00:00.000 --> 00:00.300\nOne\n\n00:00.740 --> 00:00.800\nTwo\n\n00:00.900 --> 00:00.950\nThree";

        using var rig = new CaptionFeedRig();
        rig.Subtitles(Beep, "en", ThreeLines);
        SceneNode speaker = rig.Place(new Vector3(0, 0, -200));
        rig.Play(Beep, speaker, FromAfar);
        RunTo(rig, speaker, 0.5, Speed);

        // One frame of twelve ticks, in which another sound takes the source.
        // The level counts 0.7 seconds and the device was 0.07 ahead of that.
        for (int tick = 0; tick < 12; tick++)
        {
            Move(rig, speaker, Speed);
            rig.Sound.Tick();
        }

        rig.Sound.Backend.Finish(rig.Sound.Backend.PlayingSources().ShouldHaveSingleItem());
        rig.Sound.Audio.Update();
        rig.Sound.Presenter.Update(rig.Sound.World, 12 * SoundPresenterRig.TickSeconds);
        string[] reached = [.. rig.Shown.Select(caption => caption.Text)];
        RunTo(rig, speaker, 1.1, Speed);

        reached.ShouldContain("Two");
        rig.Written.ShouldNotContain("Caption: Three");
    }

    [Fact]
    public void While_the_listener_parts_a_line_keeps_to_the_levels_count()
    {
        // The voice is behind and is cut at the level's end. A line that
        // waited for it could be lost.
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Beep, "en", EarlyAndLate);
        SceneNode speaker = rig.Place(new Vector3(0, 0, -200));
        rig.Play(Beep, speaker, FromAfar);

        RunTo(rig, speaker, 0.90, -Speed);
        string[] before = [.. rig.Written];
        RunTo(rig, speaker, 0.95, -Speed);

        before.ShouldBe(["Caption: One"]);
        rig.Written.ShouldBe(["Caption: One", "Caption: Two"]);
    }

    [Fact]
    public void A_streamed_voice_follows_what_is_heard_too()
    {
        const string LateLine = "WEBVTT\n\n00:00.000 --> 00:00.300\nOne\n\n00:01.000 --> 00:01.800\nTwo";

        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", LateLine);
        SceneNode speaker = rig.Place(new Vector3(0, 0, -200));
        rig.Play(Speech, speaker, FromAfar);

        RunTo(rig, speaker, 0.87, Speed);
        string[] before = [.. rig.Written];
        RunTo(rig, speaker, 0.94, Speed);

        before.ShouldBe(["Caption: One"]);
        rig.Written.ShouldBe(["Caption: One", "Caption: Two"]);
    }

    [Fact]
    public void A_looped_voice_keeps_to_the_levels_count_while_the_listener_closes_in()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Beep, "en", EarlyAndLate);
        SceneNode speaker = rig.Place(new Vector3(0, 0, -200));
        rig.Play(Beep, speaker, FromAfar with { IsLooped = true });

        RunTo(rig, speaker, 0.90, Speed);
        string[] before = [.. rig.Written];
        RunTo(rig, speaker, 0.95, Speed);

        before.ShouldBe(["Caption: One"]);
        rig.Written.ShouldBe(["Caption: One", "Caption: Two"]);
    }

    // Steps up to a moment, in seconds since the level started, with the
    // speaker moving toward the listener on every tick. Away when the speed
    // is below zero.
    private static void RunTo(CaptionFeedRig rig, SceneNode speaker, double seconds, float speed)
    {
        long target = (long)Math.Round(seconds * 60d);
        while (rig.Sound.World.TickNumber < target)
        {
            Move(rig, speaker, speed);
            rig.Step();
        }
    }

    private static void Move(CaptionFeedRig rig, SceneNode speaker, float speed)
    {
        float ticked = (rig.Sound.World.TickNumber + 1) * SoundPresenterRig.TickSeconds;
        speaker.LocalPosition = new Vector3(0, 0, -200 + (speed * ticked));
    }
}
