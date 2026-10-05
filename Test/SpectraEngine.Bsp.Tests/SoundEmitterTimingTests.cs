using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Where a playing sound is, worked out from ticks alone: its length, its
/// pitch, and what a loop does to it.
/// </summary>
public sealed class SoundEmitterTimingTests
{
    private const float Tick = 1f / 60f;
    private const int Rate = 48_000;

    private readonly Scene _scene = new("Sounds");

    [Theory]
    [InlineData(1f, 60)]
    [InlineData(2f, 30)]
    [InlineData(0.5f, 120)]
    public void A_second_of_sound_ends_on_the_tick_its_pitch_gives(float pitch, int ticks)
    {
        EntityWorld world = Started();
        SoundEmitter emitter = Play(world, new SoundDescription(Rate, Rate), pitch: pitch);

        emitter.HasEndedAt(ticks - 1).ShouldBeFalse();
        emitter.HasEndedAt(ticks).ShouldBeTrue();
    }

    [Fact]
    public void Frames_played_are_ticks_times_the_tick_length_the_rate_and_the_pitch()
    {
        EntityWorld world = Started();
        SoundEmitter emitter = Play(world, new SoundDescription(Rate * 10, Rate), pitch: 1.5f);

        emitter.FramesPlayedAt(0).ShouldBe(0L);
        emitter.FramesPlayedAt(1).ShouldBe(1_200L);
        emitter.FramesPlayedAt(60).ShouldBe(72_000L);
    }

    [Fact]
    public void A_sound_started_part_way_through_a_level_counts_from_its_own_start()
    {
        EntityWorld world = Started();
        Run(world, 100);

        SoundEmitter emitter = Play(world, new SoundDescription(Rate, Rate));

        emitter.StartTick.ShouldBe(100L);
        emitter.FramesPlayedAt(100).ShouldBe(0L);
        emitter.FramesPlayedAt(130).ShouldBe(24_000L);
        emitter.HasEndedAt(159).ShouldBeFalse();
        emitter.HasEndedAt(160).ShouldBeTrue();
    }

    [Fact]
    public void A_tick_before_the_sound_started_has_played_nothing()
    {
        EntityWorld world = Started();
        Run(world, 10);

        SoundEmitter emitter = Play(world, new SoundDescription(Rate, Rate));

        emitter.FramesPlayedAt(3).ShouldBe(0L);
    }

    [Fact]
    public void A_step_that_rounds_down_as_a_float_still_ends_on_the_whole_tick()
    {
        // 0.02f is a hair under a fiftieth, so fifty of them fall short of a second.
        const float fiftieth = 0.02f;
        EntityWorld world = Started();
        world.Tick(fiftieth);

        SoundEmitter emitter = Play(world, new SoundDescription(Rate, Rate));

        emitter.HasEndedAt(1 + 49).ShouldBeFalse();
        emitter.HasEndedAt(1 + 50).ShouldBeTrue();
    }

    [Fact]
    public void A_pitch_change_keeps_what_was_played_and_moves_the_end()
    {
        EntityWorld world = Started();
        int id = world.Sounds.Play(_scene.Root, "a.wav", new SoundDescription(Rate, Rate), Settings());
        Run(world, 30);

        world.Sounds.SetPitch(id, 2f);
        world.Sounds.TryGet(id, out SoundEmitter emitter).ShouldBeTrue();

        // Half played at 800 frames a tick, the other half at 1600.
        emitter.StartTick.ShouldBe(0L);
        emitter.FramesPlayedAt(30).ShouldBe(24_000L);
        emitter.FramesPlayedAt(31).ShouldBe(25_600L);
        emitter.HasEndedAt(44).ShouldBeFalse();
        emitter.HasEndedAt(45).ShouldBeTrue();
    }

    [Fact]
    public void Slowing_a_sound_down_part_way_through_moves_the_end_out()
    {
        EntityWorld world = Started();
        int id = world.Sounds.Play(_scene.Root, "a.wav", new SoundDescription(Rate, Rate), Settings());
        Run(world, 30);

        world.Sounds.SetPitch(id, 0.5f);
        world.Sounds.TryGet(id, out SoundEmitter emitter).ShouldBeTrue();

        emitter.HasEndedAt(89).ShouldBeFalse();
        emitter.HasEndedAt(90).ShouldBeTrue();
    }

    [Fact]
    public void A_sound_that_plays_once_stops_at_its_last_frame()
    {
        EntityWorld world = Started();
        SoundEmitter emitter = Play(world, new SoundDescription(Rate, Rate));

        emitter.Loop.IsLooping.ShouldBeFalse();
        emitter.PositionAt(30).ShouldBe(new SoundPosition(0, 24_000));
        emitter.PositionAt(600).ShouldBe(new SoundPosition(0, Rate));
    }

    [Fact]
    public void A_looped_sound_with_no_region_repeats_the_whole_sound_and_never_ends()
    {
        EntityWorld world = Started();
        SoundEmitter emitter = Play(world, new SoundDescription(Rate, Rate), looped: true);

        emitter.Loop.ShouldBe(new LoopRegion(0, Rate));
        emitter.PositionAt(59).ShouldBe(new SoundPosition(0, 47_200));
        emitter.PositionAt(60).ShouldBe(new SoundPosition(1, 0));
        emitter.PositionAt(90).ShouldBe(new SoundPosition(1, 24_000));
        emitter.PositionAt(150).ShouldBe(new SoundPosition(2, 24_000));
        emitter.HasEndedAt(60_000).ShouldBeFalse();
    }

    [Fact]
    public void A_looped_sound_with_a_region_plays_up_to_its_end_and_then_repeats_the_region()
    {
        // A quarter second in, then half a second that repeats. The last
        // quarter second is never reached.
        var sound = new SoundDescription(Rate, Rate, new LoopRegion(12_000, 36_000));
        EntityWorld world = Started();
        SoundEmitter emitter = Play(world, sound, looped: true);

        emitter.Loop.ShouldBe(new LoopRegion(12_000, 36_000));
        emitter.PositionAt(10).ShouldBe(new SoundPosition(0, 8_000));
        emitter.PositionAt(44).ShouldBe(new SoundPosition(0, 35_200));
        emitter.PositionAt(45).ShouldBe(new SoundPosition(1, 12_000));
        emitter.PositionAt(60).ShouldBe(new SoundPosition(1, 24_000));
        emitter.PositionAt(75).ShouldBe(new SoundPosition(2, 12_000));
        emitter.HasEndedAt(60_000).ShouldBeFalse();
    }

    [Fact]
    public void A_sound_that_is_not_looped_ignores_the_region_in_its_file()
    {
        var sound = new SoundDescription(Rate, Rate, new LoopRegion(12_000, 36_000));
        EntityWorld world = Started();
        SoundEmitter emitter = Play(world, sound, looped: false);

        emitter.Loop.ShouldBe(LoopRegion.None);
        emitter.PositionAt(45).ShouldBe(new SoundPosition(0, 36_000));
        emitter.HasEndedAt(59).ShouldBeFalse();
        emitter.HasEndedAt(60).ShouldBeTrue();
    }

    [Fact]
    public void An_empty_sound_ends_at_once_even_when_it_is_looped()
    {
        EntityWorld world = Started();
        SoundEmitter emitter = Play(world, new SoundDescription(0, Rate), looped: true);

        emitter.Loop.ShouldBe(LoopRegion.None);
        emitter.HasEndedAt(0).ShouldBeTrue();
    }

    [Fact]
    public void A_description_refuses_a_loop_that_ends_past_the_sound()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new SoundDescription(100, Rate, new LoopRegion(0, 101)));
    }

    [Fact]
    public void A_description_with_no_markers_has_an_empty_list()
    {
        new SoundDescription(100, Rate).Markers.ShouldBeEmpty();
        default(SoundDescription).Markers.ShouldBeEmpty();
    }

    private SoundEmitter Play(EntityWorld world, SoundDescription sound, float pitch = 1f, bool looped = false)
    {
        int id = world.Sounds.Play(_scene.Root, "a.wav", in sound, Settings(pitch, looped));
        world.Sounds.TryGet(id, out SoundEmitter emitter).ShouldBeTrue();
        return emitter;
    }

    private static SoundEmitterSettings Settings(float pitch = 1f, bool looped = false) =>
        new(1f, pitch, 2f, 30f, looped);

    private static void Run(EntityWorld world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            world.Tick(Tick);
    }

    private EntityWorld Started()
    {
        var world = new EntityWorld(_scene, new CapturingLogger(), new EntityCatalog());
        world.Activate();
        return world;
    }
}
