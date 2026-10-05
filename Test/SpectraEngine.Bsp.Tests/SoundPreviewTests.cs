using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Audio;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A sound played by itself to be listened to: one voice at the listener,
/// one at a time, that stops when it is told to or when it ends.
/// </summary>
public sealed class SoundPreviewTests
{
    [Fact]
    public void A_preview_is_one_voice_at_the_listener_at_full_gain()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = PreviewOn(rig);

        preview.Play(SoundPresenterRig.Beep, out string refusal).ShouldBeTrue(refusal);

        preview.Path.ShouldBe(SoundPresenterRig.Beep);
        preview.IsPlaying.ShouldBeTrue();
        AudioSourceSettings settings = rig.OnlyVoice();
        settings.Gain.ShouldBe(1f);
        settings.Pitch.ShouldBe(1f);
        settings.Relative.ShouldBeTrue();
        settings.Position.ShouldBe(Vector3.Zero);
        settings.GainHf.ShouldBe(1f);
    }

    [Fact]
    public void A_preview_is_not_one_of_the_pools_voices()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = PreviewOn(rig);

        preview.Play(SoundPresenterRig.Beep, out _).ShouldBeTrue();

        rig.Audio.ActiveVoiceCount.ShouldBe(0);
        rig.Audio.SourceCount.ShouldBe(AudioManager.DefaultSourceCount);
        rig.Audio.Preview.IsPlaying.ShouldBeTrue();
    }

    [Fact]
    public void A_second_request_starts_over_with_the_new_file()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = PreviewOn(rig);
        preview.Play(SoundPresenterRig.Beep, out _).ShouldBeTrue();
        rig.Backend.Uploads.Clear();

        preview.Play(SoundPresenterRig.Music, out string refusal).ShouldBeTrue(refusal);

        preview.Path.ShouldBe(SoundPresenterRig.Music);
        rig.Backend.PlayingSources().ShouldHaveSingleItem();

        // Stereo, and from its first frame.
        rig.Backend.Uploads[0].Length.ShouldBe(StreamingVoice.DefaultBufferFrames * 2);
        rig.Backend.Uploads[0][0].ShouldBe(HandBuiltSaudio.Sample(0));
    }

    [Fact]
    public void Asking_for_the_same_file_again_plays_it_from_its_start()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = PreviewOn(rig);
        preview.Play(SoundPresenterRig.Speech, out _).ShouldBeTrue();
        RunFrames(rig, preview, 3);
        rig.Backend.Uploads.Clear();

        preview.Play(SoundPresenterRig.Speech, out _).ShouldBeTrue();

        rig.Backend.PlayingSources().ShouldHaveSingleItem();
        rig.Backend.Uploads[0][0].ShouldBe(HandBuiltSaudio.Sample(0));
    }

    [Fact]
    public void Stop_stops_it_and_says_whether_anything_was_playing()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = PreviewOn(rig);
        preview.Play(SoundPresenterRig.Beep, out _).ShouldBeTrue();

        preview.Stop().ShouldBeTrue();

        preview.Path.ShouldBeEmpty();
        rig.Backend.PlayingSources().ShouldBeEmpty();
        rig.Backend.LiveBufferCount.ShouldBe(0);
        preview.Stop().ShouldBeFalse();
    }

    [Fact]
    public void A_preview_ends_by_itself_and_then_names_no_file()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = PreviewOn(rig);
        preview.Play(SoundPresenterRig.Beep, out _).ShouldBeTrue();

        RunFrames(rig, preview, 2);
        preview.Path.ShouldBe(SoundPresenterRig.Beep);

        // One second is six buffers. The device finishes one a frame.
        RunFrames(rig, preview, 10);

        preview.Path.ShouldBeEmpty();
        preview.IsPlaying.ShouldBeFalse();
        rig.Backend.PlayingSources().ShouldBeEmpty();
        rig.Backend.LiveBufferCount.ShouldBe(0);
    }

    [Fact]
    public void A_sound_with_a_loop_region_is_played_once_through()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        rig.Cook("Sounds/hum.wav", HandBuiltSaudio.Resident(frames: 9_000, loopStart: 1_000, loopEnd: 8_000));
        SoundPreview preview = PreviewOn(rig);
        preview.Play("Sounds/hum.wav", out string refusal).ShouldBeTrue(refusal);

        RunFrames(rig, preview, 6);

        preview.Path.ShouldBeEmpty();
    }

    [Fact]
    public void A_path_is_reported_the_way_a_level_stores_it()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = PreviewOn(rig);

        preview.Play(@"\Sounds\beep.wav", out string refusal).ShouldBeTrue(refusal);

        preview.Path.ShouldBe(SoundPresenterRig.Beep);
    }

    [Fact]
    public void A_file_that_does_not_load_leaves_no_voice_and_one_log_line()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        var log = new CapturingLogger();
        var preview = new SoundPreview(rig.Audio, rig.Assets, log);

        preview.Apply("Sounds/nothing.wav");
        RunFrames(rig, preview, 5);

        preview.Path.ShouldBeEmpty();
        rig.Backend.PlayingSources().ShouldBeEmpty();
        string line = log.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        line.ShouldStartWith("Sound Sounds/nothing.wav was not played: ");
        log.MessagesAt(LogLevel.Error).ShouldBeEmpty();
    }

    [Fact]
    public void A_file_that_does_not_load_stops_the_preview_that_was_playing()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = PreviewOn(rig);
        preview.Play(SoundPresenterRig.Beep, out _).ShouldBeTrue();

        preview.Play("../outside.wav", out string refusal).ShouldBeFalse();

        refusal.ShouldNotBeEmpty();
        preview.Path.ShouldBeEmpty();
        rig.Backend.PlayingSources().ShouldBeEmpty();
    }

    [Fact]
    public void A_host_request_plays_a_file_and_an_empty_one_stops_it()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = PreviewOn(rig);

        preview.Apply(SoundPresenterRig.Beep);
        preview.Path.ShouldBe(SoundPresenterRig.Beep);

        preview.Apply(string.Empty);
        preview.Path.ShouldBeEmpty();
        rig.Backend.PlayingSources().ShouldBeEmpty();
    }

    [Fact]
    public void With_no_audio_device_a_preview_is_refused_with_the_reason()
    {
        using var rig = new SoundPresenterRig();
        var audio = new AudioManager(new CapturingLogger(), NoDevice);
        audio.Initialize();
        var preview = new SoundPreview(audio, rig.Assets, new CapturingLogger());

        preview.Play(SoundPresenterRig.Beep, out string refusal).ShouldBeFalse();

        refusal.ShouldBe("audio is off: the test has no audio device");
        preview.Path.ShouldBeEmpty();
    }

    [Fact]
    public void A_preview_does_not_outlive_the_audio_device()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = PreviewOn(rig);
        preview.Play(SoundPresenterRig.Beep, out _).ShouldBeTrue();

        rig.Audio.Shutdown();

        rig.Backend.LiveSourceCount.ShouldBe(0);
        rig.Backend.LiveBufferCount.ShouldBe(0);
        rig.Audio.Preview.IsPlaying.ShouldBeFalse();
    }

    [Fact]
    public void Nothing_allocates_a_frame_once_a_preview_runs()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        rig.Cook("Sounds/long.wav", HandBuiltSaudio.Resident(frames: 400 * StreamingVoice.DefaultBufferFrames));
        SoundPreview preview = PreviewOn(rig);
        rig.Play(rig.Place("hum", new Vector3(0, 0, -1)), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        preview.Play("Sounds/long.wav", out string refusal).ShouldBeTrue(refusal);

        rig.Backend.KeepsUploads = false;
        RunFrames(rig, preview, 30);

        // The least of several rounds: a one-off from the runtime is not a
        // cost per frame.
        long least = long.MaxValue;
        int uploads = rig.Backend.UploadCount;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            RunFrames(rig, preview, 60);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        least.ShouldBe(0L);
        preview.Path.ShouldBe("Sounds/long.wav");

        // The preview and the level's loop each refilled a buffer every frame.
        (rig.Backend.UploadCount - uploads).ShouldBe(5 * 60 * 2);
    }

    // What the engine does for sound each frame, with the device finishing a
    // buffer of every queue.
    internal static void RunFrames(SoundPresenterRig rig, SoundPreview preview, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            rig.World.Tick(SoundPresenterRig.TickSeconds);
            rig.Backend.ConsumeOneEverywhere();
            rig.Audio.Update();
            preview.Update();
            rig.Presenter.Update(rig.World, SoundPresenterRig.TickSeconds);
        }
    }

    internal static SoundPreview PreviewOn(SoundPresenterRig rig) =>
        new(rig.Audio, rig.Assets, new CapturingLogger());

    private static bool NoDevice(ILogger logger, [NotNullWhen(true)] out IAudioBackend? backend, out string reason)
    {
        backend = null;
        reason = "the test has no audio device";
        return false;
    }
}
