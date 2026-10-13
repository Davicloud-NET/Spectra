using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using System;
using System.Collections.Generic;
using System.Linq;
using static SpectraEngine.Editor.Tests.Logic.LogicScriptFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>
/// What scripts add to the wiring graph: the names of the scripts inside an
/// entity while editing, and a card and edges for each script that hears or
/// sends something while the level runs.
/// </summary>
public sealed class LogicScriptGraphTests
{
    [Fact]
    public void While_editing_an_entity_names_the_scripts_inside_it_and_no_script_has_a_card()
    {
        LogicGraph graph = Graph(running: null);

        graph.Card("KeyButton").ScriptNames.ShouldBe(["Beep"]);
        graph.Card("Door7").ScriptNames.ShouldBeEmpty();
        graph.Cards.ShouldAllBe(card => !card.IsScript);
        graph.Edges.ShouldAllBe(edge => !edge.IsScript);
        graph.ScriptCount.ShouldBe(2);
        graph.ScriptCardCount.ShouldBe(0);
    }

    [Fact]
    public void A_script_with_one_connection_has_a_card_and_an_edge_from_the_output_it_hears()
    {
        LogicGraph graph = Graph(Running(Hears(Main, Zone, "OnTrigger", Doors, 42)));

        LogicCard script = graph.Card("Main");
        script.IsScript.ShouldBeTrue();
        script.IsEntity.ShouldBeFalse();
        script.NodeId.ShouldBe(Main);
        script.DisplayName.ShouldBe("Script");
        script.Inputs.ShouldBeEmpty();
        script.Outputs.ShouldBeEmpty();

        LogicEdge heard = graph.Edges.Single(edge => edge.IsScript);
        heard.From.Name.ShouldBe("Zone7");
        heard.To.ShouldBeSameAs(script);
        heard.Output.ShouldBe("OnTrigger");
        heard.Input.ShouldBeEmpty();
        heard.Label.Text.ShouldBe("DoorController, line 42");
        heard.ScriptWires.ShouldBe([0]);
        heard.WireIndices.ShouldBeEmpty();
        graph.Cards.Count(card => card.IsScript).ShouldBe(1);
    }

    [Fact]
    public void A_script_with_many_connections_is_one_card_with_an_edge_for_each()
    {
        LogicGraph graph = Graph(Running(
            Hears(Main, Zone, "OnTrigger"),
            Hears(Main, Button, "OnPressed"),
            Sends(Main, Door, "Open"),
            Sends(Main, Door, "Close"),
            Sends(Main, Keypad, "Trigger")));

        LogicCard script = graph.Cards.Single(card => card.IsScript);
        graph.Arriving(script).Select(edge => $"{edge.From.Name}.{edge.Output}")
            .ShouldBe(["Zone7.OnTrigger", "KeyButton.OnPressed"]);
        graph.Leaving(script).Select(edge => $"{edge.To.Name}.{edge.Input}")
            .ShouldBe(["Door7.Open", "Door7.Close", "Keypad.Trigger"]);
    }

    [Fact]
    public void Two_scripts_that_hear_one_output_each_have_an_edge_from_it()
    {
        LogicGraph graph = Graph(Running(
            Hears(Main, Button, "OnPressed", Doors, 9),
            Hears(Beep, Button, "OnPressed", BeepFile, 3)));

        graph.Leaving(graph.Card("KeyButton")).Where(edge => edge.IsScript).Select(edge => edge.To.Name)
            .ShouldBe(["Main", "Beep"]);
        graph.Card("KeyButton").Outputs.Single(port => port.Name == "OnPressed").IsWired.ShouldBeTrue();
    }

    [Fact]
    public void A_script_that_hears_an_entity_and_sends_to_it_has_an_edge_each_way()
    {
        LogicGraph graph = Graph(Running(
            Hears(Beep, Door, "OnFullyOpen", BeepFile, 4),
            Sends(Beep, Door, "Close", BeepFile, 5)));

        LogicCard script = graph.Card("Beep");
        LogicCard door = graph.Card("Door7");

        graph.Arriving(script).ShouldHaveSingleItem().From.ShouldBeSameAs(door);
        graph.Leaving(script).ShouldHaveSingleItem().To.ShouldBeSameAs(door);
        door.Outputs.Single(port => port.Name == "OnFullyOpen").IsWired.ShouldBeTrue();
        door.Inputs.Single(port => port.Name == "Close").IsWired.ShouldBeTrue();
    }

    [Fact]
    public void Lines_of_code_that_send_the_same_input_share_one_edge_that_says_how_many()
    {
        LogicGraph graph = Graph(Running(
            Sends(Main, Door, "Open", Doors, 57),
            Sends(Main, Door, "Open", Doors, 80),
            Sends(Main, Door, "Open", "Scripts/Main.luau", 4)));

        LogicEdge sent = graph.Edges.Single(edge => edge.IsScript);
        sent.ScriptWires.ShouldBe([0, 1, 2]);
        sent.Label.Text.ShouldBe("3 lines of code");
    }

    [Fact]
    public void An_entity_only_a_script_reaches_is_wired_and_lists_the_port()
    {
        LogicGraph editing = Graph(running: null);
        LogicGraph playing = Graph(Running(Hears(Main, Zone, "OnTrigger")));

        editing.Card("Zone7").IsWired.ShouldBeFalse();
        editing.UnwiredCount.ShouldBe(1);
        playing.Card("Zone7").IsWired.ShouldBeTrue();
        playing.UnwiredCount.ShouldBe(0);
        playing.WireCount.ShouldBe(2, "what a script made is no wire of the level");
        playing.WiresGoingNowhere.ShouldBe(0);
    }

    [Fact]
    public void A_wire_of_a_script_or_to_an_entity_the_level_does_not_list_is_left_out()
    {
        LogicGraph graph = Graph(Running(
            Hears(Guid.NewGuid(), Zone, "OnTrigger"),
            Sends(Main, Guid.NewGuid(), "Open")));

        graph.Cards.ShouldAllBe(card => !card.IsScript);
        graph.Edges.ShouldAllBe(edge => !edge.IsScript);
        graph.Card("Zone7").IsWired.ShouldBeFalse("a port with no edge to draw from it gets no row");
    }

    [Fact]
    public void A_graph_that_shows_no_scripts_has_no_script_cards_and_names_none_inside()
    {
        LogicGraph graph = Graph(Designed(), showsScripts: false);

        graph.Cards.ShouldAllBe(card => !card.IsScript && card.ScriptNames.Count == 0);
        graph.Edges.ShouldAllBe(edge => !edge.IsScript);
        graph.ScriptCount.ShouldBe(0);
        graph.Card("Zone7").IsWired.ShouldBeFalse();
    }

    [Fact]
    public void The_status_counts_scripts_and_what_they_made_apart_from_entities_and_wires()
    {
        LogicScopedGraph shown = new LogicScope().Apply(Graph(Designed()), new HashSet<Guid>());

        shown.Counts.Entities.ShouldBe(4);
        shown.Counts.Scripts.ShouldBe(2);
        shown.Counts.Wires.ShouldBe(2);
        shown.Counts.ScriptWires.ShouldBe(3);
        shown.Counts.WiresGoingNowhere.ShouldBe(0);
    }

    [Fact]
    public void Near_a_selected_script_the_view_shows_its_card_and_what_it_reaches()
    {
        var scope = new LogicScope { Mode = LogicScopeMode.AroundSelection, Steps = 1 };

        LogicScopedGraph shown = scope.Apply(Graph(Designed()), new HashSet<Guid> { Main });

        shown.Cards.Select(card => card.Name).Order().ShouldBe(["Door7", "Main", "Zone7"]);
        shown.Edges.ShouldAllBe(edge => edge.IsScript);
    }

    [Fact]
    public void A_script_is_a_step_on_the_way_from_what_it_hears_to_what_it_sends()
    {
        var scope = new LogicScope { Mode = LogicScopeMode.AroundSelection, Steps = 2 };

        LogicScopedGraph shown = scope.Apply(Graph(Running(
            Hears(Main, Zone, "OnTrigger"),
            Sends(Main, Door, "Open"))), new HashSet<Guid> { Zone });

        shown.Cards.Select(card => card.Name).ShouldContain("Door7");
    }

    [Fact]
    public void The_filter_finds_a_script_by_its_name()
    {
        var scope = new LogicScope { Filter = "main" };
        LogicGraph graph = Graph(Designed());

        LogicScopedGraph shown = scope.Apply(graph, new HashSet<Guid>());

        shown.IsDimmed(graph.Card("Main")).ShouldBeFalse();
        shown.IsDimmed(graph.Card("Beep")).ShouldBeTrue();
    }

    [Fact]
    public void A_line_of_code_is_named_by_its_file_with_no_folder_and_no_ending()
    {
        LogicLabel.Place("Scripts/lib/DoorController.luau", 42).Text.ShouldBe("DoorController, line 42");
        LogicLabel.Place("Beep.luau", 3).Text.ShouldBe("Beep, line 3");
        LogicLabel.Place("Scripts/Main.luau", 0).Text.ShouldBe("Main");
        LogicLabel.Place("", 7).IsEmpty.ShouldBeTrue();
        LogicLabel.Place(null, 7).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void A_script_edge_says_in_words_what_it_does_and_that_code_made_it()
    {
        LogicGraph graph = Graph(Designed());

        LogicViewText.Sentence(graph.Edge("Zone7", "Main")).ShouldBe(
            "Main hears Zone7.OnTrigger, from DoorController, line 42. " +
            "Made by code. It lasts for as long as the level runs.");
        LogicViewText.Sentence(graph.Edge("Main", "Door7")).ShouldBe(
            "Main sends Open to Door7, from DoorController, line 57. " +
            "Made by code. It lasts for as long as the level runs.");
        LogicViewText.Sentence(graph.Card("Main")).ShouldBe("Main, Script");
    }
}
