using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Editing.Icons;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Path data turned into polylines, on shapes whose answer is known.
/// </summary>
public sealed class SvgPathFlattenerTests
{
    private const float Tolerance = 0.05f;

    private static List<Vector2[]> Flatten(string data, float tolerance = Tolerance)
    {
        var points = new List<Vector2>();
        var starts = new List<int>();
        SvgPathFlattener.Flatten(data, tolerance, points, starts);

        var polylines = new List<Vector2[]>();
        for (int i = 0; i < starts.Count; i++)
        {
            int end = i + 1 < starts.Count ? starts[i + 1] : points.Count;
            polylines.Add(points.GetRange(starts[i], end - starts[i]).ToArray());
        }

        return polylines;
    }

    private static void ShouldBeNear(Vector2 actual, Vector2 expected, float tolerance = 1e-4f) =>
        Vector2.Distance(actual, expected).ShouldBeLessThan(tolerance, $"{actual} against {expected}");

    [Fact]
    public void A_full_circle_from_two_arcs_stays_on_its_circle()
    {
        // The way the icon files draw a circle: two half turns about (8, 8).
        Vector2[] circle = Flatten("M 3 8 a 5 5 0 1 0 10 0 a 5 5 0 1 0 -10 0").ShouldHaveSingleItem();
        var centre = new Vector2(8f, 8f);

        circle.Length.ShouldBeGreaterThan(8);
        ShouldBeNear(circle[0], circle[^1]);

        for (int i = 0; i < circle.Length; i++)
        {
            Vector2.Distance(circle[i], centre).ShouldBe(5f, tolerance: 1e-4f);

            // A chord is farthest from the circle at its middle.
            if (i > 0)
            {
                float sag = 5f - Vector2.Distance((circle[i - 1] + circle[i]) * 0.5f, centre);
                sag.ShouldBeInRange(0f, Tolerance);
            }
        }
    }

    [Fact]
    public void A_tighter_tolerance_takes_more_segments()
    {
        const string Circle = "M 3 8 a 5 5 0 1 0 10 0 a 5 5 0 1 0 -10 0";

        Flatten(Circle, 0.01f)[0].Length.ShouldBeGreaterThan(Flatten(Circle, 0.2f)[0].Length);
    }

    [Theory]
    [InlineData("M 2 3 L 6 3 L 6 9 Z", "M 2 3 l 4 0 l 0 6 z")]
    [InlineData("M 2 3 H 6 V 9", "M 2 3 h 4 v 6")]
    [InlineData("M 1 1 C 2 4 6 4 7 1", "M 1 1 c 1 3 5 3 6 0")]
    [InlineData("M 1 1 C 2 4 6 4 7 1 S 12 -2 13 1", "M 1 1 c 1 3 5 3 6 0 s 5 -3 6 0")]
    [InlineData("M 1 1 Q 4 6 7 1", "M 1 1 q 3 5 6 0")]
    [InlineData("M 1 1 Q 4 6 7 1 T 13 1", "M 1 1 q 3 5 6 0 t 6 0")]
    [InlineData("M 3 8 A 5 3 30 0 1 11 9", "M 3 8 a 5 3 30 0 1 8 1")]
    [InlineData("M 2 2 L 5 2 M 8 8 L 9 9", "M 2 2 l 3 0 m 3 6 l 1 1")]
    public void A_relative_command_lands_where_the_absolute_one_does(string absolute, string relative)
    {
        List<Vector2[]> expected = Flatten(absolute);
        List<Vector2[]> actual = Flatten(relative);

        actual.Count.ShouldBe(expected.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            actual[i].Length.ShouldBe(expected[i].Length);
            for (int j = 0; j < expected[i].Length; j++)
                ShouldBeNear(actual[i][j], expected[i][j]);
        }
    }

    // From (0, 0) to (10, 0) on a circle of radius 10. The centre is 8.66
    // above or below, and each flag pair picks one of the four arcs. With the
    // sweep flag on the angle grows, which on a screen is clockwise.
    [Theory]
    [InlineData("0 0", -8.66f, false)]
    [InlineData("0 1", 8.66f, false)]
    [InlineData("1 0", 8.66f, true)]
    [InlineData("1 1", -8.66f, true)]
    public void The_arc_flags_pick_the_arc(string flags, float centreY, bool isLarge)
    {
        Vector2[] arc = Flatten($"M 0 0 A 10 10 0 {flags} 10 0", 0.01f).ShouldHaveSingleItem();
        var centre = new Vector2(5f, centreY);

        ShouldBeNear(arc[0], Vector2.Zero);
        ShouldBeNear(arc[^1], new Vector2(10f, 0f));
        foreach (Vector2 point in arc)
            Vector2.Distance(point, centre).ShouldBe(10f, tolerance: 0.01f);

        // The short arc bulges 1.34 from the chord and the long one 18.66.
        float bulge = 0f;
        foreach (Vector2 point in arc)
            bulge = MathF.Max(bulge, MathF.Abs(point.Y));

        bulge.ShouldBe(isLarge ? 18.66f : 1.34f, tolerance: 0.05f);

        // Sweep on turns clockwise on a screen, where y runs down.
        bool sweep = flags.EndsWith('1');
        float turned = Cross(arc[0] - centre, arc[1] - centre);
        (turned > 0f).ShouldBe(sweep);
    }

    [Fact]
    public void Arc_flags_may_stand_with_no_space_between_them()
    {
        Vector2[] spaced = Flatten("M 0 0 a 10 10 0 0 1 10 0").ShouldHaveSingleItem();
        Vector2[] packed = Flatten("M0 0a10 10 0 0110 0").ShouldHaveSingleItem();

        packed.Length.ShouldBe(spaced.Length);
        for (int i = 0; i < spaced.Length; i++)
            ShouldBeNear(packed[i], spaced[i]);
    }

    [Fact]
    public void A_path_with_several_subpaths_gives_several_polylines()
    {
        List<Vector2[]> polylines = Flatten("M 1 1 h 4 M 1 5 h 4 v 2 M 9 9 l 1 1");

        polylines.Count.ShouldBe(3);
        polylines[0].ShouldBe([new Vector2(1f, 1f), new Vector2(5f, 1f)]);
        polylines[1].ShouldBe([new Vector2(1f, 5f), new Vector2(5f, 5f), new Vector2(5f, 7f)]);
        polylines[2].ShouldBe([new Vector2(9f, 9f), new Vector2(10f, 10f)]);
    }

    [Fact]
    public void A_close_joins_back_to_the_start_and_the_next_line_starts_from_there()
    {
        List<Vector2[]> polylines = Flatten("M 2 2 h 4 v 4 z l -1 -1");

        polylines.Count.ShouldBe(2);
        polylines[0].ShouldBe([new Vector2(2f, 2f), new Vector2(6f, 2f), new Vector2(6f, 6f), new Vector2(2f, 2f)]);
        polylines[1].ShouldBe([new Vector2(2f, 2f), new Vector2(1f, 1f)]);
    }

    [Fact]
    public void Numbers_after_a_move_are_lines()
    {
        Flatten("M 1 1 4 1 4 5").ShouldHaveSingleItem()
            .ShouldBe([new Vector2(1f, 1f), new Vector2(4f, 1f), new Vector2(4f, 5f)]);

        Flatten("m 1 1 3 0 0 4").ShouldHaveSingleItem()
            .ShouldBe([new Vector2(1f, 1f), new Vector2(4f, 1f), new Vector2(4f, 5f)]);
    }

    [Fact]
    public void Numbers_may_run_together_the_way_a_minifier_writes_them()
    {
        Flatten("M1.5.5-2-3l1e1,2").ShouldHaveSingleItem()
            .ShouldBe([new Vector2(1.5f, 0.5f), new Vector2(-2f, -3f), new Vector2(8f, -1f)]);
    }

    [Fact]
    public void A_curve_stays_within_the_tolerance_of_its_polyline()
    {
        // A quarter circle as one cubic, the usual 0.5523 handles.
        const float Radius = 8f;
        float handle = Radius * 0.5523f;
        Vector2[] quarter = Flatten(
            FormattableString.Invariant($"M {Radius} 0 C {Radius} {handle} {handle} {Radius} 0 {Radius}"),
            0.02f).ShouldHaveSingleItem();

        quarter.Length.ShouldBeGreaterThan(4);
        for (int i = 1; i < quarter.Length; i++)
        {
            // The cubic itself is within 0.003 of the circle at this size.
            float middle = ((quarter[i - 1] + quarter[i]) * 0.5f).Length();
            (Radius - middle).ShouldBeInRange(-0.01f, 0.02f + 0.01f);
        }
    }

    [Fact]
    public void A_straight_curve_is_one_segment()
    {
        Flatten("M 0 0 C 1 1 2 2 3 3").ShouldHaveSingleItem().Length.ShouldBe(2);
        Flatten("M 0 0 Q 2 2 4 4").ShouldHaveSingleItem().Length.ShouldBe(2);
    }

    [Fact]
    public void A_smooth_curve_after_a_line_bends_from_where_the_path_is()
    {
        // With no curve before it, a smooth cubic's first handle is the current point.
        Vector2[] smooth = Flatten("M 0 0 L 2 0 S 6 4 8 0").ShouldHaveSingleItem();
        Vector2[] spelled = Flatten("M 0 0 L 2 0 C 2 0 6 4 8 0").ShouldHaveSingleItem();

        smooth.Length.ShouldBe(spelled.Length);
        for (int i = 0; i < spelled.Length; i++)
            ShouldBeNear(smooth[i], spelled[i]);
    }

    [Fact]
    public void A_move_nothing_is_drawn_from_leaves_no_polyline()
    {
        Flatten("M 1 1 M 2 2 l 1 0 M 9 9").ShouldHaveSingleItem()
            .ShouldBe([new Vector2(2f, 2f), new Vector2(3f, 2f)]);
        Flatten("").ShouldBeEmpty();
    }

    [Fact]
    public void An_arc_with_no_radius_is_a_line()
    {
        Flatten("M 0 0 A 0 5 0 0 1 4 4").ShouldHaveSingleItem()
            .ShouldBe([Vector2.Zero, new Vector2(4f, 4f)]);
    }

    [Fact]
    public void An_arc_too_small_for_its_chord_grows_until_it_spans_it()
    {
        // Radius 1 cannot reach across 10, so it becomes a half circle of radius 5.
        Vector2[] arc = Flatten("M 0 0 A 1 1 0 0 1 10 0", 0.01f).ShouldHaveSingleItem();

        foreach (Vector2 point in arc)
            Vector2.Distance(point, new Vector2(5f, 0f)).ShouldBe(5f, tolerance: 0.01f);
    }

    [Theory]
    [InlineData("1 2 3")]
    [InlineData("M 1")]
    [InlineData("M 1 1 X 2 2")]
    [InlineData("M 0 0 A 5 5 0 2 1 4 4")]
    [InlineData("M 1 1 L 2 two")]
    public void Text_that_is_not_path_data_is_refused(string data)
    {
        Should.Throw<FormatException>(() => Flatten(data));
    }

    private static float Cross(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);
}
