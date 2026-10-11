using System;
using System.Numerics;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// An icon that a pile's row would be drawn over is pushed clear of it, and
/// so are two rows that meet.
/// </summary>
public sealed class IconFanBesideTests
{
    private const float Half = 10f;
    private const float Width = Half * 2f;

    private static readonly Vector2 Point = new(50f, 30f);

    // A pile of this many on the point, and one more icon listed last.
    private static Vector2[] PileAnd(int pile, Vector2 beside)
    {
        var anchors = new Vector2[pile + 1];
        var halfWidths = new float[pile + 1];
        Array.Fill(anchors, Point);
        Array.Fill(halfWidths, Half);
        anchors[pile] = beside;

        var places = new Vector2[pile + 1];
        new IconFan().Spread(anchors, halfWidths, places);
        return places;
    }

    private static float Nearest(Vector2[] places, int icon)
    {
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < places.Length; i++)
        {
            if (i != icon)
                nearest = MathF.Min(nearest, Vector2.Distance(places[i], places[icon]));
        }

        return nearest;
    }

    [Fact]
    public void An_icon_on_the_spot_where_a_rows_end_is_drawn_stands_under_it()
    {
        // The fourth of the row is drawn a width and a half from the point.
        Vector2 end = Point + new Vector2(1.5f * Width, 0f);

        Vector2[] places = PileAnd(4, end);

        places[3].ShouldBe(end - new Vector2(0f, Half));
        places[4].ShouldBe(end + new Vector2(0f, Half));
        places[2].ShouldBe(Point + new Vector2(0.5f * Width, 0f));
    }

    [Theory]
    [InlineData(1.1f)]
    [InlineData(1.3f)]
    [InlineData(1.5f)]
    [InlineData(1.7f)]
    [InlineData(2f)]
    public void An_icon_level_with_a_row_and_within_its_reach_is_not_drawn_under_it(float widths)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            Vector2[] places = PileAnd(4, Point + new Vector2(side * widths * Width, 0f));

            Nearest(places, 4).ShouldBeGreaterThan(Width * 0.75f, $"side {side}");
        }
    }

    [Fact]
    public void An_icon_clear_of_a_row_stays_put_and_the_row_stays_whole()
    {
        Vector2 beside = Point + new Vector2(2.5f * Width, 0f);

        Vector2[] places = PileAnd(4, beside);

        places[4].ShouldBe(beside);
        for (int i = 0; i < 4; i++)
            places[i].ShouldBe(Point + new Vector2((i - 1.5f) * Width, 0f));
    }

    [Fact]
    public void Two_rows_that_meet_end_to_end_part_at_the_ends_that_meet()
    {
        // Two piles of three, two and a half widths apart: the rows would
        // overlap by half an icon.
        Vector2 other = Point + new Vector2(2.5f * Width, 0f);
        Vector2[] anchors = [Point, Point, Point, other, other, other];
        var places = new Vector2[6];

        new IconFan().Spread(anchors, [Half, Half, Half, Half, Half, Half], places);

        Vector2.Distance(places[2], places[3]).ShouldBeGreaterThan(Width * 0.9f);
        places[0].ShouldBe(Point - new Vector2(Width, 0f));
        places[5].ShouldBe(other + new Vector2(Width, 0f));
    }

    // The push has one spot where it turns round, and an icon there is drawn
    // on the other. It is half a width above a row icon and listed after it.
    [Fact]
    public void Few_places_beside_a_row_leave_an_icon_half_covered()
    {
        const int Steps = 60;
        int outside = 0;
        int covered = 0;

        for (int x = -Steps; x <= Steps; x++)
        {
            for (int y = -Steps / 2; y <= Steps / 2; y++)
            {
                Vector2 from = new Vector2(x, y) * (4f * Width / Steps);
                if (from.Length() < Width)
                    continue;

                outside++;
                if (Nearest(PileAnd(4, Point + from), 4) < Half)
                    covered++;
            }
        }

        // One in twenty before the push.
        (covered / (float)outside).ShouldBeLessThan(0.01f);
    }

    [Fact]
    public void An_icon_passing_a_rows_end_moves_it_little_by_little()
    {
        const float Step = 0.01f;
        float largest = 0f;

        foreach (float height in new[] { 0f, -0.3f * Width, -0.5f * Width, 0.4f * Width })
        {
            Vector2[] before = PileAnd(4, Point + new Vector2(3f * Width, height));

            for (float along = 3f * Width; along > 1f * Width; along -= Step)
            {
                Vector2[] after = PileAnd(4, Point + new Vector2(along, height));
                for (int i = 0; i < after.Length; i++)
                    largest = MathF.Max(largest, Vector2.Distance(before[i], after[i]));

                before = after;
            }
        }

        // In steps of the anchor. A jump would be a thousand.
        (largest / Step).ShouldBeLessThan(8f);
    }
}
