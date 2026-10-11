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
    // A stroke is two lines in colour. A mark is four hairlines.
    private const int LinesAStroke = 2;
    private const int LinesAMark = 4;

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
    public void Each_icon_of_a_pile_has_a_line_home_from_its_rim_and_a_mark_on_the_point()
    {
        var rig = new IconPileRig();
        rig.AddLift(Vector3.Zero);
        SceneIcon[] markers = rig.Place().Markers.ToArray();

        DebugDraw output = Draw(rig);

        List<Vector3> sounds = DebugLines.Colored(output, IconPileRig.SoundTint * LightOverlay.HomeLineDim);
        List<Vector3> relays = DebugLines.Colored(output, IconPileRig.RelayTint * LightOverlay.HomeLineDim);

        // The two in the middle reach the point in one piece. The two at the
        // ends have an icon in the way, so theirs is in two.
        sounds.Count.ShouldBe((2 + 1 + 1 + (3 * LinesAMark)) * 2);
        relays.Count.ShouldBe((2 + LinesAMark) * 2);

        for (int i = 0; i < 4; i++)
        {
            List<Vector3> lines = i < 3 ? sounds : relays;
            StartsOnRim(lines, in markers[i]).ShouldBeTrue($"icon {i} leaves its rim");
            EndsOnNode(lines, in markers[i]).ShouldBeTrue($"icon {i} reaches the point");
        }
    }

    [Fact]
    public void A_line_home_is_not_drawn_where_another_icon_stands_over_it()
    {
        var rig = new IconPileRig();
        rig.AddLift(Vector3.Zero);
        SceneIcon[] markers = rig.Place().Markers.ToArray();

        DebugDraw output = Draw(rig);

        // Whatever is not a mark's edge is a piece of a line home. No point
        // of one is inside an icon's rim.
        float mark = 2f * markers[0].Pixel;
        foreach (Vector3 tint in new[] { IconPileRig.SoundTint, IconPileRig.RelayTint })
        {
            List<Vector3> lines = DebugLines.Colored(output, tint * LightOverlay.HomeLineDim);
            for (int i = 0; i + 1 < lines.Count; i += 2)
            {
                if (MathF.Abs(lines[i].Length() - mark) < 1e-5f && MathF.Abs(lines[i + 1].Length() - mark) < 1e-5f)
                    continue;

                Vector3 middle = (lines[i] + lines[i + 1]) * 0.5f;
                foreach (SceneIcon marker in markers)
                    Vector3.Distance(middle, marker.At).ShouldBeGreaterThan(marker.Radius);
            }
        }
    }

    [Fact]
    public void In_a_row_of_three_the_lines_home_stop_at_the_icon_in_the_middle()
    {
        // That one stands on the point, so the point needs no mark.
        var rig = new IconPileRig();
        rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        rig.AddEntity(IconPileRig.Relay, Vector3.Zero);
        rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        SceneIcon[] markers = rig.Place().Markers.ToArray();

        DebugDraw output = Draw(rig);

        List<Vector3> sounds = DebugLines.Colored(output, IconPileRig.SoundTint * LightOverlay.HomeLineDim);
        sounds.Count.ShouldBe(2 * 2);
        DebugLines.CountOf(output, IconPileRig.RelayTint * LightOverlay.HomeLineDim).ShouldBe(0);

        foreach (SceneIcon end in new[] { markers[0], markers[2] })
        {
            StartsOnRim(sounds, in end).ShouldBeTrue();
            EndsOnNode(sounds, in end).ShouldBeFalse();
        }

        // Each ends on the middle icon's rim.
        for (int i = 1; i < sounds.Count; i += 2)
            Vector3.Distance(sounds[i], markers[1].At).ShouldBe(markers[1].Radius, tolerance: 1e-4f);
    }

    [Fact]
    public void An_icon_that_still_covers_its_node_has_no_line_home_and_no_mark()
    {
        // Nine tenths of a width apart, each is moved a little.
        var rig = new IconPileRig();
        rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        SceneIcon alone = rig.Place().Markers[0];
        float width = alone.Radius * ViewportGlyphs.Reach * 2f;
        rig.AddEntity(IconPileRig.Relay, new Vector3(width * 0.9f, 0f, 0f));

        DebugDraw output = Draw(rig);

        foreach (SceneIcon marker in rig.Place().Markers)
        {
            marker.IsMoved.ShouldBeTrue();
            Vector3.Distance(marker.At, marker.Anchor).ShouldBeLessThan(marker.Radius * 0.5f);
        }

        DebugLines.CountOf(output, IconPileRig.SoundTint * LightOverlay.HomeLineDim).ShouldBe(0);
        DebugLines.CountOf(output, IconPileRig.RelayTint * LightOverlay.HomeLineDim).ShouldBe(0);
    }

    [Fact]
    public void The_lines_home_are_drawn_before_any_glyph()
    {
        var rig = new IconPileRig();
        rig.AddLift(Vector3.Zero);

        DebugDraw output = Draw(rig);

        int home = DebugLines.Colored(output, IconPileRig.SoundTint * LightOverlay.HomeLineDim).Count +
                   DebugLines.Colored(output, IconPileRig.RelayTint * LightOverlay.HomeLineDim).Count;
        home.ShouldBeGreaterThan(0);

        // No vertex of them after the first of anything else.
        ReadOnlySpan<float> data = output.Vertices;
        for (int vertex = 0; vertex < output.VertexCount; vertex++)
        {
            var color = new Vector3(data[(vertex * 6) + 3], data[(vertex * 6) + 4], data[(vertex * 6) + 5]);
            bool isHome = color == IconPileRig.SoundTint * LightOverlay.HomeLineDim ||
                          color == IconPileRig.RelayTint * LightOverlay.HomeLineDim;

            isHome.ShouldBe(vertex < home, $"vertex {vertex}");
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
    public void The_icon_under_the_cursor_is_marked_on_the_icon_as_well_as_on_its_node()
    {
        var rig = new IconPileRig();
        SceneNode[] lift = rig.AddLift(Vector3.Zero);
        SceneIcons icons = rig.Place();
        var outline = new SelectionOutline { Icons = icons };
        var output = new DebugDraw();

        outline.Draw(output, rig.Scene, rig.Camera, rig.ViewportSize, hovered: lift[3]);

        // A cross is three lines. One on the node, one on the icon at the end of the row.
        CrossCentres(output, SelectionOutline.HoverColor).ShouldBe([Vector3.Zero, icons.Markers[3].At]);
    }

    [Fact]
    public void A_selected_icon_of_a_pile_is_marked_on_the_icon_as_well_as_on_its_node()
    {
        var rig = new IconPileRig();
        SceneNode[] lift = rig.AddLift(Vector3.Zero);
        rig.Scene.Selection.Add(lift[1]);
        SceneIcons icons = rig.Place();
        var outline = new SelectionOutline { Icons = icons };
        var output = new DebugDraw();

        outline.Draw(output, rig.Scene, rig.Camera, rig.ViewportSize, hovered: null);

        CrossCentres(output, SelectionOutline.SelectedColor).ShouldBe([Vector3.Zero, icons.Markers[1].At]);
    }

    [Fact]
    public void An_icon_that_was_not_moved_is_marked_on_its_node_alone()
    {
        var rig = new IconPileRig();
        SceneNode sound = rig.AddEntity(IconPileRig.Sound, new Vector3(1f, 2f, 0f));
        var outline = new SelectionOutline { Icons = rig.Place() };
        var output = new DebugDraw();

        outline.Draw(output, rig.Scene, rig.Camera, rig.ViewportSize, hovered: sound);

        CrossCentres(output, SelectionOutline.HoverColor).ShouldBe([new Vector3(1f, 2f, 0f)]);
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

    // The middle of each cross drawn in this colour, three lines to a cross.
    private static List<Vector3> CrossCentres(DebugDraw output, Vector3 color)
    {
        List<Vector3> vertices = DebugLines.Colored(output, color);
        (vertices.Count % 6).ShouldBe(0);

        var centres = new List<Vector3>();
        for (int i = 0; i < vertices.Count; i += 6)
        {
            Vector3 centre = (vertices[i] + vertices[i + 1]) * 0.5f;
            Vector3.Distance(centre, (vertices[i + 2] + vertices[i + 3]) * 0.5f).ShouldBeLessThan(1e-5f);
            Vector3.Distance(centre, (vertices[i + 4] + vertices[i + 5]) * 0.5f).ShouldBeLessThan(1e-5f);
            centres.Add(centre);
        }

        return centres;
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

    // Whether one of these lines starts on the icon's rim, on the side its node is.
    private static bool StartsOnRim(List<Vector3> lines, in SceneIcon icon)
    {
        Vector3 rim = icon.At + (Vector3.Normalize(icon.Anchor - icon.At) * icon.Radius);
        for (int i = 0; i + 1 < lines.Count; i += 2)
        {
            if (Vector3.Distance(lines[i], rim) < 1e-4f)
                return true;
        }

        return false;
    }

    // Whether one of these lines comes from the icon's side and ends on its node.
    private static bool EndsOnNode(List<Vector3> lines, in SceneIcon icon)
    {
        Vector3 toward = Vector3.Normalize(icon.Anchor - icon.At);
        for (int i = 0; i + 1 < lines.Count; i += 2)
        {
            Vector3 along = lines[i + 1] - lines[i];
            if (Vector3.Distance(lines[i + 1], icon.Anchor) < 1e-5f &&
                Vector3.Dot(Vector3.Normalize(along), toward) > 0.999f)
            {
                return true;
            }
        }

        return false;
    }
}
