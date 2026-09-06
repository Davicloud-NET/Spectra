using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Gizmos;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// The seven views: which way each looks, what happens to the projection, and
/// what leaves one.
/// </summary>
/// <remarks>
/// <b>The tests pin the SCREEN axes rather than the angles.</b> "Front is a yaw
/// of -pi/2" is not something anybody can check by reading, and getting it wrong
/// renders a picture that is plausible and mirrored: a level drawn back to front
/// is not obviously wrong until somebody has built half a room against it.
/// </remarks>
public sealed class EditorViewPresetTests
{
    private static EditorCameraController Controller()
    {
        var scene = new Scene("Test");
        scene.Camera.AspectRatio = 16f / 9f;

        var controller = new EditorCameraController(scene);
        controller.SetOrbit(Vector3.Zero, 20f, 0f, -0.3f);
        return controller;
    }

    [Theory]
    [InlineData(EditorViewPreset.Top, 0f, -1f, 0f)]
    [InlineData(EditorViewPreset.Bottom, 0f, 1f, 0f)]
    [InlineData(EditorViewPreset.Front, 0f, 0f, -1f)]
    [InlineData(EditorViewPreset.Back, 0f, 0f, 1f)]
    [InlineData(EditorViewPreset.Right, -1f, 0f, 0f)]
    [InlineData(EditorViewPreset.Left, 1f, 0f, 0f)]
    public void Each_view_looks_along_its_own_axis(EditorViewPreset preset, float x, float y, float z)
    {
        EditorCameraController controller = Controller();
        controller.SetView(preset);

        Vector3 expected = new(x, y, z);
        Vector3.Dot(controller.Camera.Forward, expected).ShouldBe(1f, 1e-3f);
    }

    [Theory]
    [InlineData(EditorViewPreset.Front, 1f, 0f, 0f)]
    [InlineData(EditorViewPreset.Back, -1f, 0f, 0f)]
    [InlineData(EditorViewPreset.Right, 0f, 0f, -1f)]
    [InlineData(EditorViewPreset.Left, 0f, 0f, 1f)]
    public void Each_side_view_puts_the_expected_axis_to_the_right(
        EditorViewPreset preset, float x, float y, float z)
    {
        EditorCameraController controller = Controller();
        controller.SetView(preset);

        Vector3.Dot(controller.Camera.Right, new Vector3(x, y, z)).ShouldBe(1f, 1e-3f);
    }

    [Fact]
    public void An_elevation_view_puts_world_up_on_the_screen_up()
    {
        EditorCameraController controller = Controller();
        controller.SetView(EditorViewPreset.Front);

        Vector3.Dot(controller.Camera.Up, Vector3.UnitY).ShouldBe(1f, 1e-3f);
    }

    [Fact]
    public void Entering_a_view_switches_the_projection_and_derives_its_height()
    {
        EditorCameraController controller = Controller();
        controller.SetView(EditorViewPreset.Top);

        controller.Camera.ProjectionKind.ShouldBe(CameraProjectionKind.Orthographic);

        // Derived from the distance, so framing carries across the switch:
        // the height is what a perspective camera would span at that distance.
        float expected = 2f * controller.Distance * MathF.Tan(controller.Camera.FieldOfView * 0.5f);
        controller.Camera.OrthographicHeight.ShouldBe(expected, 1e-2f);
    }

    [Fact]
    public void The_eye_sits_at_the_focus_so_both_sides_of_it_render()
    {
        EditorCameraController controller = Controller();
        controller.SetOrbit(new Vector3(4f, 0f, -7f), 25f, 0f, -0.3f);
        controller.SetView(EditorViewPreset.Top);

        // Pulled back instead, a plan view would clip away everything between
        // the camera and the floor, which is every ceiling in the level.
        Vector3.Distance(controller.Camera.Position, controller.Focus).ShouldBeLessThan(1e-3f);
    }

    [Fact]
    public void Leaving_puts_the_perspective_projection_back()
    {
        EditorCameraController controller = Controller();
        controller.SetView(EditorViewPreset.Front);
        controller.SetView(EditorViewPreset.Perspective);

        controller.Camera.ProjectionKind.ShouldBe(CameraProjectionKind.Perspective);
        controller.View.ShouldBe(EditorViewPreset.Perspective);
    }

    [Fact]
    public void Looking_around_leaves_an_orthographic_view()
    {
        EditorCameraController controller = Controller();
        controller.SetView(EditorViewPreset.Top);

        controller.ApplyFreeLook(new Vector2(10f, 0f));

        // Refusing would teach that the view is stuck, which is worse than a
        // change the status line explains once.
        controller.View.ShouldBe(EditorViewPreset.Perspective);
        controller.Camera.ProjectionKind.ShouldBe(CameraProjectionKind.Perspective);
    }

    [Fact]
    public void Orbiting_leaves_one_too()
    {
        EditorCameraController controller = Controller();
        controller.SetView(EditorViewPreset.Right);

        controller.ApplyOrbit(new Vector2(0f, 8f));

        controller.View.ShouldBe(EditorViewPreset.Perspective);
    }

    [Fact]
    public void Setting_the_view_it_already_has_changes_nothing()
    {
        EditorCameraController controller = Controller();
        controller.SetView(EditorViewPreset.Top).ShouldBeTrue();
        controller.SetView(EditorViewPreset.Top).ShouldBeFalse();
    }

    [Fact]
    public void Zooming_keeps_the_world_point_under_the_cursor()
    {
        EditorCameraController controller = Controller();
        controller.SetView(EditorViewPreset.Top);

        var viewport = new Vector2(1280f, 720f);
        var cursor = new Vector2(1000f, 200f);

        Ray3 before = controller.Camera.ScreenPointToRay(cursor, viewport);
        Vector3 target = PlaneHit(before);

        controller.ApplyZoom(-3f, cursor, viewport);
        controller.SnapToTarget();

        Ray3 after = controller.Camera.ScreenPointToRay(cursor, viewport);
        Vector3 landed = PlaneHit(after);

        // Zooming toward the cursor is what makes a plan view navigable at all:
        // without it, magnifying moves the thing being looked at off screen.
        Vector3.Distance(target, landed).ShouldBeLessThan(0.05f);
    }

    [Fact]
    public void Panning_moves_by_the_height_per_pixel()
    {
        EditorCameraController controller = Controller();
        controller.SetView(EditorViewPreset.Top);

        var viewport = new Vector2(1280f, 720f);
        float perPixel = controller.Camera.OrthographicHeight / viewport.Y;

        Vector3 before = controller.Focus;
        controller.ApplyPan(new Vector2(0f, 100f), viewport);
        controller.SnapToTarget();

        // A hundred pixels of drag moves the world by a hundred pixels' worth,
        // which is what makes a pan feel like dragging the paper.
        float moved = Vector3.Distance(before, controller.Focus);
        moved.ShouldBe(100f * perPixel, 0.5f);
    }

    [Fact]
    public void A_gizmo_handle_is_the_same_size_at_every_zoom()
    {
        EditorCameraController controller = Controller();
        controller.SetView(EditorViewPreset.Top);

        var viewport = new Vector2(1280f, 720f);

        float near = GizmoMath.WorldPerPixel(controller.Camera, viewport.Y, viewDepth: 1f);
        float far = GizmoMath.WorldPerPixel(controller.Camera, viewport.Y, viewDepth: 4000f);

        // Depth is irrelevant under a parallel projection, so a handle that kept
        // the perspective formula would shrink toward the focus plane and grow
        // behind it, which reads as handles changing size while nothing moved.
        near.ShouldBe(far);
        near.ShouldBe(controller.Camera.OrthographicHeight / viewport.Y, 1e-5f);
    }

    [Fact]
    public void A_point_behind_an_orthographic_eye_is_still_in_view()
    {
        EditorCameraController controller = Controller();
        controller.SetView(EditorViewPreset.Top);

        Vector3 above = controller.Camera.Position + (Vector3.UnitY * 30f);

        // Every caller treats a negative depth as "behind the camera, do not
        // draw", and an orthographic slab is symmetric about the eye: measuring
        // from the slab's near face keeps that predicate meaning "out of view".
        GizmoMath.ViewDepth(controller.Camera, above).ShouldBeGreaterThan(0f);
    }

    [Fact]
    public void The_default_keymap_resolves_every_view()
    {
        Check("Keypad5", Core.Input.KeyModifiers.None, EditorCameraCommand.ViewPerspective);
        Check("Keypad7", Core.Input.KeyModifiers.None, EditorCameraCommand.ViewTop);
        Check("Keypad7", Core.Input.KeyModifiers.Control, EditorCameraCommand.ViewBottom);
        Check("Keypad1", Core.Input.KeyModifiers.None, EditorCameraCommand.ViewFront);
        Check("Keypad1", Core.Input.KeyModifiers.Control, EditorCameraCommand.ViewBack);
        Check("Keypad3", Core.Input.KeyModifiers.None, EditorCameraCommand.ViewRight);
        Check("Keypad3", Core.Input.KeyModifiers.Control, EditorCameraCommand.ViewLeft);

        static void Check(string key, Core.Input.KeyModifiers modifiers, EditorCameraCommand expected)
        {
            EditorCameraShortcuts.TryResolve(key, modifiers, out EditorCameraCommand command)
                .ShouldBeTrue(key);
            command.ShouldBe(expected);
        }
    }

    [Fact]
    public void The_grid_moves_to_the_plane_the_view_is_looking_at()
    {
        // An edge-on grid is one row of pixels, which is worse than none.
        EditorViewPresets.GridPlaneOf(EditorViewPreset.Top).ShouldBe(GridPlane.Ground);
        EditorViewPresets.GridPlaneOf(EditorViewPreset.Bottom).ShouldBe(GridPlane.Ground);
        EditorViewPresets.GridPlaneOf(EditorViewPreset.Perspective).ShouldBe(GridPlane.Ground);
        EditorViewPresets.GridPlaneOf(EditorViewPreset.Front).ShouldBe(GridPlane.Front);
        EditorViewPresets.GridPlaneOf(EditorViewPreset.Back).ShouldBe(GridPlane.Front);
        EditorViewPresets.GridPlaneOf(EditorViewPreset.Right).ShouldBe(GridPlane.Side);
        EditorViewPresets.GridPlaneOf(EditorViewPreset.Left).ShouldBe(GridPlane.Side);
    }

    [Fact]
    public void Every_view_has_a_name_and_only_perspective_is_not_orthographic()
    {
        EditorViewPresets.NameOf(EditorViewPreset.Perspective).ShouldBe("Perspective");
        EditorViewPresets.IsOrthographic(EditorViewPreset.Perspective).ShouldBeFalse();

        foreach (EditorViewPreset preset in new[]
        {
            EditorViewPreset.Top, EditorViewPreset.Bottom, EditorViewPreset.Front,
            EditorViewPreset.Back, EditorViewPreset.Right, EditorViewPreset.Left,
        })
        {
            EditorViewPresets.IsOrthographic(preset).ShouldBeTrue();
            EditorViewPresets.NameOf(preset).ShouldNotBeNullOrWhiteSpace();

            // Interned, because it crosses the frame snapshot on every publish
            // and a fresh string per publish is render-thread garbage for a
            // label that rarely changes.
            ReferenceEquals(
                EditorViewPresets.NameOf(preset),
                EditorViewPresets.NameOf(preset)).ShouldBeTrue();
        }
    }

    // Where a ray crosses y = 0, which is the plane a top view is measuring on.
    private static Vector3 PlaneHit(in Ray3 ray)
    {
        float t = -ray.Origin.Y / ray.Direction.Y;
        return ray.Origin + (ray.Direction * t);
    }
}
