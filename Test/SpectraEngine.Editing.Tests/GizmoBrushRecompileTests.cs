using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// A gizmo drag of a brush node looks the same to the static-world compile as
/// assigning <c>LocalPosition</c>: same placement, same dirty cells.
/// </summary>
// A 2-unit cube at (16,16,16) sits mid-cell (0,0,0); at (48,16,16), mid-cell (1,0,0).
public sealed class GizmoBrushRecompileTests
{
    private const float AlongAxis = 0.8f;

    private static readonly Vector3 Start = new(16f, 16f, 16f);

    [Fact]
    public void A_gizmo_drag_lands_a_brush_on_exactly_the_scripted_position()
    {
        (GizmoHarness scripted, SceneNode scriptedNode, CompilingRenderer scriptedRenderer) = BrushScene();
        scripted.Scene.RebuildStaticWorld(scriptedRenderer);
        scriptedNode.LocalPosition = new Vector3(48f, 16f, 16f);

        (GizmoHarness dragged, SceneNode draggedNode, CompilingRenderer draggedRenderer) = BrushScene();
        dragged.Scene.RebuildStaticWorld(draggedRenderer);
        DragBrushToX(dragged, 48f);

        // Equal, not close: a snapped axis drag lands on an exact grid multiple.
        draggedNode.LocalPosition.ShouldBe(scriptedNode.LocalPosition);
    }

    [Fact]
    public void A_gizmo_drag_across_a_chunk_border_dirties_the_same_cells_as_a_scripted_move()
    {
        var target = new Vector3(48f, 16f, 16f);

        IReadOnlyList<ChunkCoord> scripted = ScriptedMoveDirtyCells(target);
        IReadOnlyList<ChunkCoord> dragged = DraggedMoveDirtyCells(48f);

        scripted.ShouldBe(new[] { new ChunkCoord(0, 0, 0), new ChunkCoord(1, 0, 0) });
        dragged.ShouldBe(scripted);
    }

    [Fact]
    public void A_gizmo_drag_within_a_chunk_dirties_only_that_cell()
    {
        IReadOnlyList<ChunkCoord> scripted = ScriptedMoveDirtyCells(new Vector3(20f, 16f, 16f));
        IReadOnlyList<ChunkCoord> dragged = DraggedMoveDirtyCells(20f);

        scripted.ShouldBe(new[] { new ChunkCoord(0, 0, 0) });
        dragged.ShouldBe(scripted);
    }

    [Fact]
    public void A_cancelled_drag_leaves_the_static_world_clean()
    {
        (GizmoHarness harness, SceneNode node, CompilingRenderer renderer) = BrushScene();
        harness.Scene.RebuildStaticWorld(renderer);
        int compilesAfterLoad = harness.Scene.StaticWorldCompileCount;

        float length = harness.GeometryAt(Start).AxisLength;
        harness.Grab(Start + Vector3.UnitX * (length * AlongAxis));
        harness.DragBy(Vector3.UnitX * 32f);
        harness.Scene.StaticWorldDirty.ShouldBeTrue();

        harness.PressEscape();

        node.LocalPosition.ShouldBe(Start);
        harness.Scene.RebuildStaticWorld(renderer);
        harness.Scene.LastCompileDirtyCells.ShouldBeEmpty();
        harness.Scene.StaticWorldCompileCount.ShouldBe(compilesAfterLoad + 1);
    }

    [Fact]
    public void Every_frame_of_a_drag_dirties_the_scene_so_the_recompile_can_keep_up()
    {
        (GizmoHarness harness, SceneNode node, CompilingRenderer renderer) = BrushScene();
        harness.Scene.RebuildStaticWorld(renderer);

        float length = harness.GeometryAt(Start).AxisLength;
        harness.Grab(Start + Vector3.UnitX * (length * AlongAxis));

        for (int frame = 1; frame <= 4; frame++)
        {
            harness.Scene.RebuildStaticWorld(renderer); // clears the dirty flag
            harness.Scene.StaticWorldDirty.ShouldBeFalse();

            harness.DragBy(Vector3.UnitX * frame);
            harness.Scene.StaticWorldDirty.ShouldBeTrue();
        }

        harness.Release();
        node.LocalPosition.X.ShouldBe(20f, 1e-3f);
    }

    private static IReadOnlyList<ChunkCoord> ScriptedMoveDirtyCells(Vector3 target)
    {
        (GizmoHarness harness, SceneNode node, CompilingRenderer renderer) = BrushScene();
        harness.Scene.RebuildStaticWorld(renderer);

        node.LocalPosition = target;
        harness.Scene.RebuildStaticWorld(renderer);

        return harness.Scene.LastCompileDirtyCells;
    }

    private static IReadOnlyList<ChunkCoord> DraggedMoveDirtyCells(float targetX)
    {
        (GizmoHarness harness, SceneNode _, CompilingRenderer renderer) = BrushScene();
        harness.Scene.RebuildStaticWorld(renderer);

        DragBrushToX(harness, targetX);
        harness.Scene.RebuildStaticWorld(renderer);

        return harness.Scene.LastCompileDirtyCells;
    }

    // Snap stays on its default one-unit grid so the landing is an exact float.
    private static void DragBrushToX(GizmoHarness harness, float targetX)
    {
        float length = harness.GeometryAt(Start).AxisLength;
        Vector3 grabAt = Start + Vector3.UnitX * (length * AlongAxis);

        harness.Grab(grabAt).ShouldBe(GizmoUpdateResult.DragBegan);
        harness.Gizmo.ActiveHandle.ShouldBe(GizmoHandle.AxisX);

        // A hair off the target so the snap has something to do.
        harness.DragBy(Vector3.UnitX * (targetX - Start.X + 0.21f));
        harness.Release().ShouldBe(GizmoUpdateResult.DragCommitted);
    }

    // The far brush is there to show the dirty-cell diff is per node.
    private static (GizmoHarness Harness, SceneNode Node, CompilingRenderer Renderer) BrushScene()
    {
        // Off-axis so the three arrows are separated on screen.
        var harness = new GizmoHarness(Start + new Vector3(12f, 9f, 15f), Start);
        var renderer = new CompilingRenderer();

        SceneNode node = harness.Scene.Root.CreateChild("Brush");
        node.LocalPosition = Start;
        node.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        harness.Scene.Selection.Add(node);

        SceneNode far = harness.Scene.Root.CreateChild("Far");
        far.LocalPosition = new Vector3(80f, 16f, 16f); // cell (2,0,0)
        far.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));

        return (harness, node, renderer);
    }
}
