using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a playing sound becomes on the audio device: where its voice is, how
/// loud, how fast and how muffled, and which way its samples reach the device.
/// </summary>
public sealed class SoundPresenterTests
{
    [Fact]
    public void A_playing_sound_becomes_a_voice_with_the_emitters_pitch_and_the_paths_place_gain_and_high_end()
    {
        var propagation = new ScriptedPropagation { Gain = 0.5f, GainHf = 0.25f, Offset = new Vector3(0, 3, 0) };
        using var rig = new SoundPresenterRig(propagation: propagation);
        SceneNode speaker = rig.Place("speaker", new Vector3(4, 0, -2));
        rig.Play(speaker, SoundPresenterRig.Beep, new SoundEmitterSettings(0.8f, 1.5f, 2f, 30f, IsLooped: false));

        rig.Frame();

        AudioSourceSettings voice = rig.OnlyVoice();
        voice.Position.ShouldBe(new Vector3(4, 3, -2));
        voice.Gain.ShouldBe(0.8f * 0.5f);
        voice.Pitch.ShouldBe(1.5f);
        voice.GainHf.ShouldBe(0.25f);
        voice.Relative.ShouldBeFalse();
        rig.Stats.ShouldBe(new SoundStats(Playing: 1, WithSource: 1, WithoutSource: 0, RefusedStarts: 0, Unplayable: 0));
    }

    [Fact]
    public void A_sound_gets_quieter_with_distance_by_the_falloff_and_not_by_the_device()
    {
        using var rig = new SoundPresenterRig();
        SceneNode speaker = rig.Place("speaker", new Vector3(0, 0, -8));
        rig.Play(speaker, SoundPresenterRig.Beep, SoundPresenterRig.Once);

        rig.Frame();

        rig.OnlyVoice().Gain.ShouldBe(SoundFalloff.Gain(8f, 2f, 30f));
        rig.OnlyVoice().GainHf.ShouldBe(1f);
    }

    [Fact]
    public void A_sound_turned_up_past_full_volume_plays_at_full_volume()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Once with { Gain = 4f });

        rig.Frame();

        rig.OnlyVoice().Gain.ShouldBe(1f);
    }

    [Fact]
    public void A_sound_turned_up_past_full_volume_stays_at_full_volume_further_out()
    {
        using var rig = new SoundPresenterRig();
        SceneNode speaker = rig.Place("speaker", new Vector3(0, 0, -8));
        rig.Play(speaker, SoundPresenterRig.Beep, SoundPresenterRig.Once with { Gain = 2f });

        rig.Frame();

        rig.OnlyVoice().Gain.ShouldBe(2f * SoundFalloff.Gain(8f, 2f, 30f));
    }

    [Fact]
    public void A_change_of_the_emitters_gain_or_pitch_reaches_the_voice_on_the_next_frame()
    {
        using var rig = new SoundPresenterRig();
        int id = rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Frame();
        uint source = rig.Backend.PlayingSources().ShouldHaveSingleItem();

        rig.World.Sounds.SetGain(id, 0.25f);
        rig.World.Sounds.SetPitch(id, 2f);
        rig.Frame();

        // The same voice, told the new numbers: nothing was started again.
        rig.Backend.PlayingSources().ShouldBe([source]);
        rig.OnlyVoice().Gain.ShouldBe(0.25f);
        rig.OnlyVoice().Pitch.ShouldBe(2f);
    }

    [Fact]
    public void A_sound_under_a_node_that_moves_has_its_source_follow()
    {
        using var rig = new SoundPresenterRig();
        SceneNode door = rig.Place("door", new Vector3(1, 0, 0));
        SceneNode squeak = door.CreateChild("squeak");
        squeak.LocalPosition = new Vector3(0, 1, 0);
        rig.Play(squeak, SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Frame();
        rig.OnlyVoice().Position.ShouldBe(new Vector3(1, 1, 0));
        long version = rig.World.Sounds.Version;

        door.LocalPosition = new Vector3(1, 0, -1.5f);
        rig.Frame();

        rig.OnlyVoice().Position.ShouldBe(new Vector3(1, 1, -1.5f));
        rig.World.Sounds.Version.ShouldBe(version);
    }

    [Fact]
    public void A_short_sound_played_once_is_one_buffer_that_every_play_of_it_shares()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Place("a", new Vector3(1, 0, 0)), SoundPresenterRig.Beep, SoundPresenterRig.Once);
        rig.Play(rig.Place("b", new Vector3(-1, 0, 0)), SoundPresenterRig.Beep, SoundPresenterRig.Once);

        rig.Frame();

        uint[] sources = rig.Backend.PlayingSources();
        sources.Length.ShouldBe(2);
        rig.Backend.Uploads.ShouldHaveSingleItem().Length.ShouldBe(SoundPresenterRig.Rate);
        rig.Backend.QueueDepth(sources[0]).ShouldBe(0);
        rig.Backend.QueueDepth(sources[1]).ShouldBe(0);
    }

    [Fact]
    public void A_looped_sound_is_fed_from_its_loaded_samples_through_a_queue()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Looped);

        rig.Frame();

        uint source = rig.Backend.PlayingSources().ShouldHaveSingleItem();
        rig.Backend.QueueDepth(source).ShouldBe(StreamingVoice.DefaultBufferCount);
        rig.Backend.Uploads[0].Length.ShouldBe(StreamingVoice.DefaultBufferFrames);
        rig.Backend.Uploads[0][0].ShouldBe(HandBuiltSaudio.Sample(0));
        rig.Backend.Uploads[1][0].ShouldBe(HandBuiltSaudio.Sample(StreamingVoice.DefaultBufferFrames));
    }

    [Fact]
    public void A_long_sound_played_once_is_fed_through_a_queue_too()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Scene.Root, SoundPresenterRig.Speech, SoundPresenterRig.Once);

        rig.Frame();

        uint source = rig.Backend.PlayingSources().ShouldHaveSingleItem();
        rig.Backend.QueueDepth(source).ShouldBe(StreamingVoice.DefaultBufferCount);
        rig.Backend.Uploads[0].Length.ShouldBe(StreamingVoice.DefaultBufferFrames);
    }

    [Fact]
    public void A_stereo_sound_plays_at_the_listener_as_loud_as_its_distance_gives()
    {
        using var rig = new SoundPresenterRig();
        SceneNode radio = rig.Place("radio", new Vector3(0, 0, -8));
        rig.Play(radio, SoundPresenterRig.Music, SoundPresenterRig.Looped);

        rig.Frame();

        AudioSourceSettings voice = rig.OnlyVoice();
        voice.Relative.ShouldBeTrue();
        voice.Position.ShouldBe(Vector3.Zero);
        voice.Gain.ShouldBe(SoundFalloff.Gain(8f, 2f, 30f));

        // Two samples a frame reach the device.
        rig.Backend.Uploads[0].Length.ShouldBe(StreamingVoice.DefaultBufferFrames * 2);
    }

    [Fact]
    public void Only_the_first_of_two_paths_is_played()
    {
        var propagation = new ScriptedPropagation { PathCount = 2, Gain = 0.5f, Offset = new Vector3(0, 0, 5) };
        using var rig = new SoundPresenterRig(propagation: propagation);
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Once);

        rig.Frame();

        AudioSourceSettings voice = rig.OnlyVoice();
        voice.Position.ShouldBe(new Vector3(0, 0, 5));
        voice.Gain.ShouldBe(0.5f);
    }

    [Fact]
    public void A_sound_that_no_path_carries_to_the_listener_has_no_voice()
    {
        var propagation = new ScriptedPropagation { PathCount = 0 };
        using var rig = new SoundPresenterRig(propagation: propagation);
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Once);

        rig.Frame();

        rig.Backend.PlayingSources().ShouldBeEmpty();
        rig.Stats.Silent.ShouldBe(1);
    }

    [Fact]
    public void A_path_with_a_place_that_is_not_a_number_fades_out_where_the_voice_last_was()
    {
        var propagation = new ScriptedPropagation();
        using var rig = new SoundPresenterRig(propagation: propagation);
        rig.Play(rig.Place("speaker", new Vector3(2, 0, 0)), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Frame();

        // Still full gain, from nowhere.
        propagation.Offset = new Vector3(float.NaN, 0, 0);
        rig.Frame();

        AudioSourceSettings voice = rig.OnlyVoice();
        voice.Position.ShouldBe(new Vector3(2, 0, 0));
        voice.Gain.ShouldBeInRange(SoundPresenter.SilenceGain, 0.9f);

        rig.Frame(120);

        rig.Backend.PlayingSources().ShouldBeEmpty();
    }

    [Fact]
    public void A_sound_out_of_earshot_takes_no_source_and_gets_one_when_the_listener_comes_near()
    {
        using var rig = new SoundPresenterRig();
        SceneNode speaker = rig.Place("speaker", new Vector3(0, 0, -100));
        rig.Play(speaker, SoundPresenterRig.Beep, SoundPresenterRig.Looped);

        rig.Frame();

        rig.Backend.PlayingSources().ShouldBeEmpty();
        rig.Backend.Uploads.ShouldBeEmpty();
        rig.Stats.ShouldBe(new SoundStats(Playing: 1, WithSource: 0, WithoutSource: 0, RefusedStarts: 0, Unplayable: 0));
        rig.Stats.Silent.ShouldBe(1);

        rig.Listen(new Vector3(0, 0, -99));
        rig.Frame();

        rig.OnlyVoice().Gain.ShouldBe(1f);
        rig.Stats.WithSource.ShouldBe(1);
    }

    [Fact]
    public void A_sound_that_fades_out_of_earshot_gives_its_source_back()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Frame();
        rig.Stats.WithSource.ShouldBe(1);

        // Walked, not jumped: five steps take the listener out of range.
        for (int step = 1; step <= 5; step++)
        {
            rig.Listen(new Vector3(0, 0, 7 * step));
            rig.Frame();
        }

        // Still sliding down, so the cut to come is not heard.
        rig.OnlyVoice().Gain.ShouldBeInRange(SoundPresenter.SilenceGain, 0.9f);

        rig.Frame(120);

        rig.Backend.PlayingSources().ShouldBeEmpty();
        rig.Audio.ActiveVoiceCount.ShouldBe(0);
        rig.Stats.Silent.ShouldBe(1);
    }

    [Fact]
    public void A_listener_that_jumps_hears_the_new_loudness_at_once()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Frame();

        rig.Listen(new Vector3(0, 0, 20));
        rig.Frame();

        rig.OnlyVoice().Gain.ShouldBe(SoundFalloff.Gain(20f, 2f, 30f));
    }

    [Fact]
    public void A_sound_that_only_just_started_plays_from_its_first_frame()
    {
        using var rig = new SoundPresenterRig();
        rig.Tick(10);
        rig.Play(rig.Scene.Root, SoundPresenterRig.Speech, SoundPresenterRig.Once);

        // A slow frame ran a few ticks before the presenter saw the sound.
        rig.Tick(4);
        rig.Frame();

        rig.Backend.Uploads[0][0].ShouldBe(HandBuiltSaudio.Sample(0));
    }

    [Fact]
    public void A_sound_first_heard_well_after_it_started_plays_from_where_the_simulation_is()
    {
        using var rig = new SoundPresenterRig();
        SceneNode speaker = rig.Place("speaker", new Vector3(0, 0, -100));
        int id = rig.Play(speaker, SoundPresenterRig.Speech, SoundPresenterRig.Once);
        rig.Tick(30);
        rig.Frame();
        rig.Backend.PlayingSources().ShouldBeEmpty();

        rig.Listen(new Vector3(0, 0, -99));
        rig.Frame();

        rig.World.Sounds.TryGet(id, out SoundEmitter emitter).ShouldBeTrue();
        long frame = emitter.PositionAt(rig.World.TickNumber).Frame;
        frame.ShouldBe(24_000L);
        rig.Backend.Uploads[0][0].ShouldBe(HandBuiltSaudio.Sample(frame));
    }
}
