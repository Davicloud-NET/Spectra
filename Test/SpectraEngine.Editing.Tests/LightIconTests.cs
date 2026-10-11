using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Icons;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// A light's icon is the file of its kind, in the light's own colour, with an
/// arrow when the light points somewhere.
/// </summary>
public sealed class LightIconTests
{
    // A stroke is two lines in colour, and an arrow is three strokes.
    private const int LinesAStroke = 2;
    private const int ArrowStrokes = 3;

    // Full strength in one channel and none too dark, so the icon and the
    // shape of a selected light are both the light's own colour.
    private static readonly Vector3 Warm = new(1f, 0.5f, 0.4f);

    public static TheoryData<LightKind> EveryKind => new(Enum.GetValues<LightKind>());

    private static ViewportHarness Fixture()
    {
        var harness = new ViewportHarness();
        harness.Scene.Camera.Position = new Vector3(0f, 0f, 12f);
        harness.Scene.Camera.LookAt(Vector3.Zero);
        return harness;
    }

    private static SceneNode AddLight(ViewportHarness harness, LightKind kind, Vector3 position)
    {
        var node = new SceneNode(kind.ToString())
        {
            LocalPosition = position,
            Light = new Light { Kind = kind, Color = Warm, Range = 3f },
        };

        harness.Scene.Root.AddChild(node);
        return node;
    }

    private static DebugDraw Draw(ViewportHarness harness, LightOverlay? overlay = null)
    {
        var output = new DebugDraw();
        (overlay ?? new LightOverlay()).Draw(output, harness.Scene, harness.Scene.Camera, harness.ViewportSize);
        return output;
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void A_light_is_drawn_as_the_icon_of_its_kind_in_its_own_colour(LightKind kind)
    {
        ViewportHarness harness = Fixture();
        AddLight(harness, kind, Vector3.Zero);

        DebugDraw output = Draw(harness);

        ViewportGlyphs.TryGet(WorldIcons.ForLight(kind), out ViewportGlyph? glyph).ShouldBeTrue();
        int strokes = glyph.ShouldNotBeNull().SegmentCount + (kind == LightKind.Point ? 0 : ArrowStrokes);

        // As many backing lines again, and nothing else.
        DebugLines.CountOf(output, Warm).ShouldBe(strokes * LinesAStroke);
        output.VertexCount.ShouldBe(strokes * LinesAStroke * 2 * 2);
    }

    [Fact]
    public void A_sun_and_a_spot_draw_different_strokes()
    {
        ViewportHarness suns = Fixture();
        AddLight(suns, LightKind.Directional, Vector3.Zero);
        ViewportHarness spots = Fixture();
        AddLight(spots, LightKind.Spot, Vector3.Zero);

        DebugLines.Coloured(Draw(suns), Warm).ShouldNotBe(DebugLines.Coloured(Draw(spots), Warm));
    }

    [Theory]
    [InlineData(LightKind.Directional)]
    [InlineData(LightKind.Spot)]
    [InlineData(LightKind.Rect)]
    [InlineData(LightKind.Disc)]
    public void A_light_that_points_has_an_arrow_along_its_direction(LightKind kind)
    {
        ViewportHarness harness = Fixture();
        SceneNode lamp = AddLight(harness, kind, Vector3.Zero);

        foreach (Vector3 facing in new[] { Vector3.UnitX, -Vector3.UnitY, new Vector3(1f, 1f, 0f) })
        {
            Vector3 forward = Vector3.Normalize(facing);
            lamp.LocalRotation = Light.RotationForDirection(forward);

            Vector3 tip = Vector3.Zero;
            foreach (Vector3 vertex in DebugLines.Coloured(Draw(harness), Warm))
            {
                if (vertex.LengthSquared() > tip.LengthSquared())
                    tip = vertex;
            }

            Vector3.Dot(Vector3.Normalize(tip), forward).ShouldBeGreaterThan(0.99f, $"facing {facing}");
        }
    }

    [Fact]
    public void A_smaller_icon_is_drawn_with_strokes_one_pixel_wide()
    {
        ViewportHarness harness = Fixture();
        SceneNode spot = AddLight(harness, LightKind.Spot, new Vector3(0f, 0f, -14f));

        DebugDraw output = Draw(harness);

        LightPicking.PixelRadius(harness.Scene.Camera, harness.ViewportSize, spot.WorldPosition)
            .ShouldBeInRange(7f, 10f);

        // One line of colour between two of backing.
        ViewportGlyphs.TryGet(WorldIcons.ForLight(LightKind.Spot), out ViewportGlyph? glyph).ShouldBeTrue();
        int strokes = glyph.ShouldNotBeNull().SegmentCount + ArrowStrokes;
        DebugLines.CountOf(output, Warm).ShouldBe(strokes);
        output.VertexCount.ShouldBe(strokes * 3 * 2);
    }

    [Fact]
    public void A_far_light_is_a_ring_and_nothing_else()
    {
        ViewportHarness harness = Fixture();
        AddLight(harness, LightKind.Spot, new Vector3(0f, 0f, -400f));

        DebugDraw output = Draw(harness);

        // One backing ring and two in colour, twelve hairlines each.
        output.VertexCount.ShouldBe(3 * 12 * 2);
        DebugLines.CountOf(output, Warm).ShouldBe(2 * 12);
    }

    [Fact]
    public void A_light_that_is_switched_off_is_grey()
    {
        ViewportHarness harness = Fixture();
        SceneNode lamp = AddLight(harness, LightKind.Point, Vector3.Zero);
        lamp.Light.ShouldNotBeNull().Enabled = false;

        DebugDraw output = Draw(harness);

        DebugLines.CountOf(output, Warm).ShouldBe(0);
        output.VertexCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_selected_lamp_still_draws_how_far_it_reaches()
    {
        ViewportHarness harness = Fixture();
        SceneNode lamp = AddLight(harness, LightKind.Point, Vector3.Zero);
        int icon = Draw(harness).VertexCount;

        harness.Scene.Selection.Add(lamp);
        List<Vector3> drawn = DebugLines.Coloured(Draw(harness), Warm);

        Draw(harness).VertexCount.ShouldBeGreaterThan(icon);
        drawn.ShouldContain(vertex => MathF.Abs(vertex.Length() - 3f) < 1e-3f);
    }

    [Fact]
    public void A_second_draw_allocates_nothing()
    {
        ViewportHarness harness = Fixture();
        LightKind[] kinds = Enum.GetValues<LightKind>();
        for (int i = 0; i < 40; i++)
            AddLight(harness, kinds[i % kinds.Length], new Vector3((i % 8) - 4f, (i / 8) - 2f, 0f));
        var overlay = new LightOverlay();
        var output = new DebugDraw();

        overlay.Draw(output, harness.Scene, harness.Scene.Camera, harness.ViewportSize);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++)
        {
            output.Clear();
            overlay.Draw(output, harness.Scene, harness.Scene.Camera, harness.ViewportSize);
        }

        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).ShouldBe(0);
        overlay.DrawnLastDraw.ShouldBe(40);
    }
}
