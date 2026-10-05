using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// The sound entity's markers and loops: which tick a marker fires on, what a
/// looped sound repeats, and what a sound that is not looped ignores.
/// </summary>
// At 48 kHz and 60 ticks a second a tick is 800 frames.
public sealed class PointSoundMarkerTests
{
    private const string Line = "Sounds/line.wav";
    private const int Rate = SoundRig.Rate;

    private readonly SoundRig _rig = new();

    [Fact]
    public void A_marker_fires_with_its_name_on_the_tick_playback_reaches_it()
    {
        Describe(Rate, default, (12_000, "quarter"), (36_000, "three quarters"));
        _rig.Sound(Line, ("startplaying", "1"));
        EntityWorld world = _rig.Start();

        Movers.Run(world, 100);

        _rig.Fired.ShouldBe(["15:OnMarker:quarter", "45:OnMarker:three quarters", "60:OnEnded"]);
        _rig.Heard.ShouldBe(["sink:OnMarker:quarter", "sink:OnMarker:three quarters", "sink:OnEnded:"]);
    }

    [Fact]
    public void A_marker_between_two_ticks_fires_on_the_later_one()
    {
        Describe(Rate, default, (12_001, "just after"));
        _rig.Sound(Line, ("startplaying", "1"));
        EntityWorld world = _rig.Start();

        Movers.Run(world, 20);

        _rig.Fired.ShouldBe(["16:OnMarker:just after"]);
    }

    [Fact]
    public void Markers_reached_on_one_tick_all_fire_in_file_order()
    {
        // "b" before "a" on one frame: the file's order, not the alphabet's.
        Describe(Rate, default, (11_300, "one"), (11_900, "b"), (11_900, "a"));
        _rig.Sound(Line, ("startplaying", "1"));
        EntityWorld world = _rig.Start();

        Movers.Run(world, 20);

        _rig.Fired.ShouldBe(["15:OnMarker:one", "15:OnMarker:b", "15:OnMarker:a"]);
    }

    [Fact]
    public void A_faster_pitch_brings_the_markers_forward()
    {
        Describe(Rate, default, (12_000, "quarter"));
        _rig.Sound(Line, ("startplaying", "1"), ("pitch", "2"));
        EntityWorld world = _rig.Start();

        Movers.Run(world, 40);

        _rig.Fired.ShouldBe(["8:OnMarker:quarter", "30:OnEnded"]);
    }

    [Fact]
    public void A_marker_on_the_first_frame_fires_as_the_sound_starts()
    {
        Describe(Rate, default, (0, "start"));
        _rig.Sound(Line);
        EntityWorld world = _rig.Start();
        Movers.Run(world, 4);

        world.QueueInput("sound", "Play");
        Movers.Run(world, 3);

        // Delivered on tick 5, and the sound ticks on the tick it starts.
        _rig.Fired.ShouldBe(["5:OnMarker:start"]);
    }

    [Fact]
    public void A_marker_at_the_very_end_fires_just_before_the_end_does()
    {
        Describe(Rate, default, (Rate, "end"));
        _rig.Sound(Line, ("startplaying", "1"));
        EntityWorld world = _rig.Start();

        Movers.Run(world, 100);

        _rig.Fired.ShouldBe(["60:OnMarker:end", "60:OnEnded"]);
    }

    [Fact]
    public void Play_while_playing_fires_the_markers_again_from_the_start()
    {
        Describe(Rate, default, (12_000, "quarter"));
        SceneNode node = _rig.Sound(Line, ("startplaying", "1"));
        EntityWorld world = _rig.Start();
        Movers.Run(world, 30);

        EntityRuntime.Send(EntityRuntime.Live<PointSound>(world, node), "Play");
        Movers.Run(world, 100);

        _rig.Fired.ShouldBe(["15:OnMarker:quarter", "45:OnMarker:quarter", "90:OnEnded"]);
    }

    [Fact]
    public void Stop_before_a_marker_means_it_never_fires()
    {
        Describe(Rate, default, (12_000, "quarter"));
        SceneNode node = _rig.Sound(Line, ("startplaying", "1"));
        EntityWorld world = _rig.Start();
        Movers.Run(world, 10);

        EntityRuntime.Send(EntityRuntime.Live<PointSound>(world, node), "Stop");
        Movers.Run(world, 100);

        _rig.Fired.ShouldBeEmpty();
    }

    [Fact]
    public void A_looped_sound_with_no_region_repeats_the_whole_sound_and_never_ends()
    {
        Describe(Rate, default, (24_000, "half"));
        SceneNode node = _rig.Sound(Line, ("startplaying", "1"), ("looped", "1"));
        EntityWorld world = _rig.Start();

        Movers.Run(world, 200);

        _rig.Fired.ShouldBe(["30:OnMarker:half", "90:OnMarker:half", "150:OnMarker:half"]);
        EntityRuntime.Live<PointSound>(world, node).IsPlaying.ShouldBeTrue();
        world.Sounds.Playing[0].PositionAt(world.TickNumber).ShouldBe(new SoundPosition(3, 16_000));
    }

    [Fact]
    public void A_looped_sound_with_a_region_plays_in_once_and_then_repeats_the_region()
    {
        // A quarter second in, half a second that repeats, and a quarter
        // second of tail the loop never reaches.
        Describe(Rate, new LoopRegion(12_000, 36_000), (6_000, "intro"), (24_000, "middle"), (42_000, "tail"));
        _rig.Sound(Line, ("startplaying", "1"), ("looped", "1"));
        EntityWorld world = _rig.Start();

        Movers.Run(world, 100);

        _rig.Fired.ShouldBe(
            ["8:OnMarker:intro", "30:OnMarker:middle", "60:OnMarker:middle", "90:OnMarker:middle"]);
        world.Sounds.Playing[0].Loop.ShouldBe(new LoopRegion(12_000, 36_000));
    }

    [Fact]
    public void A_sound_that_is_not_looped_plays_through_and_ignores_the_region()
    {
        Describe(Rate, new LoopRegion(12_000, 36_000), (6_000, "intro"), (24_000, "middle"), (42_000, "tail"));
        _rig.Sound(Line, ("startplaying", "1"));
        EntityWorld world = _rig.Start();

        Movers.Run(world, 100);

        _rig.Fired.ShouldBe(["8:OnMarker:intro", "30:OnMarker:middle", "53:OnMarker:tail", "60:OnEnded"]);
    }

    [Fact]
    public void Markers_at_the_start_and_the_end_of_a_loop_both_fire_on_every_pass()
    {
        Describe(Rate, default, (0, "start"), (Rate, "end"));
        _rig.Sound(Line, ("startplaying", "1"), ("looped", "1"));
        EntityWorld world = _rig.Start();

        Movers.Run(world, 130);

        // The pass ends, then the next one starts.
        _rig.Fired.ShouldBe(
        [
            "1:OnMarker:start",
            "60:OnMarker:end", "60:OnMarker:start",
            "120:OnMarker:end", "120:OnMarker:start",
        ]);
    }

    [Fact]
    public void A_loop_that_turns_round_eight_times_a_tick_does_not_fire_a_marker_once_a_pass()
    {
        Describe(Rate, new LoopRegion(0, 100), (50, "buzz"));
        _rig.Sound(Line, ("startplaying", "1"), ("looped", "1"));
        EntityWorld world = _rig.Start();

        Movers.Run(world, 10);

        _rig.Fired.Count.ShouldBe(10);
        world.DispatchBudgetTripCount.ShouldBe(0);
    }

    [Fact]
    public void A_loop_shorter_than_a_tick_fires_a_marker_twice_a_tick_at_most()
    {
        // 300 frames turn round two and two thirds times in a tick of 800.
        Describe(Rate, new LoopRegion(0, 300), (50, "buzz"));
        _rig.Sound(Line, ("startplaying", "1"), ("looped", "1"));
        EntityWorld world = _rig.Start();

        Movers.Run(world, 6);

        // A tick fires the rest of the pass the last tick left off in and the
        // start of the pass it ends in. The passes in between fire nothing.
        _rig.Fired.ShouldBe(
        [
            "1:OnMarker:buzz", "1:OnMarker:buzz",
            "2:OnMarker:buzz",
            "4:OnMarker:buzz", "4:OnMarker:buzz",
            "5:OnMarker:buzz",
        ]);
    }

    [Fact]
    public void A_pitch_change_between_markers_fires_each_once_and_the_later_ones_sooner()
    {
        Describe(Rate, default, (12_000, "before"), (24_000, "at"), (36_000, "after"));
        SceneNode node = _rig.Sound(Line, ("startplaying", "1"));
        EntityWorld world = _rig.Start();
        Movers.Run(world, 30);

        // Half is played, up to the marker "at". The rest goes by at 1600
        // frames a tick.
        EntityRuntime.Send(EntityRuntime.Live<PointSound>(world, node), "SetPitch", "2");
        Movers.Run(world, 100);

        _rig.Fired.ShouldBe(
            ["15:OnMarker:before", "30:OnMarker:at", "38:OnMarker:after", "45:OnEnded"]);
    }

    [Fact]
    public void A_pitch_change_on_a_looped_sound_keeps_its_place_and_fires_a_marker_once_a_pass()
    {
        Describe(Rate, default, (24_000, "half"));
        SceneNode node = _rig.Sound(Line, ("startplaying", "1"), ("looped", "1"));
        EntityWorld world = _rig.Start();
        Movers.Run(world, 45);

        // Three quarters round. From here it plays 400 frames a tick, so the
        // pass ends on tick 75 and each one after takes 120 ticks.
        EntityRuntime.Send(EntityRuntime.Live<PointSound>(world, node), "SetPitch", "0.5");
        Movers.Run(world, 215);

        _rig.Fired.ShouldBe(["30:OnMarker:half", "135:OnMarker:half", "255:OnMarker:half"]);
        world.Sounds.Playing[0].PositionAt(world.TickNumber).ShouldBe(new SoundPosition(2, 26_000));
    }

    [Fact]
    public void Stop_on_the_tick_a_sound_would_end_comes_first_so_the_end_never_fires()
    {
        Describe(Rate, default, (Rate, "end"));
        _rig.Sound(Line, ("startplaying", "1"));
        EntityWorld world = _rig.Start();
        Movers.Run(world, 59);

        // Delivered on tick 60, before the sound's own turn in that tick.
        world.QueueInput("sound", "Stop");
        Movers.Run(world, 60);

        _rig.Fired.ShouldBeEmpty();
        world.Sounds.Count.ShouldBe(0);
    }

    [Fact]
    public void Play_on_the_tick_a_sound_would_end_starts_it_over_and_the_end_it_had_never_fires()
    {
        Describe(Rate, default, (0, "start"), (Rate, "end"));
        _rig.Sound(Line, ("startplaying", "1"));
        EntityWorld world = _rig.Start();
        Movers.Run(world, 59);

        // Delivered on tick 60, before the sound's own turn in that tick.
        world.QueueInput("sound", "Play");
        Movers.Run(world, 100);

        _rig.Fired.ShouldBe(
        [
            "1:OnMarker:start",
            "60:OnMarker:start",
            "120:OnMarker:end", "120:OnEnded",
        ]);
    }

    [Fact]
    public void A_sound_with_no_markers_fires_only_its_end()
    {
        _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"));
        EntityWorld world = _rig.Start();

        Movers.Run(world, 100);

        _rig.Fired.ShouldBe(["60:OnEnded"]);
    }

    private void Describe(long frames, LoopRegion loop, params (long Frame, string Name)[] markers) =>
        _rig.Catalog.Add(
            Line,
            new SoundDescription(frames, Rate, loop, [.. markers.Select(marker => new AudioMarker(marker.Frame, marker.Name))]));
}
