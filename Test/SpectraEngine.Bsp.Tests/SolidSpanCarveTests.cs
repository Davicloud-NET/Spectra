using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The span query sees the world as carved: a cut doorway is open, the wall
/// beside it is solid, and overlapping brushes are one stretch of solid.
/// </summary>
// The wall fills z from -4.5 to -4. Its doorway is x from -1 to 1, up to 2.4.
public sealed class SolidSpanCarveTests
{
    private const float Exact = 1e-4f;

    private static readonly Vector3 Near = new(0f, 1f, 0f);
    private static readonly Vector3 Far = new(0f, 1f, -8f);

    [Fact]
    public void A_line_through_a_cut_doorway_passes_through_nothing()
    {
        SpanLevel level = Room();

        level.Trace(Near, Far).ShouldBeEmpty();
    }

    [Fact]
    public void A_line_through_the_wall_beside_the_doorway_passes_through_the_wall()
    {
        SpanLevel level = Room();
        var aside = new Vector3(3f, 0f, 0f);

        SolidSpan span = level.Trace(Near + aside, Far + aside).ShouldHaveSingleItem();

        span.Start.ShouldBe(4f, Exact);
        span.End.ShouldBe(4.5f, Exact);
        span.Material.ShouldBe(SpanLevel.Plaster);
    }

    [Fact]
    public void A_line_over_the_doorway_passes_through_the_lintel()
    {
        SpanLevel level = Room();
        var up = new Vector3(0f, 1.7f, 0f);

        level.Trace(Near + up, Far + up).ShouldHaveSingleItem().Thickness.ShouldBe(0.5f, Exact);
    }

    [Fact]
    public void A_line_that_clips_both_edges_of_the_doorway_passes_through_two_pieces_of_wall()
    {
        SpanLevel level = Room();

        // Almost along the wall: it is inside the wall's slab from x = -15/7
        // to x = 15/7, and the doorway takes the middle out of that.
        var from = new Vector3(-3f, 1f, -3.9f);
        var to = new Vector3(3f, 1f, -4.6f);
        float perUnitOfX = Vector3.Distance(from, to) / 6f;

        SolidSpan[] spans = level.Trace(from, to);

        spans.Length.ShouldBe(2);
        spans[0].Start.ShouldBe((3f - 15f / 7f) * perUnitOfX, Exact);
        spans[0].End.ShouldBe(2f * perUnitOfX, Exact);
        spans[1].Start.ShouldBe(4f * perUnitOfX, Exact);
        spans[1].End.ShouldBe((3f + 15f / 7f) * perUnitOfX, Exact);

        // Both are the wall, entered through its near face.
        spans[0].Material.ShouldBe(SpanLevel.Plaster);
        spans[1].Material.ShouldBe(SpanLevel.Plaster);
    }

    [Fact]
    public void A_line_that_leaves_the_wall_into_the_doorway_stops_at_the_jamb()
    {
        SpanLevel level = Room();

        // Into the wall's near face at x = -5/3, out through the side of the
        // doorway at x = -1, and it ends in the doorway.
        var from = new Vector3(-2.5f, 1f, -3.75f);
        var to = new Vector3(0f, 1f, -4.5f);
        float perUnitOfX = Vector3.Distance(from, to) / 2.5f;

        SolidSpan span = level.Trace(from, to).ShouldHaveSingleItem();

        span.Start.ShouldBe(5f / 6f * perUnitOfX, Exact);
        span.End.ShouldBe(1.5f * perUnitOfX, Exact);
    }

    [Fact]
    public void A_cut_placed_before_its_wall_opens_it_all_the_same()
    {
        var level = new SpanLevel();
        level.Doorway();
        level.Wall();
        level.Compile();

        level.Trace(Near, Far).ShouldBeEmpty();
        level.Trace(Near + new Vector3(3f, 0f, 0f), Far + new Vector3(3f, 0f, 0f)).Length.ShouldBe(1);
    }

    [Fact]
    public void A_brush_that_lies_inside_a_cut_is_not_solid()
    {
        SpanLevel level = Room();
        level.Box("Plug", new Vector3(0f, 1.2f, -4.25f), new Vector3(0.5f, 0.5f, 0.1f), SpanLevel.Wood);
        level.Compile();

        level.Trace(Near, Far).ShouldBeEmpty();
    }

    [Fact]
    public void A_cut_that_reaches_only_part_way_into_a_wall_leaves_the_rest()
    {
        var level = new SpanLevel();
        level.Wall();
        level.Cut("Niche", new Vector3(0f, 1f, -4.1f), new Vector3(0.5f, 0.5f, 0.1f));
        level.Compile();

        SolidSpan span = level.Trace(Near, Far).ShouldHaveSingleItem();

        span.Start.ShouldBe(4.2f, Exact);
        span.End.ShouldBe(4.5f, Exact);
        span.Material.ShouldBe(SpanLevel.Plaster);
    }

    [Fact]
    public void A_pocket_sealed_inside_a_block_splits_it_in_two()
    {
        var level = new SpanLevel();
        level.Box("Block", new Vector3(0f, 1f, -6f), new Vector3(2f, 2f, 2f), SpanLevel.Brick);
        level.Cut("Pocket", new Vector3(0f, 1f, -6f), new Vector3(0.5f, 0.5f, 0.5f));
        level.Compile();

        SolidSpan[] spans = level.Trace(Near, new Vector3(0f, 1f, -10f));

        spans.Length.ShouldBe(2);
        spans[0].Start.ShouldBe(4f, Exact);
        spans[0].End.ShouldBe(5.5f, Exact);
        spans[1].Start.ShouldBe(6.5f, Exact);
        spans[1].End.ShouldBe(8f, Exact);
    }

    [Fact]
    public void Two_overlapping_brushes_are_one_unbroken_stretch_and_the_earlier_one_keeps_the_overlap()
    {
        var level = new SpanLevel();
        level.Box("First", new Vector3(2f, 1f, 0f), new Vector3(2f, 1f, 1f), SpanLevel.Brick);
        level.Box("Second", new Vector3(4f, 1f, 0f), new Vector3(2f, 1f, 1f), SpanLevel.Wood);
        level.Compile();

        SolidSpan[] spans = level.Trace(new Vector3(-2f, 1f, 0f), new Vector3(8f, 1f, 0f));

        spans.Length.ShouldBe(2);
        spans[0].Start.ShouldBe(2f, Exact);
        spans[0].End.ShouldBe(6f, Exact);
        spans[0].Material.ShouldBe(SpanLevel.Brick);
        spans[1].End.ShouldBe(8f, Exact);
        spans[1].Material.ShouldBe(SpanLevel.Wood);

        // No air between them.
        spans[1].Start.ShouldBe(spans[0].End);
    }

    [Fact]
    public void The_split_moves_when_the_other_brush_was_placed_first()
    {
        var level = new SpanLevel();
        level.Box("First", new Vector3(4f, 1f, 0f), new Vector3(2f, 1f, 1f), SpanLevel.Wood);
        level.Box("Second", new Vector3(2f, 1f, 0f), new Vector3(2f, 1f, 1f), SpanLevel.Brick);
        level.Compile();

        SolidSpan[] spans = level.Trace(new Vector3(-2f, 1f, 0f), new Vector3(8f, 1f, 0f));

        spans.Length.ShouldBe(2);
        spans[0].Start.ShouldBe(2f, Exact);
        spans[0].End.ShouldBe(4f, Exact);
        spans[0].Material.ShouldBe(SpanLevel.Brick);
        spans[1].Start.ShouldBe(spans[0].End);
        spans[1].End.ShouldBe(8f, Exact);
        spans[1].Material.ShouldBe(SpanLevel.Wood);
    }

    [Fact]
    public void The_split_is_the_same_from_either_end()
    {
        var level = new SpanLevel();
        level.Box("First", new Vector3(2f, 1f, 0f), new Vector3(2f, 1f, 1f), SpanLevel.Brick);
        level.Box("Second", new Vector3(4f, 1f, 0f), new Vector3(2f, 1f, 1f), SpanLevel.Wood);
        level.Compile();

        SolidSpan[] back = level.Trace(new Vector3(8f, 1f, 0f), new Vector3(-2f, 1f, 0f));

        // The second brush's two units first, then the first brush's four.
        back.Length.ShouldBe(2);
        back[0].Thickness.ShouldBe(2f, Exact);
        back[0].Material.ShouldBe(SpanLevel.Wood);
        back[1].Thickness.ShouldBe(4f, Exact);
        back[1].Material.ShouldBe(SpanLevel.Brick);
    }

    [Fact]
    public void Two_overlapping_brushes_of_one_material_are_one_span()
    {
        var level = new SpanLevel();
        level.Box("First", new Vector3(2f, 1f, 0f), new Vector3(2f, 1f, 1f), SpanLevel.Brick);
        level.Box("Second", new Vector3(4f, 1f, 0f), new Vector3(2f, 1f, 1f), SpanLevel.Brick);
        level.Compile();

        SolidSpan span = level.Trace(new Vector3(-2f, 1f, 0f), new Vector3(8f, 1f, 0f)).ShouldHaveSingleItem();

        span.Start.ShouldBe(2f, Exact);
        span.End.ShouldBe(8f, Exact);
    }

    [Fact]
    public void Two_walls_standing_flush_against_each_other_have_no_air_between_them()
    {
        var level = new SpanLevel();
        level.Box("Outer", new Vector3(0f, 1f, -4.3f), new Vector3(3f, 1f, 0.3f), SpanLevel.Brick);
        level.Box("Inner", new Vector3(0f, 1f, -4.7f), new Vector3(3f, 1f, 0.1f), SpanLevel.Wood);
        level.Compile();

        SolidSpan[] spans = level.Trace(Near, Far);

        spans.Length.ShouldBe(2);
        spans[0].Material.ShouldBe(SpanLevel.Brick);
        spans[1].Material.ShouldBe(SpanLevel.Wood);
        spans[1].Start.ShouldBe(spans[0].End);
        (spans[1].End - spans[0].Start).ShouldBe(0.8f, Exact);
    }

    private static SpanLevel Room()
    {
        var level = new SpanLevel();
        level.Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(6f, 0.5f, 6f), SpanLevel.Tile);
        level.Wall();
        level.Doorway();
        return level.Compile();
    }
}
