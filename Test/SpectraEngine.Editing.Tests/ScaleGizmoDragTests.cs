using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// What a resize drag does. A brush node must stay rigid, so its resize edits
/// the brush planes and position and never writes node scale.
/// </summary>
public sealed class ScaleGizmoDragTests
{
    private const float Tolerance = 1e-3f;

    [Theory]
    [InlineData(GizmoHandle.AxisX)]
    [InlineData(GizmoHandle.AxisY)]
    [InlineData(GizmoHandle.AxisZ)]
    public void An_axis_drag_resizes_a_mesh_node_along_that_axis_only(GizmoHandle handle)
    {
        var harness = ResizeHarness();
        // A one-unit cube, so +1 of size is a ×2 scale.
        SceneNode node = harness.AddSelectedMeshNode(Vector3.Zero, halfExtent: 0.5f);
        ScaleGizmo scale = Scale(harness);
        scale.Snap.Enabled = false;

        DragAxisBy(harness, handle, 1f);

        Vector3 expectedScale = handle switch
        {
            GizmoHandle.AxisX => new Vector3(2f, 1f, 1f),
            GizmoHandle.AxisY => new Vector3(1f, 2f, 1f),
            _ => new Vector3(1f, 1f, 2f),
        };

        node.LocalScale.ShouldBeCloseTo(expectedScale, Tolerance);
        // Face-anchored: the node moves half the growth.
        Vector3 expectedPosition = (expectedScale - Vector3.One) * 0.5f;
        node.LocalPosition.ShouldBeCloseTo(expectedPosition, Tolerance);
        node.LocalRotation.ShouldBe(Quaternion.Identity);
    }

    [Fact]
    public void A_negative_drag_shrinks_a_mesh_node()
    {
        var harness = ResizeHarness();
        SceneNode node = harness.AddSelectedMeshNode(Vector3.Zero, halfExtent: 0.5f);
        Scale(harness).Snap.Enabled = false;

        DragAxisBy(harness, GizmoHandle.AxisY, -0.5f);

        node.LocalScale.ShouldBeCloseTo(new Vector3(1f, 0.5f, 1f), Tolerance);
        node.LocalPosition.ShouldBeCloseTo(new Vector3(0f, -0.25f, 0f), Tolerance);
    }

    [Fact]
    public void An_existing_scale_is_multiplied_not_replaced()
    {
        var harness = ResizeHarness();
        SceneNode node = harness.AddSelectedMeshNode(Vector3.Zero, halfExtent: 0.5f);
        node.LocalScale = new Vector3(3f, 4f, 5f);
        Scale(harness).Snap.Enabled = false;

        // 3 world units across x already; +3 makes 6, a ×2.
        DragAxisBy(harness, GizmoHandle.AxisX, 3f);

        node.LocalScale.ShouldBeCloseTo(new Vector3(6f, 4f, 5f), Tolerance * 10f);
    }

    [Fact]
    public void The_uniform_handle_resizes_all_three_axes_together()
    {
        var harness = ResizeFrontHarness();
        SceneNode node = harness.AddSelectedMeshNode(Vector3.Zero, halfExtent: 0.5f);
        ScaleGizmo scale = Scale(harness);
        scale.Snap.Enabled = false;

        GizmoGeometry geometry = Prime(harness);
        Vector3 pivot = harness.Gizmo.Pivot;

        harness.Grab(pivot).ShouldBe(GizmoUpdateResult.DragBegan);
        scale.ActiveHandle.ShouldBe(GizmoHandle.Screen);

        // One world unit of travel grows the largest dimension by one unit.
        Vector3 diagonal = Vector3.Normalize(geometry.ViewRight + geometry.ViewUp);
        harness.DragTo(pivot + diagonal);
        scale.DragSizeChange.ShouldBe(1f, Tolerance);
        harness.Release().ShouldBe(GizmoUpdateResult.DragCommitted);

        node.LocalScale.ShouldBeCloseTo(new Vector3(2f), Tolerance);
        // The uniform handle is symmetric.
        node.LocalPosition.ShouldBe(Vector3.Zero);
    }

    [Fact]
    public void Resizing_a_brush_node_edits_its_plane_extents_and_never_its_scale()
    {
        var harness = ResizeHarness();
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        Brush original = node.Brush!;
        Transform before = node.LocalTransform;

        Scale(harness).Snap.Enabled = false;
        DragAxisBy(harness, GizmoHandle.AxisX, 2f);

        // No tolerance: a scaled brush node breaks CSG and the static-world
        // snapshot rejects it.
        node.LocalTransform.Scale.ShouldBe(before.Scale);
        node.LocalTransform.Scale.ShouldBe(Vector3.One);
        node.LocalTransform.Rotation.ShouldBe(before.Rotation);

        // A new instance: the carve cache keys on brush reference.
        node.Brush.ShouldNotBeSameAs(original);
        node.Brush!.LocalBounds.Min.ShouldBeCloseTo(new Vector3(-2f, -1f, -1f), Tolerance);
        node.Brush.LocalBounds.Max.ShouldBeCloseTo(new Vector3(2f, 1f, 1f), Tolerance);

        // Half the growth, which keeps the -x face in place.
        node.LocalPosition.ShouldBeCloseTo(new Vector3(1f, 0f, 0f), Tolerance);

        original.LocalBounds.Max.ShouldBeCloseTo(Vector3.One, Tolerance);
    }

    [Fact]
    public void A_uniform_brush_resize_scales_all_three_extents()
    {
        var harness = ResizeFrontHarness();
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 2f);
        ScaleGizmo scale = Scale(harness);
        scale.Snap.Enabled = false;

        GizmoGeometry geometry = Prime(harness);
        Vector3 pivot = harness.Gizmo.Pivot;
        harness.Grab(pivot).ShouldBe(GizmoUpdateResult.DragBegan);

        // Four units across, dragged two smaller.
        Vector3 diagonal = Vector3.Normalize(geometry.ViewRight + geometry.ViewUp);
        harness.DragTo(pivot - diagonal * 2f);
        harness.Release().ShouldBe(GizmoUpdateResult.DragCommitted);

        node.LocalScale.ShouldBe(Vector3.One);
        // Tolerance on purpose: a symmetric resize holds the bounds centre,
        // which is derived from the planes and is not bit-zero.
        node.LocalPosition.ShouldBeCloseTo(Vector3.Zero, Tolerance);
        node.Brush!.LocalBounds.Max.ShouldBeCloseTo(new Vector3(1f), Tolerance * 10f);
    }

    [Fact]
    public void Undoing_a_brush_resize_puts_the_original_brush_instance_and_position_back()
    {
        var harness = ResizeHarness();
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero);
        Brush original = node.Brush!;
        Transform before = node.LocalTransform;
        Scale(harness).Snap.Enabled = false;

        DragAxisBy(harness, GizmoHandle.AxisZ, 3f);
        harness.Undo.Count.ShouldBe(1);
        harness.Undo.UndoName.ShouldBe("Resize");

        harness.Undo.Undo().ShouldBeTrue();

        // Same instance, so the cached carve is valid again.
        node.Brush.ShouldBeSameAs(original);
        node.LocalPosition.ShouldBe(before.Position);
    }

    [Fact]
    public void A_cancelled_brush_resize_restores_the_original_brush_and_leaves_no_history()
    {
        var harness = ResizeHarness();
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero);
        Brush original = node.Brush!;
        Transform before = node.LocalTransform;
        Scale(harness).Snap.Enabled = false;

        GrabAxis(harness, GizmoHandle.AxisX, out Vector3 pivot, out Vector3 axis, out float length);
        harness.DragTo(pivot + axis * (length + 2.5f));
        node.Brush.ShouldNotBeSameAs(original); // the live drag really did swap it

        harness.PressEscape().ShouldBe(GizmoUpdateResult.DragCancelled);

        node.Brush.ShouldBeSameAs(original);
        node.LocalTransform.Scale.ShouldBe(before.Scale);
        node.LocalTransform.Position.ShouldBe(before.Position);
        harness.Undo.Count.ShouldBe(0);
    }

    [Fact]
    public void A_mixed_selection_resizes_the_mesh_node_and_reshapes_the_brush_node()
    {
        var harness = ResizeHarness();
        SceneNode mesh = harness.AddSelectedMeshNode(new Vector3(-2f, 0f, 0f), halfExtent: 0.5f, name: "Mesh");
        SceneNode brush = harness.AddSelectedBrushNode(new Vector3(2f, 0f, 0f));
        Scale(harness).Snap.Enabled = false;

        DragAxisBy(harness, GizmoHandle.AxisY, 1f);

        // Both grew one world unit: the mesh by scale, the brush by planes.
        mesh.LocalScale.ShouldBeCloseTo(new Vector3(1f, 2f, 1f), Tolerance);
        brush.LocalScale.ShouldBe(Vector3.One);
        brush.Brush!.LocalBounds.Max.ShouldBeCloseTo(new Vector3(1f, 1.5f, 1f), Tolerance);
    }

    [Fact]
    public void A_brush_resize_marks_the_static_world_dirty_like_any_other_brush_edit()
    {
        var harness = ResizeHarness();
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero);
        var renderer = new CompilingRenderer();
        harness.Scene.RebuildStaticWorld(renderer);
        harness.Scene.StaticWorldDirty.ShouldBeFalse();

        Scale(harness).Snap.Enabled = false;
        DragAxisBy(harness, GizmoHandle.AxisX, 2f);

        harness.Scene.StaticWorldDirty.ShouldBeTrue();
        harness.Scene.RebuildStaticWorld(renderer);
        harness.Scene.LastCompileDirtyCells.ShouldNotBeEmpty();
        node.LocalScale.ShouldBe(Vector3.One);
    }

    [Fact]
    public void A_resize_that_never_left_its_starting_size_commits_nothing()
    {
        var harness = ResizeHarness();
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero);
        Brush original = node.Brush!;
        Scale(harness);

        GrabAxis(harness, GizmoHandle.AxisX, out _, out _, out _);
        harness.Release().ShouldBe(GizmoUpdateResult.DragCancelled);

        harness.Undo.Count.ShouldBe(0);
        node.Brush.ShouldBeSameAs(original);
    }

    [Fact]
    public void The_resize_gizmo_offers_no_world_orientation()
    {
        var harness = ResizeHarness();
        SceneNode node = harness.AddSelectedNode(Vector3.Zero);
        node.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);

        ScaleGizmo scale = Scale(harness);
        scale.SupportsOrientation.ShouldBeFalse();

        // After the quarter turn the node's local +x is world -z.
        harness.Gizmos.Orientation = GizmoOrientation.World;
        Prime(harness).AxisX.ShouldBeCloseTo(-Vector3.UnitZ, Tolerance);
    }

    // Studio style: face anchoring belongs to the style, and the harness
    // default is Classic.
    internal static GizmoHarness ResizeHarness() => GizmoHarness.ThreeQuarterView(GizmoStyle.Studio);

    // The uniform handle needs a face-on view.
    internal static GizmoHarness ResizeFrontHarness() => GizmoHarness.FrontView(style: GizmoStyle.Studio);

    internal static ScaleGizmo Scale(GizmoHarness harness)
    {
        harness.Use(GizmoMode.Scale);
        return harness.Scale;
    }

    // One update with the cursor off screen: builds the geometry and publishes
    // the pivot without claiming a hover.
    internal static GizmoGeometry Prime(GizmoHarness harness)
    {
        harness.Gizmos.Update(harness.Frame(new Vector2(-10f, -10f)));
        return harness.Gizmo.Geometry;
    }

    internal static void GrabAxis(
        GizmoHarness harness, GizmoHandle handle, out Vector3 pivot, out Vector3 axis, out float length)
    {
        GizmoGeometry geometry = Prime(harness);
        pivot = harness.Gizmo.Pivot;
        axis = geometry.Axis(handle);

        // Reach, not AxisLength: Studio handles stand on the selection's box.
        length = geometry.AxisReach(handle);

        harness.Hover(pivot + axis * length);
        harness.Gizmo.HoveredHandle.ShouldBe(handle);
        harness.Grab(pivot + axis * length).ShouldBe(GizmoUpdateResult.DragBegan);
    }

    // Grabs the handle, drags it worldDelta world units along its axis, commits.
    internal static void DragAxisBy(
        GizmoHarness harness, GizmoHandle handle, float worldDelta, KeyModifiers modifiers = KeyModifiers.None)
    {
        GrabAxis(harness, handle, out Vector3 pivot, out Vector3 axis, out float length);
        harness.DragTo(pivot + axis * (length + worldDelta), modifiers);
        harness.Release().ShouldBe(GizmoUpdateResult.DragCommitted);
    }
}
