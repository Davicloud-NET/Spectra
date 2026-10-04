using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Viewport;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Camera versus tools: camera buttons always reach the camera, a live gesture
/// keeps the pointer, and a locked cursor disables every position-based path.
/// </summary>
public sealed class ViewportNavigationArbitrationTests
{
    private const float AlongAxis = 0.8f;

    private static ViewportHarness Fixture()
    {
        var harness = new ViewportHarness();
        harness.Orbit(Vector3.Zero, 24f, 0.9f, -0.4f);
        return harness;
    }

    [Fact]
    public void A_right_press_over_a_gizmo_handle_still_goes_to_the_camera()
    {
        var harness = Fixture();
        harness.AddSelectedBrush(Vector3.Zero, 4f);
        Vector2 handlePixel = HandlePixel(harness);

        // A left press here would grab the handle.
        harness.Viewport.ClassifyPress(harness.Frame(handlePixel))
            .ShouldBe(ViewportDragMode.Manipulate);

        harness.Viewport.ClassifyPress(harness.Frame(
            handlePixel, down: PointerButtons.Right, pressed: PointerButtons.Right))
            .ShouldBe(ViewportDragMode.None);

        harness.Viewport.Update(harness.Frame(
            handlePixel, down: PointerButtons.Right, pressed: PointerButtons.Right))
            .ShouldBe(ViewportDragMode.None);
        harness.Viewport.Update(harness.Frame(handlePixel + new Vector2(40f, 0f), down: PointerButtons.Right));

        harness.Gizmos.Active.State.ShouldNotBe(GizmoInteractionState.Dragging);
        harness.EditorCamera.IsFreeLooking.ShouldBeTrue();
        harness.EditorCamera.Yaw.ShouldNotBe(0.9f);
    }

    [Fact]
    public void A_right_press_over_an_object_still_goes_to_the_camera()
    {
        var harness = Fixture();
        SceneNode node = harness.AddBrush(Vector3.Zero, 2f);
        Vector2 objectPixel = harness.WorldToScreen(Vector3.Zero);

        harness.Viewport.Update(harness.Frame(
            objectPixel, down: PointerButtons.Right, pressed: PointerButtons.Right))
            .ShouldBe(ViewportDragMode.None);

        harness.Scene.Selection.Contains(node).ShouldBeFalse();
        harness.Viewport.PressedNode.ShouldBeNull();
    }

    [Fact]
    public void Alt_and_the_left_button_orbit_instead_of_starting_a_marquee()
    {
        var harness = Fixture();
        SceneNode node = harness.AddSelectedBrush(new Vector3(30f, 0f, 0f), 1f);
        var empty = new Vector2(8f, 8f);

        harness.Viewport.ClassifyPress(harness.Frame(
            empty, down: PointerButtons.Left, pressed: PointerButtons.Left, modifiers: KeyModifiers.Alt))
            .ShouldBe(ViewportDragMode.None);

        harness.Viewport.Update(harness.Frame(
            empty, down: PointerButtons.Left, pressed: PointerButtons.Left, modifiers: KeyModifiers.Alt))
            .ShouldBe(ViewportDragMode.None);
        harness.Viewport.Update(harness.Frame(
            empty + new Vector2(40f, 0f), down: PointerButtons.Left, modifiers: KeyModifiers.Alt));

        harness.Viewport.BoxSelect.IsActive.ShouldBeFalse();
        harness.Scene.Selection.Contains(node).ShouldBeTrue();
        harness.EditorCamera.IsOrbiting.ShouldBeTrue();
    }

    [Fact]
    public void Alt_and_the_left_button_do_not_grab_a_handle_either()
    {
        var harness = Fixture();
        harness.AddSelectedBrush(Vector3.Zero, 4f);
        Vector2 handlePixel = HandlePixel(harness);

        harness.Viewport.Update(harness.Frame(
            handlePixel, down: PointerButtons.Left, pressed: PointerButtons.Left, modifiers: KeyModifiers.Alt))
            .ShouldBe(ViewportDragMode.None);

        harness.Gizmos.Active.State.ShouldNotBe(GizmoInteractionState.Dragging);
        harness.EditorCamera.IsOrbiting.ShouldBeTrue();
    }

    [Fact]
    public void The_middle_button_pans_rather_than_selecting()
    {
        var harness = Fixture();
        harness.AddBrush(Vector3.Zero, 2f);

        harness.Viewport.ClassifyPress(harness.Frame(
            harness.CenterPixel, down: PointerButtons.Middle, pressed: PointerButtons.Middle))
            .ShouldBe(ViewportDragMode.None);
    }

    [Fact]
    public void A_right_press_mid_gizmo_drag_cancels_the_drag_and_does_not_turn_the_view()
    {
        var harness = Fixture();
        SceneNode node = harness.AddSelectedBrush(Vector3.Zero, 1f);
        Vector3 origin = node.WorldPosition;
        float yaw = harness.EditorCamera.Yaw;

        harness.Viewport.Update(harness.Frame(
            HandlePixel(harness), down: PointerButtons.Left, pressed: PointerButtons.Left))
            .ShouldBe(ViewportDragMode.Manipulate);
        harness.Viewport.Update(harness.Frame(
            HandlePixel(harness) + new Vector2(90f, 0f), down: PointerButtons.Left))
            .ShouldBe(ViewportDragMode.Manipulate);
        node.WorldPosition.ShouldNotBe(origin);

        harness.Viewport.Update(harness.Frame(
            HandlePixel(harness) + new Vector2(200f, 60f),
            down: PointerButtons.Left | PointerButtons.Right,
            pressed: PointerButtons.Right))
            .ShouldBe(ViewportDragMode.None);

        node.WorldPosition.ShouldBe(origin);
        harness.EditorCamera.Yaw.ShouldBe(yaw);
        harness.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void A_right_drag_that_follows_a_cancelled_manipulation_starts_from_scratch()
    {
        // Cursor travel during the manipulation must not reach the camera as
        // one flick when it takes over.
        var harness = Fixture();
        harness.AddSelectedBrush(Vector3.Zero, 1f);
        Vector2 grab = HandlePixel(harness);

        harness.Viewport.Update(harness.Frame(grab, down: PointerButtons.Left, pressed: PointerButtons.Left));
        harness.Viewport.Update(harness.Frame(grab + new Vector2(120f, 40f), down: PointerButtons.Left));
        harness.Viewport.Update(harness.Frame(
            grab + new Vector2(240f, 80f),
            down: PointerButtons.Left | PointerButtons.Right,
            pressed: PointerButtons.Right));

        float yaw = harness.EditorCamera.Yaw;

        // Right still down, cursor where the cancel left it.
        harness.Viewport.Update(harness.Frame(grab + new Vector2(240f, 80f), down: PointerButtons.Right));
        harness.EditorCamera.Yaw.ShouldBe(yaw);

        harness.Viewport.Update(harness.Frame(grab + new Vector2(250f, 80f), down: PointerButtons.Right));
        harness.EditorCamera.Yaw.ShouldBe(yaw + 10f * harness.EditorCamera.LookSensitivity, 1e-5f);
    }

    [Fact]
    public void A_marquee_in_progress_keeps_the_pointer_when_the_look_button_arrives()
    {
        var harness = Fixture();
        harness.AddBrush(new Vector3(40f, 0f, 0f), 1f);

        harness.Press(new Vector2(8f, 8f)).ShouldBe(ViewportDragMode.BoxSelect);
        float yaw = harness.EditorCamera.Yaw;

        harness.Viewport.Update(harness.Frame(
            new Vector2(200f, 200f),
            down: PointerButtons.Left | PointerButtons.Right,
            pressed: PointerButtons.Right))
            .ShouldBe(ViewportDragMode.BoxSelect);

        harness.EditorCamera.Yaw.ShouldBe(yaw);
        harness.EditorCamera.IsFreeLooking.ShouldBeFalse();
        harness.CursorLock.Requested.ShouldBe(CursorMode.Normal);
    }

    [Fact]
    public void A_left_press_during_a_pan_neither_selects_nor_stops_the_pan()
    {
        // The middle button went down on an earlier frame, so ClaimsPress alone
        // does not cover the left press.
        var harness = Fixture();
        SceneNode node = harness.AddBrush(Vector3.Zero, 2f);
        Vector3 origin = node.WorldPosition;

        var grab = new Vector2(600f, 120f);
        harness.Viewport.Update(harness.Frame(grab, down: PointerButtons.Middle, pressed: PointerButtons.Middle))
            .ShouldBe(ViewportDragMode.None);
        Vector3 beforePan = harness.EditorCamera.Position;

        harness.Viewport.Update(harness.Frame(grab + new Vector2(20f, 0f), down: PointerButtons.Middle))
            .ShouldBe(ViewportDragMode.None);
        harness.EditorCamera.IsPanning.ShouldBeTrue();
        harness.EditorCamera.Position.ShouldNotBe(beforePan);

        Vector2 objectPixel = harness.WorldToScreen(Vector3.Zero);
        harness.Viewport.Update(harness.Frame(
            objectPixel,
            down: PointerButtons.Middle | PointerButtons.Left,
            pressed: PointerButtons.Left))
            .ShouldBe(ViewportDragMode.None);

        harness.Scene.Selection.Count.ShouldBe(0);
        harness.Viewport.PressedNode.ShouldBeNull();
        harness.Gizmos.Active.State.ShouldNotBe(GizmoInteractionState.Dragging);
        harness.Undo.IsTransactionOpen.ShouldBeFalse();
        harness.EditorCamera.IsPanning.ShouldBeTrue();

        harness.Viewport.Update(harness.Frame(
            objectPixel + new Vector2(50f, 30f),
            down: PointerButtons.Middle | PointerButtons.Left));
        harness.Viewport.Update(harness.Frame(
            objectPixel + new Vector2(50f, 30f),
            released: PointerButtons.Middle | PointerButtons.Left));

        node.WorldPosition.ShouldBe(origin);
        harness.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void A_left_press_during_a_freelook_is_refused_before_the_cursor_lock_lands()
    {
        // The lock is a two-thread latch: for at least one frame the freelook
        // is live and IsCursorLocked is still false.
        var harness = Fixture();
        SceneNode node = harness.AddSelectedBrush(Vector3.Zero, 4f);
        Vector3 origin = node.WorldPosition;
        Vector2 handlePixel = HandlePixel(harness);

        harness.Viewport.Update(harness.Frame(
            handlePixel, down: PointerButtons.Right, pressed: PointerButtons.Right))
            .ShouldBe(ViewportDragMode.None);
        harness.CursorLock.Requested.ShouldBe(CursorMode.Locked);
        harness.CursorLock.IsCursorLocked.ShouldBeFalse();

        harness.Viewport.Update(harness.Frame(
            handlePixel,
            down: PointerButtons.Right | PointerButtons.Left,
            pressed: PointerButtons.Left))
            .ShouldBe(ViewportDragMode.None);

        harness.Gizmos.Active.State.ShouldNotBe(GizmoInteractionState.Dragging);
        harness.Undo.IsTransactionOpen.ShouldBeFalse();
        harness.EditorCamera.IsFreeLooking.ShouldBeTrue();
        // The lock request survives the stray press.
        harness.EditorCamera.IsCursorLockRequested.ShouldBeTrue();
        harness.CursorLock.Requested.ShouldBe(CursorMode.Locked);

        // The lock lands while left is still held.
        harness.CursorLock.Pump();
        harness.Viewport.Update(harness.Frame(
            handlePixel,
            down: PointerButtons.Right | PointerButtons.Left,
            cursorDelta: new Vector2(30f, 0f),
            locked: true))
            .ShouldBe(ViewportDragMode.None);

        node.WorldPosition.ShouldBe(origin);
        harness.Undo.UndoCount.ShouldBe(0);
        harness.EditorCamera.IsFreeLooking.ShouldBeTrue();
    }

    [Fact]
    public void A_left_press_during_a_freelook_is_refused_when_the_host_never_locks_the_cursor()
    {
        // A null CursorLock is supported, and then IsCursorLocked is never true.
        // The guard has to be the gesture, not the lock.
        var harness = Fixture();
        harness.EditorCamera.CursorLock = null;
        SceneNode node = harness.AddSelectedBrush(Vector3.Zero, 4f);
        Vector3 origin = node.WorldPosition;
        Vector2 handlePixel = HandlePixel(harness);
        float yaw = harness.EditorCamera.Yaw;

        harness.Viewport.Update(harness.Frame(
            handlePixel, down: PointerButtons.Right, pressed: PointerButtons.Right));
        harness.Viewport.Update(harness.Frame(
            handlePixel + new Vector2(10f, 0f), down: PointerButtons.Right));
        harness.EditorCamera.Yaw.ShouldNotBe(yaw);

        harness.Viewport.Update(harness.Frame(
            handlePixel,
            down: PointerButtons.Right | PointerButtons.Left,
            pressed: PointerButtons.Left))
            .ShouldBe(ViewportDragMode.None);
        harness.Viewport.Update(harness.Frame(
            handlePixel + new Vector2(120f, 0f),
            down: PointerButtons.Right | PointerButtons.Left))
            .ShouldBe(ViewportDragMode.None);

        harness.Gizmos.Active.State.ShouldNotBe(GizmoInteractionState.Dragging);
        harness.EditorCamera.IsFreeLooking.ShouldBeTrue();
        node.WorldPosition.ShouldBe(origin);
        harness.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Classifying_a_press_mid_navigation_answers_none_and_changes_nothing()
    {
        var harness = Fixture();
        harness.AddSelectedBrush(Vector3.Zero, 4f);
        Vector2 handlePixel = HandlePixel(harness);

        harness.Viewport.ClassifyPress(harness.Frame(handlePixel)).ShouldBe(ViewportDragMode.Manipulate);

        harness.Viewport.Update(harness.Frame(
            handlePixel, down: PointerButtons.Middle, pressed: PointerButtons.Middle));
        harness.EditorCamera.IsPanning.ShouldBeTrue();

        harness.Viewport.ClassifyPress(harness.Frame(
            handlePixel,
            down: PointerButtons.Middle | PointerButtons.Left,
            pressed: PointerButtons.Left))
            .ShouldBe(ViewportDragMode.None);

        harness.EditorCamera.IsPanning.ShouldBeTrue();
        harness.Gizmos.Active.State.ShouldNotBe(GizmoInteractionState.Dragging);
    }

    [Fact]
    public void The_pointer_comes_back_the_moment_the_camera_gesture_ends()
    {
        // Boundary frame: the navigation button comes up as the left goes down.
        var harness = Fixture();
        SceneNode node = harness.AddBrush(Vector3.Zero, 2f);

        var grab = new Vector2(600f, 120f);
        harness.Viewport.Update(harness.Frame(grab, down: PointerButtons.Middle, pressed: PointerButtons.Middle));
        harness.Viewport.Update(harness.Frame(grab + new Vector2(20f, 0f), down: PointerButtons.Middle));
        harness.EditorCamera.IsPanning.ShouldBeTrue();

        harness.Viewport.Update(harness.Frame(
            harness.WorldToScreen(Vector3.Zero),
            down: PointerButtons.Left,
            pressed: PointerButtons.Left,
            released: PointerButtons.Middle))
            .ShouldBe(ViewportDragMode.SelectAndMove);

        harness.Scene.Selection.Contains(node).ShouldBeTrue();
        harness.EditorCamera.IsNavigating.ShouldBeFalse();
    }

    [Fact]
    public void Nothing_can_be_picked_while_the_cursor_is_locked()
    {
        var harness = Fixture();
        SceneNode node = harness.AddSelectedBrush(Vector3.Zero, 4f);
        // Far enough from the gizmo that no handle claims this pixel.
        harness.AddBrush(new Vector3(0f, 0f, 14f), 2f, "Other");
        Vector2 objectPixel = harness.WorldToScreen(new Vector3(0f, 0f, 14f));
        Vector2 handlePixel = HandlePixel(harness);

        harness.Viewport.ClassifyPress(harness.Frame(handlePixel)).ShouldBe(ViewportDragMode.Manipulate);
        harness.Viewport.ClassifyPress(harness.Frame(objectPixel)).ShouldBe(ViewportDragMode.SelectAndMove);
        harness.Gizmos.Active.PickAt(harness.Frame(handlePixel)).IsHit.ShouldBeTrue();

        // Locked: the reported position is stale.
        harness.Viewport.ClassifyPress(harness.Frame(handlePixel, locked: true))
            .ShouldBe(ViewportDragMode.None);
        harness.Viewport.ClassifyPress(harness.Frame(objectPixel, locked: true))
            .ShouldBe(ViewportDragMode.None);
        harness.Gizmos.Active.PickAt(harness.Frame(handlePixel, locked: true)).IsHit.ShouldBeFalse();

        harness.Scene.Selection.Contains(node).ShouldBeTrue();
    }

    [Fact]
    public void A_left_press_while_locked_starts_neither_a_drag_nor_a_marquee()
    {
        var harness = Fixture();
        SceneNode node = harness.AddSelectedBrush(Vector3.Zero, 4f);

        harness.Viewport.Update(harness.Frame(
            HandlePixel(harness),
            down: PointerButtons.Right | PointerButtons.Left,
            pressed: PointerButtons.Left,
            locked: true))
            .ShouldBe(ViewportDragMode.None);
        harness.Viewport.Update(harness.Frame(
            new Vector2(8f, 8f),
            down: PointerButtons.Right | PointerButtons.Left,
            pressed: PointerButtons.Left,
            locked: true))
            .ShouldBe(ViewportDragMode.None);

        harness.Gizmos.Active.State.ShouldNotBe(GizmoInteractionState.Dragging);
        harness.Viewport.BoxSelect.IsActive.ShouldBeFalse();
        harness.Scene.Selection.Contains(node).ShouldBeTrue();
        harness.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void A_locked_frame_clears_the_gizmo_hover()
    {
        var harness = Fixture();
        harness.AddSelectedBrush(Vector3.Zero, 4f);
        Vector2 handlePixel = HandlePixel(harness);

        harness.Viewport.Update(harness.Frame(handlePixel));
        harness.Gizmos.Active.HoveredHandle.ShouldNotBe(GizmoHandle.None);

        harness.Viewport.Update(harness.Frame(handlePixel, down: PointerButtons.Right, locked: true));
        harness.Gizmos.Active.HoveredHandle.ShouldBe(GizmoHandle.None);
    }

    private static Vector2 HandlePixel(ViewportHarness harness)
    {
        float axisLength = GizmoGeometry.Build(
            harness.Scene.Camera, Vector3.Zero, Quaternion.Identity,
            harness.ViewportSize, harness.Gizmos.Active.HandlePixelSize).AxisLength;
        return harness.WorldToScreen(Vector3.UnitX * (axisLength * AlongAxis));
    }
}
