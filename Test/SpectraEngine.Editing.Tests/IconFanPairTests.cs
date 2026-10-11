using System;
using System.Numerics;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Two icons that overlap are drawn touching, whichever side each anchor is
/// on. One spot is left where they are not, and it is off the level line.
/// </summary>
public sealed class IconFanPairTests
{
    private const float Half = 10f;
    private const float Width = Half * 2f;

    // Which way the later anchor lies from the earlier one on the seam: up and
    // to the left.
    private const float SeamDegrees = 210f;

    private static Vector2[] Spread(Vector2 apart)
    {
        var places = new Vector2[2];
        new IconFan().Spread([Vector2.Zero, apart], [Half, Half], places);
        return places;
    }

    private static float Drawn(Vector2 apart)
    {
        Vector2[] places = Spread(apart);
        return Vector2.Distance(places[0], places[1]);
    }

    private static Vector2 Heading(float degrees)
    {
        float radians = degrees * (MathF.PI / 180f);
        return new Vector2(MathF.Cos(radians), MathF.Sin(radians));
    }

    [Theory]
    [InlineData(0.95f)]
    [InlineData(0.85f)]
    [InlineData(0.80f)]
    [InlineData(0.75f)]
    [InlineData(0.70f)]
    [InlineData(0.60f)]
    public void A_pair_listed_against_the_screen_is_drawn_touching_all_the_way_in(float widths)
    {
        // The first listed is on the right.
        Drawn(new Vector2(-widths * Width, 0f)).ShouldBe(Width, tolerance: 1e-3f);
    }

    [Fact]
    public void A_pair_listed_against_the_screen_changes_sides_with_the_later_one_going_underneath()
    {
        Vector2[] touching = Spread(new Vector2(-0.999f * Width, 0f));
        (touching[1] - touching[0]).X.ShouldBeLessThan(-Width * 0.99f);

        for (float widths = 0.98f; widths > 0.52f; widths -= 0.02f)
        {
            Vector2[] places = Spread(new Vector2(-widths * Width, 0f));
            (places[1].Y - places[0].Y).ShouldBeGreaterThan(0f, $"{widths} widths");
        }

        Vector2[] row = Spread(new Vector2(-0.5f * Width, 0f));
        (row[1] - row[0]).X.ShouldBe(Width, tolerance: 1e-3f);
        (row[1] - row[0]).Y.ShouldBe(0f, tolerance: 1e-3f);
    }

    [Fact]
    public void Off_the_seam_a_pair_that_overlaps_is_drawn_touching_whichever_way_it_lies()
    {
        for (int degrees = 0; degrees < 360; degrees += 5)
        {
            // The two ways round are blended 15 degrees to each side of the seam.
            if (MathF.Abs(degrees - SeamDegrees) < 16f)
                continue;

            for (int step = 1; step < 20; step++)
            {
                float widths = step * 0.05f;
                Drawn(Heading(degrees) * (widths * Width))
                    .ShouldBe(Width, tolerance: 1e-3f, $"{degrees} degrees, {widths} widths");
            }
        }
    }

    [Fact]
    public void A_pair_stays_round_the_middle_of_its_anchors()
    {
        for (int degrees = 0; degrees < 360; degrees += 15)
        {
            Vector2 apart = Heading(degrees) * (Width * 0.7f);
            Vector2[] places = Spread(apart);

            Vector2.Distance((places[0] + places[1]) * 0.5f, apart * 0.5f)
                .ShouldBeLessThan(1e-3f, $"{degrees} degrees");
        }
    }

    // A rule that never jumps has to draw the pair on one point somewhere.
    // Here that is by the seam, and nowhere on the level line or straight
    // above or below.
    [Fact]
    public void Only_a_small_spot_by_the_seam_draws_a_pair_nearer_than_half_a_width()
    {
        const int Steps = 120;
        int inZone = 0;
        int near = 0;
        int nearerThanAnchors = 0;

        for (int x = -Steps; x <= Steps; x++)
        {
            for (int y = -Steps; y <= Steps; y++)
            {
                Vector2 apart = new Vector2(x, y) * (Width / Steps);
                float anchors = apart.Length();
                if (anchors >= Width || anchors <= 0f)
                    continue;

                inZone++;
                float drawn = Drawn(apart);
                if (drawn < anchors - 1e-3f)
                    nearerThanAnchors++;

                if (drawn >= Half)
                    continue;

                near++;
                float degrees = MathF.Atan2(-apart.Y, -apart.X) * (180f / MathF.PI) + 180f;
                MathF.Abs(degrees - SeamDegrees).ShouldBeLessThan(10f, $"at {apart / Width}");
                (anchors / Width).ShouldBeInRange(0.6f, 0.78f, $"at {apart / Width}");
            }
        }

        near.ShouldBeGreaterThan(0);
        (near / (float)inZone).ShouldBeLessThan(0.005f);
        (nearerThanAnchors / (float)inZone).ShouldBeLessThan(0.012f);
    }
}
