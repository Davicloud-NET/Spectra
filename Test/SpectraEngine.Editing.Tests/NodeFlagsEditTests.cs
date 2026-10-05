using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editing.Undo;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Writing a node's collide, query, touch and drawn flags: the command and the
/// property editor path over it.
/// </summary>
public sealed class NodeFlagsEditTests
{
    private static Brush Box() => Brush.CreateBox(new Vector3(-1f), new Vector3(1f), default);

    private static SceneNode Part(string name) =>
        new(name) { BrushKind = BrushKind.Part, Brush = Box() };

    private static SceneNode Wall(string name) => new(name) { Brush = Box() };

    private sealed class Rig
    {
        public Rig(params SceneNode[] nodes)
        {
            Scene = new Scene("flags");
            foreach (SceneNode node in nodes)
                Scene.Root.AddChild(node);

            Undo = new UndoStack(Scene);
            Nodes = nodes;
        }

        public Scene Scene { get; }
        public UndoStack Undo { get; }
        public IReadOnlyList<SceneNode> Nodes { get; }

        public int Apply(PropertyId id, bool flag, bool inGesture = false) =>
            PropertyEditor.Apply(Undo, Nodes, new PropertyEdit { Id = id, Flag = flag }, inGesture);
    }

    [Fact]
    public void A_cleared_flag_is_written_and_undo_and_redo_walk_it_both_ways()
    {
        SceneNode node = Part("door");
        var rig = new Rig(node);

        rig.Apply(PropertyId.CanCollide, false).ShouldBe(1);
        node.CanCollide.ShouldBeFalse();

        rig.Undo.Undo().ShouldBeTrue();
        node.CanCollide.ShouldBeTrue();

        rig.Undo.Redo().ShouldBeTrue();
        node.CanCollide.ShouldBeFalse();
    }

    [Theory]
    [InlineData(PropertyId.CanCollide, PhysicsFlags.CanCollide)]
    [InlineData(PropertyId.CanQuery, PhysicsFlags.CanQuery)]
    [InlineData(PropertyId.CanTouch, PhysicsFlags.CanTouch)]
    public void Each_row_writes_its_own_bit_and_no_other(PropertyId id, PhysicsFlags bit)
    {
        SceneNode node = Part("volume");
        var rig = new Rig(node);

        rig.Apply(id, false).ShouldBe(1);

        node.PhysicsFlags.ShouldBe(PhysicsFlags.Default & ~bit);
        node.IsRendered.ShouldBeTrue();
    }

    [Fact]
    public void Hiding_a_part_lists_it_as_hidden_and_undo_draws_it_again()
    {
        SceneNode node = Part("trigger");
        var rig = new Rig(node);

        rig.Apply(PropertyId.IsRendered, false).ShouldBe(1);
        node.IsRendered.ShouldBeFalse();
        rig.Scene.HiddenBrushNodes.ShouldBe([node]);
        node.PhysicsFlags.ShouldBe(PhysicsFlags.Default, "Drawn is not a physics bit");

        rig.Undo.Undo().ShouldBeTrue();
        node.IsRendered.ShouldBeTrue();
        rig.Scene.HiddenBrushNodes.ShouldBeEmpty();
    }

    [Fact]
    public void Undo_restores_the_flags_the_node_had_rather_than_the_defaults()
    {
        SceneNode node = Part("clip");
        node.CanQuery = false;
        node.Anchored = false;
        node.IsRendered = false;
        PhysicsFlags authored = node.PhysicsFlags;
        var rig = new Rig(node);

        rig.Apply(PropertyId.CanCollide, false).ShouldBe(1);
        rig.Undo.Undo().ShouldBeTrue();

        node.PhysicsFlags.ShouldBe(authored);
        node.IsRendered.ShouldBeFalse();
    }

    [Fact]
    public void One_click_over_many_nodes_is_one_undo_entry()
    {
        SceneNode a = Part("a");
        SceneNode b = Part("b");
        SceneNode c = Wall("c");
        var rig = new Rig(a, b, c);

        rig.Apply(PropertyId.CanTouch, false).ShouldBe(3);
        rig.Undo.UndoCount.ShouldBe(1);

        rig.Undo.Undo().ShouldBeTrue();
        a.CanTouch.ShouldBeTrue();
        b.CanTouch.ShouldBeTrue();
        c.CanTouch.ShouldBeTrue();
    }

    [Fact]
    public void Edits_inside_one_gesture_are_one_undo_entry()
    {
        SceneNode node = Part("trigger");
        var rig = new Rig(node);

        rig.Undo.BeginTransaction("Behavior");
        rig.Apply(PropertyId.CanCollide, false, inGesture: true).ShouldBe(1);
        rig.Apply(PropertyId.IsRendered, false, inGesture: true).ShouldBe(1);
        rig.Undo.CommitTransaction();

        rig.Undo.UndoCount.ShouldBe(1);

        rig.Undo.Undo().ShouldBeTrue();
        node.CanCollide.ShouldBeTrue();
        node.IsRendered.ShouldBeTrue();
    }

    [Fact]
    public void A_click_that_changes_nothing_records_nothing()
    {
        var rig = new Rig(Part("door"));

        rig.Apply(PropertyId.CanCollide, true).ShouldBe(0);
        rig.Apply(PropertyId.IsRendered, true).ShouldBe(0);

        rig.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Only_the_nodes_that_differ_record_a_command()
    {
        SceneNode solid = Part("solid");
        SceneNode ghost = Part("ghost");
        ghost.CanCollide = false;
        var rig = new Rig(solid, ghost);

        rig.Apply(PropertyId.CanCollide, false).ShouldBe(1, "the ghost already holds the value");
    }

    [Fact]
    public void Drawn_reaches_only_the_nodes_that_draw_on_their_own()
    {
        // The static world draws a world brush whatever its node says.
        SceneNode wall = Wall("wall");
        SceneNode part = Part("part");
        var rig = new Rig(wall, part);

        rig.Apply(PropertyId.IsRendered, false).ShouldBe(1);

        part.IsRendered.ShouldBeFalse();
        wall.IsRendered.ShouldBeTrue();
    }

    [Fact]
    public void A_node_with_no_geometry_is_left_alone()
    {
        var group = new SceneNode("group");
        var lamp = new SceneNode("lamp") { Light = new Light() };
        var rig = new Rig(group, lamp);

        rig.Apply(PropertyId.CanCollide, false).ShouldBe(0);
        rig.Apply(PropertyId.IsRendered, false).ShouldBe(0);

        group.PhysicsFlags.ShouldBe(PhysicsFlags.Default);
        lamp.IsRendered.ShouldBeTrue();
        rig.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void The_physics_layers_body_bit_is_never_written()
    {
        SceneNode node = Part("crate");
        var rig = new Rig(node);

        rig.Apply(PropertyId.CanCollide, false).ShouldBe(1);

        // A body made after the edit must survive its undo and its redo.
        node.PhysicsFlags |= PhysicsFlags.HasBody;

        rig.Undo.Undo().ShouldBeTrue();
        node.PhysicsFlags.ShouldBe(PhysicsFlags.Default | PhysicsFlags.HasBody);

        rig.Undo.Redo().ShouldBeTrue();
        node.PhysicsFlags.ShouldBe((PhysicsFlags.Default & ~PhysicsFlags.CanCollide) | PhysicsFlags.HasBody);
    }

    [Fact]
    public void Cancelling_restores_a_node_that_left_the_scene_during_the_gesture()
    {
        SceneNode node = Part("trigger");
        var rig = new Rig(node);
        var hidden = new SetNodeFlagsCommand.NodeFlags(PhysicsFlags.CanTouch | PhysicsFlags.Anchored, false);

        rig.Undo.BeginTransaction("Behavior");
        rig.Undo.Execute(SetNodeFlagsCommand.Capture(node, hidden));
        node.CanCollide.ShouldBeFalse();
        node.IsRendered.ShouldBeFalse();

        rig.Scene.Root.RemoveChild(node);
        rig.Undo.CancelTransaction();

        node.PhysicsFlags.ShouldBe(PhysicsFlags.Default);
        node.IsRendered.ShouldBeTrue();
        rig.Undo.Count.ShouldBe(0);
    }

    [Fact]
    public void An_ordinary_undo_ignores_a_node_that_is_not_in_the_scene()
    {
        SceneNode node = Part("trigger");
        var rig = new Rig(node);
        rig.Apply(PropertyId.IsRendered, false).ShouldBe(1);

        rig.Scene.Root.RemoveChild(node);
        rig.Undo.Undo().ShouldBeTrue();

        node.IsRendered.ShouldBeFalse();
    }

    [Fact]
    public void An_edit_through_the_editor_reaches_the_selection_as_one_undo_entry()
    {
        var scene = new Scene("Editor");
        SceneNode part = Part("trigger");
        scene.Root.AddChild(part);
        var renderer = new CompilingRenderer();
        renderer.SetFramebufferSize(new Vector2D<int>(1280, 720));
        var host = new SceneEditorHost(
            NullLoggerFactory.Instance, scene, renderer,
            new InputManager(NullLogger<InputManager>.Instance));
        scene.Selection.Select(part);

        host.ApplyProperty(new PropertyEdit { Id = PropertyId.IsRendered, Flag = false }).ShouldBe(1);

        part.IsRendered.ShouldBeFalse();
        host.UndoDepth.ShouldBe(1);

        host.Apply(EditorHostCommand.Undo);
        part.IsRendered.ShouldBeTrue();
    }

    [Fact]
    public void A_missing_target_is_a_no_op_rather_than_a_throw()
    {
        // History behind an undone delete names absent nodes.
        var scene = new Scene("flags");
        var command = new SetNodeFlagsCommand(
            Guid.NewGuid(),
            new SetNodeFlagsCommand.NodeFlags(PhysicsFlags.Default, true),
            new SetNodeFlagsCommand.NodeFlags(PhysicsFlags.None, false));

        Should.NotThrow(() => command.Do(scene));
        Should.NotThrow(() => command.Undo(scene));
        Should.NotThrow(() => command.RollBack(scene));
    }

    [Fact]
    public void The_command_follows_a_node_rebuilt_under_the_same_id()
    {
        // Undo of a delete recreates the node, so the id is all that lasts.
        SceneNode node = Part("trigger");
        var rig = new Rig(node);
        rig.Apply(PropertyId.IsRendered, false).ShouldBe(1);

        rig.Scene.Root.RemoveChild(node);
        var rebuilt = new SceneNode("trigger", node.Id)
        {
            BrushKind = BrushKind.Part,
            Brush = Box(),
            IsRendered = false,
        };
        rig.Scene.Root.AddChild(rebuilt);

        rig.Undo.Undo().ShouldBeTrue();

        rebuilt.IsRendered.ShouldBeTrue();
    }
}
