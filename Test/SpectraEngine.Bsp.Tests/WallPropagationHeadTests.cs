using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Audio.Propagation;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The ring round the listener's head in <see cref="WallPropagation"/>: a
/// point of it that lies inside a solid the listener is not in is no place
/// to listen from, and its line counts as the listener's own.
/// </summary>
// The ring is a quarter unit wide. For a sound level with the ear it stands
// upright, and its highest point is 0.23 over the ear.
public sealed class WallPropagationHeadTests
{
    private const float NearEnough = 2e-3f;

    [Fact]
    public void A_ceiling_the_head_all_but_touches_does_not_dim_a_sound_on_the_level()
    {
        var level = new SpanLevel();
        level.Box("Ceiling", new Vector3(0f, 2.75f, -4f), new Vector3(6f, 0.25f, 6f), WallRig.Concrete);
        level.Compile();

        // The ear is 0.18 under the ceiling, as at the top of a jump.
        var rig = new WallRig(level) { Listener = new Vector3(0f, 2.32f, 0f) };
        int sound = rig.Add(new Vector3(0f, 2.32f, -8f));

        rig.Frame();

        rig.Heard(sound).ShouldBe(rig.Direct(sound));
    }

    [Fact]
    public void A_wall_beside_the_head_does_not_dim_a_sound_in_front()
    {
        var level = new SpanLevel();
        level.Box("Side", new Vector3(1.1f, 1.5f, -4f), new Vector3(1f, 1.5f, 6f), WallRig.Concrete);
        level.Compile();

        // A tenth of a unit from the wall's face at x = 0.1.
        var rig = new WallRig(level) { Listener = new Vector3(0f, 1.5f, 0f) };
        int sound = rig.Add(new Vector3(0f, 1.5f, -8f));

        rig.Frame();

        rig.Heard(sound).ShouldBe(rig.Direct(sound));
    }

    [Fact]
    public void A_wall_that_stands_between_still_counts_in_full_for_a_listener_close_to_it()
    {
        var level = new SpanLevel();
        level.Wall();
        level.Compile();

        // A tenth of a unit from the plaster, with the sound behind the wall
        // and well off to one side, so the ring leans into the wall.
        var rig = new WallRig(level) { Listener = new Vector3(0f, 1.5f, -3.9f) };
        int sound = rig.Add(new Vector3(3f, 1.5f, -7f));

        rig.Frame();

        // The wall is met aslant: 0.5 thick, and the line climbs 3.1 in z over 4.31.
        float aslant = 0.5f * 4.314f / 3.1f;
        rig.Through(sound).Gain.ShouldBe(AcousticPresets.Brick.GainsThrough(aslant).Gain, NearEnough);
    }

    [Fact]
    public void A_listener_inside_a_wall_hears_each_line_by_how_deep_its_own_end_is()
    {
        var level = new SpanLevel();
        level.Wall();
        level.Compile();

        // Two centimetres into the plaster, with the sound off to one side,
        // so the ring's ends are at other depths than the listener.
        var rig = new WallRig(level) { Listener = new Vector3(0f, 1.5f, -4.02f) };
        int sound = rig.Add(new Vector3(3f, 1.5f, 0f));

        rig.Frame();

        float toListener = OneLine(level, rig.Listener, new Vector3(3f, 1.5f, 0f));
        rig.Through(sound).Gain.ShouldBeInRange(0.5f, toListener - 0.05f);
    }

    // What the walls leave along the one line to the listener.
    private static float OneLine(SpanLevel level, Vector3 listener, Vector3 from)
    {
        var rig = new WallRig(level, WallPropagationSettings.Default with { Lines = 1 }) { Listener = listener };
        int sound = rig.Add(from);

        rig.Frame();

        return rig.Through(sound).Gain;
    }
}
