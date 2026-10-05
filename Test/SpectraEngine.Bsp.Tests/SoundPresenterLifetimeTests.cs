using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// When a level's sounds have no voice at all: no level, another level, a
/// stopped sound, a node out of the scene, a sound that cannot be played.
/// </summary>
public sealed class SoundPresenterLifetimeTests
{
    [Fact]
    public void When_the_level_ends_a_sound_other_code_plays_goes_on()
    {
        using var rig = new SoundPresenterRig();
        AudioClip clip = rig.Audio.CreateClip(new AudioFormat(SoundPresenterRig.Rate, 1), new short[600]).ShouldNotBeNull();
        AudioVoice preview = rig.Audio.Play(clip).ShouldNotBeNull();
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Frame();
        rig.Audio.ActiveVoiceCount.ShouldBe(2);

        rig.Presenter.Update(null, SoundPresenterRig.TickSeconds);

        preview.IsFinished.ShouldBeFalse();
        rig.Audio.ActiveVoiceCount.ShouldBe(1);
    }

    [Fact]
    public void When_the_level_ends_every_voice_stops_and_the_device_is_as_it_was()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Place("a", new Vector3(1, 0, 0)), SoundPresenterRig.Beep, SoundPresenterRig.Once);
        rig.Play(rig.Place("b", new Vector3(0, 0, 1)), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Play(rig.Place("c", new Vector3(-1, 0, 0)), SoundPresenterRig.Music, SoundPresenterRig.Looped);
        rig.Frame();
        rig.Audio.ActiveVoiceCount.ShouldBe(3);

        rig.Presenter.Update(null, SoundPresenterRig.TickSeconds);

        rig.Audio.ActiveVoiceCount.ShouldBe(0);
        rig.Backend.PlayingSources().ShouldBeEmpty();
        rig.Backend.LiveBufferCount.ShouldBe(0);
        rig.Stats.ShouldBe(default(SoundStats));
        EveryFreeSourceCanBeHad(rig);
    }

    [Fact]
    public void A_sound_that_is_playing_out_stops_when_the_level_ends()
    {
        using var rig = new SoundPresenterRig();
        int id = rig.Play(rig.Scene.Root, SoundPresenterRig.Speech, SoundPresenterRig.Once);
        rig.Frame();
        rig.Tick(120);
        rig.World.Sounds.Stop(id);
        rig.Frame();
        rig.Backend.PlayingSources().ShouldHaveSingleItem();

        rig.Presenter.Update(null, SoundPresenterRig.TickSeconds);

        rig.Audio.ActiveVoiceCount.ShouldBe(0);
        rig.Backend.PlayingSources().ShouldBeEmpty();
        rig.Backend.LiveBufferCount.ShouldBe(0);
    }

    [Fact]
    public void A_level_that_was_stopped_is_no_level_even_while_its_world_is_still_handed_over()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Once);
        rig.Frame();
        rig.Backend.LiveBufferCount.ShouldBe(1);

        rig.World.Deactivate();
        rig.Frame();

        // The buffer the sound was played from is the level's and goes with
        // it. A sound that only stopped would leave it for the next play.
        rig.Audio.ActiveVoiceCount.ShouldBe(0);
        rig.Backend.LiveBufferCount.ShouldBe(0);
    }

    [Fact]
    public void Another_level_starts_with_no_voice_of_the_one_before()
    {
        using var rig = new SoundPresenterRig();
        SceneNode first = rig.Place("first", new Vector3(1, 0, 0));
        SceneNode second = rig.Place("second", new Vector3(-1, 0, 0));
        int id = rig.Play(first, SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Frame();
        rig.Tick(30);

        // Stopped and started between two frames. The new world's first
        // sound has the id the old one's had.
        rig.World.Deactivate();
        rig.StartLevel();
        rig.Play(second, SoundPresenterRig.Speech, SoundPresenterRig.Once).ShouldBe(id);
        rig.Backend.Uploads.Clear();
        rig.Frame();

        rig.HasVoiceAt(first.WorldPosition).ShouldBeFalse();
        rig.HasVoiceAt(second.WorldPosition).ShouldBeTrue();
        rig.Audio.ActiveVoiceCount.ShouldBe(1);
        rig.Backend.Uploads[0][0].ShouldBe(HandBuiltSaudio.Sample(0));
    }

    [Fact]
    public void A_sound_that_is_stopped_gives_its_source_back()
    {
        using var rig = new SoundPresenterRig();
        int stays = rig.Play(rig.Place("a", new Vector3(1, 0, 0)), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        int stops = rig.Play(rig.Place("b", new Vector3(-1, 0, 0)), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Frame();

        rig.World.Sounds.Stop(stops);
        rig.Frame();

        rig.HasVoiceAt(new Vector3(1, 0, 0)).ShouldBeTrue();
        rig.HasVoiceAt(new Vector3(-1, 0, 0)).ShouldBeFalse();
        rig.Audio.ActiveVoiceCount.ShouldBe(1);
        rig.Stats.Playing.ShouldBe(1);
        rig.World.Sounds.TryGet(stays, out _).ShouldBeTrue();
    }

    [Fact]
    public void A_sound_on_a_node_out_of_the_scene_is_silent_until_an_undo_puts_the_node_back()
    {
        using var rig = new SoundPresenterRig();
        SceneNode speaker = rig.Place("speaker", new Vector3(1, 0, 0));
        int id = rig.Play(speaker, SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Frame();
        rig.Tick(30);

        rig.Scene.Root.RemoveChild(speaker);
        rig.Frame();

        rig.Backend.PlayingSources().ShouldBeEmpty();
        rig.Stats.ShouldBe(new SoundStats(Playing: 1, WithSource: 0, WithoutSource: 0, RefusedStarts: 0, Unplayable: 0));

        // What an undo of a delete does: a new node under the old id.
        var restored = new SceneNode("speaker", speaker.Id) { LocalPosition = new Vector3(0, 0, 1) };
        rig.Scene.Root.AddChild(restored);
        rig.Backend.Uploads.Clear();
        rig.Frame();

        rig.HasVoiceAt(new Vector3(0, 0, 1)).ShouldBeTrue();
        rig.World.Sounds.TryGet(id, out SoundEmitter emitter).ShouldBeTrue();
        rig.Backend.Uploads[0][0].ShouldBe(HandBuiltSaudio.Sample(emitter.PositionAt(rig.World.TickNumber).Frame));
    }

    [Fact]
    public void A_sound_that_cannot_be_loaded_is_counted_and_the_rest_still_play()
    {
        using var rig = new SoundPresenterRig();
        var missing = new SoundDescription(SoundPresenterRig.Rate, SoundPresenterRig.Rate);
        rig.World.Sounds.Play(rig.Scene.Root, "Sounds/nothing.wav", in missing, SoundPresenterRig.Looped);
        rig.World.Sounds.Play(rig.Scene.Root, "Sounds/nothing.wav", in missing, SoundPresenterRig.Looped);
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Looped);

        rig.Frame(3);

        rig.Stats.ShouldBe(new SoundStats(Playing: 3, WithSource: 1, WithoutSource: 0, RefusedStarts: 0, Unplayable: 2));
        rig.Stats.Silent.ShouldBe(0);
        rig.Audio.ActiveVoiceCount.ShouldBe(1);
    }

    [Fact]
    public void A_file_that_is_not_the_sound_the_level_counted_is_not_played()
    {
        using var rig = new SoundPresenterRig();

        // The level thinks the sound is twice as long as the cooked file is.
        var other = new SoundDescription(2 * SoundPresenterRig.Rate, SoundPresenterRig.Rate);
        rig.World.Sounds.Play(rig.Scene.Root, SoundPresenterRig.Beep, in other, SoundPresenterRig.Looped);

        rig.Frame(3);

        rig.Audio.ActiveVoiceCount.ShouldBe(0);
        rig.Stats.Unplayable.ShouldBe(1);
    }

    [Fact]
    public void A_sound_that_cannot_be_loaded_is_named_in_the_log_once()
    {
        using var rig = new SoundPresenterRig();
        var missing = new SoundDescription(SoundPresenterRig.Rate, SoundPresenterRig.Rate);
        rig.World.Sounds.Play(rig.Scene.Root, "Sounds/nothing.wav", in missing, SoundPresenterRig.Looped);
        rig.World.Sounds.Play(rig.Scene.Root, "Sounds/nothing.wav", in missing, SoundPresenterRig.Looped);

        rig.Frame(3);

        string warning = rig.Log.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        warning.ShouldStartWith("Sound Sounds/nothing.wav will not be heard: ");
    }

    [Fact]
    public void A_file_that_is_not_the_sound_the_level_counted_is_named_in_the_log_once()
    {
        using var rig = new SoundPresenterRig();
        var other = new SoundDescription(2 * SoundPresenterRig.Rate, SoundPresenterRig.Rate);
        rig.World.Sounds.Play(rig.Scene.Root, SoundPresenterRig.Beep, in other, SoundPresenterRig.Looped);
        rig.World.Sounds.Play(rig.Scene.Root, SoundPresenterRig.Beep, in other, SoundPresenterRig.Looped);

        rig.Frame(3);

        rig.Log.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem().ShouldBe(
            "Sound Sounds/beep.wav will not be heard: the level counted 96000 frames and the file has 48000");
    }

    [Fact]
    public void A_sound_that_plays_leaves_the_log_alone()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Once);
        rig.Play(rig.Scene.Root, SoundPresenterRig.Music, SoundPresenterRig.Looped);

        rig.Frame(3);

        rig.Log.Describe().ShouldBe("(no log entries)");
    }

    [Fact]
    public void A_sound_unloaded_while_the_level_runs_is_opened_again_for_its_next_play()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Place("first", new Vector3(1, 0, 0)), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Frame();

        rig.Assets.UnloadAudio(SoundPresenterRig.Beep).ShouldBeTrue();
        rig.Play(rig.Place("second", new Vector3(-1, 0, 0)), SoundPresenterRig.Beep, SoundPresenterRig.Once);
        rig.Frame();

        // A whole second in one buffer, not the nothing the released sound holds.
        rig.Backend.Uploads[^1].Length.ShouldBe(SoundPresenterRig.Rate);
        rig.HasVoiceAt(new Vector3(-1, 0, 0)).ShouldBeTrue();
        rig.Stats.Unplayable.ShouldBe(0);
    }

    [Fact]
    public void With_no_audio_device_the_sounds_are_counted_and_nothing_else_happens()
    {
        using var rig = new SoundPresenterRig();
        var audio = new AudioManager(new CapturingLogger(), NoDevice);
        audio.Initialize();
        var presenter = new SoundPresenter(audio, rig.Assets, new DirectPropagation(), rig.Log);
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Looped);

        presenter.Update(rig.World, SoundPresenterRig.TickSeconds);
        presenter.Update(rig.World, SoundPresenterRig.TickSeconds);

        presenter.Stats.ShouldBe(new SoundStats(Playing: 1, WithSource: 0, WithoutSource: 1, RefusedStarts: 0, Unplayable: 0));
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("map load")]
    [InlineData("save")]
    public void Each_way_a_hosted_level_ends_leaves_no_voice(string ending)
    {
        using var rig = new SoundPresenterRig();
        rig.Cook(SpawnSoundEntity.Path, HandBuiltSaudio.Resident(frames: SoundPresenterRig.Rate));
        SceneManager manager = Hosted(rig);
        EntityRuntime.Place(manager.ActiveScene.ShouldNotBeNull().Root, "speaker", "spawn_sound");
        manager.StartEntityWorld();
        rig.Presenter.Update(manager.EntityWorld, SoundPresenterRig.TickSeconds);
        rig.Audio.ActiveVoiceCount.ShouldBe(1);

        switch (ending)
        {
            case "stop": manager.StopEntityWorld(); break;
            case "map load": manager.OnSceneReplaced(); break;
            default: manager.TakeAuthoredMap().ShouldNotBeNull(); break;
        }

        rig.Presenter.Update(manager.EntityWorld, SoundPresenterRig.TickSeconds);

        rig.Audio.ActiveVoiceCount.ShouldBe(0);
        rig.Backend.PlayingSources().ShouldBeEmpty();
        rig.Backend.LiveBufferCount.ShouldBe(0);
    }

    // The pool hands out every source it has without taking one from a
    // playing sound, which it could not if a voice had been left on one.
    private static void EveryFreeSourceCanBeHad(SoundPresenterRig rig)
    {
        var provider = new RampSampleProvider(new AudioFormat(SoundPresenterRig.Rate, 1), 100_000, new LoopRegion(0, 100_000));
        for (int i = 0; i < rig.Audio.SourceCount; i++)
            rig.Audio.PlayStream(provider, AudioSourceSettings.Default).ShouldNotBeNull();

        rig.Audio.StolenVoiceCount.ShouldBe(0);
        rig.Audio.DroppedVoiceCount.ShouldBe(0);
    }

    // Own catalogue: EntityCatalog.Shared freezes on first read.
    private static SceneManager Hosted(SoundPresenterRig rig)
    {
        EntityCatalog catalog = EntityRuntime.Catalog([]);
        catalog.Add(new EntitySchema("spawn_sound"), () => new SpawnSoundEntity());

        var manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = catalog,
        };

        manager.LoadStartupScene(new FakeRenderer(), rig.Assets);
        return manager;
    }

    private static bool NoDevice(ILogger logger, [NotNullWhen(true)] out IAudioBackend? backend, out string reason)
    {
        backend = null;
        reason = "the test has no audio device";
        return false;
    }
}
