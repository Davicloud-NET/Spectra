using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editing.Undo;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Remove entity, and the command under it that puts entity data on a node
/// and takes it off.
/// </summary>
public sealed class EntityRemoveTests
{
    [Fact]
    public void Removing_the_entity_puts_the_flags_back_and_keeps_the_part()
    {
        var rig = new EntityEditRig();
        SceneNode block = rig.AddBlock("Doorway");
        rig.Make(EntityEditRig.Trigger, block);

        EntityEditReport report = rig.Remove(block);

        report.Applied.ShouldBeTrue(report.Message);
        report.Message.ShouldContain("stays a part");
        block.Entity.ShouldBeNull();
        block.BrushKind.ShouldBe(BrushKind.Part);
        block.PhysicsFlags.ShouldBe(PhysicsFlags.Default);
        block.IsRendered.ShouldBeTrue();
        rig.Host.UndoDepth.ShouldBe(2);
    }

    [Fact]
    public void Undoing_a_remove_brings_back_the_keyvalues_and_the_wires()
    {
        var rig = new EntityEditRig();
        SceneNode block = rig.AddBlock("Doorway");
        rig.Make(EntityEditRig.Trigger, block);
        block.Entity!.SetValue("wait", "2.5");
        block.Entity.Connections.Add(new EntityConnection("OnTrigger", "door1", "Open", "", 0f, 1));

        rig.Remove(block);
        rig.Host.Apply(EditorHostCommand.Undo);

        block.Entity.ShouldNotBeNull();
        block.Entity.ClassName.ShouldBe(EntityEditRig.Trigger);
        block.Entity.TryGetValue("wait", out string wait).ShouldBeTrue();
        wait.ShouldBe("2.5");
        block.Entity.Connections.Count.ShouldBe(1);
        block.IsRendered.ShouldBeFalse();
        block.CanCollide.ShouldBeFalse();
    }

    [Fact]
    public void Removing_from_a_group_resets_the_brushes_it_owned_and_no_others()
    {
        var rig = new EntityEditRig();
        SceneNode group = rig.Scene.Root.CreateChild("Zone");
        SceneNode owned = rig.AddBlock("Owned", group);
        SceneNode sensor = rig.AddBlock("Sensor", group);
        rig.Make(EntityEditRig.Trigger, sensor);
        rig.Make(EntityEditRig.Trigger, group);

        rig.Remove(group).Applied.ShouldBeTrue();

        owned.IsRendered.ShouldBeTrue();
        owned.CanCollide.ShouldBeTrue();
        sensor.IsRendered.ShouldBeFalse("it belongs to its own entity");
        sensor.Entity.ShouldNotBeNull();
    }

    [Fact]
    public void Remove_takes_every_selected_entity_in_one_entry_and_skips_the_rest()
    {
        var rig = new EntityEditRig();
        SceneNode a = rig.AddBlock("A");
        SceneNode b = rig.AddBlock("B");
        SceneNode plain = rig.AddBlock("Plain");
        rig.Make(EntityEditRig.Door, a);
        rig.Make(EntityEditRig.Door, b);
        int depth = rig.Host.UndoDepth;

        rig.Remove(a, plain, b).Applied.ShouldBeTrue();

        a.Entity.ShouldBeNull();
        b.Entity.ShouldBeNull();
        plain.BrushKind.ShouldBe(BrushKind.World);
        rig.Host.UndoDepth.ShouldBe(depth + 1);
    }

    [Fact]
    public void Remove_with_no_entity_selected_says_so_and_records_nothing()
    {
        var rig = new EntityEditRig();
        SceneNode block = rig.AddBlock("Block");

        EntityEditReport report = rig.Remove(block);

        report.Applied.ShouldBeFalse();
        report.Message.ShouldContain("Nothing selected is an entity");
        rig.Host.UndoDepth.ShouldBe(0);
    }

    [Fact]
    public void A_point_entity_can_be_removed_too()
    {
        var rig = new EntityEditRig();
        rig.Host.InsertEntity(EntityEditRig.Relay);
        SceneNode node = rig.Scene.Root.Children[0];

        rig.Remove(node).Applied.ShouldBeTrue();

        node.Entity.ShouldBeNull();
        rig.Scene.Root.Children.ShouldBe([node], "the node stays, for Delete to take");
    }

    [Fact]
    public void The_command_walks_both_ways_between_an_entity_and_none()
    {
        var scene = new Scene("entities");
        SceneNode node = scene.Root.CreateChild("Door");
        var undo = new UndoStack(scene);

        undo.Execute(SetEntityCommand.Capture(node, new EntityData("func_door")));
        node.Entity!.ClassName.ShouldBe("func_door");

        undo.Undo().ShouldBeTrue();
        node.Entity.ShouldBeNull();

        undo.Redo().ShouldBeTrue();
        node.Entity!.ClassName.ShouldBe("func_door");
    }

    [Fact]
    public void An_edit_to_the_live_data_does_not_change_what_the_command_holds()
    {
        // Entity data is mutable. History must hold its own copies.
        var scene = new Scene("entities");
        SceneNode node = scene.Root.CreateChild("Door");
        var data = new EntityData("func_door");
        var undo = new UndoStack(scene);

        undo.Execute(SetEntityCommand.Capture(node, data));
        node.Entity.ShouldNotBeSameAs(data);
        data.SetValue("speed", "9");
        node.Entity!.SetValue("speed", "5");

        undo.Undo();
        undo.Redo();

        node.Entity!.Keyvalues.ShouldBeEmpty();
    }

    [Fact]
    public void A_cancelled_gesture_restores_a_node_that_left_the_scene()
    {
        var scene = new Scene("entities");
        SceneNode node = scene.Root.CreateChild("Door");
        var undo = new UndoStack(scene);

        undo.BeginTransaction("Make");
        undo.Execute(SetEntityCommand.Capture(node, new EntityData("func_door")));
        scene.Root.RemoveChild(node);
        undo.CancelTransaction();

        node.Entity.ShouldBeNull();
    }

    [Fact]
    public void A_node_the_scene_no_longer_has_is_skipped()
    {
        var scene = new Scene("entities");
        var command = new SetEntityCommand(System.Guid.NewGuid(), null, new EntityData("func_door"));

        Should.NotThrow(() => command.Do(scene));
        Should.NotThrow(() => command.Undo(scene));
    }
}
