using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Tests.Logic;

// The level the Logic view was designed on: two buttons count up to a relay
// that opens a vault door and a lift, with one wire aimed at a name nothing
// has. The classes are shaped like the engine's own.
internal static class LogicFixture
{
    public static readonly Guid PlayerStart = Id(1);
    public static readonly Guid ButtonA = Id(2);
    public static readonly Guid ButtonB = Id(3);
    public static readonly Guid Presses = Id(4);
    public static readonly Guid OpenVault = Id(5);
    public static readonly Guid VaultDoor = Id(6);
    public static readonly Guid Lift = Id(7);
    public static readonly Guid LiftButton = Id(8);
    public static readonly Guid StartZone = Id(9);
    public static readonly Guid StartDoor = Id(10);

    // Written out, not read from the built-in classes: loading their assembly
    // registers them in the shared catalogue, which another test here freezes.
    public static EntitySchemaCatalog Catalog { get; } = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
    [
        new EntitySchema(
            "func_button", "Button", "Movers",
            inputs: ["Use", "Press"],
            outputs: ["OnPressed", "OnIn", "OnOut"]),
        new EntitySchema(
            "func_door", "Door", "Movers",
            inputs: ["Open", "Close", "Toggle"],
            outputs: ["OnOpen", "OnClose", "OnFullyOpen", "OnFullyClosed"]),
        new EntitySchema(
            "func_movelinear", "Linear Mover", "Movers",
            inputs: ["Open", "Close", "SetPosition"],
            outputs: ["OnFullyOpen", "OnFullyClosed"]),
        new EntitySchema(
            "math_counter", group: "Logic",
            inputs: ["Add", "Subtract", "SetValue", "SetValueNoFire", "SetHitMax", "SetHitMin", "GetValue"],
            outputs: ["OutValue", "OnHitMax", "OnHitMin"]),
        new EntitySchema(
            "logic_relay", group: "Logic",
            inputs: ["Trigger", "Enable", "Disable", "Toggle"],
            outputs: ["OnTrigger"]),
        new EntitySchema(
            "logic_timer", group: "Logic",
            inputs: ["Enable", "Disable", "Toggle"],
            outputs: ["OnTimer"]),
        new EntitySchema(
            "trigger_multiple", group: "Triggers",
            inputs: ["Enable", "Disable", "Toggle"],
            outputs: ["OnStartTouch", "OnEndTouch", "OnTrigger"]),
        new EntitySchema("info_player_start", "Player start", "Player"),
        new EntitySchema("info_teleport_destination", group: "Triggers"),
    ]));

    public static Guid Id(int number) => new(number, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public static EntityConnection Wire(
        string output,
        string target,
        string input,
        string parameter = "",
        float delay = 0f,
        int times = EntityConnection.Infinite) =>
        new(output, target, input, parameter, delay, times);

    public static LogicEntityInfo Entity(int id, string name, string className, params EntityConnection[] wires) =>
        new(Id(id), name, className, wires);

    public static LogicGraphInfo Level(params LogicEntityInfo[] entities) =>
        new() { Entities = entities, TotalEntities = entities.Length };

    public static LogicEntityInfo[] VaultEntities() =>
    [
        Entity(1, "PlayerStart", "info_player_start"),
        Entity(2, "ButtonA", "func_button", Wire("OnPressed", "Presses", "Add", "1")),
        Entity(3, "ButtonB", "func_button", Wire("OnPressed", "Presses", "Add", "1")),
        Entity(4, "Presses", "math_counter", Wire("OnHitMax", "OpenVault", "Trigger")),
        Entity(
            5, "OpenVault", "logic_relay",
            Wire("OnTrigger", "VaultDoor", "Open", times: 1),
            Wire("OnTrigger", "Lift", "Open", delay: 2f),
            Wire("OnTrigger", "VaultDor", "Close")),
        Entity(6, "VaultDoor", "func_door"),
        Entity(7, "Lift", "func_movelinear", Wire("OnFullyOpen", "Lift", "Close", delay: 3f)),
        Entity(8, "LiftButton", "func_button", Wire("OnPressed", "Lift", "Open")),
        Entity(9, "StartZone", "trigger_multiple", Wire("OnTrigger", "StartDoor", "Open")),
        Entity(10, "StartDoor", "func_door"),
        Entity(11, "Exit", "info_teleport_destination"),
        Entity(12, "SideDoor", "func_door"),
        Entity(13, "Clock", "logic_timer"),
    ];

    public static LogicGraph Vault() => Graph(VaultEntities());

    public static LogicGraph Graph(params LogicEntityInfo[] entities) =>
        LogicGraph.Build(Level(entities), Catalog);

    // The whole level, laid out with the fake ruler.
    public static LogicScene Arrange(LogicGraph graph, LogicLayoutOptions? options = null, params Guid[] selected)
    {
        var selection = new HashSet<Guid>(selected);
        return LogicLayout.Arrange(
            new LogicScope().Apply(graph, selection),
            selection,
            options ?? new LogicLayoutOptions(),
            new FixedWidthRuler());
    }

    public static LogicSceneCard Card(this LogicScene scene, string name) =>
        scene.Cards.Single(card => card.Card.Name == name);

    public static LogicSceneEdge Edge(this LogicScene scene, string from, string to) =>
        scene.Edges.Single(edge => edge.Edge.From.Name == from && edge.Edge.To.Name == to);

    public static LogicCard Card(this LogicGraph graph, string name) =>
        graph.Cards.Single(card => card.Name == name);

    public static LogicEdge Edge(this LogicGraph graph, string from, string to) =>
        graph.Edges.Single(edge => edge.From.Name == from && edge.To.Name == to);
}
