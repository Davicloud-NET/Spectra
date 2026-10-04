using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Hosting;
using System;
using System.Linq;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// The editor host's command surface: the verbs a toolbar posts, and the state
/// it reads back.
/// </summary>
public sealed class SceneEditorHostCommandTests
{
    private static SceneEditorHost NewHost(Scene scene)
    {
        var renderer = new CompilingRenderer();

        // The host reads the viewport size in its constructor; zero breaks picking.
        renderer.SetFramebufferSize(new Vector2D<int>(1280, 720));

        return new SceneEditorHost(
            NullLoggerFactory.Instance,
            scene,
            renderer,
            new InputManager(NullLogger<InputManager>.Instance));
    }

    private static SceneNode AddChild(Scene scene, string name)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.LocalPosition = new Vector3(1f, 0f, 0f);
        return node;
    }

    [Fact]
    public void The_tool_and_its_handle_style_are_reported_separately()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.GizmoModeName.ShouldBe("move");
        host.GizmoStyleName.ShouldBe("Studio");

        host.Apply(GizmoCommand.UseRotate);
        host.GizmoModeName.ShouldBe("rotate");

        host.Apply(GizmoCommand.ToggleStyle);
        host.GizmoStyleName.ShouldBe("Classic");
        host.GizmoModeName.ShouldBe("rotate", "the style does not change the tool");
    }

    [Fact]
    public void The_labels_are_interned_constants_rather_than_formatted_strings()
    {
        // Read every frame by the stats line, so they must not allocate.
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        ReferenceEquals(host.GizmoModeName, host.GizmoModeName).ShouldBeTrue();
        ReferenceEquals(host.GizmoStyleName, host.GizmoStyleName).ShouldBeTrue();
        ReferenceEquals(host.GizmoOrientationName, host.GizmoOrientationName).ShouldBeTrue();
        ReferenceEquals(host.NavigationModeName, host.NavigationModeName).ShouldBeTrue();
    }

    [Fact]
    public void The_orientation_label_follows_the_frame_toggle()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.GizmoOrientationName.ShouldBe("world");
        host.Apply(GizmoCommand.ToggleOrientation);
        host.GizmoOrientationName.ShouldBe("local");
    }

    [Fact]
    public void The_snap_increment_is_the_live_tools_own_unit()
    {
        // World units under move, degrees under rotate.
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.SnapEnabled.ShouldBeTrue();
        float moveIncrement = host.SnapIncrement;

        host.Apply(GizmoCommand.UseRotate);
        host.SnapIncrement.ShouldNotBe(moveIncrement, "degrees are not world units");
        host.SnapIncrement.ShouldBe(15f);
    }

    [Fact]
    public void Toggling_snap_reports_through()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.Apply(GizmoCommand.ToggleSnap);
        host.SnapEnabled.ShouldBeFalse();

        host.Apply(GizmoCommand.ToggleSnap);
        host.SnapEnabled.ShouldBeTrue();
    }

    // Set verbs, not toggles: a toggle sent against a stale snapshot flips
    // the wrong way.
    [Fact]
    public void An_idempotent_verb_names_a_state_and_reports_whether_it_changed()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.Apply(GizmoCommand.UseLocalOrientation).ShouldBeTrue();
        host.GizmoOrientationName.ShouldBe("local");

        host.Apply(GizmoCommand.UseLocalOrientation).ShouldBeFalse("already local");
        host.GizmoOrientationName.ShouldBe("local");

        host.Apply(GizmoCommand.UseStudioStyle).ShouldBeFalse("Studio is the default");
        host.Apply(GizmoCommand.UseClassicStyle).ShouldBeTrue();
        host.GizmoStyleName.ShouldBe("Classic");

        host.Apply(GizmoCommand.EnableSnap).ShouldBeFalse("snap starts on");
        host.Apply(GizmoCommand.DisableSnap).ShouldBeTrue();
        host.SnapEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Every_tools_increment_is_readable_without_switching_tools()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.MoveSnapIncrement.ShouldBe(1f);
        host.RotateSnapIncrement.ShouldBe(15f);
        host.ResizeSnapIncrement.ShouldBe(1f);
        host.GizmoModeName.ShouldBe("move", "reading them switched nothing");
    }

    [Fact]
    public void Setting_an_increment_targets_one_tool_and_leaves_the_others_alone()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.SetSnapIncrement(GizmoMode.Rotate, 22.5f);

        host.RotateSnapIncrement.ShouldBe(22.5f);
        host.MoveSnapIncrement.ShouldBe(1f);
        host.ResizeSnapIncrement.ShouldBe(1f);
    }

    [Fact]
    public void A_bad_increment_is_refused_before_anything_is_written()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        Should.NotThrow(() => host.SetSnapIncrement(GizmoMode.Translate, 0f));
        Should.NotThrow(() => host.SetSnapIncrement(GizmoMode.Translate, -2f));
        Should.NotThrow(() => host.SetSnapIncrement(GizmoMode.Translate, float.NaN));

        host.MoveSnapIncrement.ShouldBe(1f);
    }

    [Fact]
    public void Select_all_takes_the_top_level_not_the_whole_graph()
    {
        var scene = new Scene("Editor");
        SceneNode a = AddChild(scene, "A");
        a.CreateChild("Grandchild");
        SceneNode b = AddChild(scene, "B");

        SceneEditorHost host = NewHost(scene);
        host.Apply(EditorHostCommand.SelectAll);

        scene.Selection.Count.ShouldBe(2);
        scene.Selection.Items.ShouldContain(a);
        scene.Selection.Items.ShouldContain(b);

        host.Apply(EditorHostCommand.ClearSelection);
        scene.Selection.Count.ShouldBe(0);
    }

    [Fact]
    public void Insert_creates_selects_and_is_one_undo_entry()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.Insert(InsertKind.WorldBrush);

        scene.Root.Children.Count.ShouldBe(1);
        SceneNode node = scene.Root.Children[0];
        node.Name.ShouldBe("Brush");
        node.Brush.ShouldNotBeNull();
        node.BrushKind.ShouldBe(BrushKind.World);
        scene.Selection.Items.ShouldBe([node]);
        host.UndoDepth.ShouldBe(1);

        Guid id = node.Id;
        host.Apply(EditorHostCommand.Undo);
        scene.Root.Children.ShouldBeEmpty();

        host.Apply(EditorHostCommand.Redo);
        scene.Root.Children.Count.ShouldBe(1);
        scene.Root.Children[0].Id.ShouldBe(id);
    }

    [Fact]
    public void An_inserted_hole_is_subtractive_and_world_kind()
    {
        // A subtractive part carves nothing and draws nothing.
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.Insert(InsertKind.SubtractiveBrush);

        SceneNode node = scene.Root.Children[0];
        node.Brush.ShouldNotBeNull();
        node.Brush.Operation.ShouldBe(SpectraEngine.Core.Bsp.BrushOperation.Subtractive);
        node.BrushKind.ShouldBe(BrushKind.World);
    }

    [Fact]
    public void An_inserted_part_leaves_the_carve()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.Insert(InsertKind.PartBrush);

        scene.Root.Children[0].BrushKind.ShouldBe(BrushKind.Part);
    }

    [Fact]
    public void An_inserted_light_carries_a_valid_point_light()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.Insert(InsertKind.PointLight);

        SceneNode node = scene.Root.Children[0];
        node.Light.ShouldNotBeNull();
        node.Light.Kind.ShouldBe(LightKind.Point);
        node.Light.Intensity.ShouldBeGreaterThan(0f);
        node.Light.Range.ShouldBeGreaterThan(0f);
    }

    [Fact]
    public void A_brush_rests_on_the_aimed_surface_and_a_hole_bites_into_it()
    {
        // A hole resting flush on a surface only shares a plane with it and
        // carves nothing, so its centre goes on the surface.
        var scene = new Scene("Editor");
        SceneNode plate = scene.Root.CreateChild("Plate");
        plate.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(
            new System.Numerics.Vector3(-8f, -1f, -8f), new System.Numerics.Vector3(8f, 1f, 8f));

        // Look target is on the plate's top plane (y = 1).
        scene.Camera.Position = new System.Numerics.Vector3(0.5f, 8f, 4f);
        scene.Camera.LookAt(new System.Numerics.Vector3(0.5f, 1f, 0.5f));

        SceneEditorHost host = NewHost(scene);

        host.Insert(InsertKind.SubtractiveBrush);
        SceneNode hole = scene.Root.Children[^1];
        hole.LocalPosition.Y.ShouldBe(1f, 0.001f, "a hole starts half-buried in the surface");

        // Undo first: the placement ray sees every pickable node, so the
        // second insert would rest on the first.
        host.Apply(EditorHostCommand.Undo);

        host.Insert(InsertKind.WorldBrush);
        SceneNode brush = scene.Root.Children[^1];
        brush.LocalPosition.Y.ShouldBe(2f, 0.001f, "an additive brush rests flush on the surface");
    }

    [Fact]
    public void Inserts_land_on_the_move_grid_while_snap_is_on()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);
        host.SnapEnabled.ShouldBeTrue();

        host.Insert(InsertKind.WorldBrush);

        System.Numerics.Vector3 position = scene.Root.Children[0].LocalPosition;
        position.X.ShouldBe(MathF.Round(position.X));
        position.Y.ShouldBe(MathF.Round(position.Y));
        position.Z.ShouldBe(MathF.Round(position.Z));
    }

    [Fact]
    public void Inserting_an_entity_is_one_history_entry_and_leaves_it_selected()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.InsertEntity("logic_relay");

        scene.Root.Children.Count.ShouldBe(1);
        SceneNode node = scene.Root.Children[0];
        node.Entity.ShouldNotBeNull();
        node.Entity.ClassName.ShouldBe("logic_relay");
        scene.Selection.Items.ShouldBe([node]);
        host.UndoDepth.ShouldBe(1);

        Guid id = node.Id;
        host.Apply(EditorHostCommand.Undo);
        scene.Root.Children.ShouldBeEmpty();

        host.Apply(EditorHostCommand.Redo);
        scene.Root.Children[0].Id.ShouldBe(id);
        scene.Root.Children[0].Entity.ShouldNotBeNull();
    }

    [Fact]
    public void A_fresh_entity_carries_no_keyvalues_at_all()
    {
        // Defaults are omitted from the map, so a later change to a schema
        // default still reaches saved levels.
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.InsertEntity("logic_timer");

        scene.Root.Children[0].Entity!.Keyvalues.ShouldBeEmpty();
        scene.Root.Children[0].Entity!.Connections.ShouldBeEmpty();
    }

    [Fact]
    public void An_inserted_entity_is_named_after_its_class()
    {
        // The node name is the targetname. Duplicates are legal: firing at a
        // name fires every match.
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.InsertEntity("logic_relay");
        host.InsertEntity("logic_relay");

        scene.Root.Children.Select(n => n.Name).ShouldBe(["logic_relay", "logic_relay"]);
    }

    [Fact]
    public void An_entity_with_no_class_is_refused()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.InsertEntity("   ");

        scene.Root.Children.ShouldBeEmpty();
        host.UndoDepth.ShouldBe(0);
    }

    [Fact]
    public void Inserting_an_entity_is_refused_while_play_mode_owns_the_scene()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);
        host.Suspend();

        host.InsertEntity("logic_relay");

        scene.Root.Children.ShouldBeEmpty();
        host.UndoDepth.ShouldBe(0);

        host.Resume();
        host.InsertEntity("logic_relay");
        scene.Root.Children.Count.ShouldBe(1);
    }

    [Fact]
    public void Duplicate_copies_the_selection_and_undo_takes_the_copy_back()
    {
        var scene = new Scene("Editor");
        SceneNode original = AddChild(scene, "Original");
        SceneEditorHost host = NewHost(scene);
        scene.Selection.Select(original);

        host.Apply(EditorHostCommand.Duplicate);

        scene.Root.Children.Count.ShouldBe(2);
        host.UndoDepth.ShouldBe(1);

        host.Apply(EditorHostCommand.Undo);

        scene.Root.Children.Count.ShouldBe(1);
        host.UndoDepth.ShouldBe(0);
        host.RedoDepth.ShouldBe(1);
    }

    [Fact]
    public void Delete_removes_the_selection_and_redo_puts_it_back_under_the_same_id()
    {
        var scene = new Scene("Editor");
        SceneNode target = AddChild(scene, "Doomed");
        Guid id = target.Id;
        SceneEditorHost host = NewHost(scene);
        scene.Selection.Select(target);

        host.Apply(EditorHostCommand.Delete);
        scene.TryFindById(id, out _).ShouldBeFalse();

        host.Apply(EditorHostCommand.Undo);
        scene.TryFindById(id, out SceneNode? restored).ShouldBeTrue();
        restored!.Name.ShouldBe("Doomed");

        host.Apply(EditorHostCommand.Redo);
        scene.TryFindById(id, out _).ShouldBeFalse();
    }

    [Fact]
    public void Group_and_ungroup_are_one_history_entry_each()
    {
        var scene = new Scene("Editor");
        SceneNode a = AddChild(scene, "A");
        SceneNode b = AddChild(scene, "B");
        SceneEditorHost host = NewHost(scene);
        scene.Selection.SetRange([a, b]);

        host.Apply(EditorHostCommand.Group);
        host.UndoDepth.ShouldBe(1);
        scene.Root.Children.Count.ShouldBe(1, "both nodes moved under one new parent");

        host.Apply(EditorHostCommand.Ungroup);
        host.UndoDepth.ShouldBe(2);
        scene.Root.Children.Count.ShouldBe(2);
    }

    [Fact]
    public void A_verb_with_nothing_selected_is_a_no_op_rather_than_a_throw()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        Should.NotThrow(() => host.Apply(EditorHostCommand.Duplicate));
        Should.NotThrow(() => host.Apply(EditorHostCommand.Delete));
        Should.NotThrow(() => host.Apply(EditorHostCommand.Group));
        Should.NotThrow(() => host.Apply(EditorHostCommand.Undo));
        Should.NotThrow(() => host.Apply(EditorHostCommand.Redo));

        host.UndoDepth.ShouldBe(0);
    }

    [Fact]
    public void Selecting_by_id_replaces_the_selection()
    {
        var scene = new Scene("Editor");
        SceneNode a = AddChild(scene, "A");
        SceneNode b = AddChild(scene, "B");
        SceneEditorHost host = NewHost(scene);
        scene.Selection.Select(a);

        host.SelectById(b.Id);

        scene.Selection.Count.ShouldBe(1);
        scene.Selection.Contains(b).ShouldBeTrue();
        scene.Selection.Contains(a).ShouldBeFalse();
    }

    [Fact]
    public void Selecting_by_id_can_extend_and_toggle()
    {
        var scene = new Scene("Editor");
        SceneNode a = AddChild(scene, "A");
        SceneNode b = AddChild(scene, "B");
        SceneEditorHost host = NewHost(scene);

        host.SelectById(a.Id);
        host.SelectById(b.Id, SelectionUpdate.Add);
        scene.Selection.Count.ShouldBe(2);

        host.SelectById(b.Id, SelectionUpdate.Toggle);
        scene.Selection.Count.ShouldBe(1);
        scene.Selection.Contains(a).ShouldBeTrue();
    }

    [Fact]
    public void An_id_the_scene_no_longer_has_is_ordinary_rather_than_exceptional()
    {
        // A UI's view of the graph lags, so it can ask for a node just deleted.
        var scene = new Scene("Editor");
        SceneNode a = AddChild(scene, "A");
        SceneEditorHost host = NewHost(scene);
        scene.Selection.Select(a);

        Should.NotThrow(() => host.SelectById(Guid.NewGuid()));
        scene.Selection.Count.ShouldBe(0);

        scene.Selection.Select(a);
        host.SelectById(Guid.NewGuid(), SelectionUpdate.Add);
        scene.Selection.Count.ShouldBe(1);
    }

    [Fact]
    public void The_navigation_toggle_swaps_the_reported_camera()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        string first = host.NavigationModeName;
        host.Apply(EditorHostCommand.ToggleNavigation);
        host.NavigationModeName.ShouldNotBe(first);

        host.Apply(EditorHostCommand.ToggleNavigation);
        host.NavigationModeName.ShouldBe(first);
    }

    [Fact]
    public void Framing_an_empty_selection_does_not_move_the_camera()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);
        Vector3 before = scene.Camera.Position;

        host.Apply(EditorCameraCommand.FrameSelection);

        scene.Camera.Position.ShouldBe(before);
    }

    [Fact]
    public void The_interaction_state_names_are_interned_constants()
    {
        // Read once per snapshot, so it must not allocate.
        var scene = new Scene("Test");
        SceneEditorHost host = NewHost(scene);

        string first = host.InteractionStateName;
        string second = host.InteractionStateName;

        ReferenceEquals(first, second).ShouldBeTrue();
        first.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_suspended_editor_reports_suspended_whatever_else_is_true()
    {
        var scene = new Scene("Test");
        SceneEditorHost host = NewHost(scene);
        host.Suspend();

        host.InteractionStateName.ShouldBe("suspended");
    }

    [Fact]
    public void The_fly_camera_is_its_own_state()
    {
        var scene = new Scene("Test");
        SceneEditorHost host = NewHost(scene);
        host.Apply(EditorHostCommand.ToggleNavigation);

        host.InteractionStateName.ShouldBe("fly");
    }
}
