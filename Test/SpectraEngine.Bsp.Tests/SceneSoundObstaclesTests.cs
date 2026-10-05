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

    private static SpanLevel Walled()
    {
        var level = new SpanLevel();
        level.Wall();
        return level.Compile();
    }
}
