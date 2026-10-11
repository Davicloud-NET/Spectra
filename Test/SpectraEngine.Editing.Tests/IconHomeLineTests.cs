using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Icons;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// A moved icon shows where its node is: a line from its rim to the node and a
/// small mark on it, neither drawn where another icon stands over it.
/// </summary>
public sealed class IconHomeLineTests
{
    // A mark is four hairlines.
    private const int LinesAMark = 4;

    private static DebugDraw Draw(IconPileRig rig)
    {
        var output = new DebugDraw();
        new EntityMarkerOverlay().Draw(output, rig.Scene, rig.Camera, rig.ViewportSize);
        return output;
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

    // Four are a row. From seven on there are rows above each other, and
    // lines home that run at a slant past several icons.
    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(13)]
    public void A_line_home_is_not_drawn_where_another_icon_stands_over_it(int count)
    {
        var rig = new IconPileRig();
        for (int i = 0; i < count; i++)
            rig.AddEntity(i % 2 == 0 ? IconPileRig.Sound : IconPileRig.Relay, Vector3.Zero);

        SceneIcon[] markers = rig.Place().Markers.ToArray();

        DebugDraw output = Draw(rig);
        int pieces = 0;

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

                pieces++;
            }
        }

        pieces.ShouldBeGreaterThanOrEqualTo(count);
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
    public void Seen_from_above_the_lines_home_of_things_over_each_other_stop_at_the_icon_in_the_middle()
    {
        // The three are on one point of the screen, each at its own height.
        var rig = new IconPileRig();
        rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        rig.AddEntity(IconPileRig.Relay, new Vector3(0f, 2f, 0f));
        rig.AddEntity(IconPileRig.Sound, new Vector3(0f, 4f, 0f));
        rig.Harness.Orbit(Vector3.Zero, 10f, 0.4f, -0.5f);
        rig.Harness.EditorCamera.SetView(EditorViewPreset.Top);
        SceneIcon[] markers = rig.Place().Markers.ToArray();
        markers[0].IsMoved.ShouldBeTrue();
        markers[1].IsMoved.ShouldBeFalse();
        markers[2].IsMoved.ShouldBeTrue();

        DebugDraw output = Draw(rig);

        // One line each, and no mark: the middle icon is drawn over the point.
        List<Vector3> sounds = DebugLines.Colored(output, IconPileRig.SoundTint * LightOverlay.HomeLineDim);
        sounds.Count.ShouldBe(2 * 2);
        DebugLines.CountOf(output, IconPileRig.RelayTint * LightOverlay.HomeLineDim).ShouldBe(0);

        // Each ends where the middle icon's rim is on screen.
        for (int i = 1; i < sounds.Count; i += 2)
        {
            Vector3 from = sounds[i] - markers[1].At;
            float across = Vector3.Dot(from, rig.Camera.Right);
            float up = Vector3.Dot(from, rig.Camera.Up);
            MathF.Sqrt((across * across) + (up * up)).ShouldBe(markers[1].Radius, tolerance: 1e-3f);
        }
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
