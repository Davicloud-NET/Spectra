using System;
using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Icons;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// When more point entities are near enough for a glyph than may have one, a
/// pile has closed by the time one of its icons loses the glyph.
/// </summary>
public sealed class IconGlyphCutTests
{
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

        float open = Open(rig, icons);
        open.ShouldBe(LightOverlay.IconPixels * ViewportGlyphs.Reach * 2f, tolerance: 0.01f);

        float largest = 0f;
        for (float z = -2f; z <= 2f; z += 0.002f)
        {
            passer.LocalPosition = new Vector3(4f, 0f, z);
            float now = Open(rig, icons);
            largest = MathF.Max(largest, MathF.Abs(now - open));
            open = now;
        }

        // The passer has the glyph now, and one of the pair is a diamond.
        icons.Markers[2].IsGlyph.ShouldBeTrue();
        icons.Markers[1].IsGlyph.ShouldBeFalse();
        open.ShouldBe(0f);

        // In pixels a step. The pair's width is thirty.
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

        float cut = Open(rig, new SceneIcons { MaxMarkerGlyphs = 2 });
        float spare = Open(rig, new SceneIcons());

        cut.ShouldBe(spare);
    }

    // How far apart the pair is drawn, in pixels.
    private static float Open(IconPileRig rig, SceneIcons icons)
    {
        icons.Place(rig.Scene, rig.Camera, rig.ViewportSize);
        ReadOnlySpan<SceneIcon> markers = icons.Markers;
        return Vector3.Distance(markers[0].At, markers[1].At) / markers[0].Pixel;
    }
}
