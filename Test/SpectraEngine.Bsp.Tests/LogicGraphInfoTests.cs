using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a wiring view is told about a level: every entity with its wires, and
/// the same capture back while nothing changed.
/// </summary>
public sealed class LogicGraphInfoTests
{
    private static SceneNode Placed(string name, string className, params EntityConnection[] wires)
    {
        var data = new EntityData(className);
        data.Connections.AddRange(wires);
        return new SceneNode(name) { Entity = data };
    }

    private static EntityConnection Wire(string output, string target, string input) =>
        new(output, target, input, "", 0f, EntityConnection.Infinite);

    private static Scene NewScene(params SceneNode[] nodes)
    {
        var scene = new Scene("Wired");
        foreach (SceneNode node in nodes)
            scene.Root.AddChild(node);

        return scene;
    }

    [Fact]
    public void Every_entity_rides_along_with_its_wires_in_authored_order()
    {
        SceneNode button = Placed(
            "Button", "func_button",
            Wire("OnPressed", "Door", "Open"),
            Wire("OnPressed", "Lift", "Open"));
        SceneNode door = Placed("Door", "func_door");
        Scene scene = NewScene(button, door);

        LogicGraphInfo graph = LogicGraphInfo.Capture(scene);

        graph.Entities.Count.ShouldBe(2);
        graph.TotalEntities.ShouldBe(2);
        graph.IsTruncated.ShouldBeFalse();

        LogicEntityInfo first = graph.Entities[0];
        first.NodeId.ShouldBe(button.Id);
        first.Name.ShouldBe("Button");
        first.ClassName.ShouldBe("func_button");
        first.Wires.Select(wire => wire.TargetName).ShouldBe(["Door", "Lift"]);

        graph.Entities[1].Wires.ShouldBeEmpty();
    }

    [Fact]
    public void A_node_with_no_entity_is_left_out()
    {
        Scene scene = NewScene(new SceneNode("plain"), Placed("Door", "func_door"));

        LogicGraphInfo.Capture(scene).Entities.Select(entity => entity.Name).ShouldBe(["Door"]);
    }

    [Fact]
    public void An_unchanged_scene_gets_the_same_capture_back()
    {
        Scene scene = NewScene(Placed("Button", "func_button", Wire("OnPressed", "Door", "Open")));
        LogicGraphInfo first = LogicGraphInfo.Capture(scene);

        LogicGraphInfo.Capture(scene, first).ShouldBeSameAs(first);
    }

    [Fact]
    public void A_wire_edited_in_place_gets_a_new_capture()
    {
        // The wiring command refills the node's list in place, so no scene
        // event says it changed.
        SceneNode button = Placed("Button", "func_button", Wire("OnPressed", "Door", "Open"));
        Scene scene = NewScene(button);
        LogicGraphInfo first = LogicGraphInfo.Capture(scene);

        button.Entity!.Connections[0] = Wire("OnPressed", "Door", "Close");
        LogicGraphInfo second = LogicGraphInfo.Capture(scene, first);

        second.ShouldNotBeSameAs(first);
        second.Entities[0].Wires[0].Input.ShouldBe("Close");
    }

    [Fact]
    public void A_wire_added_or_removed_gets_a_new_capture()
    {
        SceneNode button = Placed("Button", "func_button", Wire("OnPressed", "Door", "Open"));
        Scene scene = NewScene(button);
        LogicGraphInfo first = LogicGraphInfo.Capture(scene);

        button.Entity!.Connections.Add(Wire("OnPressed", "Lift", "Open"));
        LogicGraphInfo grown = LogicGraphInfo.Capture(scene, first);
        grown.ShouldNotBeSameAs(first);

        button.Entity.Connections.Clear();
        LogicGraphInfo.Capture(scene, grown).ShouldNotBeSameAs(grown);
    }

    [Fact]
    public void A_rename_gets_a_new_capture()
    {
        SceneNode door = Placed("Door", "func_door");
        Scene scene = NewScene(door);
        LogicGraphInfo first = LogicGraphInfo.Capture(scene);

        door.Name = "VaultDoor";
        LogicGraphInfo second = LogicGraphInfo.Capture(scene, first);

        second.ShouldNotBeSameAs(first);
        second.Entities[0].Name.ShouldBe("VaultDoor");
    }

    [Fact]
    public void An_entity_that_joins_or_leaves_gets_a_new_capture()
    {
        SceneNode door = Placed("Door", "func_door");
        Scene scene = NewScene(door);
        LogicGraphInfo first = LogicGraphInfo.Capture(scene);

        SceneNode relay = Placed("Relay", "logic_relay");
        scene.Root.AddChild(relay);
        LogicGraphInfo grown = LogicGraphInfo.Capture(scene, first);
        grown.Entities.Count.ShouldBe(2);

        scene.Root.RemoveChild(relay);
        LogicGraphInfo.Capture(scene, grown).Entities.Count.ShouldBe(1);
    }

    [Fact]
    public void The_capture_holds_copies_of_the_wires()
    {
        SceneNode button = Placed("Button", "func_button", Wire("OnPressed", "Door", "Open"));
        Scene scene = NewScene(button);
        LogicGraphInfo graph = LogicGraphInfo.Capture(scene);

        button.Entity!.Connections.Clear();

        graph.Entities[0].Wires.Count.ShouldBe(1);
    }

    [Fact]
    public void More_entities_than_the_cap_are_counted_and_cut()
    {
        var scene = new Scene("Crowded");
        for (int i = 0; i < LogicGraphInfo.MaxEntities + 3; i++)
            scene.Root.AddChild(Placed($"relay{i}", "logic_relay"));

        LogicGraphInfo graph = LogicGraphInfo.Capture(scene);

        graph.Entities.Count.ShouldBe(LogicGraphInfo.MaxEntities);
        graph.TotalEntities.ShouldBe(LogicGraphInfo.MaxEntities + 3);
        graph.IsTruncated.ShouldBeTrue();

        LogicGraphInfo.Capture(scene, graph).ShouldBeSameAs(graph);
    }
}
