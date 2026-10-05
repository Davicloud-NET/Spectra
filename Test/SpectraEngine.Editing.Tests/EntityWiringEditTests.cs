using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editing.Undo;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace SpectraEngine.Editing.Tests;

/// <summary>Entity wiring edits: the command, the editor verb over it, and the map bytes.</summary>
public sealed class EntityWiringEditTests
{
    private static EntityConnection Wire(
        string output, string target, string input = "Trigger",
        string param = "", float delay = 0f, int times = EntityConnection.Infinite) =>
        new(output, target, input, param, delay, times);

    private static SceneNode Placed(string name, string className, params EntityConnection[] wires)
    {
        var data = new EntityData(className);
        data.Connections.AddRange(wires);
        return new SceneNode(name) { Entity = data };
    }

    private sealed class Rig
    {
        public Rig(params SceneNode[] nodes)
        {
            Scene = new Scene("entities");
            foreach (SceneNode node in nodes)
                Scene.Root.AddChild(node);

            Undo = new UndoStack(Scene);
        }

        public Scene Scene { get; }
        public UndoStack Undo { get; }
    }

    private static IReadOnlyList<EntityConnection> Wiring(SceneNode node) =>
        node.Entity!.Connections;

    [Fact]
    public void Undo_restores_the_exact_list_in_the_authored_order()
    {
        SceneNode node = Placed(
            "door", "func_door",
            Wire("OnOpen", "light1", "TurnOn"),
            Wire("OnClose", "light2", "TurnOff", "x", 1.5f, 3),
            Wire("OnOpen", "sound1", "PlaySound"));

        var rig = new Rig(node);
        EntityConnection[] before = [.. Wiring(node)];

        // Reorders as well as removes; a membership check alone misses order.
        rig.Undo.Execute(SetEntityConnectionsCommand.Capture(
            node, [before[2], before[0]]));

        Wiring(node).Count.ShouldBe(2);
        Wiring(node)[0].ShouldBe(before[2]);
        Wiring(node)[1].ShouldBe(before[0]);

        rig.Undo.Undo();

        Wiring(node).Count.ShouldBe(3);
        Wiring(node)[0].ShouldBe(before[0]);
        Wiring(node)[1].ShouldBe(before[1]);
        Wiring(node)[2].ShouldBe(before[2]);

        rig.Undo.Redo();
        Wiring(node)[0].ShouldBe(before[2]);
    }

    [Fact]
    public void The_captured_before_state_is_a_copy_of_the_live_list()
    {
        // EntityData hands out its live list.
        SceneNode node = Placed("door", "func_door", Wire("OnOpen", "a"));
        var rig = new Rig(node);

        var command = SetEntityConnectionsCommand.Capture(node, []);
        command.Before.Count.ShouldBe(1);

        rig.Undo.Execute(command);
        Wiring(node).ShouldBeEmpty();
        command.Before.Count.ShouldBe(1, "the before state is not the list it just cleared");

        rig.Undo.Undo();
        Wiring(node).Count.ShouldBe(1);
    }

    [Fact]
    public void A_missing_target_is_a_no_op_rather_than_a_throw()
    {
        // History behind an undone delete names absent nodes.
        var scene = new Scene("entities");
        var command = new SetEntityConnectionsCommand(
            Guid.NewGuid(), [Wire("OnOpen", "a")], []);

        Should.NotThrow(() => command.Do(scene));
        Should.NotThrow(() => command.Undo(scene));
        Should.NotThrow(() => command.RollBack(scene));
    }

    [Fact]
    public void A_node_that_lost_its_entity_is_a_no_op_too()
    {
        SceneNode node = Placed("door", "func_door", Wire("OnOpen", "a"));
        var scene = new Scene("entities");
        scene.Root.AddChild(node);

        var command = SetEntityConnectionsCommand.Capture(node, []);
        node.Entity = null;

        Should.NotThrow(() => command.Do(scene));
    }

    [Fact]
    public void Capturing_from_a_node_with_no_entity_is_refused_at_the_call()
    {
        Should.Throw<InvalidOperationException>(
            () => SetEntityConnectionsCommand.Capture(new SceneNode("plain"), []));
    }

    [Fact]
    public void Sameness_is_order_sensitive_and_exact()
    {
        EntityConnection a = Wire("OnOpen", "light1");
        EntityConnection b = Wire("OnClose", "light2");

        SetEntityConnectionsCommand.SameWiring([a, b], [a, b]).ShouldBeTrue();
        SetEntityConnectionsCommand.SameWiring([a, b], [b, a]).ShouldBeFalse();
        SetEntityConnectionsCommand.SameWiring([a], [a, b]).ShouldBeFalse();

        // No tolerance: a delay one ulp off is a different file.
        SetEntityConnectionsCommand.SameWiring(
            [Wire("OnOpen", "x", delay: 1f)],
            [Wire("OnOpen", "x", delay: 1.0000001f)]).ShouldBeFalse();
    }

    [Fact]
    public void A_run_of_edits_to_one_nodes_wiring_is_one_history_entry()
    {
        SceneNode node = Placed("door", "func_door", Wire("OnOpen", "start"));
        var rig = new Rig(node);

        rig.Undo.BeginTransaction("Entity Wiring");
        rig.Undo.Execute(SetEntityConnectionsCommand.Capture(node, [Wire("OnOpen", "a")]));
        rig.Undo.Execute(SetEntityConnectionsCommand.Capture(node, [Wire("OnOpen", "ab")]));
        rig.Undo.Execute(SetEntityConnectionsCommand.Capture(
            node, [Wire("OnOpen", "abc"), Wire("OnClose", "z")]));
        rig.Undo.CommitTransaction();

        rig.Undo.UndoCount.ShouldBe(1);
        Wiring(node).Count.ShouldBe(2);

        rig.Undo.Undo();
        Wiring(node).Count.ShouldBe(1);
        Wiring(node)[0].TargetName.ShouldBe("start", "the entry spans the whole run");
    }

    [Fact]
    public void A_run_never_absorbs_an_edit_to_a_different_node()
    {
        var first = new SetEntityConnectionsCommand(
            Guid.NewGuid(), [], [Wire("OnOpen", "a")]);
        var other = new SetEntityConnectionsCommand(
            Guid.NewGuid(), [], [Wire("OnClose", "b")]);

        first.TryAbsorb(other).ShouldBeFalse();
        first.After[0].TargetName.ShouldBe("a");
    }

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
    public void The_verb_addresses_a_node_id_rather_than_the_selection()
    {
        // A wiring edit replaces the whole list, so it must not land on
        // whatever happens to be selected.
        SceneNode wired = Placed("door", "func_door", Wire("OnOpen", "a"));
        SceneNode other = Placed("relay", "logic_relay", Wire("OnTrigger", "b"));

        var scene = new Scene("Editor");
        scene.Root.AddChild(wired);
        scene.Root.AddChild(other);
        SceneEditorHost host = NewHost(scene);

        scene.Selection.Select(other);

        host.ApplyEntityConnections(wired.Id, [Wire("OnOpen", "z")]).ShouldBeTrue();

        Wiring(wired)[0].TargetName.ShouldBe("z");
        Wiring(other)[0].TargetName.ShouldBe("b", "the selection is not the subject");
    }

    [Fact]
    public void Writing_the_list_the_node_already_has_records_nothing()
    {
        // The panel commits on blur, so tabbing through fields sends this.
        SceneNode node = Placed("door", "func_door", Wire("OnOpen", "a", "TurnOn", "p", 2f, 4));
        var scene = new Scene("Editor");
        scene.Root.AddChild(node);
        SceneEditorHost host = NewHost(scene);

        host.ApplyEntityConnections(node.Id, [.. Wiring(node)]).ShouldBeFalse();
        host.UndoDepth.ShouldBe(0);

        host.ApplyEntityConnections(node.Id, [Wire("OnOpen", "b")]).ShouldBeTrue();
        host.UndoDepth.ShouldBe(1);
    }

    [Fact]
    public void A_node_with_no_entity_is_refused_rather_than_given_one()
    {
        var plain = new SceneNode("plain");
        var scene = new Scene("Editor");
        scene.Root.AddChild(plain);
        SceneEditorHost host = NewHost(scene);

        host.ApplyEntityConnections(plain.Id, [Wire("OnOpen", "a")]).ShouldBeFalse();
        plain.Entity.ShouldBeNull();
        host.UndoDepth.ShouldBe(0);
    }

    [Fact]
    public void A_suspended_editor_refuses_a_wiring_edit()
    {
        // The panel's view of play mode lags by up to a publish interval.
        SceneNode node = Placed("door", "func_door", Wire("OnOpen", "a"));
        var scene = new Scene("Editor");
        scene.Root.AddChild(node);
        SceneEditorHost host = NewHost(scene);

        host.Suspend();
        host.ApplyEntityConnections(node.Id, [Wire("OnOpen", "z")]).ShouldBeFalse();
        Wiring(node)[0].TargetName.ShouldBe("a");

        host.Resume();
        host.ApplyEntityConnections(node.Id, [Wire("OnOpen", "z")]).ShouldBeTrue();
    }

    [Fact]
    public void A_wire_whose_target_does_not_exist_is_kept_rather_than_dropped()
    {
        // The map loader keeps unresolved wires too: the target may be
        // spawned at run time.
        SceneNode node = Placed("door", "func_door");
        var scene = new Scene("Editor");
        scene.Root.AddChild(node);
        SceneEditorHost host = NewHost(scene);

        host.ApplyEntityConnections(
            node.Id, [Wire("OnOpen", "nothing_is_called_this")]).ShouldBeTrue();

        Wiring(node).Count.ShouldBe(1);
        Wiring(node)[0].TargetName.ShouldBe("nothing_is_called_this");
    }

    // Hand-written, so byte identity is against a file a person could type.
    // No brushes: Brush re-normalises its planes, which changes the bytes.
    // 'spectramap' is the engine's current version: a save stamps it.
    private static readonly byte[] WiredMap = Encoding.UTF8.GetBytes("""
        {
          "spectramap": 4,
          "minimumReadableVersion": 3,
          "engine": "1.0.0",
          "scene": {
            "name": "Wired"
          },
          "nodes": [
            {
              "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
              "name": "door",
              "transform": {"p":[0,0,0]},
              "entity": {
                "class": "func_door",
                "keys": {"speed":"100"},
                "outputs": [
                  {"output":"OnFullyOpen","target":"light1","input":"TurnOn"},
                  {"output":"OnFullyClosed","target":"light1","input":"TurnOff","param":"2","delay":1.5,"times":3},
                  {"output":"OnFullyOpen","target":"missing_entity","input":"Kill"}
                ]
              },
              "children": []
            },
            {
              "id": "7d1b9e40-2c55-4f13-8a6c-1e9d5b04a7f2",
              "name": "light1",
              "transform": {"p":[0,0,0]},
              "entity": {
                "class": "logic_relay"
              },
              "children": []
            }
          ]
        }
        """.ReplaceLineEndings("\n") + "\n");

    [Fact]
    public void A_wired_map_saves_byte_for_byte_after_a_load()
    {
        var loaded = new Scene("Testmap");
        MapSceneBinder.ApplyTo(MapReader.Read(WiredMap), loaded);

        MapWriter.Write(MapSceneBinder.FromScene(loaded)).ShouldBe(WiredMap);
    }

    [Fact]
    public void A_wiring_edit_undone_saves_byte_for_byte_again()
    {
        // The edit reorders, so an undo that re-sorted the list would show.
        var loaded = new Scene("Testmap");
        MapSceneBinder.ApplyTo(MapReader.Read(WiredMap), loaded);

        SceneNode door = loaded.Root.Children[0];
        door.Name.ShouldBe("door");

        var undo = new UndoStack(loaded);
        EntityConnection[] original = [.. Wiring(door)];

        undo.Execute(SetEntityConnectionsCommand.Capture(
            door,
            [
                original[2],
                original[0],
                new EntityConnection("OnFullyOpen", "light1", "Toggle", "", 0.25f, 1),
            ]));

        MapWriter.Write(MapSceneBinder.FromScene(loaded)).ShouldNotBe(WiredMap);

        undo.Undo();

        MapWriter.Write(MapSceneBinder.FromScene(loaded)).ShouldBe(WiredMap);
    }

    [Fact]
    public void An_added_wire_survives_a_save_and_a_load_in_its_authored_place()
    {
        var loaded = new Scene("Testmap");
        MapSceneBinder.ApplyTo(MapReader.Read(WiredMap), loaded);

        SceneNode door = loaded.Root.Children[0];
        var undo = new UndoStack(loaded);

        List<EntityConnection> wires = [.. Wiring(door)];
        wires.Insert(1, new EntityConnection("OnOpen", "light1", "Blink", "", 0f, 2));
        undo.Execute(SetEntityConnectionsCommand.Capture(door, wires));

        var reloaded = new Scene("Testmap");
        MapSceneBinder.ApplyTo(
            MapReader.Read(MapWriter.Write(MapSceneBinder.FromScene(loaded))), reloaded);

        IReadOnlyList<EntityConnection> back = Wiring(reloaded.Root.Children[0]);
        back.Count.ShouldBe(4);
        back[1].Output.ShouldBe("OnOpen");
        back[1].Input.ShouldBe("Blink");
        back[1].TimesToFire.ShouldBe(2);
        back[2].Input.ShouldBe("TurnOff", "the wires after the insert kept their order");
    }

    [Fact]
    public void A_transform_is_untouched_by_a_wiring_edit()
    {
        SceneNode node = Placed("door", "func_door", Wire("OnOpen", "a"));
        node.LocalPosition = new Vector3(3f, 4f, 5f);
        var rig = new Rig(node);

        rig.Undo.Execute(SetEntityConnectionsCommand.Capture(node, []));

        node.LocalPosition.ShouldBe(new Vector3(3f, 4f, 5f));
    }
}
