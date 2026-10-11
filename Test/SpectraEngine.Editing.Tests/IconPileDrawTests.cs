using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Icons;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Icons that would be drawn on top of each other are drawn side by side,
/// each with a line back to where its node is.
/// </summary>
public sealed class IconPileDrawTests
{
    // A stroke is two lines in colour. A line home is one, and its mark four.
    private const int LinesAStroke = 2;
    private const int LinesHome = 5;

    private static DebugDraw Draw(IconPileRig rig, EntityMarkerOverlay? overlay = null)
    {
        var output = new DebugDraw();
        (overlay ?? new EntityMarkerOverlay()).Draw(output, rig.Scene, rig.Camera, rig.ViewportSize);
        return output;
    }

    private static int Segments(string key)
    {
        ViewportGlyphs.TryGet(key, out ViewportGlyph? glyph).ShouldBeTrue(key);
        return glyph.ShouldNotBeNull().SegmentCount;
    }

    private static float HalfWidth(in SceneIcon icon) => icon.Radius * ViewportGlyphs.Reach;

    [Fact]
    public void Four_entities_at_one_point_stand_in_a_row_across_the_screen_in_the_order_listed()
    {
        var rig = new IconPileRig();
        rig.AddLift(Vector3.Zero);

        ReadOnlySpan<SceneIcon> markers = rig.Place().Markers;

        markers.Length.ShouldBe(4);
        float width = HalfWidth(in markers[0]) * 2f;

        for (int i = 0; i < 4; i++)
        {
            markers[i].Node.ShouldBe(i);
            markers[i].IsMoved.ShouldBeTrue();
            markers[i].Anchor.ShouldBe(Vector3.Zero);

            Vector3 moved = markers[i].At - markers[i].Anchor;
            Vector3.Dot(moved, rig.Camera.Right).ShouldBe((i - 1.5f) * width, tolerance: 1e-5f);
            Vector3.Dot(moved, rig.Camera.Up).ShouldBe(0f, tolerance: 1e-5f);
            Vector3.Dot(moved, rig.Camera.Forward).ShouldBe(0f, tolerance: 1e-5f);
        }
    }

    [Fact]
    public void Four_entities_at_one_point_draw_four_glyphs_that_do_not_touch()
    {
        var rig = new IconPileRig();
        rig.AddLift(Vector3.Zero);
        SceneIcon[] markers = rig.Place().Markers.ToArray();

        DebugDraw output = Draw(rig);

        // Three sounds, then the relay at the right-hand end.
        int sound = Segments("IconEntitySound") * LinesAStroke;
        int relay = Segments("IconEntityRelay") * LinesAStroke;
        DebugLines.CountOf(output, IconPileRig.SoundTint).ShouldBe(3 * sound);
        DebugLines.CountOf(output, IconPileRig.RelayTint).ShouldBe(relay);

        int[] sounds = CountByNearest(rig, DebugLines.Colored(output, IconPileRig.SoundTint), markers);
        sounds.ShouldBe([sound * 2, sound * 2, sound * 2, 0]);

        int[] relays = CountByNearest(rig, DebugLines.Colored(output, IconPileRig.RelayTint), markers);
        relays.ShouldBe([0, 0, 0, relay * 2]);
    }

    [Fact]
    public void Each_icon_of_a_pile_has_a_line_from_its_rim_to_the_point_and_a_mark_on_the_point()
    {
        var rig = new IconPileRig();
        rig.AddLift(Vector3.Zero);
        SceneIcon[] markers = rig.Place().Markers.ToArray();

        DebugDraw output = Draw(rig);

        List<Vector3> sounds = DebugLines.Colored(output, IconPileRig.SoundTint * LightOverlay.HomeLineDim);
        List<Vector3> relays = DebugLines.Colored(output, IconPileRig.RelayTint * LightOverlay.HomeLineDim);
        sounds.Count.ShouldBe(3 * LinesHome * 2);
        relays.Count.ShouldBe(LinesHome * 2);

        for (int i = 0; i < 4; i++)
            HasLineHome(i < 3 ? sounds : relays, in markers[i]).ShouldBeTrue($"icon {i}");

        // The mark is small: two pixels either way.
        foreach (Vector3 vertex in sounds)
        {
            bool onRim = false;
            foreach (SceneIcon marker in markers)
                onRim |= MathF.Abs(Vector3.Distance(vertex, marker.At) - marker.Radius) < 1e-4f;

            if (!onRim)
                vertex.Length().ShouldBeLessThanOrEqualTo((2f * markers[0].Pixel) + 1e-5f);
        }
    }

    [Fact]
    public void The_lines_home_are_drawn_before_any_glyph()
    {
        var rig = new IconPileRig();
        rig.AddLift(Vector3.Zero);

        DebugDraw output = Draw(rig);

        // Twenty lines home, and no vertex of them after the first of anything else.
        ReadOnlySpan<float> data = output.Vertices;
        for (int vertex = 0; vertex < output.VertexCount; vertex++)
        {
            var color = new Vector3(data[(vertex * 6) + 3], data[(vertex * 6) + 4], data[(vertex * 6) + 5]);
            bool isHome = color == IconPileRig.SoundTint * LightOverlay.HomeLineDim ||
                          color == IconPileRig.RelayTint * LightOverlay.HomeLineDim;

            isHome.ShouldBe(vertex < 4 * LinesHome * 2, $"vertex {vertex}");
        }
    }

    [Fact]
    public void An_entity_alone_is_drawn_on_its_node_with_no_line_home()
    {
        var rig = new IconPileRig();
        rig.AddEntity(IconPileRig.Sound, new Vector3(1f, 2f, 0f));
        rig.AddEntity(IconPileRig.Relay, new Vector3(-3f, 0f, 0f));

        DebugDraw output = Draw(rig);

        foreach (SceneIcon marker in rig.Place().Markers)
        {
            marker.IsMoved.ShouldBeFalse();
            marker.At.ShouldBe(marker.Anchor);
        }

        DebugLines.CountOf(output, IconPileRig.SoundTint * LightOverlay.HomeLineDim).ShouldBe(0);
        DebugLines.CountOf(output, IconPileRig.RelayTint * LightOverlay.HomeLineDim).ShouldBe(0);

        int strokes = Segments("IconEntitySound") + Segments("IconEntityRelay");
        output.VertexCount.ShouldBe(strokes * LinesAStroke * 2 * 2);
    }

    [Fact]
    public void Far_diamonds_at_one_point_are_not_fanned()
    {
        var rig = new IconPileRig();
        Vector3 far = new(0f, 0f, -400f);
        rig.AddLift(far);

        DebugDraw output = Draw(rig);

        foreach (SceneIcon marker in rig.Place().Markers)
        {
            marker.IsGlyph.ShouldBeFalse();
            marker.IsMoved.ShouldBeFalse();
        }

        // Four diamonds on the one point: a backing and two in colour, four
        // hairlines each. No lines home.
        output.VertexCount.ShouldBe(4 * 3 * 4 * 2);
        DebugLines.CountOf(output, IconPileRig.SoundTint).ShouldBe(3 * 2 * 4);
        DebugLines.CountOf(output, IconPileRig.RelayTint).ShouldBe(2 * 4);
    }

    [Fact]
    public void A_pile_closes_before_its_glyphs_turn_into_diamonds()
    {
        // At eight pixels and more a pile is a full row. From there down to
        // the size of a diamond it closes, so it does not snap shut.
        var rig = new IconPileRig();
        SceneNode[] lift = rig.AddLift(Vector3.Zero);

        float open = Spread(rig, lift, depth: 24f, out float openPixels);
        float closing = Spread(rig, lift, depth: 29f, out float closingPixels);
        float closed = Spread(rig, lift, depth: 31.8f, out float closedPixels);

        openPixels.ShouldBeGreaterThan(8f);
        closingPixels.ShouldBeInRange(7f, 7.5f);
        closedPixels.ShouldBeInRange(SceneIcons.DetailPixels, 6.6f);

        // Three icon widths between the first and the last of four.
        open.ShouldBe(3f * 2f * ViewportGlyphs.Reach * openPixels, tolerance: 0.01f);
        closing.ShouldBeInRange(1f, open * 0.5f);
        closed.ShouldBeLessThan(0.5f);
    }

    [Fact]
    public void The_row_keeps_its_order_and_lies_across_the_screen_as_the_camera_goes_round()
    {
        var rig = new IconPileRig();
        rig.AddLift(new Vector3(1f, 0.5f, -2f));

        for (int step = 0; step < 24; step++)
        {
            rig.Harness.Orbit(new Vector3(1f, 0.5f, -2f), 9f, step * 0.31f, -1.2f + (step * 0.1f));
            SceneIcon[] markers = rig.Place().Markers.ToArray();
            float width = HalfWidth(in markers[0]) * 2f;

            for (int i = 1; i < markers.Length; i++)
            {
                Vector3 next = markers[i].At - markers[i - 1].At;
                Vector3.Dot(next, rig.Camera.Right).ShouldBe(width, tolerance: 1e-4f, $"step {step}, icon {i}");
                Vector3.Dot(next, rig.Camera.Up).ShouldBe(0f, tolerance: 1e-4f, $"step {step}, icon {i}");
                Vector3.Dot(next, rig.Camera.Forward).ShouldBe(0f, tolerance: 1e-4f, $"step {step}, icon {i}");
            }
        }
    }

    [Fact]
    public void A_moved_icon_keeps_its_arrow_along_the_way_its_node_faces()
    {
        var rig = new IconPileRig();
        SceneNode start = rig.AddEntity(IconPileRig.Start, Vector3.Zero);
        rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        start.LocalRotation = Light.RotationForDirection(Vector3.UnitY);

        DebugDraw output = Draw(rig);
        SceneIcon marker = rig.Place().Markers[0];

        marker.IsMoved.ShouldBeTrue();

        Vector3 tip = marker.At;
        foreach (Vector3 vertex in DebugLines.Colored(output, IconPileRig.StartTint))
        {
            if (Vector3.DistanceSquared(vertex, marker.At) > Vector3.DistanceSquared(tip, marker.At))
                tip = vertex;
        }

        Vector3.Dot(Vector3.Normalize(tip - marker.At), Vector3.UnitY).ShouldBeGreaterThan(0.99f);
        Vector3.Distance(tip, marker.At).ShouldBeGreaterThan(marker.Radius * 2f);
    }

    [Fact]
    public void A_second_draw_of_piles_allocates_nothing()
    {
        var rig = new IconPileRig();
        for (int pile = 0; pile < 6; pile++)
        {
            var at = new Vector3((pile % 3) - 1f, (pile / 3) - 0.5f, 0f);
            rig.AddLift(at * 2f);
            rig.AddLamp(at * 2f);
        }

        var icons = new SceneIcons();
        var markers = new EntityMarkerOverlay(icons);
        var lamps = new LightOverlay(icons);
        var output = new DebugDraw();

        for (int i = 0; i < 3; i++)
            Frame();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++)
            Frame();
        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).ShouldBe(0);
        markers.IconsLastDraw.ShouldBe(24);
        lamps.DrawnLastDraw.ShouldBe(6);

        void Frame()
        {
            output.Clear();
            icons.Place(rig.Scene, rig.Camera, rig.ViewportSize);
            lamps.DrawPlaced(output, rig.Scene, rig.Camera);
            markers.DrawPlaced(output, rig.Scene, rig.Camera);
        }
    }

    // How far apart the first and the last icon of the lift are drawn, in
    // pixels, with the lift this far in front of the camera.
    private static float Spread(IconPileRig rig, SceneNode[] lift, float depth, out float pixels)
    {
        foreach (SceneNode node in lift)
            node.LocalPosition = rig.Camera.Position + (rig.Camera.Forward * depth);

        ReadOnlySpan<SceneIcon> markers = rig.Place().Markers;
        pixels = markers[0].Radius / markers[0].Pixel;
        markers[0].IsGlyph.ShouldBeTrue();

        return Vector3.Distance(markers[0].At, markers[3].At) / markers[0].Pixel;
    }

    // How many of these vertices lie in each icon's box and nowhere else.
    private static int[] CountByNearest(IconPileRig rig, List<Vector3> vertices, SceneIcon[] icons)
    {
        var counts = new int[icons.Length];
        foreach (Vector3 vertex in vertices)
        {
            int inside = -1;
            for (int i = 0; i < icons.Length; i++)
            {
                Vector3 from = vertex - icons[i].At;
                float reach = HalfWidth(in icons[i]) - icons[i].Pixel;
                if (MathF.Abs(Vector3.Dot(from, rig.Camera.Right)) > reach ||
                    MathF.Abs(Vector3.Dot(from, rig.Camera.Up)) > reach)
                {
                    continue;
                }

                inside.ShouldBe(-1, "a vertex in two icons' boxes");
                inside = i;
            }

            inside.ShouldBeGreaterThanOrEqualTo(0, "a vertex in no icon's box");
            counts[inside]++;
        }

        return counts;
    }

    // Whether one of these lines runs from the icon's rim to its node.
    private static bool HasLineHome(List<Vector3> lines, in SceneIcon icon)
    {
        for (int i = 0; i + 1 < lines.Count; i += 2)
        {
            if (Vector3.Distance(lines[i + 1], icon.Anchor) < 1e-5f &&
                MathF.Abs(Vector3.Distance(lines[i], icon.At) - icon.Radius) < 1e-4f)
            {
                return true;
            }
        }

        return false;
    }
}
