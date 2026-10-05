using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a segment through plain walls reports: how thick the solid is, which
/// face names its material, and what the ends of the segment do.
/// </summary>
// The wall fills z from -4.5 to -4. Thicknesses are held to a tenth of a
// millimetre.
public sealed class SolidSpanWallTests
{
    private const float Exact = 1e-4f;

    [Fact]
    public void A_wall_crossed_square_on_gives_its_thickness_and_the_material_of_the_face_entered()
    {
        SpanLevel level = WalledLevel();

        SolidSpan span = level.Trace(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, -8f)).ShouldHaveSingleItem();

        span.Start.ShouldBe(4f, Exact);
        span.End.ShouldBe(4.5f, Exact);
        span.Thickness.ShouldBe(0.5f, Exact);
        span.Material.ShouldBe(SpanLevel.Plaster);
    }

    [Fact]
    public void The_same_wall_from_behind_names_the_face_on_that_side()
    {
        SpanLevel level = WalledLevel();

        SolidSpan span = level.Trace(new Vector3(0f, 1f, -8f), new Vector3(0f, 1f, 0f)).ShouldHaveSingleItem();

        span.Start.ShouldBe(3.5f, Exact);
        span.End.ShouldBe(4f, Exact);
        span.Material.ShouldBe(SpanLevel.Brick);
    }

    [Fact]
    public void A_wall_crossed_at_an_angle_is_thicker_by_the_slope()
    {
        SpanLevel level = WalledLevel();

        // Ten units long, eight of them toward the wall: the wall is met after
        // 5 and is 0.5 / 0.8 thick along the line.
        SolidSpan span = level.Trace(new Vector3(-3f, 1f, 0f), new Vector3(3f, 1f, -8f)).ShouldHaveSingleItem();

        span.Start.ShouldBe(5f, Exact);
        span.Thickness.ShouldBe(0.625f, Exact);
        span.Material.ShouldBe(SpanLevel.Plaster);
    }

    [Fact]
    public void A_wall_crossed_on_a_slope_in_all_three_axes_keeps_its_thickness_along_the_line()
    {
        SpanLevel level = WalledLevel();
        var from = new Vector3(-2f, 0.5f, 1f);
        var to = new Vector3(1f, 2.5f, -9f);

        SolidSpan span = level.Trace(from, to).ShouldHaveSingleItem();

        // Only z matters for where the line meets the two faces.
        float perUnitOfZ = Vector3.Distance(from, to) / 10f;
        span.Start.ShouldBe(5f * perUnitOfZ, Exact);
        span.End.ShouldBe(5.5f * perUnitOfZ, Exact);
    }

    [Fact]
    public void Two_walls_give_two_spans_in_order()
    {
        SpanLevel level = new();
        level.Wall();
        level.Box("Far", new Vector3(0f, 1.5f, -6.5f), new Vector3(6f, 1.5f, 0.5f), SpanLevel.Wood);
        level.Compile();

        SolidSpan[] spans = level.Trace(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, -8f));

        spans.Length.ShouldBe(2);
        spans[0].Start.ShouldBe(4f, Exact);
        spans[0].End.ShouldBe(4.5f, Exact);
        spans[0].Material.ShouldBe(SpanLevel.Plaster);
        spans[1].Start.ShouldBe(6f, Exact);
        spans[1].End.ShouldBe(7f, Exact);
        spans[1].Material.ShouldBe(SpanLevel.Wood);
    }

    [Fact]
    public void A_segment_that_starts_inside_a_wall_gets_a_span_from_zero_naming_the_face_it_leaves_by()
    {
        SpanLevel level = WalledLevel();

        SolidSpan span = level.Trace(new Vector3(0f, 1f, -4.25f), new Vector3(0f, 1f, 0f)).ShouldHaveSingleItem();

        span.Start.ShouldBe(0f);
        span.End.ShouldBe(0.25f, Exact);
        span.Material.ShouldBe(SpanLevel.Plaster);
    }

    [Fact]
    public void A_segment_that_ends_inside_a_wall_gets_a_span_up_to_its_length()
    {
        SpanLevel level = WalledLevel();

        SolidSpan span = level.Trace(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, -4.25f)).ShouldHaveSingleItem();

        span.Start.ShouldBe(4f, Exact);
        span.End.ShouldBe(4.25f);
        span.Material.ShouldBe(SpanLevel.Plaster);
    }

    [Fact]
    public void A_segment_wholly_inside_a_wall_is_one_span_of_its_whole_length()
    {
        SpanLevel level = WalledLevel();

        SolidSpan span = level.Trace(new Vector3(-2f, 1f, -4.25f), new Vector3(2f, 1f, -4.25f)).ShouldHaveSingleItem();

        span.Start.ShouldBe(0f);
        span.End.ShouldBe(4f);

        // The face it would leave by if it went on: the wall's end toward +x.
        span.Material.ShouldBe(SpanLevel.Tile);
    }

    [Fact]
    public void A_segment_that_misses_everything_gives_nothing()
    {
        SpanLevel level = WalledLevel();

        level.Trace(new Vector3(0f, 4f, 0f), new Vector3(0f, 4f, -8f)).ShouldBeEmpty();
        level.Trace(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, -3.5f)).ShouldBeEmpty();
        level.Trace(new Vector3(40f, 1f, 0f), new Vector3(40f, 1f, -8f)).ShouldBeEmpty();
    }

    [Fact]
    public void A_segment_that_runs_along_a_face_passes_through_nothing()
    {
        SpanLevel level = WalledLevel();

        // In the plane of the wall's near face, then of its top.
        level.Trace(new Vector3(-3f, 1f, -4f), new Vector3(3f, 1f, -4f)).ShouldBeEmpty();
        level.Trace(new Vector3(-3f, 3f, -4.25f), new Vector3(3f, 3f, -4.25f)).ShouldBeEmpty();
    }

    [Fact]
    public void A_segment_that_touches_an_edge_passes_through_nothing()
    {
        SpanLevel level = WalledLevel();

        // Down across the wall's top front edge at (y 3, z -4), on the open side.
        level.Trace(new Vector3(0f, 4f, -5f), new Vector3(0f, 2f, -3f)).ShouldBeEmpty();
    }

    [Fact]
    public void A_segment_that_stops_at_a_face_passes_through_nothing()
    {
        SpanLevel level = WalledLevel();

        level.Trace(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, -4f)).ShouldBeEmpty();
        level.Trace(new Vector3(0f, 1f, -4f), new Vector3(0f, 1f, 0f)).ShouldBeEmpty();
    }

    [Fact]
    public void A_segment_that_starts_on_a_face_and_goes_in_names_that_face()
    {
        SpanLevel level = WalledLevel();

        SolidSpan span = level.Trace(new Vector3(0f, 1f, -4f), new Vector3(0f, 1f, -8f)).ShouldHaveSingleItem();

        span.Start.ShouldBe(0f);
        span.End.ShouldBe(0.5f, Exact);
        span.Material.ShouldBe(SpanLevel.Plaster);
    }

    [Fact]
    public void A_segment_with_no_length_gives_nothing_even_inside_a_wall()
    {
        SpanLevel level = WalledLevel();
        var inside = new Vector3(0f, 1f, -4.25f);

        level.Trace(inside, inside).ShouldBeEmpty();
    }

    [Fact]
    public void A_segment_through_a_corner_for_less_than_the_tolerance_gives_nothing()
    {
        SpanLevel level = WalledLevel();

        // Across the wall's top front edge, half a millimetre inside it.
        level.Trace(new Vector3(0f, 4f, -5.0005f), new Vector3(0f, 2f, -3.0005f)).ShouldBeEmpty();
    }

    [Fact]
    public void Too_small_a_span_array_gets_the_nearest_wall_and_is_told()
    {
        SpanLevel level = new();
        level.Wall();
        level.Box("Far", new Vector3(0f, 1.5f, -6.5f), new Vector3(6f, 1.5f, 0.5f), SpanLevel.Wood);
        level.Compile();

        var one = new SolidSpan[1];
        int count = level.Scene.TraceSolidSpans(
            new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, -8f), one, out bool truncated);

        count.ShouldBe(1);
        truncated.ShouldBeTrue();
        one[0].Material.ShouldBe(SpanLevel.Plaster);

        level.Scene.TraceSolidSpans(
            new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, -8f), Span<SolidSpan>.Empty, out truncated).ShouldBe(0);
        truncated.ShouldBeTrue();
    }

    [Fact]
    public void A_scene_with_no_world_and_no_parts_gives_nothing()
    {
        var level = new SpanLevel();

        level.Trace(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, -8f)).ShouldBeEmpty();
    }

    [Fact]
    public void A_wall_added_after_the_last_compile_does_not_count_until_the_world_is_compiled_again()
    {
        SpanLevel level = WalledLevel();
        level.Box("Late", new Vector3(0f, 1.5f, -6.5f), new Vector3(6f, 1.5f, 0.5f), SpanLevel.Wood);

        level.Trace(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, -8f)).Length.ShouldBe(1);

        level.Compile();

        level.Trace(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, -8f)).Length.ShouldBe(2);
    }

    private static SpanLevel WalledLevel()
    {
        var level = new SpanLevel();
        level.Wall();
        return level.Compile();
    }
}
