using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The rule that turns per-brush stretches along a segment into solid: who
/// keeps an overlap, what a cut removes, what a part adds, and what is joined
/// or dropped at the end.
/// </summary>
public sealed class SolidSpanComposerTests
{
    private static readonly MaterialRef Brick = SpanLevel.Brick;
    private static readonly MaterialRef Wood = SpanLevel.Wood;
    private static readonly MaterialRef Tile = SpanLevel.Tile;

    [Fact]
    public void Nothing_added_composes_to_nothing()
    {
        Compose(new SolidSpanComposer()).ShouldBeEmpty();
    }

    [Fact]
    public void Of_two_overlapping_world_brushes_the_earlier_placement_keeps_the_overlap()
    {
        var composer = new SolidSpanComposer();
        composer.AddWorldSolid(4f, 8f, Brick, rank: 0u);
        composer.AddWorldSolid(2f, 6f, Wood, rank: 1u);

        Compose(composer).ShouldBe([new SolidSpan(2f, 4f, Wood), new SolidSpan(4f, 8f, Brick)]);
    }

    [Fact]
    public void The_order_brushes_are_found_in_does_not_change_who_keeps_an_overlap()
    {
        var composer = new SolidSpanComposer();
        composer.AddWorldSolid(2f, 6f, Wood, rank: 1u);
        composer.AddWorldSolid(4f, 8f, Brick, rank: 0u);

        Compose(composer).ShouldBe([new SolidSpan(2f, 4f, Wood), new SolidSpan(4f, 8f, Brick)]);
    }

    [Fact]
    public void A_later_brush_that_reaches_out_of_both_ends_of_an_earlier_one_is_split_in_two()
    {
        var composer = new SolidSpanComposer();
        composer.AddWorldSolid(3f, 5f, Brick, rank: 0u);
        composer.AddWorldSolid(1f, 9f, Wood, rank: 1u);

        Compose(composer).ShouldBe(
        [
            new SolidSpan(1f, 3f, Wood), new SolidSpan(3f, 5f, Brick), new SolidSpan(5f, 9f, Wood),
        ]);
    }

    [Fact]
    public void A_cut_removes_solid_from_brushes_placed_before_it_and_after_it()
    {
        var composer = new SolidSpanComposer();
        composer.AddWorldSolid(0f, 10f, Brick, rank: 0u);
        composer.AddCut(3f, 5f);
        composer.AddWorldSolid(4f, 12f, Wood, rank: 2u);

        Compose(composer).ShouldBe(
        [
            new SolidSpan(0f, 3f, Brick), new SolidSpan(5f, 10f, Brick), new SolidSpan(10f, 12f, Wood),
        ]);
    }

    [Fact]
    public void A_cut_in_open_air_adds_nothing()
    {
        var composer = new SolidSpanComposer();
        composer.AddCut(1f, 2f);
        composer.AddWorldSolid(5f, 6f, Brick, rank: 0u);

        Compose(composer).ShouldBe([new SolidSpan(5f, 6f, Brick)]);
    }

    [Fact]
    public void A_part_fills_a_cut_and_gives_way_to_the_world_around_it()
    {
        var composer = new SolidSpanComposer();
        composer.AddWorldSolid(0f, 10f, Brick, rank: 0u);
        composer.AddCut(3f, 5f);
        composer.AddPart(2f, 4.5f, Wood, tieBreak: 7u);

        Compose(composer).ShouldBe(
        [
            new SolidSpan(0f, 3f, Brick), new SolidSpan(3f, 4.5f, Wood), new SolidSpan(5f, 10f, Brick),
        ]);
    }

    [Fact]
    public void Of_two_overlapping_parts_the_one_entered_first_keeps_the_overlap()
    {
        var composer = new SolidSpanComposer();
        composer.AddPart(3f, 7f, Brick, tieBreak: 1u);
        composer.AddPart(1f, 5f, Wood, tieBreak: 2u);

        Compose(composer).ShouldBe([new SolidSpan(1f, 5f, Wood), new SolidSpan(5f, 7f, Brick)]);
    }

    [Fact]
    public void Two_parts_entered_at_one_distance_are_ordered_by_their_tie_break_whichever_is_added_first()
    {
        var one = new SolidSpanComposer();
        one.AddPart(1f, 5f, Wood, tieBreak: 2u);
        one.AddPart(1f, 7f, Brick, tieBreak: 1u);

        var other = new SolidSpanComposer();
        other.AddPart(1f, 7f, Brick, tieBreak: 1u);
        other.AddPart(1f, 5f, Wood, tieBreak: 2u);

        Compose(one).ShouldBe([new SolidSpan(1f, 7f, Brick)]);
        Compose(other).ShouldBe([new SolidSpan(1f, 7f, Brick)]);
    }

    [Fact]
    public void Touching_pieces_of_one_material_are_one_span()
    {
        var composer = new SolidSpanComposer();
        composer.AddWorldSolid(1f, 2f, Brick, rank: 0u);
        composer.AddWorldSolid(2f, 3f, Brick, rank: 1u);
        composer.AddWorldSolid(1.5f, 4f, Brick, rank: 2u);

        Compose(composer).ShouldBe([new SolidSpan(1f, 4f, Brick)]);
    }

    [Fact]
    public void A_gap_thinner_than_the_tolerance_is_closed()
    {
        var composer = new SolidSpanComposer();
        composer.AddWorldSolid(1f, 2f, Brick, rank: 0u);
        composer.AddWorldSolid(2.0005f, 3f, Brick, rank: 1u);
        composer.AddWorldSolid(3.0005f, 4f, Wood, rank: 2u);

        // The second gap has another material after it, so the two spans stay
        // apart and meet at one number.
        Compose(composer).ShouldBe([new SolidSpan(1f, 3f, Brick), new SolidSpan(3f, 4f, Wood)]);
    }

    [Fact]
    public void A_gap_wider_than_the_tolerance_stays_open()
    {
        var composer = new SolidSpanComposer();
        composer.AddWorldSolid(1f, 2f, Brick, rank: 0u);
        composer.AddWorldSolid(2.01f, 3f, Brick, rank: 1u);

        Compose(composer).Length.ShouldBe(2);
    }

    [Fact]
    public void Solid_thinner_than_the_tolerance_is_left_out()
    {
        var composer = new SolidSpanComposer();
        composer.AddWorldSolid(1f, 1.0004f, Brick, rank: 0u);
        composer.AddWorldSolid(5f, 6f, Wood, rank: 1u);

        Compose(composer).ShouldBe([new SolidSpan(5f, 6f, Wood)]);
    }

    [Fact]
    public void Too_small_a_span_array_gets_the_nearest_spans_and_is_told()
    {
        var composer = new SolidSpanComposer();
        composer.AddWorldSolid(7f, 8f, Tile, rank: 0u);
        composer.AddWorldSolid(1f, 2f, Brick, rank: 1u);
        composer.AddWorldSolid(4f, 5f, Wood, rank: 2u);

        var spans = new SolidSpan[2];
        int count = composer.Compose(spans, out bool truncated);

        count.ShouldBe(2);
        truncated.ShouldBeTrue();
        spans.ShouldBe([new SolidSpan(1f, 2f, Brick), new SolidSpan(4f, 5f, Wood)]);
    }

    [Fact]
    public void A_span_array_that_holds_every_span_is_not_called_too_small()
    {
        var composer = new SolidSpanComposer();
        composer.AddWorldSolid(1f, 2f, Brick, rank: 0u);

        var spans = new SolidSpan[1];
        composer.Compose(spans, out bool truncated).ShouldBe(1);
        truncated.ShouldBeFalse();
    }

    [Fact]
    public void Clearing_starts_the_next_segment_from_nothing()
    {
        var composer = new SolidSpanComposer();
        composer.AddWorldSolid(1f, 2f, Brick, rank: 0u);
        composer.AddCut(1f, 2f);
        composer.AddPart(3f, 4f, Wood, tieBreak: 0u);
        Compose(composer);

        composer.Clear();
        composer.AddWorldSolid(1f, 2f, Tile, rank: 0u);

        Compose(composer).ShouldBe([new SolidSpan(1f, 2f, Tile)]);
    }

    [Fact]
    public void Many_brushes_in_a_row_come_out_in_order()
    {
        // More than the scratch arrays start with, added far end first.
        var composer = new SolidSpanComposer();
        for (int i = 59; i >= 0; i--)
            composer.AddWorldSolid(i * 2f, i * 2f + 1f, i % 2 == 0 ? Brick : Wood, rank: (UInt128)i);

        SolidSpan[] spans = Compose(composer, capacity: 64);

        spans.Length.ShouldBe(60);
        for (int i = 0; i < spans.Length; i++)
            spans[i].ShouldBe(new SolidSpan(i * 2f, i * 2f + 1f, i % 2 == 0 ? Brick : Wood));
    }

    private static SolidSpan[] Compose(SolidSpanComposer composer, int capacity = 16)
    {
        var spans = new SolidSpan[capacity];
        int count = composer.Compose(spans, out bool truncated);
        truncated.ShouldBeFalse();
        return spans[..count];
    }
}
