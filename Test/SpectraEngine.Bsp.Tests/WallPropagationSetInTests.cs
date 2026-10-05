using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A sound on a part that is set in a wall: it is heard from the part, the
/// wall close round the part is not in its way, and the rest of the wall is.
/// </summary>
// The wall is a unit of brick at z from -5 to -4, with a doorway at x from
// -0.7 to 0.7. The door is 0.8 thick and slides along x into the wall, as the
// demo's start room door does.
public sealed class WallPropagationSetInTests
{
    private const float NearEnough = 2e-3f;
    private const float Travel = 1.4f;

    private static readonly Vector3 DoorCenter = new(0f, 1.1f, -4.5f);
    private static readonly Vector3 DoorHalf = new(0.7f, 1.1f, 0.4f);

    [Theory]
    [InlineData(-3.5f)]
    [InlineData(-5.5f)]
    public void A_doors_sound_is_clear_from_in_front_of_the_doorway_all_the_way_into_the_wall(float earZ)
    {
        SpanLevel level = ThickWall(out SceneNode door);
        SceneNode squeak = door.CreateChild("Squeak");

        for (float slid = 0f; slid <= Travel; slid += 0.05f)
        {
            door.LocalPosition = DoorCenter + new Vector3(slid, 0f, 0f);
            AcousticGains through = Through(level, squeak, new Vector3(0f, 1.62f, earZ));

            through.Gain.ShouldBe(1f, 1e-4f, $"slid {slid}");
            through.GainHf.ShouldBe(1f, 1e-4f, $"slid {slid}");
        }
    }

    // The line from the door's middle to a listener beside the doorway meets
    // the jamb, and so do some of the lines to the ring round the head.
    [Theory]
    [InlineData(1.5f, -3.5f)]
    [InlineData(-1.5f, -3.5f)]
    [InlineData(1.5f, -5.5f)]
    [InlineData(-1.5f, -5.5f)]
    public void A_doors_sound_stays_within_three_decibels_of_clear_from_beside_the_doorway(float earX, float earZ)
    {
        SpanLevel level = ThickWall(out SceneNode door);
        SceneNode squeak = door.CreateChild("Squeak");
        float? last = null;

        for (float slid = 0f; slid <= Travel; slid += 0.05f)
        {
            door.LocalPosition = DoorCenter + new Vector3(slid, 0f, 0f);
            AcousticGains through = Through(level, squeak, new Vector3(earX, 1.62f, earZ));

            through.Gain.ShouldBeGreaterThan(0.7f, $"slid {slid}");
            through.GainHf.ShouldBeGreaterThan(0.85f, $"slid {slid}");

            // One line of the five passing the jamb's edge, and no more.
            MathF.Abs(through.Gain - (last ?? through.Gain)).ShouldBeLessThan(0.15f, $"slid {slid}");
            last = through.Gain;
        }
    }

    [Fact]
    public void A_sound_on_a_button_pressed_into_a_wall_is_behind_the_rest_of_that_wall()
    {
        SpanLevel level = ThickWall(out _);

        // Half sunk into the face at z = -4, so its sound is on that face.
        SceneNode button = level.Part("Button", new Vector3(3f, 1.5f, -4f), new Vector3(0.3f, 0.3f, 0.075f));

        AcousticGains through = Through(level, button, new Vector3(3f, 1.5f, -8f));

        // The button reaches to -4.075, and the wall counts from the reach on.
        float wall = 1f - 0.075f - SceneSoundObstacles.BodyReach;
        through.Gain.ShouldBe(AcousticPresets.Brick.GainsThrough(wall).Gain, NearEnough);
    }

    [Fact]
    public void A_sound_under_a_trigger_that_stands_in_a_wall_is_behind_that_wall()
    {
        SpanLevel level = ThickWall(out _);
        SceneNode zone = level.Part("Zone", new Vector3(3f, 1.5f, -4.5f), new Vector3(0.3f, 0.3f, 0.3f));
        zone.CanCollide = false;

        AcousticGains through = Through(level, zone.CreateChild("Hum"), new Vector3(3f, 1.5f, 0f));

        through.Gain.ShouldBe(AcousticPresets.Brick.GainsThrough(0.5f).Gain, NearEnough);
    }

    [Fact]
    public void A_door_in_a_wall_is_behind_the_length_of_that_wall_for_a_listener_past_its_end()
    {
        SpanLevel level = ThickWall(out SceneNode door);
        door.LocalPosition = DoorCenter + new Vector3(Travel, 0f, 0f);

        // In line with the wall, two units past where it ends at x = 6.
        AcousticGains through = Through(level, door, new Vector3(8f, 1.1f, -4.5f));

        through.Gain.ShouldBe(MathF.Pow(10f, -AcousticLoss.MaxDb / 20f), NearEnough);
    }

    // What the walls leave of a sound on a node, by a rig of its own.
    private static AcousticGains Through(SpanLevel level, SceneNode body, Vector3 ear)
    {
        var rig = new WallRig(level) { Listener = ear };
        int sound = rig.Add(body);

        rig.Frame();

        return rig.Through(sound);
    }

    private static SpanLevel ThickWall(out SceneNode door)
    {
        var level = new SpanLevel();
        level.Box("Thick", new Vector3(0f, 1.5f, -4.5f), new Vector3(6f, 1.5f, 0.5f), SpanLevel.Brick);
        level.Cut("Doorway", DoorCenter, DoorHalf with { Z = 0.5f });
        level.Compile();

        door = level.Part("Door", DoorCenter, DoorHalf, SpanLevel.Wood);
        return level;
    }
}
