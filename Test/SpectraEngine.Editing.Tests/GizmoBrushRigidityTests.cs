using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// A brush node's world transform stays rigid (rotation and translation only)
/// under the rotate and resize gizmos.
/// </summary>
// The static-world compile rejects a non-rigid brush placement, so each test
// ends by running the real compile.
public sealed class GizmoBrushRigidityTests
{
    [Fact]
    public void Rotating_a_brush_node_keeps_its_transform_rigid_and_compiles()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        var renderer = new CompilingRenderer();
        harness.Scene.RebuildStaticWorld(renderer);

        RotateGizmo rotate = (RotateGizmo)harness.Use(GizmoMode.Rotate);
        rotate.Snap.Increment = 45f;
        RotateGizmoDragTests.Sweep(harness, GizmoHandle.AxisY, 0.8f); // snaps to 45°

        node.LocalScale.ShouldBe(Vector3.One);
        ShouldBeRigid(node.WorldMatrix);

        harness.Scene.StaticWorldDirty.ShouldBeTrue();
        harness.Scene.RebuildStaticWorld(renderer);
        harness.Scene.LastCompileDirtyCells.ShouldNotBeEmpty();
        harness.Scene.StaticWorld.ShouldNotBeNull();
    }

    [Fact]
    public void Resizing_a_brush_node_keeps_its_transform_rigid_and_compiles()
    {
        // Studio style: its face-anchored resize also shifts the node.
        var harness = ScaleGizmoDragTests.ResizeHarness();
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        var renderer = new CompilingRenderer();
        harness.Scene.RebuildStaticWorld(renderer);

        ScaleGizmo scale = (ScaleGizmo)harness.Use(GizmoMode.Scale);
        scale.Snap.Enabled = false;

        harness.Gizmos.Update(harness.Frame(new Vector2(-10f, -10f)));
        GizmoGeometry geometry = harness.Gizmo.Geometry;
        float reach = geometry.AxisReach(GizmoHandle.AxisX);
        harness.Grab(geometry.AxisX * reach).ShouldBe(GizmoUpdateResult.DragBegan);
        // Cursor travel is the size change: 2 units wide becomes 5.
        harness.DragTo(geometry.AxisX * (reach + 3f));
        harness.Release().ShouldBe(GizmoUpdateResult.DragCommitted);

        node.LocalScale.ShouldBe(Vector3.One);
        ShouldBeRigid(node.WorldMatrix);
        node.Brush!.LocalBounds.Max.X.ShouldBe(2.5f, 1e-3f);
        // Face-anchored: the node moves by half the growth.
        node.LocalPosition.X.ShouldBe(1.5f, 1e-3f);

        harness.Scene.RebuildStaticWorld(renderer);
        harness.Scene.StaticWorld.ShouldNotBeNull();
    }

    [Fact]
    public void Rotating_a_brush_node_moves_it_the_same_way_a_scripted_rotation_would()
    {
        var scripted = GizmoHarness.ThreeQuarterView();
        SceneNode scriptedNode = scripted.AddSelectedBrushNode(new Vector3(4f, 0f, 0f));
        scriptedNode.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 45f * MathF.PI / 180f);

        var dragged = GizmoHarness.ThreeQuarterView();
        SceneNode draggedNode = dragged.AddSelectedBrushNode(new Vector3(4f, 0f, 0f));
        RotateGizmo rotate = (RotateGizmo)dragged.Use(GizmoMode.Rotate);
        rotate.Snap.Increment = 45f;
        RotateGizmoDragTests.Sweep(dragged, GizmoHandle.AxisY, 0.8f);

        // One node rotates about its own position, so the position is untouched.
        draggedNode.LocalPosition.ShouldBe(scriptedNode.LocalPosition);
        RotateGizmoDragTests.ShouldRotateLike(draggedNode.LocalRotation, scriptedNode.LocalRotation);
    }

    [Fact]
    public void Resizing_a_group_node_with_brush_children_is_refused_outright()
    {
        // A scale on a brushless group makes every brush under it non-rigid,
        // and the compile then rejects the whole placement snapshot.
        var harness = GizmoHarness.ThreeQuarterView();
        SceneNode group = harness.AddNode(Vector3.Zero, "Group");
        SceneNode child = group.CreateChild("BrushChild");
        child.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        harness.Scene.Selection.Add(group);
        group.SubtreeBrushCount.ShouldBe(1);

        var renderer = new CompilingRenderer();
        harness.Scene.RebuildStaticWorld(renderer);
        harness.Scene.StaticWorld.ShouldNotBeNull();

        ScaleGizmo scale = (ScaleGizmo)harness.Use(GizmoMode.Scale);
        scale.Snap.Enabled = false;

        harness.Gizmos.Update(harness.Frame(new Vector2(-10f, -10f)));
        GizmoGeometry geometry = harness.Gizmo.Geometry;

        // The handle hit-tests but the grab is declined.
        harness.Grab(geometry.AxisX * geometry.AxisLength).ShouldBe(GizmoUpdateResult.Hovering);
        harness.Gizmo.ActiveHandle.ShouldBe(GizmoHandle.None);
        harness.Undo.IsTransactionOpen.ShouldBeFalse();
        harness.Undo.Count.ShouldBe(0);

        group.LocalScale.ShouldBe(Vector3.One);
        ShouldBeRigid(child.WorldMatrix);

        // An unrelated brush edit still compiles.
        SceneNode elsewhere = harness.AddNode(new Vector3(50f, 0f, 0f), "Far");
        elsewhere.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        harness.Scene.RebuildStaticWorld(renderer);
        harness.Scene.StaticWorld.ShouldNotBeNull();
    }

    [Fact]
    public void A_mixed_selection_resizes_what_it_can_and_declines_the_rest()
    {
        // The declined node still occupies a slot in the per-target list.
        var harness = ScaleGizmoDragTests.ResizeHarness();
        SceneNode mesh = harness.AddMeshNode(new Vector3(-4f, 0f, 0f), 0.5f, "Mesh");
        SceneNode group = harness.AddNode(new Vector3(4f, 0f, 0f), "Group");
        SceneNode child = group.CreateChild("BrushChild");
        child.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));

        harness.Scene.Selection.Add(mesh);
        harness.Scene.Selection.Add(group);

        ScaleGizmo scale = (ScaleGizmo)harness.Use(GizmoMode.Scale);
        scale.Snap.Enabled = false;

        harness.Gizmos.Update(harness.Frame(new Vector2(-10f, -10f)));
        GizmoGeometry geometry = harness.Gizmo.Geometry;
        Vector3 pivot = geometry.Pivot;

        float reach = geometry.AxisReach(GizmoHandle.AxisX);
        harness.Grab(pivot + geometry.AxisX * reach).ShouldBe(GizmoUpdateResult.DragBegan);
        // The mesh is one unit across, so +1 doubles it.
        harness.DragTo(pivot + geometry.AxisX * (reach + 1f));
        harness.Release().ShouldBe(GizmoUpdateResult.DragCommitted);

        mesh.LocalScale.X.ShouldBe(2f, 1e-2f);
        group.LocalScale.ShouldBe(Vector3.One);
        ShouldBeRigid(child.WorldMatrix);

        var renderer = new CompilingRenderer();
        harness.Scene.RebuildStaticWorld(renderer);
        harness.Scene.StaticWorld.ShouldNotBeNull();
    }

    // Scene's definition: orthonormal basis, positively oriented.
    private static void ShouldBeRigid(Matrix4x4 m)
    {
        var x = new Vector3(m.M11, m.M12, m.M13);
        var y = new Vector3(m.M21, m.M22, m.M23);
        var z = new Vector3(m.M31, m.M32, m.M33);

        const float tolerance = 1e-4f;
        x.Length().ShouldBe(1f, tolerance);
        y.Length().ShouldBe(1f, tolerance);
        z.Length().ShouldBe(1f, tolerance);

        Vector3.Dot(x, y).ShouldBe(0f, tolerance);
        Vector3.Dot(y, z).ShouldBe(0f, tolerance);
        Vector3.Dot(z, x).ShouldBe(0f, tolerance);

        Vector3.Dot(Vector3.Cross(x, y), z).ShouldBe(1f, tolerance); // not mirrored
    }
}
