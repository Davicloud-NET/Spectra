using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Input;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// What a drag has to survive: a viewport that vanishes mid-gesture, and
/// handles whose grab the drag would refuse.
/// </summary>
// The cursor ray divides by the viewport size. The tool has to refuse a
// zero-size frame itself; it cannot rely on the host to filter one out.
public sealed class GizmoRobustnessTests
{
    private const float AlongAxis = 0.8f;
    private const float RoundTrip = 1e-3f;

    [Fact]
    public void A_zero_size_viewport_frame_mid_drag_holds_the_drag_and_stays_finite()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        SceneNode node = harness.AddSelectedNode(Vector3.Zero);
        float length = harness.GeometryAt(Vector3.Zero).AxisLength;

        harness.Grab(Vector3.UnitX * (length * AlongAxis));
        harness.DragBy(Vector3.UnitX * 2f);

        // Window minimised mid-drag: a frame with no viewport.
        var degenerate = new EditorInputFrame(
            new Vector2(100f, 100f), Vector2.Zero, PointerButtons.Left,
            PointerButtons.None, PointerButtons.None, KeyModifiers.None,
            Vector2.Zero, 1f / 60f);
        harness.Gizmos.Update(degenerate).ShouldBe(GizmoUpdateResult.DragUpdated);

        float.IsFinite(node.LocalPosition.X).ShouldBeTrue();
        node.LocalPosition.X.ShouldBe(2f, RoundTrip);

        // Viewport back: the drag continues from the same grab.
        harness.DragBy(Vector3.UnitX * 3f);
        harness.Release();
        node.LocalPosition.X.ShouldBe(3f, RoundTrip);
    }

    [Fact]
    public void The_parallel_guards_refuse_a_non_finite_ray_instead_of_passing_it()
    {
        // NaN fails every comparison, so a guard written `< epsilon` lets it
        // through. The guards are written negated.
        var nanRay = new Ray3(new Vector3(float.NaN), new Vector3(float.NaN));

        GizmoMath.TryClosestPointOnLine(in nanRay, Vector3.Zero, Vector3.UnitX, out _).ShouldBeFalse();
        GizmoMath.TryRayPlane(in nanRay, Vector3.Zero, Vector3.UnitY, out _).ShouldBeFalse();
    }

    // A handle must not highlight and then refuse its grab: the press would
    // fall through to a marquee. Axis drags refuse within ~1.8 degrees of end-on.

    [Fact]
    public void An_end_on_translate_arrow_is_not_picked_because_its_drag_would_refuse()
    {
        (GizmoGeometry geometry, Ray3 ray) = EndOnAxisView();

        // The drag-side projection refuses this ray.
        GizmoMath.TryClosestPointOnLine(in ray, geometry.Pivot, geometry.AxisX, out _).ShouldBeFalse();

        GizmoPick pick = TranslateGizmoHitTester.Pick(
            in geometry, in ray, TranslateGizmoHitTester.DefaultTolerancePixels);
        pick.Handle.ShouldNotBe(GizmoHandle.AxisX);
    }

    [Fact]
    public void An_end_on_scale_shaft_is_not_picked_because_its_drag_would_refuse()
    {
        (GizmoGeometry geometry, Ray3 ray) = EndOnAxisView();

        GizmoPick pick = ScaleGizmoHitTester.Pick(
            in geometry, in ray, GizmoHitTesting.DefaultTolerancePixels);
        pick.Handle.ShouldNotBe(GizmoHandle.AxisX);
    }

    // Camera about 0.9 degrees off the +X axis, inside the parallel refusal,
    // with the pick ray aimed at the arrow's midpoint.
    private static (GizmoGeometry Geometry, Ray3 Ray) EndOnAxisView()
    {
        var camera = new Camera
        {
            Position = new Vector3(10f, 0.15f, 0f),
            AspectRatio = 800f / 600f,
        };
        camera.LookAt(Vector3.Zero);
        var viewport = new Vector2(800f, 600f);

        GizmoGeometry geometry = GizmoGeometry.Build(
            camera, Vector3.Zero, Quaternion.Identity, viewport, GizmoGeometry.DefaultPixelSize);

        Vector3 aim = geometry.Pivot + geometry.AxisX * (geometry.AxisLength * 0.5f);
        Vector4 clip = Vector4.Transform(new Vector4(aim, 1f), camera.GetViewProjection());
        var cursor = new Vector2(
            (clip.X / clip.W + 1f) * 0.5f * viewport.X,
            (1f - clip.Y / clip.W) * 0.5f * viewport.Y);

        return (geometry, camera.ScreenPointToRay(cursor, viewport));
    }
}
