using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Who gets a source when there are more sounds than sources, and where a
/// sound picks up when it gets one late.
/// </summary>
public sealed class SoundPresenterSourceTests
{
    private static readonly Vector3 Near = new(0, 0, -1);
    private static readonly Vector3 Middle = new(0, 0, -4);
    private static readonly Vector3 Far = new(0, 0, -8);

    [Fact]
    public void With_more_sounds_than_sources_the_loudest_have_them_and_the_rest_are_counted()
    {
        using var rig = new SoundPresenterRig(sources: 2);
        PlayLoopAt(rig, Far);
        PlayLoopAt(rig, Near);
        PlayLoopAt(rig, Middle);

        rig.Frame();

        rig.HasVoiceAt(Near).ShouldBeTrue();
        rig.HasVoiceAt(Middle).ShouldBeTrue();
        rig.HasVoiceAt(Far).ShouldBeFalse();
        rig.Stats.ShouldBe(new SoundStats(Playing: 3, WithSource: 2, WithoutSource: 1, RefusedStarts: 0, Unplayable: 0));
        rig.Audio.StolenVoiceCount.ShouldBe(0);
        rig.Audio.DroppedVoiceCount.ShouldBe(0);
    }

    [Fact]
    public void A_sound_that_becomes_clearly_the_loudest_takes_a_source_and_starts_where_the_simulation_is()
    {
        using var rig = new SoundPresenterRig(sources: 2);
        PlayLoopAt(rig, Near);
        PlayLoopAt(rig, Middle);
        int late = PlayLoopAt(rig, Far);
        rig.Frame();
        rig.Tick(30);
        rig.Backend.Uploads.Clear();

        // Beside the third sound now, and the first is the furthest away.
        rig.Listen(Far);
        rig.Frame(60);

        rig.HasVoiceAt(Far).ShouldBeTrue();
        rig.HasVoiceAt(Middle).ShouldBeTrue();
        rig.HasVoiceAt(Near).ShouldBeFalse();
        rig.Stats.WithoutSource.ShouldBe(1);

        rig.World.Sounds.TryGet(late, out SoundEmitter emitter).ShouldBeTrue();
        long frame = emitter.PositionAt(rig.World.TickNumber).Frame;
        frame.ShouldBe(24_000L);
        rig.Backend.Uploads[0][0].ShouldBe(HandBuiltSaudio.Sample(frame));
    }

    [Fact]
    public void Two_equally_loud_sounds_do_not_swap()
    {
        using var rig = new SoundPresenterRig(sources: 1);
        var left = new Vector3(-4, 0, 0);
        var right = new Vector3(4, 0, 0);
        PlayLoopAt(rig, left);
        PlayLoopAt(rig, right);
        rig.Frame();
        int uploads = rig.Backend.Uploads.Count;

        for (int i = 0; i < 120; i++)
        {
            rig.Tick();
            rig.Frame();
        }

        // The one that started first has the source, and nothing started since.
        rig.HasVoiceAt(left).ShouldBeTrue();
        rig.HasVoiceAt(right).ShouldBeFalse();
        rig.Backend.Uploads.Count.ShouldBe(uploads);
    }

    [Fact]
    public void A_sound_a_little_louder_than_the_one_with_the_source_does_not_take_it()
    {
        using var rig = new SoundPresenterRig(sources: 1);
        var left = new Vector3(-4, 0, 0);
        var right = new Vector3(4, 0, 0);
        PlayLoopAt(rig, left);
        PlayLoopAt(rig, right);
        rig.Frame();

        // A third louder: under the margin.
        rig.Listen(new Vector3(0.5f, 0, 0));
        rig.Frame(120);
        rig.HasVoiceAt(left).ShouldBeTrue();

        // Nearly twice as loud: over it.
        rig.Listen(new Vector3(1f, 0, 0));
        rig.Frame(120);
        rig.HasVoiceAt(right).ShouldBeTrue();
        rig.HasVoiceAt(left).ShouldBeFalse();
    }

    [Fact]
    public void A_loop_that_loses_its_source_and_gets_one_back_resumes_in_step()
    {
        using var rig = new SoundPresenterRig(sources: 1);
        int hum = PlayLoopAt(rig, Near);
        PlayLoopAt(rig, new Vector3(0, 0, -50));
        rig.Frame();
        rig.HasVoiceAt(Near).ShouldBeTrue();

        // Beside the other sound, the hum is out of earshot.
        rig.Tick(30);
        rig.Listen(new Vector3(0, 0, -49));
        rig.Frame();
        rig.HasVoiceAt(Near).ShouldBeFalse();

        // It goes on counting while nothing plays it: once round and a quarter.
        rig.Tick(45);
        rig.Backend.Uploads.Clear();
        rig.Listen(Vector3.Zero);
        rig.Frame();

        rig.HasVoiceAt(Near).ShouldBeTrue();
        rig.World.Sounds.TryGet(hum, out SoundEmitter emitter).ShouldBeTrue();
        SoundPosition position = emitter.PositionAt(rig.World.TickNumber);
        position.ShouldBe(new SoundPosition(Pass: 1, Frame: 12_000));
        rig.Backend.Uploads[0][0].ShouldBe(HandBuiltSaudio.Sample(position.Frame));
    }

    [Fact]
    public void A_sound_that_has_ended_in_the_simulation_has_no_voice_though_the_device_still_plays_it()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Once);
        rig.Frame();
        uint source = rig.Backend.PlayingSources().ShouldHaveSingleItem();

        // One second of ticks. Nothing stops the emitter and the device was
        // never told the sound is over.
        rig.Tick(60);
        rig.World.Sounds.Count.ShouldBe(1);
        rig.Backend.StateOf(source).ShouldBe(AudioSourceState.Playing);
        rig.Frame();

        rig.Backend.PlayingSources().ShouldBeEmpty();
        rig.Audio.ActiveVoiceCount.ShouldBe(0);
        rig.Stats.ShouldBe(new SoundStats(Playing: 1, WithSource: 0, WithoutSource: 0, RefusedStarts: 0, Unplayable: 0));
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

    [Fact]
    public void A_source_someone_else_plays_on_is_not_the_levels_to_take()
    {
        using var rig = new SoundPresenterRig(sources: 2);
        AudioClip clip = rig.Audio.CreateClip(new AudioFormat(SoundPresenterRig.Rate, 1), new short[600]).ShouldNotBeNull();
        AudioVoice preview = rig.Audio.Play(clip).ShouldNotBeNull();
        PlayLoopAt(rig, Near);
        PlayLoopAt(rig, Middle);

        rig.Frame(5);

        preview.IsFinished.ShouldBeFalse();
        rig.Audio.StolenVoiceCount.ShouldBe(0);
        rig.HasVoiceAt(Near).ShouldBeTrue();
        rig.HasVoiceAt(Middle).ShouldBeFalse();
        rig.Stats.ShouldBe(new SoundStats(Playing: 2, WithSource: 1, WithoutSource: 1, RefusedStarts: 0, Unplayable: 0));
    }

    [Fact]
    public void Two_hundred_sounds_on_thirty_two_sources_allocate_nothing_a_frame_once_running()
    {
        using var rig = new SoundPresenterRig(sources: 32);
        SceneNode door = rig.Place("door", Vector3.Zero);
        for (int i = 0; i < 200; i++)
        {
            SceneNode speaker = door.CreateChild($"speaker{i}");
            speaker.LocalPosition = new Vector3((i % 20) - 10, 0, -(i / 20) - 1);
            rig.Play(speaker, i % 3 == 0 ? SoundPresenterRig.Music : SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        }

        RunFrames(rig, door, 120);
        rig.Stats.WithSource.ShouldBe(32);
        rig.Stats.WithoutSource.ShouldBe(168);

        // The least of several rounds: a one-off from the runtime is not a
        // cost per frame.
        long least = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            RunFrames(rig, door, 100);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        least.ShouldBe(0L);
        rig.Stats.WithSource.ShouldBe(32);
    }

    // A tick and a frame each, with the sounds' parent swaying a little so
    // every source is moved every frame.
    private static void RunFrames(SoundPresenterRig rig, SceneNode door, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            door.LocalPosition = new Vector3(0, (i % 2) * 0.01f, 0);
            rig.World.Tick(SoundPresenterRig.TickSeconds);
            rig.Audio.Update();
            rig.Presenter.Update(rig.World, SoundPresenterRig.TickSeconds);
        }
    }

    private static int PlayLoopAt(SoundPresenterRig rig, Vector3 position) =>
        rig.Play(rig.Place($"speaker at {position}", position), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
}
