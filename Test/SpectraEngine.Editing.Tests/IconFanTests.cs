using System;
using System.Numerics;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Where the fan draws icons that overlap, on plain screen points. No camera
/// and no scene.
/// </summary>
public sealed class IconFanTests
{
    // Two icons touch when their anchors are one width apart.
    private const float Half = 10f;
    private const float Width = Half * 2f;

    private static readonly Vector2 Point = new(50f, 30f);

    private static Vector2[] Spread(params Vector2[] anchors) => Spread(anchors, Halves(anchors.Length));

    private static float[] Halves(int count)
    {
        var halfWidths = new float[count];
        Array.Fill(halfWidths, Half);
        return halfWidths;
    }

    private static Vector2[] Spread(Vector2[] anchors, float[] halfWidths, float[]? strengths = null)
    {
        var places = new Vector2[anchors.Length];
        new IconFan().Spread(anchors, halfWidths, strengths, places);
        return places;
    }

    private static Vector2[] Pile(int count)
    {
        var anchors = new Vector2[count];
        Array.Fill(anchors, Point);
        return anchors;
    }

    [Fact]
    public void Icons_that_do_not_overlap_stay_where_they_are_to_the_bit()
    {
        // The second only touches the first. The last two miss by a hair.
        Vector2[] anchors =
        [
            new(4f, 11f), new(4f + Width, 11f),
            new(100.3f, -50.9f), new(100.3f, -50.9f + Width + 0.001f),
            new(-333.3f, 0.7f),
        ];

        Spread(anchors).ShouldBe(anchors);
    }

    [Fact]
    public void Two_at_one_point_end_one_width_apart_centred_on_the_point_in_the_order_given()
    {
        Vector2[] places = Spread(Point, Point);

        places[0].ShouldBe(Point - new Vector2(Half, 0f));
        places[1].ShouldBe(Point + new Vector2(Half, 0f));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void A_pile_of_up_to_six_is_one_row_centred_on_the_point(int count)
    {
        Vector2[] places = Spread(Pile(count));

        for (int i = 0; i < count; i++)
            places[i].ShouldBe(Point + new Vector2((i - ((count - 1) * 0.5f)) * Width, 0f), $"icon {i}");
    }

    [Fact]
    public void The_seventh_of_a_pile_starts_a_second_row_under_the_sixth()
    {
        Vector2[] places = Spread(Pile(7));

        for (int i = 0; i < 6; i++)
            places[i].ShouldBe(Point + new Vector2((i - 2.5f) * Width, -Half), $"icon {i}");

        places[6].ShouldBe(Point + new Vector2(2.5f * Width, Half));
    }

    // Rows snake, so the first of a row is under the last of the row above.
    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(20)]
    [InlineData(40)]
    public void A_big_pile_is_rows_of_six_that_run_back_and_forth(int count)
    {
        Vector2[] places = Spread(Pile(count));

        int rows = (count + IconFan.RowLength - 1) / IconFan.RowLength;
        for (int i = 0; i < count; i++)
        {
            int row = i / IconFan.RowLength;
            int along = i % IconFan.RowLength;
            int column = row % 2 == 0 ? along : IconFan.RowLength - 1 - along;

            Vector2 expected = Point + new Vector2((column - 2.5f) * Width, (row - ((rows - 1) * 0.5f)) * Width);
            places[i].ShouldBe(expected, $"icon {i}");
        }
    }

    [Fact]
    public void No_two_icons_of_a_pile_are_drawn_over_each_other()
    {
        for (int count = 2; count <= 40; count++)
        {
            Vector2[] places = Spread(Pile(count));

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                    Vector2.Distance(places[i], places[j]).ShouldBeGreaterThanOrEqualTo(Width, $"{i} and {j} of {count}");
            }
        }
    }

    [Fact]
    public void Two_that_half_overlap_are_pushed_apart_by_half_and_drawn_together_by_a_sixth()
    {
        // Three quarters of a width apart is half way from touching to the
        // distance where a pair is a row. So the share is a half: each is
        // pushed half of a half width outwards, and drawn towards the other by
        // the share squared, over one and the share, of what lies between.
        const float Apart = Width * 0.75f;
        Vector2[] places = Spread(new Vector2(0f, 0f), new Vector2(Apart, 0f));

        const float Share = 0.5f;
        float pushed = Share * Half;
        float drawn = Share * Share / (1f + Share) * Apart;

        places[0].X.ShouldBe(-pushed + drawn, tolerance: 1e-4f);
        places[1].X.ShouldBe(Apart + pushed - drawn, tolerance: 1e-4f);
        places[0].Y.ShouldBe(0f);
        places[1].Y.ShouldBe(0f);

        (places[1].X - places[0].X).ShouldBe(Width, tolerance: 1e-4f);
    }

    [Fact]
    public void Two_that_half_overlap_one_above_the_other_part_sideways()
    {
        const float Apart = Width * 0.75f;
        Vector2[] places = Spread(new Vector2(0f, 0f), new Vector2(0f, Apart));

        places[0].ShouldBe(new Vector2(-5f, 2.5f));
        places[1].ShouldBe(new Vector2(5f, Apart - 2.5f));
    }

    [Fact]
    public void A_row_keeps_the_order_given_whichever_side_each_anchor_is_on()
    {
        // Right to left on screen, and all within half a width of each other.
        Vector2[] places = Spread(Point + new Vector2(4f, 0f), Point, Point - new Vector2(4f, 0f));

        places[0].ShouldBe(Point - new Vector2(Width, 0f));
        places[1].ShouldBe(Point);
        places[2].ShouldBe(Point + new Vector2(Width, 0f));
    }

    [Fact]
    public void A_pile_of_icons_near_each_other_is_a_straight_row_on_the_middle_of_them()
    {
        Vector2[] anchors = [Point + new Vector2(3f, -2f), Point + new Vector2(-2f, 3f), Point + new Vector2(-1f, -1f)];

        Vector2[] places = Spread(anchors);

        for (int i = 0; i < 3; i++)
        {
            places[i].X.ShouldBe(Point.X + ((i - 1) * Width), tolerance: 1e-4f);
            places[i].Y.ShouldBe(Point.Y, tolerance: 1e-4f);
        }
    }

    [Fact]
    public void Icons_at_one_point_take_their_places_by_the_order_given_and_nothing_else()
    {
        // The same three, with icons that are nowhere near listed between them.
        Vector2 far = new(900f, 900f);
        Vector2[] alone = Spread(Pile(3));

        Vector2[] mixed = Spread(Point, far, far + new Vector2(0f, 100f), Point, far + new Vector2(100f, 0f), Point);

        mixed[0].ShouldBe(alone[0]);
        mixed[3].ShouldBe(alone[1]);
        mixed[5].ShouldBe(alone[2]);
        mixed[1].ShouldBe(far);
    }

    [Fact]
    public void Two_piles_that_are_apart_do_not_move_each_other()
    {
        Vector2 other = Point + new Vector2(Width * 4f, 0f);

        Vector2[] places = Spread(Point, other, Point, other);

        places[0].ShouldBe(Point - new Vector2(Half, 0f));
        places[2].ShouldBe(Point + new Vector2(Half, 0f));
        places[1].ShouldBe(other - new Vector2(Half, 0f));
        places[3].ShouldBe(other + new Vector2(Half, 0f));
    }

    [Fact]
    public void A_row_is_spaced_for_its_widest_icon()
    {
        Vector2[] places = Spread([Point, Point, Point], [Half, Half * 0.5f, Half]);

        places[0].ShouldBe(Point - new Vector2(Width, 0f));
        places[1].ShouldBe(Point);
        places[2].ShouldBe(Point + new Vector2(Width, 0f));
    }

    [Fact]
    public void Icons_of_two_sizes_touch_where_their_two_half_widths_meet()
    {
        // 15 apart is where a 10 and a 5 touch, so nothing moves.
        Vector2[] anchors = [Point, Point + new Vector2(15f, 0f)];

        Spread(anchors, [Half, Half * 0.5f]).ShouldBe(anchors);
    }

    [Fact]
    public void An_icon_with_no_strength_stays_put_and_moves_nothing()
    {
        Vector2[] places = Spread(Pile(3), [Half, Half, Half], [1f, 0f, 1f]);

        places[0].ShouldBe(Point - new Vector2(Half, 0f));
        places[1].ShouldBe(Point);
        places[2].ShouldBe(Point + new Vector2(Half, 0f));
    }

    [Fact]
    public void A_pile_opens_by_the_strengths_of_each_pair()
    {
        Vector2[] places = Spread(Pile(2), [Half, Half], [0.5f, 0.5f]);

        places[0].ShouldBe(Point - new Vector2(Half * 0.25f, 0f));
        places[1].ShouldBe(Point + new Vector2(Half * 0.25f, 0f));
    }

    [Fact]
    public void The_same_input_gives_the_same_answer()
    {
        var random = new Random(7);
        var anchors = new Vector2[60];
        var halfWidths = new float[60];
        for (int i = 0; i < anchors.Length; i++)
        {
            anchors[i] = new Vector2(random.NextSingle() * 200f, random.NextSingle() * 120f);
            halfWidths[i] = 7f + (random.NextSingle() * 8f);
        }

        var fan = new IconFan();
        var first = new Vector2[60];
        var second = new Vector2[60];
        fan.Spread(anchors, halfWidths, first);

        // Something else in between, bigger, so nothing of it may be left behind.
        fan.Spread(Pile(200), Halves(200), new Vector2[200]);
        fan.Spread(anchors, halfWidths, second);

        second.ShouldBe(first);
        Spread(anchors, halfWidths).ShouldBe(first);
        first.ShouldNotBe(anchors);
    }

    [Fact]
    public void Spans_of_different_lengths_are_refused()
    {
        var fan = new IconFan();

        Should.Throw<ArgumentException>(() => fan.Spread(new Vector2[2], new float[3], new Vector2[2]));
        Should.Throw<ArgumentException>(() => fan.Spread(new Vector2[2], new float[2], new Vector2[1]));
        Should.Throw<ArgumentException>(() => fan.Spread(new Vector2[2], new float[2], new float[1], new Vector2[2]));
    }
}
