using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// The sound entity run through a real <see cref="EntityWorld"/> with no audio
/// device: what it registers, when it ends, and what its inputs change.
/// </summary>
public sealed class PointSoundTests
{
    private const string OnEnded = "sink:OnEnded:";

    private readonly SoundRig _rig = new();

    [Fact]
    public void Play_registers_the_sound_on_its_node_with_its_settings()
    {
        SceneNode node = _rig.Sound(
            SoundRig.OneSecond,
            ("volume", "0.5"), ("pitch", "2"), ("mindistance", "3"), ("maxdistance", "40"), ("looped", "1"));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);
        world.Sounds.Count.ShouldBe(0);

        EntityRuntime.Send(sound, "Play").ShouldBeTrue();

        sound.IsPlaying.ShouldBeTrue();
        world.Sounds.Count.ShouldBe(1);
        SoundEmitter emitter = world.Sounds.Playing[0];
        emitter.Node.ShouldBeSameAs(node);
        emitter.Path.ShouldBe(SoundRig.OneSecond);
        emitter.Gain.ShouldBe(0.5f);
        emitter.Pitch.ShouldBe(2f);
        emitter.MinDistance.ShouldBe(3f);
        emitter.MaxDistance.ShouldBe(40f);
        emitter.IsLooped.ShouldBeTrue();
        emitter.FrameCount.ShouldBe(SoundRig.Rate);
    }

    [Fact]
    public void A_sound_with_nothing_set_plays_at_full_volume_and_reaches_from_2_to_30()
    {
        SceneNode node = _rig.Sound();
        EntityWorld world = _rig.Start();

        EntityRuntime.Send(EntityRuntime.Live<PointSound>(world, node), "Play");

        SoundEmitter emitter = world.Sounds.Playing[0];
        emitter.Gain.ShouldBe(1f);
        emitter.Pitch.ShouldBe(1f);
        emitter.MinDistance.ShouldBe(2f);
        emitter.MaxDistance.ShouldBe(30f);
        emitter.IsLooped.ShouldBeFalse();
    }

    [Fact]
    public void Stop_takes_the_sound_off_the_registry_and_fires_nothing()
    {
        SceneNode node = _rig.Sound();
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);
        EntityRuntime.Send(sound, "Play");
        Movers.Run(world, 10);

        EntityRuntime.Send(sound, "Stop").ShouldBeTrue();
        Movers.Run(world, 120);

        sound.IsPlaying.ShouldBeFalse();
        world.Sounds.Count.ShouldBe(0);
        world.TickingEntityCount.ShouldBe(0);
        _rig.Fired.ShouldBeEmpty();
    }

    [Fact]
    public void Deactivating_the_world_empties_the_registry()
    {
        _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"), ("looped", "1"));
        EntityWorld world = _rig.Start();
        world.Sounds.Count.ShouldBe(1);

        world.Deactivate();

        world.Sounds.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("1", 60)]
    [InlineData("2", 30)]
    [InlineData("0.5", 120)]
    public void A_second_of_sound_ends_on_the_tick_its_pitch_gives(string pitch, int ticks)
    {
        SceneNode node = _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"), ("pitch", pitch));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);

        Movers.Run(world, ticks - 1);
        sound.IsPlaying.ShouldBeTrue();
        _rig.Fired.ShouldBeEmpty();

        Movers.Run(world, 1);
        sound.IsPlaying.ShouldBeFalse();
        world.Sounds.Count.ShouldBe(0);
        _rig.Fired.ShouldBe([$"{ticks}:OnEnded"]);

        // A wire delivers on the tick after its output fires.
        _rig.Heard.ShouldBeEmpty();
        Movers.Run(world, 1);
        _rig.Heard.ShouldBe([OnEnded]);
    }

    [Fact]
    public void A_sound_that_ended_stops_ticking_and_fires_once()
    {
        _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"));
        EntityWorld world = _rig.Start();

        Movers.Run(world, 600);

        world.TickingEntityCount.ShouldBe(0);
        _rig.Fired.ShouldBe(["60:OnEnded"]);
    }

    [Fact]
    public void A_sound_that_starts_with_the_level_keeps_time_when_the_host_ticks_at_another_rate()
    {
        _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"));
        EntityWorld world = _rig.Start();

        for (int i = 0; i < 40; i++)
            world.Tick(1f / 30f);

        _rig.Fired.ShouldBe(["30:OnEnded"]);
    }

    [Fact]
    public void Play_through_a_wire_counts_from_the_tick_it_is_delivered_on()
    {
        _rig.Sound();
        EntityWorld world = _rig.Start();
        Movers.Run(world, 7);

        world.QueueInput("sound", "Play");
        Movers.Run(world, 100);

        // Delivered on tick 8.
        _rig.Fired.ShouldBe(["68:OnEnded"]);
    }

    [Fact]
    public void SetPitch_part_way_through_moves_the_end_by_what_is_left()
    {
        SceneNode node = _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);
        Movers.Run(world, 30);

        // Half is played. The other half takes 15 ticks at twice the speed.
        EntityRuntime.Send(sound, "SetPitch", "2").ShouldBeTrue();
        Movers.Run(world, 100);

        _rig.Fired.ShouldBe(["45:OnEnded"]);
        sound.Pitch.ShouldBe(2f);
    }

    [Fact]
    public void SetPitch_on_a_stopped_sound_is_kept_for_the_next_play()
    {
        SceneNode node = _rig.Sound();
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);

        EntityRuntime.Send(sound, "SetPitch", "2");
        EntityRuntime.Send(sound, "Play");
        Movers.Run(world, 100);

        _rig.Fired.ShouldBe(["30:OnEnded"]);
    }

    [Fact]
    public void Play_while_playing_starts_the_sound_over_and_moves_the_end()
    {
        SceneNode node = _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);
        int first = world.Sounds.Playing[0].Id;
        Movers.Run(world, 40);

        EntityRuntime.Send(sound, "Play");

        world.Sounds.Count.ShouldBe(1);
        SoundEmitter restarted = world.Sounds.Playing[0];
        restarted.Id.ShouldNotBe(first);
        restarted.StartTick.ShouldBe(40L);
        restarted.FramesPlayedAt(world.TickNumber).ShouldBe(0L);

        Movers.Run(world, 200);
        _rig.Fired.ShouldBe(["100:OnEnded"]);
    }

    [Fact]
    public void A_sound_that_ended_plays_again_when_asked()
    {
        SceneNode node = _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);
        Movers.Run(world, 80);

        EntityRuntime.Send(sound, "Play");
        Movers.Run(world, 80);

        _rig.Fired.ShouldBe(["60:OnEnded", "140:OnEnded"]);
    }

    [Fact]
    public void Start_playing_plays_as_the_level_spawns_and_the_catalog_is_there_by_then()
    {
        _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"));

        EntityWorld world = _rig.Start();

        _rig.Catalog.Asked.ShouldBe([SoundRig.OneSecond]);
        world.Sounds.Count.ShouldBe(1);
        world.Sounds.Playing[0].StartTick.ShouldBe(0L);
        _rig.Logger.MessagesAt(LogLevel.Warning).ShouldBeEmpty(_rig.Logger.Describe());
    }

    [Fact]
    public void A_sound_not_set_to_start_stays_silent_until_it_is_played()
    {
        SceneNode node = _rig.Sound();
        EntityWorld world = _rig.Start();

        Movers.Run(world, 120);

        world.Sounds.Count.ShouldBe(0);
        world.TickingEntityCount.ShouldBe(0);
        _rig.Fired.ShouldBeEmpty();

        EntityRuntime.Send(EntityRuntime.Live<PointSound>(world, node), "Play");

        world.Sounds.Count.ShouldBe(1);
        world.Sounds.Playing[0].StartTick.ShouldBe(120L);
        world.TickingEntityCount.ShouldBe(1);
    }

    [Fact]
    public void A_missing_sound_warns_once_at_spawn_and_Play_does_nothing()
    {
        SceneNode node = _rig.Sound("Sounds/gone.wav", ("startplaying", "1"));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);

        EntityRuntime.Send(sound, "Play").ShouldBeTrue();
        EntityRuntime.Send(sound, "Play");
        Movers.Run(world, 120);

        _rig.Logger.MessagesAt(LogLevel.Warning).ShouldBe(
        [
            "Entity 'sound' (point_sound) cannot play 'Sounds/gone.wav': the test has no such sound.",
        ]);
        _rig.Catalog.Asked.ShouldBe(["Sounds/gone.wav"]);
        sound.IsPlaying.ShouldBeFalse();
        world.Sounds.Count.ShouldBe(0);
        world.TickingEntityCount.ShouldBe(0);
        _rig.Fired.ShouldBeEmpty();
    }

    [Fact]
    public void A_sound_with_no_file_set_warns_once_and_asks_the_catalog_nothing()
    {
        SceneNode node = _rig.Sound(path: "");
        EntityWorld world = _rig.Start();

        EntityRuntime.Send(EntityRuntime.Live<PointSound>(world, node), "Play");

        _rig.Logger.MessagesAt(LogLevel.Warning).ShouldBe(
            ["Entity 'sound' (point_sound) has no sound set, so it plays nothing."]);
        _rig.Catalog.Asked.ShouldBeEmpty();
        world.Sounds.Count.ShouldBe(0);
    }

    [Fact]
    public void A_level_started_with_no_catalog_warns_and_plays_nothing()
    {
        SceneNode node = _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"));
        var world = new EntityWorld(_rig.Scene, _rig.Logger, EntityRuntime.Catalog(_rig.Heard));
        world.Activate();

        EntityRuntime.Send(EntityRuntime.Live<PointSound>(world, node), "Play");

        _rig.Logger.MessagesAt(LogLevel.Warning).ShouldBe(
        [
            "Entity 'sound' (point_sound) cannot play 'Sounds/one_second.wav': " +
            "the level was started with no sound catalog.",
        ]);
        world.Sounds.Count.ShouldBe(0);
    }

    [Fact]
    public void SetVolume_changes_the_playing_sound_and_every_later_play()
    {
        SceneNode node = _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);

        EntityRuntime.Send(sound, "SetVolume", "0.25").ShouldBeTrue();
        world.Sounds.Playing[0].Gain.ShouldBe(0.25f);

        EntityRuntime.Send(sound, "Play");
        world.Sounds.Playing[0].Gain.ShouldBe(0.25f);
        sound.RefusedInputCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("loud")]
    [InlineData("-0.5")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void A_volume_that_is_not_a_number_from_zero_up_is_refused_and_counted(string volume)
    {
        SceneNode node = _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"), ("volume", "0.5"));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);

        EntityRuntime.Send(sound, "SetVolume", volume).ShouldBeTrue();

        sound.RefusedInputCount.ShouldBe(1);
        sound.Volume.ShouldBe(0.5f);
        world.Sounds.Playing[0].Gain.ShouldBe(0.5f);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fast")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.05")]
    [InlineData("10.5")]
    [InlineData("NaN")]
    public void A_pitch_that_is_not_a_number_from_a_tenth_to_ten_is_refused_and_counted(string pitch)
    {
        SceneNode node = _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);

        EntityRuntime.Send(sound, "SetPitch", pitch).ShouldBeTrue();
        Movers.Run(world, 100);

        sound.RefusedInputCount.ShouldBe(1);
        sound.Pitch.ShouldBe(1f);
        _rig.Fired.ShouldBe(["60:OnEnded"]);
    }

    [Theory]
    [InlineData("pitch", "0")]
    [InlineData("pitch", "40")]
    [InlineData("volume", "-2")]
    public void An_authored_number_out_of_range_is_refused_and_the_default_plays(string key, string value)
    {
        SceneNode node = _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"), (key, value));
        EntityWorld world = _rig.Start();

        _rig.Logger.MessagesAt(LogLevel.Warning).ShouldBe(
        [
            $"Entity 'sound' (point_sound) cannot read keyvalue '{key}' = '{value}'; keeping the default.",
        ]);
        world.Sounds.Playing[0].Gain.ShouldBe(1f);
        world.Sounds.Playing[0].Pitch.ShouldBe(1f);
        EntityRuntime.Live<PointSound>(world, node).RefusedInputCount.ShouldBe(0);
    }

    [Fact]
    public void A_sound_that_is_despawned_stops()
    {
        SceneNode node = _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"), ("looped", "1"));
        EntityWorld world = _rig.Start();

        world.QueueDespawn(EntityRuntime.Live<PointSound>(world, node));
        Movers.Run(world, 1);

        world.Sounds.Count.ShouldBe(0);
        world.TickingEntityCount.ShouldBe(0);
    }

    [Fact]
    public void A_sound_stopped_from_outside_the_entity_fires_nothing_and_can_play_again()
    {
        SceneNode node = _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);
        Movers.Run(world, 10);

        world.Sounds.Stop(world.Sounds.Playing[0].Id);
        Movers.Run(world, 100);

        sound.IsPlaying.ShouldBeFalse();
        _rig.Fired.ShouldBeEmpty();

        EntityRuntime.Send(sound, "Play");
        Movers.Run(world, 60);
        _rig.Fired.ShouldBe(["170:OnEnded"]);
    }

    [Fact]
    public void The_generated_schema_describes_what_the_class_declares()
    {
        EntitySchema schema = PointSound.SpectraSchema;

        schema.ClassName.ShouldBe("point_sound");
        schema.DisplayName.ShouldBe("Sound");
        schema.Group.ShouldBe("Sound");
        schema.Placement.ShouldBe(EntityPlacement.Point);
        schema.Inputs.ShouldBe(["Play", "Stop", "SetVolume", "SetPitch"]);
        schema.Outputs.ShouldBe([PointSound.OnEnded, PointSound.OnMarker]);

        schema.Keyvalues.Select(keyvalue => keyvalue.Name).ShouldBe(
            ["sound", "volume", "pitch", "mindistance", "maxdistance", "looped", "startplaying"]);
        schema.Keyvalues.Select(keyvalue => keyvalue.Default).ShouldBe(["", "1", "1", "2", "30", "0", "0"]);
        schema.Keyvalues.ShouldAllBe(keyvalue => keyvalue.Display.Length > 0 && keyvalue.Tooltip.Length > 0);
        schema.Keyvalues[0].Type.ShouldBe(KeyvalueType.AssetSound);

        KeyvalueDescriptor pitch = schema.Keyvalues[2];
        pitch.Min.ShouldBe(PointSound.MinimumPitch);
        pitch.Max.ShouldBe(PointSound.MaximumPitch);
    }
}
