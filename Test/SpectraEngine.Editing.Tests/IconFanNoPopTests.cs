using System;
using System.Numerics;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// The fan does not pop: when an anchor moves a little, what is drawn moves a
/// little, also as icons join a pile and leave it.
/// </summary>
// Each test moves anchors a hundredth of a pixel at a time and looks at the
// largest move of anything drawn between two steps. A jump would be thousands
// of steps long.
public sealed class IconFanNoPopTests
{
    private const float Half = 10f;
    private const float Width = Half * 2f;
    private const float Step = 0.01f;

    private static readonly Vector2 Point = new(50f, 30f);

    public static TheoryData<float> EveryWay
    {
        get
        {
            var ways = new TheoryData<float>();
            for (int degrees = 0; degrees < 360; degrees += 15)
                ways.Add(degrees);

            return ways;
        }
    }

    [Theory]
    [MemberData(nameof(EveryWay))]
    public void Sliding_one_icon_onto_another_moves_neither_more_than_seven_steps_at_a_time(float degrees)
    {
        // Fastest where the pair changes sides: each goes half way round the
        // other while the anchor travels half a width.
        for (int place = 0; place < 2; place++)
        {
            (float mover, float still) = Slide(still: 1, place, degrees);

            mover.ShouldBeLessThan(7f, $"the one that slides, listed at {place}");
            still.ShouldBeLessThan(6f, $"the one that stays, the other listed at {place}");
        }
    }

    [Theory]
    [MemberData(nameof(EveryWay))]
    public void A_third_icon_joining_a_pair_moves_no_more_than_nine_steps_at_a_time_and_the_pair_five(float degrees)
    {
        for (int place = 0; place < 3; place++)
        {
            (float mover, float pair) = Slide(still: 2, place, degrees);

            mover.ShouldBeLessThan(9f, $"the one that joins, listed at {place}");
            pair.ShouldBeLessThan(5f, $"the pair, the other listed at {place}");
        }
    }

    // It has furthest to go: from touching the pile to the end of a full row,
    // or round it to the row beneath.
    [Theory]
    [MemberData(nameof(EveryWay))]
    public void A_seventh_icon_joining_six_slides_to_its_place_and_the_six_move_less(float degrees)
    {
        for (int place = 0; place < 7; place++)
        {
            (float mover, float six) = Slide(still: 6, place, degrees);

            mover.ShouldBeLessThan(20f, $"the one that joins, listed at {place}");
            six.ShouldBeLessThan(8f, $"the six, the other listed at {place}");
        }
    }

    [Fact]
    public void Nothing_jumps_as_an_icon_comes_to_touch_a_pile()
    {
        var fan = new IconFan();
        float[] halfWidths = [Half, Half, Half, Half];

        // Overlap is judged by the anchors, so one width from the point is
        // where the fourth touches the pile. It is on the row's end there.
        const float Hair = 0.001f;
        var nearly = new Vector2[4];
        var touching = new Vector2[4];
        var past = new Vector2[4];
        fan.Spread([Point, Point + new Vector2(Width + Hair, 0f), Point, Point], halfWidths, nearly);
        fan.Spread([Point, Point + new Vector2(Width, 0f), Point, Point], halfWidths, touching);
        fan.Spread([Point, Point + new Vector2(Width - Hair, 0f), Point, Point], halfWidths, past);

        for (int i = 0; i < 4; i++)
        {
            Vector2.Distance(nearly[i], touching[i]).ShouldBeLessThan(Hair * 8f, $"icon {i}, before");
            Vector2.Distance(past[i], touching[i]).ShouldBeLessThan(Hair * 8f, $"icon {i}, after");
        }
    }

    [Fact]
    public void An_icon_that_only_touches_another_moves_neither()
    {
        var places = new Vector2[2];
        Vector2[] anchors = [Point, Point + new Vector2(Width, 0f)];

        new IconFan().Spread(anchors, [Half, Half], places);

        places.ShouldBe(anchors);
    }

    [Fact]
    public void A_pile_opens_little_by_little_as_its_icons_gain_strength()
    {
        // What the scene does as a pile's icons grow from small marks into glyphs.
        var fan = new IconFan();
        Vector2[] pile = [Point, Point, Point, Point, Point, Point, Point, Point];
        var halfWidths = new float[pile.Length];
        var strengths = new float[pile.Length];
        var before = new Vector2[pile.Length];
        var after = new Vector2[pile.Length];
        Array.Fill(halfWidths, Half);

        fan.Spread(pile, halfWidths, strengths, before);
        before.ShouldBe(pile);

        const int Steps = 2000;
        float largest = 0f;
        for (int step = 1; step <= Steps; step++)
        {
            Array.Fill(strengths, step / (float)Steps);
            fan.Spread(pile, halfWidths, strengths, after);

            for (int i = 0; i < pile.Length; i++)
                largest = MathF.Max(largest, Vector2.Distance(before[i], after[i]));

            after.CopyTo(before, 0);
        }

        // No step is a quarter of a pixel. A jump would be a width.
        largest.ShouldBeLessThan(0.25f);
        after[7].ShouldBe(Point + new Vector2(1.5f * Width, Half));
    }

    [Fact]
    public void Twelve_icons_wandering_through_each_other_never_jump()
    {
        var random = new Random(11);
        const int Count = 12;
        const float Area = Width * 3f;

        var anchors = new Vector2[Count];
        var headings = new Vector2[Count];
        var halfWidths = new float[Count];
        for (int i = 0; i < Count; i++)
        {
            anchors[i] = new Vector2(random.NextSingle(), random.NextSingle()) * Area;
            halfWidths[i] = Half;
        }

        var fan = new IconFan();
        var before = new Vector2[Count];
        var after = new Vector2[Count];
        fan.Spread(anchors, halfWidths, before);

        float largest = 0f;
        for (int frame = 0; frame < 40_000; frame++)
        {
            for (int i = 0; i < Count; i++)
            {
                if (frame % 1000 == 0)
                    headings[i] = Heading(random.NextSingle() * 360f) * Step;

                anchors[i] = Vector2.Clamp(anchors[i] + headings[i], Vector2.Zero, new Vector2(Area));
            }

            fan.Spread(anchors, halfWidths, after);

            for (int i = 0; i < Count; i++)
                largest = MathF.Max(largest, Vector2.Distance(before[i], after[i]));

            after.CopyTo(before, 0);
        }

        // Every anchor moves a step a frame, and each icon is moved by all it
        // overlaps. A jump would be a width: two thousand steps.
        (largest / Step).ShouldBeLessThan(22f);
    }

    // Slides one icon in a straight line from three widths away onto a pile
    // at one point. Returns the largest move between two steps of the icon
    // that slides, and of any icon of the pile, in steps.
    private static (float Mover, float Still) Slide(int still, int place, float degrees)
    {
        int count = still + 1;
        var anchors = new Vector2[count];
        var halfWidths = new float[count];
        var before = new Vector2[count];
        var after = new Vector2[count];
        Array.Fill(anchors, Point);
        Array.Fill(halfWidths, Half);

        var fan = new IconFan();
        Vector2 heading = Heading(degrees);
        float largestMover = 0f;
        float largestStill = 0f;

        int steps = (int)(Width * 3f / Step);
        for (int step = 0; step <= steps; step++)
        {
            anchors[place] = Point + (heading * ((steps - step) * Step));
            fan.Spread(anchors, halfWidths, after);

            for (int i = 0; i < count && step > 0; i++)
            {
                float moved = Vector2.Distance(before[i], after[i]) / Step;
                if (i == place)
                    largestMover = MathF.Max(largestMover, moved);
                else
                    largestStill = MathF.Max(largestStill, moved);
            }

            after.CopyTo(before, 0);
        }

        return (largestMover, largestStill);
    }

    private static Vector2 Heading(float degrees)
    {
        float radians = degrees * (MathF.PI / 180f);
        return new Vector2(MathF.Cos(radians), MathF.Sin(radians));
    }
}
