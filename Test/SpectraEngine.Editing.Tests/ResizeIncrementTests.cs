using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// One snap notch changes an object's world size by the increment, whatever
/// the object already measures, and the opposite face stays put.
/// </summary>
public sealed class ResizeIncrementTests
{
    private const float Tolerance = 1e-3f;

    // Between half a notch and a whole one, so the drag snaps to one increment.
    private const float PastHalfANotch = 0.6f;

    [Theory]
    [InlineData(0.2f)]  // a 0.4-unit brush
    [InlineData(5f)]    // a 10-unit brush
    public void One_notch_changes_a_brush_by_exactly_the_increment_whatever_it_measures(float halfExtent)
    {
        var harness = ScaleGizmoDragTests.ResizeHarness();
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent);
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Enabled = true;
        scale.Snap.Increment = 1f;

        float startSize = node.Brush!.LocalBounds.Size.X;
        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisX, PastHalfANotch);

        (node.Brush!.LocalBounds.Size.X - startSize).ShouldBe(1f, Tolerance);
        node.LocalScale.ShouldBe(Vector3.One);
    }

    [Fact]
    public void The_increment_is_settable_in_code_and_the_notch_follows_it()
    {
        var harness = ScaleGizmoDragTests.ResizeHarness();
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Increment = 0.25f;

        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisX, 0.15f);

        node.Brush!.LocalBounds.Size.X.ShouldBe(2.25f, Tolerance);
    }

    [Fact]
    public void Every_object_in_a_multi_selection_lands_on_its_own_increment()
    {
        // Camera pulled back: the selection is 26 units wide and Studio handles
        // stand on its box, so the usual view puts the +x handle off screen.
        var harness = new GizmoHarness(new Vector3(24f, 18f, 30f), Vector3.Zero, style: GizmoStyle.Studio);
        SceneNode small = harness.AddSelectedBrushNode(new Vector3(-8f, 0f, 0f), 0.2f, "Small");
        SceneNode big = harness.AddSelectedBrushNode(new Vector3(8f, 0f, 0f), 5f, "Big");
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Increment = 1f;

        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisX, PastHalfANotch);

        // Both grow by one notch, not by one shared factor.
        small.Brush!.LocalBounds.Size.X.ShouldBe(1.4f, Tolerance);
        big.Brush!.LocalBounds.Size.X.ShouldBe(11f, Tolerance);
    }

    [Fact]
    public void A_face_anchored_notch_moves_the_dragged_face_and_plants_the_opposite_one()
    {
        var harness = ScaleGizmoDragTests.ResizeHarness();
        SceneNode node = harness.AddSelectedBrushNode(new Vector3(3f, 0f, 0f), halfExtent: 1f);
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Increment = 1f;

        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisX, PastHalfANotch);

        float min = node.LocalPosition.X + node.Brush!.LocalBounds.Min.X;
        float max = node.LocalPosition.X + node.Brush.LocalBounds.Max.X;

        min.ShouldBe(2f, Tolerance);  // planted, to the world unit it started on
        max.ShouldBe(5f, Tolerance);  // out by exactly one increment, from 4
        node.LocalPosition.X.ShouldBe(3.5f, Tolerance); // half the increment
        node.LocalPosition.Y.ShouldBe(0f);
        node.LocalPosition.Z.ShouldBe(0f);
    }

    [Fact]
    public void A_symmetric_resize_leaves_the_node_where_it_is_and_still_lands_on_the_increment()
    {
        var harness = ScaleGizmoDragTests.ResizeHarness();
        SceneNode node = harness.AddSelectedBrushNode(new Vector3(3f, 0f, 0f), halfExtent: 1f);
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Increment = 1f;
        scale.SymmetricModifier.ShouldBe(KeyModifiers.Shift);

        // Both faces move, so 0.3 of travel asks for 0.6 and snaps to 1.
        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisX, 0.3f, KeyModifiers.Shift);

        node.Brush!.LocalBounds.Size.X.ShouldBe(3f, Tolerance);
        node.LocalPosition.ShouldBe(new Vector3(3f, 0f, 0f)); // bit-identical: nothing wrote it
    }

    [Fact]
    public void A_mesh_node_lands_on_the_requested_world_size_through_its_bounds()
    {
        var harness = ScaleGizmoDragTests.ResizeHarness();
        // Two units across in its own frame, scaled to four in the world.
        SceneNode node = harness.AddSelectedMeshNode(Vector3.Zero, halfExtent: 1f);
        node.LocalScale = new Vector3(2f);
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Increment = 1f;

        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisX, PastHalfANotch);

        ResizeMath.TryMeasure(node, out Vector3 size, out _).ShouldBeTrue();
        size.X.ShouldBe(5f, Tolerance);        // four units plus one increment
        node.LocalScale.X.ShouldBe(2.5f, Tolerance);
        node.LocalPosition.X.ShouldBe(0.5f, Tolerance); // half an increment, in world units
    }

    [Fact]
    public void A_mesh_under_a_scaled_parent_still_grows_by_one_world_increment()
    {
        var harness = ScaleGizmoDragTests.ResizeHarness();
        SceneNode parent = harness.AddNode(Vector3.Zero, "Parent");
        parent.LocalScale = new Vector3(2f);

        SceneNode node = parent.CreateChild("Mesh");
        node.MeshRenderer = new MeshRenderer(BoxMesh.Centred(Vector3.One), new Material(null));
        harness.Scene.Selection.Add(node);

        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Increment = 1f;

        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisX, PastHalfANotch);

        ResizeMath.TryMeasure(node, out Vector3 size, out _).ShouldBeTrue();
        size.X.ShouldBe(5f, Tolerance);            // four world units plus one
        node.LocalScale.X.ShouldBe(1.25f, Tolerance); // in parent units, which are twice as big
    }

    [Fact]
    public void A_drag_shorter_than_half_an_increment_snaps_back_to_nothing_and_commits_nothing()
    {
        var harness = ScaleGizmoDragTests.ResizeHarness();
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        Brush original = node.Brush!;
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Increment = 1f;

        ScaleGizmoDragTests.GrabAxis(harness, GizmoHandle.AxisX, out Vector3 pivot, out Vector3 axis, out float length);
        harness.DragTo(pivot + axis * (length + 0.3f));
        harness.Release().ShouldBe(GizmoUpdateResult.DragCancelled);

        node.Brush.ShouldBeSameAs(original);
        node.LocalPosition.ShouldBe(Vector3.Zero);
        harness.Undo.Count.ShouldBe(0);
    }

    [Fact]
    public void A_whole_snapped_drag_is_one_undo_entry_that_restores_both_halves_of_the_edit()
    {
        var harness = ScaleGizmoDragTests.ResizeHarness();
        SceneNode node = harness.AddSelectedBrushNode(new Vector3(3f, 0f, 0f), halfExtent: 1f);
        Brush original = node.Brush!;
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Increment = 1f;

        ScaleGizmoDragTests.GrabAxis(harness, GizmoHandle.AxisX, out Vector3 pivot, out Vector3 axis, out float length);
        foreach (float travel in new[] { 0.4f, 1.2f, 2.7f, 1.1f, 0.6f })
            harness.DragTo(pivot + axis * (length + travel));
        harness.Release().ShouldBe(GizmoUpdateResult.DragCommitted);

        // Two commands (extents and position), one history entry.
        harness.Undo.Count.ShouldBe(1);
        node.Brush!.LocalBounds.Size.X.ShouldBe(3f, Tolerance);
        node.LocalPosition.X.ShouldBe(3.5f, Tolerance);

        harness.Undo.Undo().ShouldBeTrue();
        node.Brush.ShouldBeSameAs(original);
        node.LocalPosition.ShouldBe(new Vector3(3f, 0f, 0f));

        harness.Undo.Redo().ShouldBeTrue();
        node.Brush!.LocalBounds.Size.X.ShouldBe(3f, Tolerance);
        node.LocalPosition.X.ShouldBe(3.5f, Tolerance);
    }

    [Fact]
    public void A_cancelled_snapped_drag_restores_the_transform_exactly()
    {
        var harness = ScaleGizmoDragTests.ResizeHarness();
        SceneNode node = harness.AddSelectedBrushNode(new Vector3(3f, 0f, 0f), halfExtent: 1f);
        Brush original = node.Brush!;
        Transform before = node.LocalTransform;
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Increment = 1f;

        ScaleGizmoDragTests.GrabAxis(harness, GizmoHandle.AxisX, out Vector3 pivot, out Vector3 axis, out float length);
        harness.DragTo(pivot + axis * (length + 2.6f));
        node.LocalPosition.ShouldNotBe(before.Position); // the live drag really did move it

        harness.PressEscape().ShouldBe(GizmoUpdateResult.DragCancelled);

        // No tolerance: the commands carry absolute before-values.
        node.Brush.ShouldBeSameAs(original);
        node.LocalTransform.Position.ShouldBe(before.Position);
        node.LocalTransform.Rotation.ShouldBe(before.Rotation);
        node.LocalTransform.Scale.ShouldBe(before.Scale);
        harness.Undo.Count.ShouldBe(0);
    }

    [Fact]
    public void A_node_with_no_measurable_size_falls_back_to_a_proportional_factor_and_says_so()
    {
        var harness = ScaleGizmoDragTests.ResizeHarness();
        SceneNode node = harness.AddSelectedNode(Vector3.Zero); // no brush, no mesh: no size
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Enabled = false;

        ScaleGizmoDragTests.GrabAxis(harness, GizmoHandle.AxisX, out Vector3 pivot, out Vector3 axis, out float reach);
        // The fallback measures travel in gizmo lengths, not handle reach.
        harness.DragTo(pivot + axis * (reach + harness.Gizmo.Geometry.AxisLength));
        harness.Release().ShouldBe(GizmoUpdateResult.DragCommitted);

        // One gizmo length of travel doubles the scale.
        scale.ProportionalFallbackCount.ShouldBe(1);
        node.LocalScale.X.ShouldBe(2f, 1e-2f);
        node.LocalPosition.ShouldBe(Vector3.Zero);
    }

    [Fact]
    public void A_snapped_brush_drag_rebuilds_once_per_notch_not_once_per_frame()
    {
        // Each new Brush invalidates its carve and triggers a CSG recompile.
        var harness = ScaleGizmoDragTests.ResizeHarness();
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Enabled = true;
        scale.Snap.Increment = 4f;

        ScaleGizmoDragTests.GrabAxis(
            harness, GizmoHandle.AxisX, out Vector3 pivot, out Vector3 axis, out float length);

        // 5.5 units of travel at a 4-unit increment crosses one half-notch
        // boundary (at 2).
        Brush? last = node.Brush;
        int rebuilds = 0;
        const int Frames = 200;
        for (int i = 1; i <= Frames; i++)
        {
            harness.DragTo(pivot + axis * (length + 5.5f * i / Frames));
            if (!ReferenceEquals(node.Brush, last))
            {
                rebuilds++;
                last = node.Brush;
            }
        }

        harness.Release().ShouldBe(GizmoUpdateResult.DragCommitted);

        rebuilds.ShouldBe(1);
        node.Brush!.LocalBounds.Size.X.ShouldBe(6f, Tolerance); // two units plus one notch
        scale.ProportionalFallbackCount.ShouldBe(0);
    }

    [Fact]
    public void A_proportional_fallback_target_still_steps_its_own_factor_ladder_when_snapping()
    {
        var harness = ScaleGizmoDragTests.ResizeHarness();
        SceneNode node = harness.AddSelectedNode(Vector3.Zero); // no brush, no mesh: no size
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Enabled = true;
        scale.Snap.Increment = 1f;

        ScaleGizmoDragTests.GrabAxis(
            harness, GizmoHandle.AxisX, out Vector3 pivot, out Vector3 axis, out float reach);
        harness.DragTo(pivot + axis * (reach + harness.Gizmo.Geometry.AxisLength));
        harness.Release().ShouldBe(GizmoUpdateResult.DragCommitted);

        scale.ProportionalFallbackCount.ShouldBe(1);
        // ×2 is a rung of the 0.25 factor ladder, so snapping leaves it there.
        node.LocalScale.X.ShouldBe(2f, 1e-2f);
    }

    [Fact]
    public void A_measurable_selection_reports_no_proportional_fallback()
    {
        var harness = ScaleGizmoDragTests.ResizeHarness();
        harness.AddSelectedBrushNode(new Vector3(-3f, 0f, 0f), 1f, "Brush");
        harness.AddSelectedMeshNode(new Vector3(3f, 0f, 0f), 1f, "Mesh");
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);

        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisX, PastHalfANotch);

        scale.ProportionalFallbackCount.ShouldBe(0);
    }

    [Fact]
    public void The_resize_increment_is_one_world_unit_on_the_same_ladder_as_the_move_grid()
    {
        var settings = new ResizeSnapSettings();

        settings.Increment.ShouldBe(ResizeSnapSettings.DefaultIncrement);
        settings.Increment.ShouldBe(GridSnapSettings.DefaultIncrement);
        ResizeSnapSettings.Presets.ShouldBe(GridSnapSettings.Presets);

        var harness = ScaleGizmoDragTests.ResizeHarness();
        harness.Scale.Snap.ShouldNotBeSameAs(harness.Translate.Snap);

        settings.ToggleModifier.ShouldBe(KeyModifiers.Alt);
        settings.IsActiveWith(KeyModifiers.Alt).ShouldBeFalse();
        settings.CyclePreset(-1).ShouldBe(0.5f);
    }

    [Fact]
    public void Snapping_off_makes_the_resize_continuous_again()
    {
        var harness = ScaleGizmoDragTests.ResizeHarness();
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Enabled = false;

        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisX, 0.37f);

        node.Brush!.LocalBounds.Size.X.ShouldBe(2.37f, Tolerance);
    }

    [Fact]
    public void Alt_drops_a_snapped_resize_to_free_movement_for_the_gesture()
    {
        var harness = ScaleGizmoDragTests.ResizeHarness();
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Enabled = true;
        scale.Snap.Increment = 1f;

        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisX, 0.37f, KeyModifiers.Alt);

        node.Brush!.LocalBounds.Size.X.ShouldBe(2.37f, Tolerance);
    }
}
