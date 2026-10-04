using SpectraEngine.Core.Graphics;
using SpectraEngine.Editing.Gizmos;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>What the translate gizmo writes into the debug line buffer.</summary>
public sealed class TranslateGizmoRenderingTests
{
    // DebugDraw's layout: position + colour.
    private const int FloatsPerVertex = 6;

    // Three nine-line arrows, three four-line quads, the centre circle.
    private const int ExpectedLines = (3 * 9) + (3 * 4) + GizmoHitTesting.RingSegments;

    [Fact]
    public void The_gizmo_draws_its_arrows_quads_and_centre_disc()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        harness.AddSelectedNode(Vector3.Zero);
        harness.Hover(Vector3.Zero);

        var output = new DebugDraw();
        harness.Gizmo.Draw(output);

        output.VertexCount.ShouldBe(ExpectedLines * 2);
    }

    [Fact]
    public void An_empty_selection_draws_nothing()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        harness.AddNode(Vector3.Zero);
        harness.Hover(Vector3.Zero);

        var output = new DebugDraw();
        harness.Gizmo.Draw(output);

        output.VertexCount.ShouldBe(0);
    }

    [Fact]
    public void A_gizmo_behind_the_camera_draws_nothing()
    {
        var harness = GizmoHarness.FrontView();
        var output = new DebugDraw();

        // Camera is at z = 10 looking down -z.
        GizmoGeometry geometry = harness.GeometryAt(new Vector3(0f, 0f, 40f));
        TranslateGizmoRenderer.Draw(output, in geometry, GizmoHandle.None);

        output.VertexCount.ShouldBe(0);
    }

    [Fact]
    public void Every_drawn_vertex_stays_inside_the_gizmo_s_constant_screen_footprint()
    {
        foreach (float distance in new[] { 3f, 50f, 5_000f })
        {
            var harness = GizmoHarness.FrontView(distance);
            harness.AddSelectedNode(Vector3.Zero);
            harness.Hover(Vector3.Zero);

            var output = new DebugDraw();
            harness.Gizmo.Draw(output);
            output.VertexCount.ShouldBeGreaterThan(0);

            Vector2 centre = harness.WorldToScreen(Vector3.Zero);
            float furthest = 0f;
            foreach (Vector3 position in Positions(output))
                furthest = MathF.Max(furthest, Vector2.Distance(harness.WorldToScreen(position), centre));

            // The x and y arrows lie in the view plane, so they reach full length.
            furthest.ShouldBeGreaterThan(harness.Gizmo.HandlePixelSize * 0.9f);
            furthest.ShouldBeLessThan(harness.Gizmo.HandlePixelSize * 1.2f);
        }
    }

    [Fact]
    public void The_hovered_handle_is_drawn_in_the_highlight_colour()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        harness.AddSelectedNode(Vector3.Zero);
        float length = harness.GeometryAt(Vector3.Zero).AxisLength;

        harness.Hover(new Vector3(-5f, -5f, -5f) * length);
        CountColour(Draw(harness), GizmoColors.For(GizmoHandle.AxisX, GizmoHandle.AxisX))
            .ShouldBe(0);

        harness.Hover(Vector3.UnitX * (length * 0.8f)).ShouldBe(GizmoUpdateResult.Hovering);
        harness.Gizmo.HoveredHandle.ShouldBe(GizmoHandle.AxisX);

        Vector3 highlight = GizmoColors.For(GizmoHandle.AxisX, GizmoHandle.AxisX);
        CountColour(Draw(harness), highlight).ShouldBe(9 * 2);

        // The arrow is not drawn twice. Only the x-normal quad still wears the red.
        Vector3 plain = GizmoColors.For(GizmoHandle.AxisX, GizmoHandle.None);
        CountColour(Draw(harness), plain).ShouldBe(4 * 2);
    }

    [Fact]
    public void The_dragged_handle_stays_highlighted_while_the_cursor_wanders()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        harness.AddSelectedNode(Vector3.Zero);
        float length = harness.GeometryAt(Vector3.Zero).AxisLength;

        harness.Grab(Vector3.UnitY * (length * 0.8f)).ShouldBe(GizmoUpdateResult.DragBegan);
        harness.DragBy(Vector3.UnitY * 20f);

        Vector3 highlight = GizmoColors.For(GizmoHandle.AxisY, GizmoHandle.AxisY);
        CountColour(Draw(harness), highlight).ShouldBe(9 * 2);
    }

    [Fact]
    public void Each_axis_arrow_carries_its_own_colour()
    {
        GizmoColors.For(GizmoHandle.AxisX, GizmoHandle.None).X.ShouldBeGreaterThan(0.9f);
        GizmoColors.For(GizmoHandle.AxisY, GizmoHandle.None).Y.ShouldBeGreaterThan(0.9f);
        GizmoColors.For(GizmoHandle.AxisZ, GizmoHandle.None).Z.ShouldBeGreaterThan(0.9f);

        // A plane quad shares the colour of the axis it is normal to.
        GizmoColors.For(GizmoHandle.PlaneYZ, GizmoHandle.None)
            .ShouldBe(GizmoColors.For(GizmoHandle.AxisX, GizmoHandle.None));
    }

    private static DebugDraw Draw(GizmoHarness harness)
    {
        var output = new DebugDraw();
        harness.Gizmo.Draw(output);
        return output;
    }

    private static Vector3[] Positions(DebugDraw output)
    {
        ReadOnlySpan<float> data = output.Vertices;
        var positions = new Vector3[output.VertexCount];
        for (int i = 0; i < positions.Length; i++)
        {
            int b = i * FloatsPerVertex;
            positions[i] = new Vector3(data[b], data[b + 1], data[b + 2]);
        }
        return positions;
    }

    private static int CountColour(DebugDraw output, Vector3 colour)
    {
        ReadOnlySpan<float> data = output.Vertices;
        int count = 0;
        for (int i = 0; i < output.VertexCount; i++)
        {
            int b = i * FloatsPerVertex;
            if (data[b + 3] == colour.X && data[b + 4] == colour.Y && data[b + 5] == colour.Z)
                count++;
        }
        return count;
    }
}
