using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <see cref="SceneSoundObstacles"/>: the scene that is current, as what
/// stands in a sound's way.
/// </summary>
public sealed class SceneSoundObstaclesTests
{
    private static readonly Vector3 Near = new(0f, 1f, 0f);
    private static readonly Vector3 Far = new(0f, 1f, -8f);

    // A door 0.4 thick, in the plane of the wall the span level builds.
    private static readonly Vector3 DoorCenter = new(3f, 1f, -4.25f);
    private static readonly Vector3 DoorHalf = new(1f, 1f, 0.2f);
    private static readonly Vector3 DoorEar = new(3f, 1f, 0f);

    [Fact]
    public void With_no_scene_there_is_no_world_and_a_trace_finds_nothing()
    {
        var obstacles = new SceneSoundObstacles(() => null);
        var spans = new SolidSpan[4];

        obstacles.TryReadWorld(out _).ShouldBeFalse();
        obstacles.Trace(Near, Far, null, spans, out bool truncated).ShouldBe(0);
        truncated.ShouldBeFalse();
    }

    [Fact]
    public void The_revision_stays_while_the_world_does()
    {
        SpanLevel level = Walled();
        var obstacles = new SceneSoundObstacles(() => level.Scene);

        obstacles.TryReadWorld(out long first).ShouldBeTrue();
        obstacles.TryReadWorld(out long second).ShouldBeTrue();

        second.ShouldBe(first);
    }

    [Fact]
    public void A_compile_that_landed_is_another_revision()
    {
        SpanLevel level = Walled();
        var obstacles = new SceneSoundObstacles(() => level.Scene);
        obstacles.TryReadWorld(out long before);

        level.Box("More", new Vector3(0f, 1.5f, -6f), new Vector3(1f));
        level.Compile();

        obstacles.TryReadWorld(out long after).ShouldBeTrue();
        after.ShouldNotBe(before);
    }

    [Fact]
    public void A_compile_has_changed_the_lines_near_it_and_no_others()
    {
        SpanLevel level = Walled();
        var obstacles = new SceneSoundObstacles(() => level.Scene);
        obstacles.TryReadWorld(out long before);

        // Six compile cells from the wall.
        var far = new Vector3(200f, 1f, 200f);
        level.Box("More", far, new Vector3(1f));
        level.CompileChanges();
        obstacles.TryReadWorld(out long after).ShouldBeTrue();

        after.ShouldNotBe(before);
        obstacles.HasChangedSince(before, Near, Far, 0.25f).ShouldBeFalse();
        obstacles.HasChangedSince(before, far + (Vector3.UnitX * 3f), far - (Vector3.UnitX * 3f), 0.25f).ShouldBeTrue();
    }

    [Fact]
    public void Another_scene_has_changed_every_line()
    {
        SpanLevel walled = Walled();
        Scene current = walled.Scene;
        var obstacles = new SceneSoundObstacles(() => current);
        obstacles.TryReadWorld(out long before);

        current = new Scene("Open");
        obstacles.TryReadWorld(out _);

        obstacles.HasChangedSince(before, Near, Far, 0.25f).ShouldBeTrue();
    }

    [Fact]
    public void A_part_that_moved_is_not_another_revision()
    {
        SpanLevel level = Walled();
        SceneNode door = level.Part("Door", new Vector3(0f, 1.2f, -2f), new Vector3(1f, 1.2f, 0.2f));
        var obstacles = new SceneSoundObstacles(() => level.Scene);
        obstacles.TryReadWorld(out long before);

        door.LocalPosition += new Vector3(2f, 0f, 0f);

        obstacles.TryReadWorld(out long after);
        after.ShouldBe(before);
    }

    [Fact]
    public void Another_scene_is_another_revision_and_it_is_the_one_that_is_traced()
    {
        SpanLevel walled = Walled();
        var open = new Scene("Open");
        Scene current = walled.Scene;
        var obstacles = new SceneSoundObstacles(() => current);
        var spans = new SolidSpan[4];

        obstacles.TryReadWorld(out long before);
        obstacles.Trace(Near, Far, null, spans, out _).ShouldBe(1);

        current = open;

        obstacles.TryReadWorld(out long after).ShouldBeTrue();
        after.ShouldNotBe(before);
        obstacles.Trace(Near, Far, null, spans, out _).ShouldBe(0);
    }

    [Fact]
    public void The_sounds_own_part_and_the_parts_above_it_are_left_out_and_no_other()
    {
        var level = new SpanLevel();
        SceneNode lift = level.Part("Lift", new Vector3(0f, 1f, -2f), new Vector3(1f, 1f, 0.5f));
        SceneNode speaker = lift.CreateChild("Speaker");
        level.Part("Crate", new Vector3(0f, 1f, -5f), new Vector3(1f, 1f, 0.5f));
        var obstacles = new SceneSoundObstacles(() => level.Scene);
        var spans = new SolidSpan[4];

        obstacles.Trace(Far, Near, null, spans, out _).ShouldBe(2);
        obstacles.Trace(Far, Near, lift, spans, out _).ShouldBe(1);

        obstacles.Trace(Far, Near, speaker, spans, out _).ShouldBe(1);
        spans[0].Start.ShouldBe(2.5f, 1e-4f);
        spans[0].End.ShouldBe(3.5f, 1e-4f);
    }

    [Fact]
    public void A_solid_that_a_sound_on_a_part_stands_in_is_traced_like_any_other()
    {
        SpanLevel level = Walled();
        SceneNode door = level.Part("Door", DoorCenter, DoorHalf);
        var obstacles = new SceneSoundObstacles(() => level.Scene);
        var spans = new SolidSpan[4];

        // The door is in the wall, a quarter unit from the face toward the ear.
        obstacles.Trace(DoorCenter, DoorEar, door, spans, out _).ShouldBe(1);
        spans[0].Start.ShouldBe(0f);
        spans[0].End.ShouldBe(0.25f, 1e-4f);
    }

    [Fact]
    public void A_sound_on_a_part_is_heard_from_the_reach_outside_that_part()
    {
        var level = new SpanLevel();
        SceneNode door = level.Part("Door", DoorCenter, DoorHalf);
        SceneNode squeak = door.CreateChild("Squeak");
        var obstacles = new SceneSoundObstacles(() => level.Scene);

        // Out by the door's face at z = -4.05, and on by the reach.
        var outside = new Vector3(3f, 1f, -4.05f + SceneSoundObstacles.BodyReach);
        Apart(obstacles.HeardFrom(DoorCenter, DoorEar, door), outside).ShouldBeLessThan(1e-4f);
        Apart(obstacles.HeardFrom(DoorCenter, DoorEar, squeak), outside).ShouldBeLessThan(1e-4f);
    }

    [Fact]
    public void A_sound_under_a_group_under_a_part_is_heard_from_that_part()
    {
        var level = new SpanLevel();
        SceneNode door = level.Part("Door", DoorCenter, DoorHalf);
        SceneNode squeak = door.CreateChild("Sounds").CreateChild("Squeak");
        var obstacles = new SceneSoundObstacles(() => level.Scene);

        obstacles.HeardFrom(DoorCenter, DoorEar, squeak).Z.ShouldBe(-3.8f, 1e-4f);
    }

    [Fact]
    public void The_reach_turns_with_the_part()
    {
        var level = new SpanLevel();
        SceneNode door = level.Part("Door", DoorCenter, DoorHalf);
        door.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var obstacles = new SceneSoundObstacles(() => level.Scene);

        // The door's thin way now lies along x, and its width along z.
        obstacles.HeardFrom(DoorCenter, DoorCenter + (Vector3.UnitX * 4f), door).X.ShouldBe(3.45f, 1e-4f);
        obstacles.HeardFrom(DoorCenter, DoorEar, door).Z.ShouldBe(-3f, 1e-4f);
    }

    [Fact]
    public void A_listener_within_the_reach_of_the_part_is_where_the_line_ends()
    {
        var level = new SpanLevel();
        SceneNode door = level.Part("Door", DoorCenter, DoorHalf);
        var obstacles = new SceneSoundObstacles(() => level.Scene);
        var close = new Vector3(3.5f, 1f, -3.9f);

        Apart(obstacles.HeardFrom(DoorCenter, close, door), close).ShouldBeLessThan(1e-5f);
    }

    [Fact]
    public void A_sound_on_no_part_is_heard_from_where_it_is()
    {
        var level = new SpanLevel();
        level.Part("Door", DoorCenter, DoorHalf);
        SceneNode speaker = level.Scene.Root.CreateChild("Speaker");
        var obstacles = new SceneSoundObstacles(() => level.Scene);

        obstacles.HeardFrom(DoorCenter, DoorEar, speaker).ShouldBe(DoorCenter);
        obstacles.HeardFrom(DoorCenter, DoorEar, null).ShouldBe(DoorCenter);
    }

    [Fact]
    public void A_sound_further_from_its_part_than_the_reach_is_heard_from_where_it_is()
    {
        var level = new SpanLevel();
        SceneNode door = level.Part("Door", DoorCenter, DoorHalf);
        SceneNode horn = door.CreateChild("Horn");
        horn.LocalPosition = new Vector3(0f, 0f, 1f);
        var obstacles = new SceneSoundObstacles(() => level.Scene);

        obstacles.HeardFrom(horn.WorldPosition, DoorEar, horn).ShouldBe(horn.WorldPosition);
    }

    [Fact]
    public void A_sound_under_a_trigger_has_no_part_to_be_heard_from()
    {
        var level = new SpanLevel();
        SceneNode zone = level.Part("Zone", DoorCenter, DoorHalf);
        zone.CanCollide = false;
        var obstacles = new SceneSoundObstacles(() => level.Scene);

        obstacles.HeardFrom(DoorCenter, DoorEar, zone.CreateChild("Hum")).ShouldBe(DoorCenter);
    }

    [Fact]
    public void A_full_list_says_so()
    {
        var level = new SpanLevel();
        level.Part("A", new Vector3(0f, 1f, -2f), new Vector3(1f, 1f, 0.25f));
        level.Part("B", new Vector3(0f, 1f, -4f), new Vector3(1f, 1f, 0.25f));
        var obstacles = new SceneSoundObstacles(() => level.Scene);
        var spans = new SolidSpan[1];

        obstacles.Trace(Near, Far, null, spans, out bool truncated).ShouldBe(1);

        truncated.ShouldBeTrue();
        spans[0].Start.ShouldBe(1.75f, 1e-4f);
    }

    private static float Apart(Vector3 a, Vector3 b) => Vector3.Distance(a, b);

    private static SpanLevel Walled()
    {
        var level = new SpanLevel();
        level.Wall();
        return level.Compile();
    }
}
