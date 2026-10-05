using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using System.Linq;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>A level's wiring read into cards and edges, with what the schemas say is wrong.</summary>
public sealed class LogicGraphTests
{
    [Fact]
    public void Every_wired_entity_is_a_card_and_the_rest_are_only_counted()
    {
        LogicGraph graph = Vault();

        graph.Cards.Select(card => card.Name).ShouldBe(
        [
            "ButtonA", "ButtonB", "Presses", "OpenVault", "VaultDoor",
            "Lift", "LiftButton", "StartZone", "StartDoor", "VaultDor",
        ]);

        graph.UnwiredCount.ShouldBe(4);
        graph.FirstUnwiredName.ShouldBe("PlayerStart");
        graph.WireCount.ShouldBe(9);
    }

    [Fact]
    public void A_card_says_what_its_class_declares_and_which_ports_are_wired()
    {
        LogicCard button = Vault().Card("ButtonA");

        button.NodeId.ShouldBe(ButtonA);
        button.ClassName.ShouldBe("func_button");
        button.DisplayName.ShouldBe("Button");
        button.Group.ShouldBe("Movers");
        button.IsKnownClass.ShouldBeTrue();
        button.IsStub.ShouldBeFalse();

        button.Outputs.ShouldBe(
        [
            new LogicPort("OnPressed", true, true, true),
            new LogicPort("OnIn", true, true, false),
            new LogicPort("OnOut", true, true, false),
        ]);

        button.Inputs.ShouldAllBe(port => port.IsDeclared && !port.IsWired);
    }

    [Fact]
    public void A_class_with_no_display_name_shows_its_class_name()
    {
        Vault().Card("Presses").DisplayName.ShouldBe("math_counter");
    }

    [Fact]
    public void A_name_nothing_has_gets_a_stub_and_its_wire_goes_nowhere()
    {
        LogicGraph graph = Vault();
        LogicCard stub = graph.Card("VaultDor");

        stub.Stub.ShouldBe(LogicStubKind.MissingName);
        stub.DisplayName.ShouldBe("No entity has this name");
        stub.Inputs.ShouldBe([new LogicPort("Close", false, false, true)]);

        LogicEdge edge = graph.Edge("OpenVault", "VaultDor");
        edge.Verdict.ShouldBe(LogicVerdict.TargetMissing);
        edge.GoesNowhere.ShouldBeTrue();

        graph.WiresGoingNowhere.ShouldBe(1);
        graph.FirstGoingNowhere.ShouldBeSameAs(edge);
        graph.GoesNowhere(new LogicWireKey(OpenVault, 2)).ShouldBeTrue();
        graph.GoesNowhere(new LogicWireKey(OpenVault, 0)).ShouldBeFalse();
    }

    [Fact]
    public void A_wire_to_the_senders_own_name_is_an_edge_from_the_card_to_itself()
    {
        LogicEdge loop = Vault().Edge("Lift", "Lift");

        loop.IsLoop.ShouldBeTrue();
        loop.Output.ShouldBe("OnFullyOpen");
        loop.Input.ShouldBe("Close");
        loop.Verdict.ShouldBe(LogicVerdict.Fine);
    }

    [Theory]
    [InlineData("!self")]
    [InlineData("!caller")]
    public void Self_and_caller_send_back_to_the_sender(string token)
    {
        LogicGraph graph = Graph(Entity(1, "Relay", "logic_relay", Wire("OnTrigger", token, "Disable")));

        LogicEdge edge = graph.Edges.ShouldHaveSingleItem();
        edge.From.ShouldBeSameAs(edge.To);
        edge.TargetName.ShouldBe(token);
        graph.Cards.ShouldHaveSingleItem();
    }

    [Fact]
    public void Every_wire_to_the_activator_ends_at_one_stub()
    {
        LogicGraph graph = Graph(
            Entity(1, "Zone", "trigger_multiple", Wire("OnTrigger", "!activator", "Kill")),
            Entity(2, "Pit", "trigger_multiple", Wire("OnTrigger", "!activator", "Kill")));

        LogicCard stub = graph.Cards.Single(card => card.IsStub);
        stub.Stub.ShouldBe(LogicStubKind.Activator);
        stub.Name.ShouldBe("The activator");
        stub.DisplayName.ShouldBe("Decided while playing");

        graph.Arriving(stub).Count.ShouldBe(2);
        graph.Edges.ShouldAllBe(edge => edge.Verdict == LogicVerdict.Fine);
        graph.WiresGoingNowhere.ShouldBe(0);
    }

    [Fact]
    public void A_prefix_that_matches_two_makes_two_edges_that_share_one_wire()
    {
        LogicGraph graph = Graph(
            Entity(1, "Relay", "logic_relay", Wire("OnTrigger", "Door*", "Open")),
            Entity(2, "DoorNorth", "func_door"),
            Entity(3, "DoorSouth", "func_door"),
            Entity(4, "Gate", "func_door"));

        graph.Edges.Select(edge => edge.To.Name).ShouldBe(["DoorNorth", "DoorSouth"]);
        graph.Edges.Select(edge => edge.Wire).Distinct().ShouldBe([new LogicWireKey(Id(1), 0)]);
        graph.UnwiredCount.ShouldBe(1);
    }

    [Fact]
    public void A_prefix_that_matches_nothing_gets_a_stub_of_its_own_kind()
    {
        LogicGraph graph = Graph(Entity(1, "Relay", "logic_relay", Wire("OnTrigger", "Door*", "Open")));

        LogicCard stub = graph.Card("Door*");
        stub.Stub.ShouldBe(LogicStubKind.MissingPrefix);
        stub.DisplayName.ShouldBe("No entity matches this");
        graph.WiresGoingNowhere.ShouldBe(1);
    }

    [Fact]
    public void An_empty_target_is_a_stub_too()
    {
        LogicGraph graph = Graph(Entity(1, "Relay", "logic_relay", Wire("OnTrigger", "", "Open")));

        LogicCard stub = graph.Cards.Single(card => card.IsStub);
        stub.Stub.ShouldBe(LogicStubKind.NoTarget);
        stub.DisplayName.ShouldBe("No target set");
        graph.Edges.ShouldHaveSingleItem().Verdict.ShouldBe(LogicVerdict.TargetMissing);
    }

    [Fact]
    public void Each_missing_name_gets_one_stub_however_many_wires_spell_it()
    {
        LogicGraph graph = Graph(
            Entity(1, "A", "logic_relay", Wire("OnTrigger", "Nobody", "Open"), Wire("OnTrigger", "Nowhere", "Open")),
            Entity(2, "B", "logic_relay", Wire("OnTrigger", "Nobody", "Close")));

        graph.Cards.Where(card => card.IsStub).Select(card => card.Name).ShouldBe(["Nobody", "Nowhere"]);
        graph.Card("Nobody").Inputs.Select(port => port.Name).ShouldBe(["Close", "Open"]);
        graph.WiresGoingNowhere.ShouldBe(3);
    }

    [Fact]
    public void Entities_that_share_a_name_all_receive()
    {
        LogicGraph graph = Graph(
            Entity(1, "Relay", "logic_relay", Wire("OnTrigger", "Door", "Open")),
            Entity(2, "Door", "func_door"),
            Entity(3, "Door", "func_door"));

        graph.Edges.Select(edge => edge.To.NodeId).ShouldBe([Id(2), Id(3)]);
    }

    [Fact]
    public void A_class_nothing_declares_is_not_blamed_for_its_ports()
    {
        LogicGraph graph = Graph(
            Entity(1, "Box", "mystery_box", Wire("OnShake", "Other", "Rattle")),
            Entity(2, "Other", "mystery_box"));

        LogicCard box = graph.Card("Box");
        box.IsKnownClass.ShouldBeFalse();
        box.DisplayName.ShouldBe("mystery_box");
        box.Outputs.ShouldBe([new LogicPort("OnShake", true, false, true)]);

        graph.Edges.ShouldHaveSingleItem().Verdict.ShouldBe(LogicVerdict.Fine);
    }

    [Fact]
    public void An_input_the_target_does_not_declare_is_flagged_and_goes_nowhere()
    {
        LogicGraph graph = Graph(
            Entity(1, "Relay", "logic_relay", Wire("OnTrigger", "Door", "Explode")),
            Entity(2, "Door", "func_door"));

        graph.Edges.ShouldHaveSingleItem().Verdict.ShouldBe(LogicVerdict.NoSuchInput);
        graph.WiresGoingNowhere.ShouldBe(1);

        // The card still shows the port, so the wire has somewhere to end.
        graph.Card("Door").Inputs.ShouldContain(new LogicPort("Explode", false, false, true));
    }

    [Fact]
    public void An_output_the_sender_does_not_declare_is_flagged_but_does_not_count_as_going_nowhere()
    {
        LogicGraph graph = Graph(
            Entity(1, "Relay", "logic_relay", Wire("OnExplode", "Door", "Open")),
            Entity(2, "Door", "func_door"));

        LogicEdge edge = graph.Edges.ShouldHaveSingleItem();
        edge.Verdict.ShouldBe(LogicVerdict.NoSuchOutput);
        edge.GoesNowhere.ShouldBeFalse();
        graph.WiresGoingNowhere.ShouldBe(0);
    }

    [Fact]
    public void A_prefix_wire_goes_somewhere_when_one_of_its_targets_takes_the_input()
    {
        LogicGraph graph = Graph(
            Entity(1, "Relay", "logic_relay", Wire("OnTrigger", "Thing*", "Open")),
            Entity(2, "ThingDoor", "func_door"),
            Entity(3, "ThingCounter", "math_counter"));

        graph.Edge("Relay", "ThingCounter").Verdict.ShouldBe(LogicVerdict.NoSuchInput);
        graph.Edge("Relay", "ThingDoor").Verdict.ShouldBe(LogicVerdict.Fine);
        graph.WiresGoingNowhere.ShouldBe(0);
    }

    [Fact]
    public void Wires_that_agree_in_output_target_and_input_share_one_edge()
    {
        LogicGraph graph = Graph(
            Entity(
                1, "Button", "func_button",
                Wire("OnPressed", "Counter", "Add", "1"),
                Wire("OnPressed", "Counter", "Subtract", "1"),
                Wire("OnPressed", "Counter", "Add", "5", delay: 1f)),
            Entity(2, "Counter", "math_counter"));

        graph.Edges.Count.ShouldBe(2);

        LogicEdge shared = graph.Edges[0];
        shared.WireIndices.ShouldBe([0, 2]);
        shared.Wire.ShouldBe(new LogicWireKey(Id(1), 0));
        shared.Label.ShouldBe(new LogicLabel("2 wires", false));

        graph.Edges[1].WireIndices.ShouldBe([1]);
    }

    [Fact]
    public void The_wires_that_arrive_at_a_card_can_be_looked_up()
    {
        LogicGraph graph = Vault();

        graph.Arriving(graph.Card("Presses"))
            .Select(edge => (edge.From.Name, edge.Output, edge.Input))
            .ShouldBe([("ButtonA", "OnPressed", "Add"), ("ButtonB", "OnPressed", "Add")]);

        graph.Leaving(graph.Card("OpenVault")).Select(edge => edge.To.Name)
            .ShouldBe(["VaultDoor", "Lift", "VaultDor"]);

        graph.CardOf(Presses).ShouldBeSameAs(graph.Card("Presses"));
        graph.CardOf(PlayerStart).ShouldBeNull();
    }

    [Fact]
    public void Without_a_catalogue_every_class_is_unknown_and_nothing_is_flagged()
    {
        LogicGraph graph = LogicGraph.Build(Level(VaultEntities()), null);

        graph.Cards.Where(card => !card.IsStub).ShouldAllBe(card => !card.IsKnownClass);
        graph.Edges.Count(edge => edge.Verdict != LogicVerdict.Fine).ShouldBe(1);
    }

    [Fact]
    public void A_snapshot_that_left_entities_out_is_reported()
    {
        var info = new LogicGraphInfo { Entities = VaultEntities(), TotalEntities = 5000 };

        LogicGraph.Build(info, Catalog).IsTruncated.ShouldBeTrue();
        Vault().IsTruncated.ShouldBeFalse();
    }

    [Fact]
    public void A_level_with_no_wires_has_no_cards()
    {
        LogicGraph graph = Graph(Entity(1, "Door", "func_door"), Entity(2, "Start", "info_player_start"));

        graph.Cards.ShouldBeEmpty();
        graph.Edges.ShouldBeEmpty();
        graph.UnwiredCount.ShouldBe(2);
        graph.FirstGoingNowhere.ShouldBeNull();
    }
}
