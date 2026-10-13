using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using System;
using System.Linq;
using static SpectraEngine.Editor.Tests.Logic.LogicScriptFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>
/// A script that sits on the node of an entity, as a part can carry both:
/// the node has a card for each, and they are told apart everywhere.
/// </summary>
public sealed class LogicScriptOnEntityTests
{
    // The door carries a script of its own, which hears the door and closes it.
    private static readonly LogicScriptInfo OnTheDoor = new(Door, "Door7", Door);

    private static LogicGraph Graph(params LogicScriptWire[] wires) => LogicGraph.Build(
        Level(LogicScriptFixture.Entities(), [.. Scripts(), OnTheDoor]), LogicFixture.Catalog, Running(wires));

    private static LogicGraph DoorThatClosesItself() => Graph(
        Hears(Door, Door, "OnFullyOpen", "Scripts/Door.luau", 2),
        Sends(Door, Door, "Close", "Scripts/Door.luau", 3));

    [Fact]
    public void The_node_has_a_card_as_an_entity_and_a_card_as_a_script()
    {
        LogicGraph graph = DoorThatClosesItself();

        LogicCard[] cards = [.. graph.Cards.Where(card => card.NodeId == Door)];
        cards.Length.ShouldBe(2);
        LogicCard entity = cards.Single(card => card.IsEntity);
        LogicCard script = cards.Single(card => card.IsScript);

        entity.ScriptNames.ShouldBe(["Door7"]);
        graph.Leaving(script).ShouldHaveSingleItem().To.ShouldBeSameAs(entity);
        graph.Arriving(script).ShouldHaveSingleItem().From.ShouldBeSameAs(entity);
        graph.Edges.Where(edge => edge.IsScript).ShouldAllBe(edge => !edge.IsLoop);
    }

    [Fact]
    public void The_nodes_id_finds_the_entitys_card_in_the_graph_and_in_the_scene()
    {
        LogicGraph graph = DoorThatClosesItself();
        LogicScene scene = Arrange(graph);

        graph.CardOf(Door).ShouldNotBeNull().IsEntity.ShouldBeTrue();
        scene.CardOf(Door).ShouldNotBeNull().Card.IsEntity.ShouldBeTrue();
        scene.Cards.Count(card => card.Card.NodeId == Door).ShouldBe(2);
        LogicSceneCheck.FirstTextProblem(scene).ShouldBeNull();
    }

    [Fact]
    public void The_id_finds_the_entity_also_when_nothing_is_wired_to_it_and_its_script_reaches_elsewhere()
    {
        // The zone has no wires. Its own script sends to the door.
        var onTheZone = new LogicScriptInfo(Zone, "Zone7", Zone);
        LogicGraph graph = LogicGraph.Build(
            Level(LogicScriptFixture.Entities(), onTheZone),
            LogicFixture.Catalog,
            Running(Sends(Zone, Door, "Open", "Scripts/Zone.luau", 4)));

        LogicCard found = graph.CardOf(Zone).ShouldNotBeNull();

        found.IsEntity.ShouldBeTrue();
        found.IsWired.ShouldBeFalse();
        graph.Cards.Single(card => card.IsScript).NodeId.ShouldBe(Zone);
    }

    [Fact]
    public void The_layout_takes_the_two_cards_in_one_order()
    {
        LogicGraph graph = DoorThatClosesItself();
        LogicCard entity = graph.Cards.Single(card => card.NodeId == Door && card.IsEntity);
        LogicCard script = graph.Cards.Single(card => card.NodeId == Door && card.IsScript);

        LogicCardOrder.Compare(entity, script).ShouldBeLessThan(0);
        LogicCardOrder.Compare(script, entity).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Selecting_the_node_rings_both_cards_and_near_it_shows_what_either_reaches()
    {
        var model = new LogicViewModel(new FixedWidthRuler()) { Schemas = LogicFixture.Catalog, Steps = 1 };
        LogicGraphInfo level = Level(LogicScriptFixture.Entities(), [.. Scripts(), OnTheDoor]);

        model.Apply(Snapshot(
            level,
            Playing(Running(Hears(Door, Door, "OnFullyOpen"), Sends(Door, Zone, "Enable"))),
            Door));

        LogicScene scene = model.Scene.ShouldNotBeNull();
        scene.Cards.Where(card => card.Card.NodeId == Door).ShouldAllBe(card => model.IsSelected(card.Card));
        scene.Cards.Select(card => card.Card.Name).Order().ShouldBe(["Door7", "Door7", "Keypad", "Zone7"]);
    }
}
