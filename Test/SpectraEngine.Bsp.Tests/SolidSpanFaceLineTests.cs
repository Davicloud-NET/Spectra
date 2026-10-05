using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a segment that lies in the plane of a brush face reports: solid only
/// where there is solid on every side of it.
/// </summary>
// The wall fills z from -4.5 to -4 and stands on a floor whose top is y = 0.
// Its doorway is x from -1 to 1, up to 2.4.
public sealed class SolidSpanFaceLineTests
{
    private const float Exact = 1e-4f;

    private static readonly Vector3 Near = new(0f, 1f, 0f);
    private static readonly Vector3 Far = new(0f, 1f, -8f);

    [Fact]
    public void A_line_along_the_sill_of_a_cut_window_passes_through_nothing()
    {
        SpanLevel level = WindowedWall();

        level.Trace(Near, Far).ShouldBeEmpty();
    }

    [Fact]
    public void A_line_along_the_head_of_a_cut_window_passes_through_nothing()
    {
        SpanLevel level = WindowedWall();
        var up = new Vector3(0f, 1f, 0f);

        level.Trace(Near + up, Far + up).ShouldBeEmpty();
    }

    [Fact]
    public void A_line_along_the_jamb_of_a_cut_doorway_passes_through_nothing()
    {
        SpanLevel level = Room();
        var aside = new Vector3(1f, 0f, 0f);

        level.Trace(Near + aside, Far + aside).ShouldBeEmpty();
        level.Trace(Near - aside, Far - aside).ShouldBeEmpty();
    }

    [Fact]
    public void A_line_along_the_jamb_of_a_doorway_with_its_door_shut_passes_through_the_door()
    {
        SpanLevel level = Room();
        level.Part("Door", new Vector3(0f, 1.2f, -4.25f), new Vector3(1f, 1.2f, 0.2f), SpanLevel.Wood);
        var aside = new Vector3(1f, 0f, 0f);

        // The wall is on one side of the line and the door on the other. The
        // door is the thinner of the two.
        SolidSpan span = level.Trace(Near + aside, Far + aside).ShouldHaveSingleItem();

        span.Start.ShouldBe(4.05f, Exact);
        span.End.ShouldBe(4.45f, Exact);
    }

    [Fact]
    public void A_line_along_the_jamb_passes_through_a_shut_door_that_was_turned_into_place()
    {
        SpanLevel level = Room();

        // The same door, built along the wall and turned a quarter turn into
        // the doorway. Its ends are now where rounding put them.
        SceneNode door = level.Part(
            "Door", new Vector3(0f, 1.2f, -4.25f), new Vector3(0.2f, 1.2f, 1f), SpanLevel.Wood);
        door.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);

        foreach (float x in (float[])[1f, -1f])
        {
            var aside = new Vector3(x, 0f, 0f);

            SolidSpan span = level.Trace(Near + aside, Far + aside).ShouldHaveSingleItem();

            span.Start.ShouldBe(4.05f, Exact);
            span.End.ShouldBe(4.45f, Exact);
        }
    }

    [Fact]
    public void A_line_in_the_seam_of_two_flush_walls_passes_through_the_wall()
    {
        var level = new SpanLevel();
        level.Box("West", new Vector3(-3f, 1.5f, -4.25f), new Vector3(3f, 1.5f, 0.25f), SpanLevel.Brick);
        level.Box("East", new Vector3(3f, 1.5f, -4.25f), new Vector3(3f, 1.5f, 0.25f), SpanLevel.Brick);
        level.Compile();

        SolidSpan span = level.Trace(Near, Far).ShouldHaveSingleItem();

        span.Start.ShouldBe(4f, Exact);
        span.End.ShouldBe(4.5f, Exact);
        span.Material.ShouldBe(SpanLevel.Brick);
    }

    [Fact]
    public void A_line_in_the_seam_of_two_materials_names_the_same_one_from_either_end()
    {
        var level = new SpanLevel();
        level.Box("West", new Vector3(-3f, 1.5f, -4.25f), new Vector3(3f, 1.5f, 0.25f), SpanLevel.Brick);
        level.Box("East", new Vector3(3f, 1.5f, -4.25f), new Vector3(3f, 1.5f, 0.25f), SpanLevel.Wood);
        level.Compile();

        SolidSpan there = level.Trace(Near, Far).ShouldHaveSingleItem();
        SolidSpan back = level.Trace(Far, Near).ShouldHaveSingleItem();

        // Neither side has more of a claim. The one toward +x is taken.
        there.Material.ShouldBe(SpanLevel.Wood);
        back.Material.ShouldBe(SpanLevel.Wood);
        back.Thickness.ShouldBe(there.Thickness, Exact);
    }

    [Fact]
    public void A_line_in_the_seam_of_two_walls_stops_where_one_of_them_does()
    {
        var level = new SpanLevel();
        level.Box("West", new Vector3(-3f, 1.5f, -4.25f), new Vector3(3f, 1.5f, 0.25f), SpanLevel.Brick);
        level.Box("East", new Vector3(3f, 1.5f, -5f), new Vector3(3f, 1.5f, 1f), SpanLevel.Wood);
        level.Compile();

        // East reaches from z = -6 to -4, West only to -4.5.
        SolidSpan span = level.Trace(Near, Far).ShouldHaveSingleItem();

        span.Start.ShouldBe(4f, Exact);
        span.End.ShouldBe(4.5f, Exact);
    }

    [Fact]
    public void A_line_along_the_floor_passes_through_a_wall_that_stands_on_it()
    {
        SpanLevel level = Room();
        var onTheFloor = new Vector3(3f, -1f, 0f);

        SolidSpan span = level.Trace(Near + onTheFloor, Far + onTheFloor).ShouldHaveSingleItem();

        span.Start.ShouldBe(4f, Exact);
        span.End.ShouldBe(4.5f, Exact);
    }

    [Fact]
    public void A_line_along_the_floor_through_the_doorway_passes_through_nothing()
    {
        SpanLevel level = Room();
        var onTheFloor = new Vector3(0f, -1f, 0f);

        level.Trace(Near + onTheFloor, Far + onTheFloor).ShouldBeEmpty();
    }

    [Fact]
    public void A_line_along_the_edge_where_four_blocks_meet_passes_through_them()
    {
        SpanLevel level = Blocks(count: 4);

        SolidSpan span = level.Trace(Near, Far).ShouldHaveSingleItem();

        span.Start.ShouldBe(4f, Exact);
        span.End.ShouldBe(5f, Exact);
    }

    [Fact]
    public void A_line_along_an_edge_with_one_block_of_the_four_missing_passes_through_nothing()
    {
        SpanLevel level = Blocks(count: 3);

        level.Trace(Near, Far).ShouldBeEmpty();
    }

    [Fact]
    public void A_line_in_the_seam_between_a_wall_and_one_turned_a_quarter_turn_passes_through_the_wall()
    {
        var level = new SpanLevel();
        level.Box("West", new Vector3(-3f, 1.5f, -4.25f), new Vector3(3f, 1.5f, 0.25f), SpanLevel.Brick);

        // Turned, the same box as West: 0.5 along z and 6 along x.
        SceneNode east = level.Box(
            "East", new Vector3(3f, 1.5f, -4.25f), new Vector3(0.25f, 1.5f, 3f), SpanLevel.Brick);
        east.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        level.Compile();

        SolidSpan span = level.Trace(Near, Far).ShouldHaveSingleItem();

        span.Start.ShouldBe(4f, Exact);
        span.End.ShouldBe(4.5f, Exact);
    }

    [Fact]
    public void A_line_along_the_face_of_a_wall_turned_a_quarter_turn_passes_through_nothing()
    {
        var level = new SpanLevel();
        SceneNode wall = level.Box(
            "Turned", new Vector3(3f, 1.5f, -4.25f), new Vector3(0.25f, 1.5f, 3f), SpanLevel.Brick);
        wall.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        level.Compile();

        // Its near face, its far face and its end, each from both directions.
        (Vector3 From, Vector3 To)[] lines =
        [
            (new(-3f, 1f, -4f), new(9f, 1f, -4f)),
            (new(-3f, 1f, -4.5f), new(9f, 1f, -4.5f)),
            (new(0f, 1f, 0f), new(0f, 1f, -8f)),
            (new(6f, 1f, 0f), new(6f, 1f, -8f)),
        ];

        foreach ((Vector3 from, Vector3 to) in lines)
        {
            level.Trace(from, to).ShouldBeEmpty($"from {from} to {to}");
            level.Trace(to, from).ShouldBeEmpty($"from {to} to {from}");
        }
    }

    [Fact]
    public void A_second_trace_along_a_seam_allocates_nothing()
    {
        SpanLevel level = Blocks(count: 4);
        level.Part("Crate", new Vector3(1f, 1.5f, -6.5f), new Vector3(1f, 0.5f, 0.5f), SpanLevel.Wood);
        var spans = new SolidSpan[16];

        int found = 0;
        for (int i = 0; i < 50; i++)
            found += level.Scene.TraceSolidSpans(Near, Far, spans, out _);

        found.ShouldBe(50);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            level.Scene.TraceSolidSpans(Near, Far, spans, out _);

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0L);
    }

    private static SpanLevel Room()
    {
        var level = new SpanLevel();
        level.Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(6f, 0.5f, 6f), SpanLevel.Tile);
        level.Wall();
        level.Doorway();
        return level.Compile();
    }

    // A window through the wall from y = 1 to y = 2.
    private static SpanLevel WindowedWall()
    {
        var level = new SpanLevel();
        level.Wall();
        level.Cut("Window", new Vector3(0f, 1.5f, -4.25f), new Vector3(1f, 0.5f, 0.25f));
        return level.Compile();
    }

    // Blocks round the line x = 0, y = 1, each z from -5 to -4.
    private static SpanLevel Blocks(int count)
    {
        var level = new SpanLevel();
        Vector3[] centers =
        [
            new(-1f, 0.5f, -4.5f), new(1f, 0.5f, -4.5f), new(-1f, 1.5f, -4.5f), new(1f, 1.5f, -4.5f),
        ];

        for (int i = 0; i < count; i++)
            level.Box($"Block{i}", centers[i], new Vector3(1f, 0.5f, 0.5f), SpanLevel.Brick);

        return level.Compile();
    }
}
