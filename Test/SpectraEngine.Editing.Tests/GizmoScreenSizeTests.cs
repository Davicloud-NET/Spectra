using SpectraEngine.Editing.Gizmos;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>A gizmo keeps the same size in pixels at any camera distance.</summary>
public sealed class GizmoScreenSizeTests
{
    [Theory]
    [InlineData(2f)]
    [InlineData(10f)]
    [InlineData(500f)]
    [InlineData(20_000f)]
    public void An_axis_arrow_covers_the_requested_pixel_length_at_any_distance(float distance)
    {
        // Front view: the x arrow lies in the view plane, no foreshortening.
        var harness = GizmoHarness.FrontView(distance);
        GizmoGeometry geometry = harness.GeometryAt(Vector3.Zero);

        geometry.TryGetAxisSegment(GizmoHandle.AxisX, out Vector3 start, out Vector3 tip).ShouldBeTrue();
        float pixels = Vector2.Distance(harness.WorldToScreen(start), harness.WorldToScreen(tip));

        pixels.ShouldBe(harness.Gizmo.HandlePixelSize, 0.05f);
    }

    [Fact]
    public void The_world_size_grows_in_proportion_to_the_camera_distance()
    {
        float near = GizmoHarness.FrontView(10f).GeometryAt(Vector3.Zero).AxisLength;
        float far = GizmoHarness.FrontView(1000f).GeometryAt(Vector3.Zero).AxisLength;

        (far / near).ShouldBe(100f, 0.01f);
    }

    [Fact]
    public void The_pick_tolerance_stays_a_screen_space_quantity_at_any_distance()
    {
        float nearWorld = GizmoHarness.FrontView(10f).GeometryAt(Vector3.Zero).WorldPerPixel;
        float farWorld = GizmoHarness.FrontView(10_000f).GeometryAt(Vector3.Zero).WorldPerPixel;

        var nearHarness = GizmoHarness.FrontView(10f);
        var farHarness = GizmoHarness.FrontView(10_000f);

        nearHarness.GeometryAt(Vector3.Zero).WorldToPixels(nearWorld * 8f).ShouldBe(8f, 1e-3f);
        farHarness.GeometryAt(Vector3.Zero).WorldToPixels(farWorld * 8f).ShouldBe(8f, 1e-3f);

        (farWorld / nearWorld).ShouldBe(1000f, 0.5f);
    }

    [Fact]
    public void A_degenerate_viewport_reports_no_pickable_size()
    {
        // A panel not laid out yet. Infinity keeps everything outside tolerance.
        var harness = new GizmoHarness(new Vector3(0f, 0f, 10f), Vector3.Zero, viewportWidth: 0f, viewportHeight: 0f);
        GizmoGeometry geometry = harness.GeometryAt(Vector3.Zero);

        geometry.WorldPerPixel.ShouldBe(0f);
        geometry.WorldToPixels(1f).ShouldBe(float.PositiveInfinity);
    }
}
