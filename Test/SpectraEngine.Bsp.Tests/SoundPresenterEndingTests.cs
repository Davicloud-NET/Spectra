using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// How a sound that plays once ends on the device, which does not run on the
/// simulation's clock: it plays out, it is cut when it is stopped, and it is
/// never started twice.
/// </summary>
public sealed class SoundPresenterEndingTests
{
    [Fact]
    public void A_sound_stopped_at_its_end_plays_out_on_the_device()
    {
        using var rig = new SoundPresenterRig();
        uint source = PlayToTheEnd(rig, out int id);

        // What point_sound does on the tick its sound ends.
        rig.World.Sounds.Stop(id);
        rig.Frame();

        rig.Backend.StateOf(source).ShouldBe(AudioSourceState.Playing);
        rig.Stats.ShouldBe(default(SoundStats));

        rig.Backend.Finish(source);
        rig.Frame();

        rig.Audio.ActiveVoiceCount.ShouldBe(0);
    }

    [Fact]
    public void A_sound_left_listed_past_its_end_plays_out_too()
    {
        using var rig = new SoundPresenterRig();
        uint source = PlayToTheEnd(rig, out _);

        rig.Frame();

        rig.World.Sounds.Count.ShouldBe(1);
        rig.Backend.StateOf(source).ShouldBe(AudioSourceState.Playing);
        rig.Stats.ShouldBe(new SoundStats(Playing: 1, WithSource: 0, WithoutSource: 0, RefusedStarts: 0, Unplayable: 0));
    }

    [Fact]
    public void A_sound_stopped_before_its_end_is_cut_at_once()
    {
        using var rig = new SoundPresenterRig();
        int id = rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Once);
        rig.Frame();

        rig.Tick(30);
        rig.World.Sounds.Stop(id);
        rig.Frame();

        rig.Backend.PlayingSources().ShouldBeEmpty();
        rig.Audio.ActiveVoiceCount.ShouldBe(0);
    }

    [Fact]
    public void A_sound_still_playing_out_after_its_time_is_cut()
    {
        using var rig = new SoundPresenterRig();
        uint source = PlayToTheEnd(rig, out int id);
        rig.World.Sounds.Stop(id);
        int frames = (int)MathF.Ceiling(SoundPresenter.TailSeconds / SoundPresenterRig.TickSeconds);

        rig.Frame(frames / 2);
        rig.Backend.StateOf(source).ShouldBe(AudioSourceState.Playing);

        rig.Frame(frames);

        rig.Backend.PlayingSources().ShouldBeEmpty();
        rig.Audio.ActiveVoiceCount.ShouldBe(0);
    }

    [Fact]
    public void A_sound_playing_out_keeps_its_source_from_the_next_sound_until_it_is_done()
    {
        using var rig = new SoundPresenterRig(sources: 1);
        uint source = PlayToTheEnd(rig, out int id);
        rig.World.Sounds.Stop(id);
        SceneNode hum = rig.Place("hum", new Vector3(0, 0, -1));
        rig.Play(hum, SoundPresenterRig.Beep, SoundPresenterRig.Looped);

        rig.Frame();

        rig.HasVoiceAt(hum.WorldPosition).ShouldBeFalse();
        rig.Stats.ShouldBe(new SoundStats(Playing: 1, WithSource: 0, WithoutSource: 1, RefusedStarts: 0, Unplayable: 0));

        rig.Backend.Finish(source);
        rig.Frame();

        rig.HasVoiceAt(hum.WorldPosition).ShouldBeTrue();
        rig.Audio.StolenVoiceCount.ShouldBe(0);
    }

    [Fact]
    public void A_sound_the_device_played_to_its_end_is_not_started_again()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Once);
        rig.Frame();
        uint source = rig.Backend.PlayingSources().ShouldHaveSingleItem();

        // The device is ahead of the ticks, as it is after a hitch.
        rig.Tick(50);
        rig.Backend.Finish(source);
        rig.Frame(3);

        rig.Backend.PlayingSources().ShouldBeEmpty();
        rig.Audio.ActiveVoiceCount.ShouldBe(0);
        rig.Stats.Silent.ShouldBe(1);
    }

    // A second-long sound and a second of ticks, with the device still on it.
    private static uint PlayToTheEnd(SoundPresenterRig rig, out int id)
    {
        id = rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Once);
        rig.Frame();
        uint source = rig.Backend.PlayingSources().ShouldHaveSingleItem();

        rig.Tick(60);
        rig.Backend.StateOf(source).ShouldBe(AudioSourceState.Playing);
        return source;
    }
}
