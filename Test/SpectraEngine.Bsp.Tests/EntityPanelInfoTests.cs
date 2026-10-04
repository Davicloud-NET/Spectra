using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a wiring panel is told about the selected entity: a copy of its data,
/// with each wire's target checked against the live graph.
/// </summary>
public sealed class EntityPanelInfoTests
{
    private static EntitySchemaCatalog Catalog(params EntitySchema[] schemas) =>
        EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(schemas));

    private static SceneNode Placed(string name, string className, params EntityConnection[] wires)
    {
        var data = new EntityData(className);
        data.Connections.AddRange(wires);
        return new SceneNode(name) { Entity = data };
    }

    private static EntityConnection Wire(string target) =>
        new("OnOpen", target, "TurnOn", "", 0f, EntityConnection.Infinite);

    private static Scene NewScene(params SceneNode[] nodes)
    {
        var scene = new Scene("Wired");
        foreach (SceneNode node in nodes)
            scene.Root.AddChild(node);

        return scene;
    }


    [Fact]
    public void A_node_with_no_entity_publishes_nothing()
    {
        // No section at all, not an empty one: its Add button would write an
        // entity payload onto geometry.
        var scene = NewScene(new SceneNode("plain"));

        EntityPanelInfo.Capture(scene.Root.Children[0], null, scene).ShouldBeNull();
    }

    [Fact]
    public void The_class_and_its_declared_outputs_ride_along()
    {
        EntitySchemaCatalog catalog = Catalog(
            new EntitySchema("func_door", outputs: ["OnOpen", "OnClose"]));

        SceneNode door = Placed("door", "func_door");
        Scene scene = NewScene(door);

        EntityPanelInfo info = EntityPanelInfo.Capture(door, catalog, scene).ShouldNotBeNull();

        info.NodeId.ShouldBe(door.Id);
        info.ClassName.ShouldBe("func_door");
        info.IsKnown.ShouldBeTrue();
        info.Outputs.ShouldBe(["OnOpen", "OnClose"]);
    }

    [Fact]
    public void A_class_nothing_declares_still_publishes_its_wiring()
    {
        // An unknown class keeps its data; only the schema's output list is missing.
        SceneNode mystery = Placed("mystery", "xyzzy_unknown", Wire("door"));
        Scene scene = NewScene(mystery);

        EntityPanelInfo info = EntityPanelInfo
            .Capture(mystery, Catalog(new EntitySchema("func_door")), scene)
            .ShouldNotBeNull();

        info.IsKnown.ShouldBeFalse();
        info.Outputs.ShouldBeEmpty();
        info.Connections.Count.ShouldBe(1);
    }

    [Fact]
    public void The_wires_are_published_in_the_authored_order()
    {
        // Wire order round-trips through map.json, so nothing may sort it.
        SceneNode door = Placed(
            "door", "func_door",
            new EntityConnection("OnClose", "z", "B", "", 0f, -1),
            new EntityConnection("OnOpen", "a", "A", "", 0f, -1));

        Scene scene = NewScene(door);
        EntityPanelInfo info = EntityPanelInfo.Capture(door, null, scene).ShouldNotBeNull();

        info.Connections.Select(c => c.Wire.Output).ShouldBe(["OnClose", "OnOpen"]);
    }


    [Fact]
    public void The_published_wiring_is_a_copy_of_the_nodes_own_list()
    {
        // The render thread mutates the node again once the frame ends.
        SceneNode door = Placed("door", "func_door", Wire("light1"));
        Scene scene = NewScene(door);

        EntityPanelInfo info = EntityPanelInfo.Capture(door, null, scene).ShouldNotBeNull();
        info.Connections.Count.ShouldBe(1);

        door.Entity!.Connections.Clear();
        door.Entity!.Connections.Add(Wire("something_else"));

        info.Connections.Count.ShouldBe(1, "the held snapshot still describes its own frame");
        info.Connections[0].Wire.TargetName.ShouldBe("light1");
    }


    [Fact]
    public void A_target_naming_an_entity_in_the_scene_resolves()
    {
        SceneNode door = Placed("door", "func_door", Wire("light1"));
        Scene scene = NewScene(door, Placed("light1", "logic_relay"));

        EntityPanelInfo.Capture(door, null, scene)!.Connections[0].TargetResolves.ShouldBeTrue();
    }

    [Fact]
    public void A_target_naming_nothing_is_flagged_and_KEPT()
    {
        // Flagged, not removed: the map loader keeps such a wire too.
        SceneNode door = Placed("door", "func_door", Wire("nobody"));
        Scene scene = NewScene(door);

        EntityPanelInfo info = EntityPanelInfo.Capture(door, null, scene).ShouldNotBeNull();

        info.Connections.Count.ShouldBe(1);
        info.Connections[0].Wire.TargetName.ShouldBe("nobody");
        info.Connections[0].TargetResolves.ShouldBeFalse();
        door.Entity!.Connections.Count.ShouldBe(1, "nothing removed it from the node either");
    }

    [Fact]
    public void A_plain_node_of_the_right_name_does_not_resolve_a_target()
    {
        // TargetNameIndex lists entities only, so a wire aimed at a plain
        // brush named "door" delivers to nothing.
        SceneNode relay = Placed("relay", "logic_relay", Wire("door"));
        Scene scene = NewScene(relay, new SceneNode("door"));

        EntityPanelInfo.Capture(relay, null, scene)!.Connections[0].TargetResolves.ShouldBeFalse();
    }

    [Fact]
    public void A_trailing_star_resolves_by_prefix()
    {
        SceneNode relay = Placed("relay", "logic_relay", Wire("light*"), Wire("lamp*"));
        Scene scene = NewScene(relay, Placed("light_ceiling", "logic_relay"));

        IReadOnlyList<EntityConnectionInfo> wires =
            EntityPanelInfo.Capture(relay, null, scene)!.Connections;

        wires[0].TargetResolves.ShouldBeTrue();
        wires[1].TargetResolves.ShouldBeFalse();
    }

    [Fact]
    public void The_runtime_forms_report_as_resolving_and_nothing_else_does()
    {
        // !self, !activator and !caller resolve while the level runs, so they
        // can't be checked here. Any other ! form is not honoured and must flag.
        SceneNode relay = Placed(
            "relay", "logic_relay",
            Wire("!self"), Wire("!activator"), Wire("!caller"), Wire("!nonsense"));

        Scene scene = NewScene(relay);
        IReadOnlyList<EntityConnectionInfo> wires =
            EntityPanelInfo.Capture(relay, null, scene)!.Connections;

        wires[0].TargetResolves.ShouldBeTrue();
        wires[1].TargetResolves.ShouldBeTrue();
        wires[2].TargetResolves.ShouldBeTrue();
        wires[3].TargetResolves.ShouldBeFalse();
    }

    [Fact]
    public void An_empty_target_does_not_resolve()
    {
        SceneNode relay = Placed("relay", "logic_relay", Wire(""));
        Scene scene = NewScene(relay);

        EntityPanelInfo.Capture(relay, null, scene)!.Connections[0].TargetResolves.ShouldBeFalse();
    }

    [Fact]
    public void An_entity_may_wire_to_itself_by_name()
    {
        SceneNode relay = Placed("relay", "logic_relay", Wire("relay"));
        Scene scene = NewScene(relay);

        EntityPanelInfo.Capture(relay, null, scene)!.Connections[0].TargetResolves.ShouldBeTrue();
    }

    [Fact]
    public void A_deeply_nested_entity_is_found_by_the_scan()
    {
        // Levels keep their logic entities inside groups.
        SceneNode relay = Placed("relay", "logic_relay", Wire("buried"));
        var group = new SceneNode("group");
        var inner = new SceneNode("inner");
        inner.AddChild(Placed("buried", "logic_relay"));
        group.AddChild(inner);

        Scene scene = NewScene(relay, group);

        EntityPanelInfo.Capture(relay, null, scene)!.Connections[0].TargetResolves.ShouldBeTrue();
    }

    [Fact]
    public void With_no_scene_to_resolve_against_nothing_is_claimed_to_resolve()
    {
        // Nothing to check against, so no wire may be reported healthy.
        SceneNode door = Placed("door", "func_door", Wire("light1"));

        EntityPanelInfo.Capture(door, null, null)!.Connections[0].TargetResolves.ShouldBeFalse();
    }
}
