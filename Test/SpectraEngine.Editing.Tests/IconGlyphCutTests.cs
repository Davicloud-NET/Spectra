using System;
using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Icons;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// When more point entities are near enough for a glyph than may have one, a
/// pile has closed by the time one of its icons loses the glyph.
/// </summary>
public sealed class IconGlyphCutTests
{
    // One icon's width in pixels, near the camera.
    private const float Width = LightOverlay.IconPixels * ViewportGlyphs.Reach * 2f;

    [Fact]
    public void A_pile_closes_little_by_little_as_a_nearer_entity_takes_one_of_its_glyphs()
    {
        // Two glyphs for three entities: a pair twelve units off, and one
        // that comes from behind the pair to in front of it.
        var rig = new IconPileRig();
        rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        SceneNode passer = rig.AddEntity(IconPileRig.Relay, new Vector3(4f, 0f, -2f));
        var icons = new SceneIcons { MaxMarkerGlyphs = 2 };

        Span(rig, icons, pile: 2).ShouldBe(Width, tolerance: 0.01f);
        float largest = Pass(rig, icons, passer, pile: 2, from: -2f, to: 2f);

        // The passer has the glyph now, and one of the pair is a diamond.
        icons.Markers[2].IsGlyph.ShouldBeTrue();
        icons.Markers[1].IsGlyph.ShouldBeFalse();
        Span(rig, icons, pile: 2).ShouldBe(0f);

        // In pixels a step. The pair's width is thirty.
        largest.ShouldBeLessThan(0.5f);
    }

    [Fact]
    public void A_pile_opens_little_by_little_for_an_icon_that_gets_a_glyph()
    {
        // Three glyphs for four entities: three on one point, and one that
        // goes from in front of them to behind them.
        var rig = new IconPileRig();
        rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        SceneNode passer = rig.AddEntity(IconPileRig.Relay, new Vector3(4f, 0f, 2f));
        var icons = new SceneIcons { MaxMarkerGlyphs = 3 };

        // Two of the three have a glyph, and stand as a pair.
        Span(rig, icons, pile: 2).ShouldBe(Width, tolerance: 0.01f);
        icons.Markers[2].IsGlyph.ShouldBeFalse();

        float largest = Pass(rig, icons, passer, pile: 3, from: 2f, to: -2.5f);

        icons.Markers[2].IsGlyph.ShouldBeTrue();
        Span(rig, icons, pile: 3).ShouldBe(Width * 2f, tolerance: 0.01f);
        largest.ShouldBeLessThan(0.5f);
    }

    [Fact]
    public void A_pile_well_inside_the_cut_stands_open_in_full()
    {
        var rig = new IconPileRig();
        rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        rig.AddEntity(IconPileRig.Relay, new Vector3(4f, 0f, -2f));
        rig.AddEntity(IconPileRig.Relay, new Vector3(-4f, 0f, -3f));

        float cut = Span(rig, new SceneIcons { MaxMarkerGlyphs = 2 }, pile: 2);
        float spare = Span(rig, new SceneIcons(), pile: 2);

        cut.ShouldBe(spare);
    }

    [Fact]
    public void A_pile_with_more_icons_than_glyphs_opens_for_the_first_listed_and_leaves_the_rest_on_the_point()
    {
        var rig = new IconPileRig();
        rig.AddLift(Vector3.Zero);
        var icons = new SceneIcons { MaxMarkerGlyphs = 2 };

        Span(rig, icons, pile: 2).ShouldBe(Width, tolerance: 0.01f);

        ReadOnlySpan<SceneIcon> markers = icons.Markers;
        markers[1].IsGlyph.ShouldBeTrue();
        markers[2].IsGlyph.ShouldBeFalse();
        markers[2].IsMoved.ShouldBeFalse();
        markers[3].IsMoved.ShouldBeFalse();
    }

    [Fact]
    public void In_a_flat_view_the_first_listed_keep_their_glyphs_and_their_piles_stand_open()
    {
        // Seen from above every icon is as far off as the next.
        var rig = new IconPileRig();
        for (int pile = 0; pile < 3; pile++)
        {
            rig.AddEntity(IconPileRig.Sound, new Vector3(pile * 3f, 0f, 0f));
            rig.AddEntity(IconPileRig.Sound, new Vector3(pile * 3f, 0f, 0f));
        }

        rig.Harness.Orbit(Vector3.Zero, 10f, 0.4f, -0.5f);
        rig.Harness.EditorCamera.SetView(EditorViewPreset.Top);
        var icons = new SceneIcons { MaxMarkerGlyphs = 4 };
        icons.Place(rig.Scene, rig.Camera, rig.ViewportSize);
        ReadOnlySpan<SceneIcon> markers = icons.Markers;

        for (int i = 0; i < 4; i++)
        {
            markers[i].IsGlyph.ShouldBeTrue($"icon {i}");
            markers[i].IsMoved.ShouldBeTrue($"icon {i}");
        }

        float radius = markers[0].Radius;
        Vector3.Distance(markers[0].At, markers[1].At).ShouldBe(radius * ViewportGlyphs.Reach * 2f, tolerance: 1e-4f);
        markers[4].IsGlyph.ShouldBeFalse();
        markers[5].IsGlyph.ShouldBeFalse();
    }

    // Moves the passer along the view in small steps. Returns the farthest
    // any of the pile's icons moved in one step, in pixels.
    private static float Pass(IconPileRig rig, SceneIcons icons, SceneNode passer, int pile, float from, float to)
    {
        const float Step = 0.002f;
        int steps = (int)(MathF.Abs(to - from) / Step);
        Span<Vector3> before = stackalloc Vector3[pile];
        float largest = 0f;

        for (int step = 0; step <= steps; step++)
        {
            passer.LocalPosition = new Vector3(4f, 0f, from + ((to - from) * step / steps));
            icons.Place(rig.Scene, rig.Camera, rig.ViewportSize);
            ReadOnlySpan<SceneIcon> markers = icons.Markers;

            for (int i = 0; i < pile; i++)
            {
                if (step > 0)
                    largest = MathF.Max(largest, Vector3.Distance(before[i], markers[i].At) / markers[i].Pixel);

                before[i] = markers[i].At;
            }
        }

        return largest;
    }

    // How far apart the first and the last of the pile's icons are drawn, in pixels.
    private static float Span(IconPileRig rig, SceneIcons icons, int pile)
    {
        icons.Place(rig.Scene, rig.Camera, rig.ViewportSize);
        ReadOnlySpan<SceneIcon> markers = icons.Markers;
        return Vector3.Distance(markers[0].At, markers[pile - 1].At) / markers[0].Pixel;
    }
}
