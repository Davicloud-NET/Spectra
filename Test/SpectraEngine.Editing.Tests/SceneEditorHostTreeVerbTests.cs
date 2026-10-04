using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Hosting;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// The verbs a scene tree and a context menu drive: selection by id, rename,
/// reparent, and pick/insert at a viewport point.
/// </summary>
public sealed class SceneEditorHostTreeVerbTests
{
    private static SceneEditorHost NewHost(Scene scene)
    {
        var renderer = new CompilingRenderer();
        renderer.SetFramebufferSize(new Vector2D<int>(1280, 720));

        return new SceneEditorHost(
            NullLoggerFactory.Instance,
            scene,
            renderer,
            new InputManager(NullLogger<InputManager>.Instance));
    }

    [Fact]
    public void Selecting_a_set_of_ids_raises_one_selection_change()
    {
        // The property panel rebuilds on every change event.
        var scene = new Scene("Editor");
        SceneNode a = scene.Root.CreateChild("A");
        SceneNode b = scene.Root.CreateChild("B");
        SceneNode c = scene.Root.CreateChild("C");
        SceneEditorHost host = NewHost(scene);

        int raised = 0;
        scene.Selection.SelectionChanged += () => raised++;

        host.SelectByIds([a.Id, b.Id, c.Id]);

        scene.Selection.Count.ShouldBe(3);
        raised.ShouldBe(1);
    }

    [Fact]
    public void Unknown_ids_are_skipped_and_the_known_ones_still_select()
    {
        var scene = new Scene("Editor");
        SceneNode a = scene.Root.CreateChild("A");
        SceneEditorHost host = NewHost(scene);

        host.SelectByIds([Guid.NewGuid(), a.Id, Guid.NewGuid()]);

        scene.Selection.Count.ShouldBe(1);
        scene.Selection.Items[0].ShouldBeSameAs(a);
    }

    [Fact]
    public void Replacing_with_nothing_resolvable_clears_the_selection()
    {
        var scene = new Scene("Editor");
        SceneNode a = scene.Root.CreateChild("A");
        SceneEditorHost host = NewHost(scene);
        host.SelectById(a.Id);

        host.SelectByIds([Guid.NewGuid()]);

        scene.Selection.Count.ShouldBe(0);
    }

    [Fact]
    public void Add_and_toggle_modes_extend_and_flip()
    {
        var scene = new Scene("Editor");
        SceneNode a = scene.Root.CreateChild("A");
        SceneNode b = scene.Root.CreateChild("B");
        SceneEditorHost host = NewHost(scene);

        host.SelectByIds([a.Id]);
        host.SelectByIds([b.Id], SelectionUpdate.Add);
        scene.Selection.Count.ShouldBe(2);

        host.SelectByIds([a.Id], SelectionUpdate.Toggle);
        scene.Selection.Count.ShouldBe(1);
        scene.Selection.Items[0].ShouldBeSameAs(b);
    }

    [Fact]
    public void Rename_is_one_history_entry_and_undo_restores_the_old_name()
    {
        var scene = new Scene("Editor");
        SceneNode node = scene.Root.CreateChild("Old");
        SceneEditorHost host = NewHost(scene);
        int depthBefore = host.UndoDepth;

        host.RenameById(node.Id, "New").ShouldBeTrue();

        node.Name.ShouldBe("New");
        host.UndoDepth.ShouldBe(depthBefore + 1);

        host.Apply(EditorHostCommand.Undo);
        node.Name.ShouldBe("Old");
    }

    [Fact]
    public void Rename_trims_and_refuses_empty_unchanged_and_unknown()
    {
        var scene = new Scene("Editor");
        SceneNode node = scene.Root.CreateChild("Kept");
        SceneEditorHost host = NewHost(scene);
        int depthBefore = host.UndoDepth;

        host.RenameById(node.Id, "   ").ShouldBeFalse();
        host.RenameById(node.Id, "Kept").ShouldBeFalse();
        host.RenameById(Guid.NewGuid(), "Anything").ShouldBeFalse();
        host.RenameById(node.Id, "  Spaced  ").ShouldBeTrue();

        node.Name.ShouldBe("Spaced");
        host.UndoDepth.ShouldBe(depthBefore + 1);
    }

    [Fact]
    public void Reparent_moves_under_the_new_parent_and_nothing_appears_to_move()
    {
        var scene = new Scene("Editor");
        SceneNode target = scene.Root.CreateChild("Target");
        target.LocalPosition = new Vector3(2f, 0f, 0f);
        SceneNode node = scene.Root.CreateChild("Mover");
        node.LocalPosition = new Vector3(5f, 1f, 0f);
        SceneEditorHost host = NewHost(scene);

        host.ReparentByIds([node.Id], target.Id, -1);

        node.Parent.ShouldBeSameAs(target);
        node.WorldMatrix.Translation.X.ShouldBe(5f, 0.0001f, "the world position is preserved");
        node.WorldMatrix.Translation.Y.ShouldBe(1f, 0.0001f);
        node.LocalPosition.X.ShouldBe(3f, 0.0001f, "the local transform was re-expressed under the new parent");
    }

    [Fact]
    public void Undo_of_a_reparent_restores_placement_and_transform()
    {
        var scene = new Scene("Editor");
        SceneNode target = scene.Root.CreateChild("Target");
        target.LocalPosition = new Vector3(2f, 0f, 0f);
        scene.Root.CreateChild("Spacer");
        SceneNode node = scene.Root.CreateChild("Mover");
        node.LocalPosition = new Vector3(5f, 0f, 0f);
        SceneEditorHost host = NewHost(scene);
        int index = node.IndexInParent;

        host.ReparentByIds([node.Id], target.Id, -1);
        host.Apply(EditorHostCommand.Undo);

        node.Parent.ShouldBeSameAs(scene.Root);
        node.IndexInParent.ShouldBe(index, "sibling index is traversal order is placement order");
        // No tolerance: the command carries absolute values.
        node.LocalPosition.ShouldBe(new Vector3(5f, 0f, 0f));
    }

    [Fact]
    public void Dropping_a_node_onto_its_own_descendant_is_refused_not_thrown()
    {
        // InsertChild throws on a cycle, which would be mid-transaction.
        var scene = new Scene("Editor");
        SceneNode parent = scene.Root.CreateChild("Parent");
        SceneNode child = parent.CreateChild("Child");
        SceneEditorHost host = NewHost(scene);
        int depthBefore = host.UndoDepth;

        Should.NotThrow(() => host.ReparentByIds([parent.Id], child.Id, -1));

        parent.Parent.ShouldBeSameAs(scene.Root);
        host.UndoDepth.ShouldBe(depthBefore, "a refused drop records nothing");
    }

    [Fact]
    public void A_mixed_drag_still_moves_the_legal_nodes()
    {
        var scene = new Scene("Editor");
        SceneNode parent = scene.Root.CreateChild("Parent");
        SceneNode child = parent.CreateChild("Child");
        SceneNode free = scene.Root.CreateChild("Free");
        SceneEditorHost host = NewHost(scene);

        // Parent cannot move under its own child; Free can.
        host.ReparentByIds([parent.Id, free.Id], child.Id, -1);

        parent.Parent.ShouldBeSameAs(scene.Root);
        free.Parent.ShouldBeSameAs(child);
    }

    [Fact]
    public void Moving_a_node_later_under_its_own_parent_lands_where_the_drop_pointed()
    {
        // "Drop A below B" is index 2 in the list as shown. A vacates slot 0
        // first, so the index has to be adjusted.
        var scene = new Scene("Editor");
        SceneNode a = scene.Root.CreateChild("A");
        SceneNode b = scene.Root.CreateChild("B");
        SceneNode c = scene.Root.CreateChild("C");
        SceneEditorHost host = NewHost(scene);

        host.ReparentByIds([a.Id], scene.Root.Id, 2);

        scene.Root.Children[0].ShouldBeSameAs(b);
        scene.Root.Children[1].ShouldBeSameAs(a);
        scene.Root.Children[2].ShouldBeSameAs(c);
    }

    // Several siblings moving within one parent. Sibling order is the static
    // world's placement order, so a wrong order compiles a different level.

    private static string Order(SceneNode parent)
    {
        var names = new List<string>(parent.Children.Count);
        foreach (SceneNode child in parent.Children)
            names.Add(child.Name);

        return string.Join(",", names);
    }

    private static (Scene Scene, SceneEditorHost Host, SceneNode[] Nodes) FiveSiblings()
    {
        var scene = new Scene("Editor");
        SceneNode[] nodes =
        [
            scene.Root.CreateChild("A"),
            scene.Root.CreateChild("B"),
            scene.Root.CreateChild("C"),
            scene.Root.CreateChild("D"),
            scene.Root.CreateChild("E"),
        ];

        return (scene, NewHost(scene), nodes);
    }

    [Fact]
    public void Two_siblings_dropped_after_a_later_row_land_together_where_the_drop_pointed()
    {
        // Drag A and B onto D's After edge (index 4). Both vacate slots above
        // the target, so the block lands at 2. One at a time gives C,A,D,B,E.
        (Scene scene, SceneEditorHost host, SceneNode[] nodes) = FiveSiblings();

        host.ReparentByIds([nodes[0].Id, nodes[1].Id], scene.Root.Id, 4);

        Order(scene.Root).ShouldBe("C,D,A,B,E");
    }

    [Fact]
    public void Undo_of_a_multi_node_sibling_move_restores_the_authored_order_exactly()
    {
        (Scene scene, SceneEditorHost host, SceneNode[] nodes) = FiveSiblings();

        host.ReparentByIds([nodes[1].Id, nodes[3].Id], scene.Root.Id, 0);
        Order(scene.Root).ShouldBe("B,D,A,C,E");

        host.Apply(EditorHostCommand.Undo);
        Order(scene.Root).ShouldBe("A,B,C,D,E");

        host.Apply(EditorHostCommand.Redo);
        Order(scene.Root).ShouldBe("B,D,A,C,E");
        host.Apply(EditorHostCommand.Undo);
        Order(scene.Root).ShouldBe("A,B,C,D,E");
    }

    [Fact]
    public void A_multi_node_drop_reads_the_same_whichever_row_was_ctrl_clicked_first()
    {
        // The ids arrive in selection order.
        (Scene first, SceneEditorHost firstHost, SceneNode[] a) = FiveSiblings();
        firstHost.ReparentByIds([a[0].Id, a[1].Id], first.Root.Id, 3);

        (Scene second, SceneEditorHost secondHost, SceneNode[] b) = FiveSiblings();
        secondHost.ReparentByIds([b[1].Id, b[0].Id], second.Root.Id, 3);

        Order(second.Root).ShouldBe(Order(first.Root));
    }

    [Fact]
    public void Dropping_a_row_onto_its_own_edge_records_nothing()
    {
        // A drop onto a row's own Before zone changes nothing.
        (Scene scene, SceneEditorHost host, SceneNode[] nodes) = FiveSiblings();
        int depthBefore = host.UndoDepth;

        host.ReparentByIds([nodes[2].Id], scene.Root.Id, 2);

        host.UndoDepth.ShouldBe(depthBefore);
        Order(scene.Root).ShouldBe("A,B,C,D,E");
    }

    [Fact]
    public void Moving_several_children_into_a_group_keeps_their_relative_order()
    {
        (Scene scene, SceneEditorHost host, SceneNode[] nodes) = FiveSiblings();
        SceneNode group = scene.Root.CreateChild("Group");
        group.CreateChild("Existing");

        host.ReparentByIds([nodes[0].Id, nodes[2].Id, nodes[4].Id], group.Id, -1);

        Order(group).ShouldBe("Existing,A,C,E");
        Order(scene.Root).ShouldBe("B,D,Group");
    }

    [Fact]
    public void Editing_verbs_refuse_while_play_mode_owns_the_scene()
    {
        // The shell gates on a stale snapshot, so the host has to refuse too.
        var scene = new Scene("Editor");
        SceneNode node = scene.Root.CreateChild("Kept");
        SceneEditorHost host = NewHost(scene);
        host.SelectById(node.Id);

        host.Suspend();
        host.IsSuspended.ShouldBeTrue();

        host.Apply(EditorHostCommand.Delete);
        host.Insert(InsertKind.WorldBrush);
        host.RenameById(node.Id, "Renamed").ShouldBeFalse();
        host.ReparentByIds([node.Id], scene.Root.Id, 0);

        scene.Root.Children.Count.ShouldBe(1, "nothing was added or deleted");
        node.Name.ShouldBe("Kept");
        host.UndoDepth.ShouldBe(0, "a refused verb records no history");

        host.Resume();
        host.IsSuspended.ShouldBeFalse();
        host.RenameById(node.Id, "Renamed").ShouldBeTrue();
    }

    [Fact]
    public void Append_lands_at_the_end_and_the_dragged_nodes_stay_selected()
    {
        var scene = new Scene("Editor");
        SceneNode group = scene.Root.CreateChild("Group");
        group.CreateChild("Existing");
        SceneNode node = scene.Root.CreateChild("Dragged");
        SceneEditorHost host = NewHost(scene);

        host.ReparentByIds([node.Id], group.Id, -1);

        group.Children[^1].ShouldBeSameAs(node);
        scene.Selection.Count.ShouldBe(1);
        scene.Selection.Items[0].ShouldBeSameAs(node, "a drop you cannot immediately act on is not a drop");
    }

    [Fact]
    public void Right_click_selection_retargets_to_an_unselected_hit_and_keeps_a_selected_one()
    {
        var scene = new Scene("Editor");
        SceneNode plate = scene.Root.CreateChild("Plate");
        plate.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(
            new Vector3(-8f, -1f, -8f), new Vector3(8f, 1f, 8f));
        SceneNode other = scene.Root.CreateChild("Other");
        scene.Camera.Position = new Vector3(0.5f, 8f, 4f);
        scene.Camera.LookAt(new Vector3(0.5f, 1f, 0.5f));
        SceneEditorHost host = NewHost(scene);
        var centre = new Vector2(640f, 360f);

        host.SelectAtPoint(centre);
        scene.Selection.Items[0].ShouldBeSameAs(plate, "an unselected hit becomes the selection");

        // The hit is already selected, so the whole set is kept for the menu.
        host.SelectByIds([plate.Id, other.Id]);
        host.SelectAtPoint(centre);
        scene.Selection.Count.ShouldBe(2);
    }

    [Fact]
    public void Right_click_on_empty_space_keeps_the_selection()
    {
        var scene = new Scene("Editor");
        SceneNode node = scene.Root.CreateChild("Kept");
        scene.Camera.Position = new Vector3(0f, 5f, 0f);
        scene.Camera.LookAt(new Vector3(0f, 5f, -10f));
        SceneEditorHost host = NewHost(scene);
        host.SelectById(node.Id);

        host.SelectAtPoint(new Vector2(640f, 360f));

        scene.Selection.Count.ShouldBe(1);
    }

    [Fact]
    public void An_insert_aimed_at_a_point_lands_under_that_point_not_the_view_centre()
    {
        var scene = new Scene("Editor");
        SceneNode plate = scene.Root.CreateChild("Plate");
        plate.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(
            new Vector3(-16f, -1f, -16f), new Vector3(16f, 1f, 16f));
        scene.Camera.Position = new Vector3(0.5f, 8f, 4f);
        scene.Camera.LookAt(new Vector3(0.5f, 1f, 0.5f));
        SceneEditorHost host = NewHost(scene);

        // Snap off, or both inserts could land on the same grid point.
        host.Apply(GizmoCommand.DisableSnap);

        host.Insert(InsertKind.WorldBrush);
        SceneNode centred = scene.Root.Children[^1];
        host.Apply(EditorHostCommand.Undo);

        host.Insert(InsertKind.WorldBrush, new Vector2(320f, 360f));
        SceneNode aimed = scene.Root.Children[^1];

        aimed.LocalPosition.X.ShouldBeLessThan(
            centred.LocalPosition.X, "a point left of centre lands left of the centre insert");
        aimed.LocalPosition.Y.ShouldBe(2f, 0.001f, "it still rests flush on the aimed surface");
    }
}
