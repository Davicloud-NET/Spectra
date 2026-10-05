using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The two ends of the line in <see cref="WallPropagation"/>: what a sound
/// is part of is not in its way, and a solid that an end is inside counts
/// by how deep that end is in it.
/// </summary>
// The wall fills z from -4.5 to -4, brick with plaster toward +z, and has a
// doorway at x from -1 to 1. The door is a wooden part in the doorway, 0.4
// thick. The listener stands at the origin's side of the wall.
public sealed class WallPropagationBodyTests
{
    private const float NearEnough = 2e-3f;

    private static readonly Vector3 Ear = new(0f, 1.2f, 0f);
    private static readonly Vector3 Behind = new(0f, 1.2f, -8f);
    private static readonly Vector3 DoorCenter = new(0f, 1.2f, -4.25f);
    private static readonly Vector3 DoorHalf = new(1f, 1.2f, 0.2f);

    [Fact]
    public void A_sound_under_a_door_is_not_muffled_by_its_own_door()
    {
        SpanLevel level = Room(out SceneNode door);
        SceneNode squeak = door.CreateChild("Squeak");
        var rig = new WallRig(level) { Listener = Ear };
        int sound = rig.Add(squeak);

        rig.Frame();

        rig.Heard(sound).ShouldBe(rig.Direct(sound));
    }

    [Fact]
    public void A_sound_on_the_door_itself_is_not_muffled_by_it()
    {
        SpanLevel level = Room(out SceneNode door);
        var rig = new WallRig(level) { Listener = Ear };
        int sound = rig.Add(door);

        rig.Frame();

        rig.Heard(sound).ShouldBe(rig.Direct(sound));
    }

    [Fact]
    public void A_sound_under_a_door_is_muffled_by_another_door()
    {
        SpanLevel level = Room(out SceneNode door);
        SceneNode squeak = door.CreateChild("Squeak");
        level.Part("Other", new Vector3(0f, 1.2f, -2f), new Vector3(1f, 1.2f, 0.05f), SpanLevel.Wood);
        var rig = new WallRig(level) { Listener = Ear };
        int sound = rig.Add(squeak);

        rig.Frame();

        rig.Through(sound).Gain.ShouldBe(AcousticPresets.Wood.GainsThrough(0.1f).Gain, NearEnough);
        rig.Through(sound).GainHf.ShouldBe(AcousticPresets.Wood.GainsThrough(0.1f).GainHf, NearEnough);
    }

    [Fact]
    public void Every_part_above_a_sound_in_the_tree_is_out_of_its_way()
    {
        var level = new SpanLevel();
        SceneNode lift = level.Part("Lift", new Vector3(0f, 1.2f, -4f), new Vector3(1f, 1.2f, 1f), SpanLevel.Wood);
        SceneNode panel = level.Part("Panel", default, new Vector3(0.5f, 0.5f, 0.75f), SpanLevel.Wood);
        lift.AddChild(panel);
        panel.LocalPosition = new Vector3(0f, 0f, 0.5f);

        // Inside the panel, which is half inside the lift.
        SceneNode voice = panel.CreateChild("Voice");
        var rig = new WallRig(level) { Listener = Ear };
        int sound = rig.Add(voice);

        rig.Frame();

        rig.Heard(sound).ShouldBe(rig.Direct(sound));
    }

    [Fact]
    public void A_part_beside_a_sound_in_the_tree_is_in_its_way()
    {
        var level = new SpanLevel();
        SceneNode group = level.Scene.Root.CreateChild("Group");
        SceneNode cover = level.Part("Cover", new Vector3(0f, 1.2f, -4f), new Vector3(1f, 1.2f, 0.05f), SpanLevel.Wood);
        group.AddChild(cover);
        SceneNode voice = group.CreateChild("Voice");
        voice.LocalPosition = Behind;
        var rig = new WallRig(level) { Listener = Ear };
        int sound = rig.Add(voice);

        rig.Frame();

        rig.Through(sound).Gain.ShouldBe(AcousticPresets.Wood.GainsThrough(0.1f).Gain, NearEnough);
    }

    [Fact]
    public void A_sound_on_no_node_that_sits_inside_a_part_is_muffled_by_it()
    {
        SpanLevel level = Room(out _);
        var rig = new WallRig(level) { Listener = Ear };
        int sound = rig.Add(DoorCenter);

        rig.Frame();

        // Its line leaves the door after 0.2 units of wood.
        rig.Through(sound).Gain.ShouldBeInRange(0.3f, 0.45f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-8f)]
    public void A_sound_under_a_door_that_slid_into_the_wall_is_not_muffled_by_that_wall(float earZ)
    {
        SpanLevel level = Room(out SceneNode door);
        SceneNode squeak = door.CreateChild("Squeak");
        door.LocalPosition += new Vector3(2.2f, 0f, 0f);
        var rig = new WallRig(level) { Listener = new Vector3(0f, 1.2f, earZ) };
        int sound = rig.Add(squeak);

        rig.Frame();

        rig.Heard(sound).ShouldBe(rig.Direct(sound));
    }

    [Fact]
    public void A_sound_under_a_door_that_slid_into_the_wall_is_muffled_by_the_next_wall()
    {
        SpanLevel level = Room(out SceneNode door);
        SceneNode squeak = door.CreateChild("Squeak");
        door.LocalPosition += new Vector3(2.2f, 0f, 0f);
        level.Part("Screen", new Vector3(1f, 1.2f, -2f), new Vector3(3f, 1.2f, 0.025f), SpanLevel.Wood);
        var rig = new WallRig(level) { Listener = Ear };
        int sound = rig.Add(squeak);

        rig.Frame();

        // The screen is met a little aslant.
        rig.Through(sound).Gain.ShouldBe(AcousticPresets.Wood.GainsThrough(0.05f).Gain, 0.01f);
    }

    [Fact]
    public void A_sound_under_no_part_that_stands_inside_a_wall_is_behind_that_wall()
    {
        SpanLevel level = Room(out _);
        SceneNode speaker = level.Scene.Root.CreateChild("Speaker");
        speaker.LocalPosition = new Vector3(3f, 1.5f, -4.25f);
        var rig = new WallRig(level) { Listener = new Vector3(3f, 1.5f, 0f) };
        int sound = rig.Add(speaker);

        rig.Frame();

        // A quarter metre deep in a wall with plaster on this side.
        AcousticGains table = AcousticPresets.Plaster.GainsThrough(0.25f);
        rig.Through(sound).Gain.ShouldBe(table.Gain, 0.01f);
    }

    [Fact]
    public void A_listener_a_hair_inside_a_wall_still_hears_the_room()
    {
        SpanLevel level = Room(out _);
        var rig = new WallRig(level) { Listener = new Vector3(3f, 1.5f, -4.02f) };
        int sound = rig.Add(new Vector3(3f, 1.5f, 0f));

        rig.Frame();

        // Two centimetres into the plaster face: under a decibel.
        rig.Through(sound).Gain.ShouldBeGreaterThan(0.89f);
        rig.Through(sound).GainHf.ShouldBeGreaterThan(0.89f);
    }

    [Fact]
    public void A_listener_deep_in_a_wall_hears_the_room_through_what_is_between()
    {
        SpanLevel level = Room(out _);
        var rig = new WallRig(level) { Listener = new Vector3(3f, 1.5f, -4.3f) };
        int sound = rig.Add(new Vector3(3f, 1.5f, 0f));

        rig.Frame();

        AcousticGains table = AcousticPresets.Plaster.GainsThrough(0.3f);
        rig.Through(sound).Gain.ShouldBe(table.Gain, NearEnough);
        rig.Through(sound).GainHf.ShouldBe(table.GainHf, NearEnough);
    }

    [Fact]
    public void A_sound_behind_a_wall_does_not_jump_as_the_listener_steps_into_that_wall()
    {
        SpanLevel level = Room(out _);

        float outside = ThroughTheWallFrom(level, earZ: -3.99f);
        float inside = ThroughTheWallFrom(level, earZ: -4.01f);

        // The centimetre the listener is in is all that is missing.
        outside.ShouldBe(AcousticPresets.Brick.GainsThrough(0.5f).Gain, NearEnough);
        inside.ShouldBe(AcousticPresets.Brick.GainsThrough(0.49f).Gain, NearEnough);
        inside.ShouldBe(outside, outside * 0.05f);
    }

    [Fact]
    public void A_closed_door_muffles_by_its_material_and_thickness()
    {
        SpanLevel level = Room(out _);
        var rig = new WallRig(level) { Listener = Ear };
        int sound = rig.Add(Behind);

        rig.Frame();

        AcousticGains table = AcousticPresets.Wood.GainsThrough(0.4f);
        rig.Through(sound).Gain.ShouldBe(table.Gain, NearEnough);
        rig.Through(sound).GainHf.ShouldBe(table.GainHf, NearEnough);
    }

    [Fact]
    public void A_door_that_slides_open_stops_muffling_within_the_refresh_time()
    {
        SpanLevel level = Room(out SceneNode door);
        var rig = new WallRig(level) { Listener = Ear };
        int sound = rig.Add(Behind);
        rig.Frame();

        door.LocalPosition += new Vector3(2.2f, 0f, 0f);

        FramesUntil(rig, () => rig.Heard(sound) == rig.Direct(sound)).ShouldBeLessThanOrEqualTo(RefreshFrames);
    }

    [Fact]
    public void A_door_that_slides_shut_muffles_within_the_refresh_time()
    {
        SpanLevel level = Room(out SceneNode door);
        door.LocalPosition += new Vector3(2.2f, 0f, 0f);
        var rig = new WallRig(level) { Listener = Ear };
        int sound = rig.Add(Behind);
        rig.Frame();
        rig.Heard(sound).ShouldBe(rig.Direct(sound));

        door.LocalPosition -= new Vector3(2.2f, 0f, 0f);

        float shut = AcousticPresets.Wood.GainsThrough(0.4f).Gain;
        FramesUntil(rig, () => MathF.Abs(rig.Through(sound).Gain - shut) < NearEnough)
            .ShouldBeLessThanOrEqualTo(RefreshFrames);
    }

    // A quarter of a second of frames, and the one the change is noticed in.
    private static int RefreshFrames =>
        (int)MathF.Ceiling(WallPropagationSettings.Default.RefreshSeconds / WallRig.FrameSeconds) + 1;

    private static int FramesUntil(WallRig rig, Func<bool> reached)
    {
        for (int frame = 1; frame <= 600; frame++)
        {
            rig.Frame();
            if (reached())
                return frame;
        }

        return int.MaxValue;
    }

    // A rig for each place: a step this small is not traced again for a
    // quarter of a second, and the answer kept until then says nothing.
    private static float ThroughTheWallFrom(SpanLevel level, float earZ)
    {
        var rig = new WallRig(level) { Listener = new Vector3(3f, 1.5f, earZ) };
        int sound = rig.Add(new Vector3(3f, 1.5f, -8f));

        rig.Frame();

        return rig.Through(sound).Gain;
    }

    private static SpanLevel Room(out SceneNode door)
    {
        var level = new SpanLevel();
        level.Wall();
        level.Doorway();
        level.Compile();

        door = level.Part("Door", DoorCenter, DoorHalf, SpanLevel.Wood);
        return level;
    }
}
