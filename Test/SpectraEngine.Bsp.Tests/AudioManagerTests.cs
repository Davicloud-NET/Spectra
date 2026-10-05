using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Audio;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

// Runs over a fake backend: CI has no sound card.
public sealed class AudioManagerTests
{
    private const int Rate = 48000;

    // Every member away from its default, so a dropped one shows.
    private static readonly AudioSourceSettings Muffled = new(
        Gain: 0.4f,
        Pitch: 1.25f,
        Position: new Vector3(3, 1, -2),
        Velocity: new Vector3(0, 0, 5),
        Relative: false,
        GainHf: 0.3f);

    [Fact]
    public void No_device_is_disabled_mode_and_every_call_is_a_safe_no_op()
    {
        var logger = new CapturingLogger();
        var audio = new AudioManager(logger, FailingBackend("no audio output device is available"));

        audio.Initialize();

        audio.IsEnabled.ShouldBeFalse();
        audio.DisabledReason.ShouldContain("no audio output device");
        audio.SourceCount.ShouldBe(0);

        // Warning, not Error: the smoke gates grep for ERR.
        logger.MessagesAt(LogLevel.Warning).Count.ShouldBe(1);
        logger.MessagesAt(LogLevel.Error).ShouldBeEmpty();

        AudioClip? clip = audio.CreateClip(new AudioFormat(Rate, 1), Tone(1000));
        clip.ShouldBeNull();

        audio.Play(clip).ShouldBeNull();
        audio.Play(clip, AudioSourceSettings.At(Vector3.One)).ShouldBeNull();
        audio.PlayStream(new RampSampleProvider(new AudioFormat(Rate, 1), 1000, LoopRegion.None), AudioSourceSettings.Default)
            .ShouldBeNull();
        audio.DestroyClip(clip);
        audio.SetListener(Vector3.One, -Vector3.UnitZ, Vector3.UnitY);
        audio.SetListener(Vector3.One, -Vector3.UnitZ, Vector3.UnitY, Vector3.Zero);
        audio.MasterGain = 0.5f;
        audio.StopAll();
        audio.Update().ShouldBe(0);
        audio.Shutdown();
        audio.Dispose();

        // The listener still reports what it was told.
        audio.ListenerPosition.ShouldBe(Vector3.One);
        audio.MasterGain.ShouldBe(0.5f);
        logger.MessagesAt(LogLevel.Error).ShouldBeEmpty(logger.Describe());
    }

    [Fact]
    public void A_disabled_manager_initializes_and_shuts_down_repeatedly_without_throwing()
    {
        var logger = new CapturingLogger();
        var audio = new AudioManager(logger, FailingBackend("the OpenAL runtime could not be loaded"));

        audio.Initialize();
        audio.Initialize();
        audio.Shutdown();
        audio.Shutdown();
        audio.Dispose();

        logger.MessagesAt(LogLevel.Error).ShouldBeEmpty(logger.Describe());
    }

    [Fact]
    public void A_clip_with_no_loop_uploads_once_and_plays_from_a_single_buffer()
    {
        var backend = new FakeAudioBackend();
        var audio = NewManager(backend);

        AudioClip clip = audio.CreateClip(new AudioFormat(Rate, 1), Tone(600))!;
        clip.ShouldNotBeNull();
        backend.Uploads.Count.ShouldBe(1);
        backend.Uploads[0].Length.ShouldBe(600);

        AudioVoice voice = audio.Play(clip)!;
        voice.ShouldBeOfType<StaticVoice>();
        audio.ActiveVoiceCount.ShouldBe(1);

        audio.Update().ShouldBe(1);

        backend.Finish(voice.Source);
        audio.Update().ShouldBe(0);
        voice.IsFinished.ShouldBeTrue();

        audio.Shutdown();
    }

    [Fact]
    public void A_clip_with_loop_points_is_played_through_a_queue_rather_than_a_single_buffer()
    {
        // AL_LOOPING repeats the whole buffer, so a loop region needs a queue.
        var backend = new FakeAudioBackend();
        var audio = NewManager(backend);

        AudioClip clip = audio.CreateClip(new AudioFormat(Rate, 1), Tone(4000), new LoopRegion(1000, 3000))!;

        // A looping clip keeps its samples on the CPU and uploads nothing yet.
        backend.Uploads.ShouldBeEmpty();

        AudioVoice voice = audio.Play(clip)!;
        voice.ShouldBeOfType<StreamingVoice>();
        backend.QueueDepth(voice.Source).ShouldBe(StreamingVoice.DefaultBufferCount);
        backend.StateOf(voice.Source).ShouldBe(AudioSourceState.Playing);

        audio.Shutdown();
    }

    [Fact]
    public void Destroying_a_clip_retires_the_voices_holding_its_buffer_first()
    {
        // AL refuses to delete a buffer a source still has bound, and it leaks.
        var backend = new FakeAudioBackend();
        var audio = NewManager(backend);

        AudioClip clip = audio.CreateClip(new AudioFormat(Rate, 1), Tone(600))!;
        AudioVoice voice = audio.Play(clip)!;
        backend.LiveBufferCount.ShouldBe(1);

        audio.DestroyClip(clip);

        voice.IsFinished.ShouldBeTrue();
        audio.ActiveVoiceCount.ShouldBe(0);
        backend.LiveBufferCount.ShouldBe(0);
        clip.IsDestroyed.ShouldBeTrue();

        // Stale handles do nothing.
        voice.Stop();
        audio.Play(clip).ShouldBeNull();

        audio.Shutdown();
    }

    [Fact]
    public void The_listener_reaches_the_driver_as_a_position_and_an_orientation_pair()
    {
        var backend = new FakeAudioBackend();
        var audio = NewManager(backend);

        audio.SetListener(new Vector3(1, 2, 3), -Vector3.UnitX, Vector3.UnitY, new Vector3(0, 0, 4));

        backend.ListenerPosition.ShouldBe(new Vector3(1, 2, 3));
        backend.ListenerForward.ShouldBe(-Vector3.UnitX);
        backend.ListenerUp.ShouldBe(Vector3.UnitY);
        backend.ListenerVelocity.ShouldBe(new Vector3(0, 0, 4));

        // A fader can round slightly negative; clamp, don't throw.
        audio.MasterGain = -0.25f;
        audio.MasterGain.ShouldBe(0f);
        backend.ListenerGain.ShouldBe(0f);

        audio.Shutdown();
    }

    [Fact]
    public void A_played_clip_reaches_the_driver_with_the_settings_it_was_given()
    {
        var backend = new FakeAudioBackend();
        var audio = NewManager(backend);
        AudioClip clip = audio.CreateClip(new AudioFormat(Rate, 1), Tone(600)).ShouldNotBeNull();

        AudioVoice voice = audio.Play(clip, Muffled).ShouldNotBeNull();

        AudioSourceSettings given = backend.SettingsOf(voice.Source);
        given.Gain.ShouldBe(0.4f);
        given.Pitch.ShouldBe(1.25f);
        given.Position.ShouldBe(new Vector3(3, 1, -2));
        given.Velocity.ShouldBe(new Vector3(0, 0, 5));
        given.Relative.ShouldBeFalse();
        given.GainHf.ShouldBe(0.3f);

        audio.Shutdown();
    }

    [Fact]
    public void A_stream_reaches_the_driver_with_the_settings_it_was_given()
    {
        var backend = new FakeAudioBackend();
        var audio = NewManager(backend);
        var provider = new RampSampleProvider(new AudioFormat(Rate, 1), 4000, LoopRegion.None);

        StreamingVoice voice = audio.PlayStream(provider, Muffled).ShouldNotBeNull();

        backend.SettingsOf(voice.Source).ShouldBe(Muffled);

        audio.Shutdown();
    }

    [Fact]
    public void Reconfiguring_a_playing_voice_reaches_the_driver()
    {
        var backend = new FakeAudioBackend();
        var audio = NewManager(backend);
        AudioClip clip = audio.CreateClip(new AudioFormat(Rate, 1), Tone(600)).ShouldNotBeNull();
        AudioVoice voice = audio.Play(clip, AudioSourceSettings.At(Vector3.Zero)).ShouldNotBeNull();

        voice.Configure(Muffled);

        backend.SettingsOf(voice.Source).ShouldBe(Muffled);
        voice.Settings.ShouldBe(Muffled);

        audio.Shutdown();
    }

    [Fact]
    public void A_reused_source_keeps_nothing_of_the_sound_it_carried_before()
    {
        var backend = new FakeAudioBackend(maxSources: 1);
        var audio = NewManager(backend, sources: 1);
        AudioClip clip = audio.CreateClip(new AudioFormat(Rate, 1), Tone(600)).ShouldNotBeNull();

        AudioVoice first = audio.Play(clip, Muffled).ShouldNotBeNull();
        uint source = first.Source;
        backend.Finish(source);
        audio.Update().ShouldBe(0);

        AudioVoice second = audio.Play(clip).ShouldNotBeNull();

        second.Source.ShouldBe(source);
        backend.SettingsOf(source).ShouldBe(AudioSourceSettings.Default);

        audio.Shutdown();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_sound_that_takes_a_source_configures_it_before_it_plays(bool looping)
    {
        // The source still has the last sound's filter until it is configured.
        var backend = new FakeAudioBackend(maxSources: 1);
        var audio = NewManager(backend, sources: 1);
        var format = new AudioFormat(Rate, 1);
        AudioClip shot = audio.CreateClip(format, Tone(600)).ShouldNotBeNull();
        AudioClip loop = audio.CreateClip(format, Tone(4000), new LoopRegion(0, 4000)).ShouldNotBeNull();
        uint source = audio.Play(shot, Muffled).ShouldNotBeNull().Source;

        AudioVoice next = audio.Play(looping ? loop : shot).ShouldNotBeNull();

        next.Source.ShouldBe(source);
        backend.SettingsWhenStarted(source).ShouldBe(AudioSourceSettings.Default);

        audio.Shutdown();
    }

    [Fact]
    public void Settings_are_unfiltered_unless_they_say_otherwise()
    {
        AudioSourceSettings.Default.GainHf.ShouldBe(1f);
        AudioSourceSettings.At(Vector3.One).GainHf.ShouldBe(1f);
        new AudioSourceSettings(1f, 1f, Vector3.Zero, Vector3.Zero, Relative: true).GainHf.ShouldBe(1f);
    }

    [Fact]
    public void Shutdown_frees_every_source_and_buffer_and_closes_the_device()
    {
        var backend = new FakeAudioBackend(maxSources: 4);
        var audio = NewManager(backend, sources: 4);

        AudioClip shot = audio.CreateClip(new AudioFormat(Rate, 1), Tone(600))!;
        AudioClip music = audio.CreateClip(new AudioFormat(Rate, 1), Tone(4000), new LoopRegion(0, 4000))!;
        audio.Play(shot);
        audio.Play(music);

        backend.LiveBufferCount.ShouldBe(1 + StreamingVoice.DefaultBufferCount);

        audio.Shutdown();

        backend.LiveBufferCount.ShouldBe(0);
        backend.LiveSourceCount.ShouldBe(0);
        backend.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public void An_exhausted_pool_drops_a_one_shot_rather_than_cutting_the_music()
    {
        var backend = new FakeAudioBackend(maxSources: 1);
        var audio = NewManager(backend, sources: 1);

        AudioClip music = audio.CreateClip(new AudioFormat(Rate, 1), Tone(4000), new LoopRegion(0, 4000))!;
        AudioClip shot = audio.CreateClip(new AudioFormat(Rate, 1), Tone(600))!;

        audio.Play(music).ShouldNotBeNull();
        audio.Play(shot).ShouldBeNull();
        audio.DroppedVoiceCount.ShouldBe(1);

        audio.Shutdown();
    }

    [Fact]
    public void A_stream_can_start_part_way_into_its_sound()
    {
        var backend = new FakeAudioBackend();
        var audio = NewManager(backend);
        var provider = new RampSampleProvider(new AudioFormat(Rate, 1), 100_000, LoopRegion.None);

        audio.PlayStream(provider, AudioSourceSettings.Default, startFrame: 30_000).ShouldNotBeNull();

        backend.Uploads[0][0].ShouldBe((short)30_000);
        Should.Throw<ArgumentOutOfRangeException>(
            () => audio.PlayStream(provider, AudioSourceSettings.Default, startFrame: -1));
        audio.ActiveVoiceCount.ShouldBe(1);

        audio.Shutdown();
    }

    [Fact]
    public void A_released_voice_gives_its_source_back_at_once()
    {
        var backend = new FakeAudioBackend(maxSources: 1);
        var audio = NewManager(backend, sources: 1);
        var provider = new RampSampleProvider(new AudioFormat(Rate, 1), 100_000, new LoopRegion(0, 100_000));
        StreamingVoice first = audio.PlayStream(provider, AudioSourceSettings.Default).ShouldNotBeNull();

        audio.Release(first);

        first.IsFinished.ShouldBeTrue();
        audio.ActiveVoiceCount.ShouldBe(0);
        backend.LiveBufferCount.ShouldBe(0);

        // No Update in between: the one source is free again already.
        audio.PlayStream(provider, AudioSourceSettings.Default).ShouldNotBeNull();
        audio.DroppedVoiceCount.ShouldBe(0);

        audio.Shutdown();
    }

    [Fact]
    public void Releasing_a_voice_twice_or_one_the_manager_does_not_hold_does_nothing()
    {
        var backend = new FakeAudioBackend(maxSources: 1);
        var audio = NewManager(backend, sources: 1);
        AudioClip clip = audio.CreateClip(new AudioFormat(Rate, 1), Tone(600)).ShouldNotBeNull();
        AudioVoice first = audio.Play(clip).ShouldNotBeNull();
        audio.Release(first);
        AudioVoice second = audio.Play(clip).ShouldNotBeNull();

        audio.Release(first);
        audio.Release(null);

        second.IsFinished.ShouldBeFalse();
        backend.StateOf(second.Source).ShouldBe(AudioSourceState.Playing);
        audio.ActiveVoiceCount.ShouldBe(1);

        audio.Shutdown();
    }

    [Fact]
    public void A_voice_whose_source_went_to_a_new_sound_is_over_and_cannot_touch_it()
    {
        var backend = new FakeAudioBackend(maxSources: 1);
        var audio = NewManager(backend, sources: 1);
        AudioClip clip = audio.CreateClip(new AudioFormat(Rate, 1), Tone(600)).ShouldNotBeNull();
        AudioVoice first = audio.Play(clip).ShouldNotBeNull();

        // The pool is full, so the new sound takes the one playing.
        AudioVoice second = audio.Play(clip, Muffled).ShouldNotBeNull();
        audio.StolenVoiceCount.ShouldBe(1);

        first.IsFinished.ShouldBeTrue();
        first.Configure(AudioSourceSettings.Default);
        backend.SettingsOf(second.Source).ShouldBe(Muffled);
        audio.Update().ShouldBe(1);

        audio.Shutdown();
    }

    [Fact]
    public void A_stopped_voice_does_not_end_the_sound_that_took_its_source()
    {
        var backend = new FakeAudioBackend(maxSources: 1);
        var audio = NewManager(backend, sources: 1);
        AudioClip clip = audio.CreateClip(new AudioFormat(Rate, 1), Tone(600)).ShouldNotBeNull();
        AudioVoice first = audio.Play(clip).ShouldNotBeNull();

        first.Stop();
        AudioVoice second = audio.Play(clip).ShouldNotBeNull();

        audio.Update().ShouldBe(1);
        second.IsFinished.ShouldBeFalse();
        backend.StateOf(second.Source).ShouldBe(AudioSourceState.Playing);
        audio.StolenVoiceCount.ShouldBe(0);

        audio.Shutdown();
    }

    private static AudioManager NewManager(FakeAudioBackend backend, int sources = AudioManager.DefaultSourceCount)
    {
        var audio = new AudioManager(new CapturingLogger(), Supply(backend), sources);
        audio.Initialize();
        audio.IsEnabled.ShouldBeTrue();
        return audio;
    }

    private static AudioBackendFactory Supply(IAudioBackend backend) =>
        (ILogger _, [NotNullWhen(true)] out IAudioBackend? created, out string reason) =>
        {
            created = backend;
            reason = string.Empty;
            return true;
        };

    private static AudioBackendFactory FailingBackend(string reason) =>
        (ILogger _, [NotNullWhen(true)] out IAudioBackend? created, out string failure) =>
        {
            created = null;
            failure = reason;
            return false;
        };

    // A ramp, so frames differ.
    private static short[] Tone(int frames)
    {
        var pcm = new short[frames];
        for (int i = 0; i < frames; i++) pcm[i] = (short)(i % short.MaxValue);
        return pcm;
    }
}
