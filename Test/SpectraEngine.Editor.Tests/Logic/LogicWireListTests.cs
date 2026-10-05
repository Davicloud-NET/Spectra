using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>The lists the Logic view asks an entity's wires to be replaced with.</summary>
public sealed class LogicWireListTests
{
    private static readonly LogicGraphInfo Level = LogicFixture.Level(VaultEntities());

    private static readonly EntityConnection[] RelayWires =
    [
        Wire("OnTrigger", "VaultDoor", "Open", times: 1),
        Wire("OnTrigger", "Lift", "Open", delay: 2f),
        Wire("OnTrigger", "VaultDor", "Close"),
    ];

    [Fact]
    public void A_new_wire_fires_forever_at_once_and_sends_no_parameter()
    {
        LogicWireList.NewWire("OnPressed", "Lift", "Open")
            .ShouldBe(new EntityConnection("OnPressed", "Lift", "Open", "", 0f, EntityConnection.Infinite));
    }

    [Fact]
    public void A_wire_is_added_at_the_end_of_the_senders_list()
    {
        EntityConnection wire = LogicWireList.NewWire("OnTrigger", "StartDoor", "Close");

        LogicWireList.Add(Level, OpenVault, wire, out EntityConnection[] wires).ShouldBe(LogicWireRefusal.None);

        wires.ShouldBe([.. RelayWires, wire]);
    }

    [Fact]
    public void A_wire_is_added_to_an_entity_that_had_none()
    {
        EntityConnection wire = LogicWireList.NewWire("OnOpen", "Lift", "Close");

        LogicWireList.Add(Level, VaultDoor, wire, out EntityConnection[] wires).ShouldBe(LogicWireRefusal.None);

        wires.ShouldBe([wire]);
    }

    [Fact]
    public void A_wire_the_sender_already_has_is_refused()
    {
        LogicWireList.Add(Level, LiftButton, LogicWireList.NewWire("OnPressed", "Lift", "Open"), out EntityConnection[] wires)
            .ShouldBe(LogicWireRefusal.AlreadyThere);

        wires.ShouldBeEmpty();
    }

    [Fact]
    public void A_wire_that_differs_only_in_its_delay_is_another_wire()
    {
        EntityConnection wire = LogicWireList.NewWire("OnTrigger", "Lift", "Open");

        LogicWireList.Add(Level, OpenVault, wire, out EntityConnection[] wires).ShouldBe(LogicWireRefusal.None);

        wires.Length.ShouldBe(4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_wire_is_removed_by_its_place_and_the_rest_keep_their_order(int index)
    {
        LogicWireList.Remove(Level, OpenVault, index, out EntityConnection[] wires).ShouldBe(LogicWireRefusal.None);

        var expected = new List<EntityConnection>(RelayWires);
        expected.RemoveAt(index);
        wires.ShouldBe(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void A_place_the_list_does_not_have_is_refused(int index)
    {
        LogicWireList.Remove(Level, OpenVault, index, out EntityConnection[] wires).ShouldBe(LogicWireRefusal.WireGone);

        wires.ShouldBeEmpty();
    }

    [Fact]
    public void A_sender_that_is_gone_is_refused()
    {
        EntityConnection wire = LogicWireList.NewWire("OnPressed", "Lift", "Open");

        LogicWireList.Add(Level, Id(99), wire, out _).ShouldBe(LogicWireRefusal.SenderGone);
        LogicWireList.Remove(Level, Id(99), 0, out _).ShouldBe(LogicWireRefusal.SenderGone);
        LogicWireList.Add(null, LiftButton, wire, out _).ShouldBe(LogicWireRefusal.SenderGone);
    }

    [Fact]
    public void A_wire_reaches_a_card_by_its_name()
    {
        LogicGraph graph = Vault();

        LogicWireList.TargetOf(graph.Card("StartZone"), graph.Card("Lift")).ShouldBe("Lift");
        LogicWireList.TargetOf(graph.Card("Lift"), graph.Card("Lift")).ShouldBe("Lift");
    }

    [Fact]
    public void Nothing_reaches_a_card_that_is_not_an_entity()
    {
        LogicGraph graph = Vault();

        LogicWireList.TargetOf(graph.Card("OpenVault"), graph.Card("VaultDor")).ShouldBeNull();
    }

    [Fact]
    public void An_entity_without_a_name_is_reached_only_by_itself()
    {
        LogicGraph graph = Graph(
            Entity(1, "", "logic_relay", Wire("OnTrigger", "Door", "Open")),
            Entity(2, "Door", "func_door"));

        LogicCard nameless = graph.Cards.Single(card => card.ClassName == "logic_relay");

        LogicWireList.TargetOf(graph.Card("Door"), nameless).ShouldBeNull();
        LogicWireList.TargetOf(nameless, nameless).ShouldBe(TargetNameIndex.SelfToken);
    }

    [Fact]
    public void An_entity_whose_name_would_be_read_as_a_prefix_is_reached_only_by_itself()
    {
        // As a target, Door* is every entity whose name starts with Door.
        LogicGraph graph = Graph(
            Entity(1, "Door*", "func_door", Wire("OnOpen", "Relay", "Trigger")),
            Entity(2, "Relay", "logic_relay"),
            Entity(3, "DoorB", "func_door", Wire("OnOpen", "Relay", "Trigger")));

        LogicWireList.TargetOf(graph.Card("Relay"), graph.Card("Door*")).ShouldBeNull();
        LogicWireList.TargetOf(graph.Card("Door*"), graph.Card("Door*")).ShouldBe(TargetNameIndex.SelfToken);
    }

    [Fact]
    public void A_name_reaches_every_entity_that_has_it()
    {
        LogicGraphInfo twins = LogicFixture.Level(
            Entity(1, "Door", "func_door"),
            Entity(2, "Door", "func_door"),
            Entity(3, "DoorB", "func_door"));

        LogicWireList.Reach(twins, "Door").ShouldBe(2);
        LogicWireList.Reach(twins, "DoorB").ShouldBe(1);
    }

    [Theory]
    [InlineData(TargetNameIndex.SelfToken)]
    [InlineData("Nobody")]
    [InlineData("")]
    public void A_token_and_a_name_nothing_has_reach_no_entity_by_name(string target)
    {
        LogicWireList.Reach(Level, target).ShouldBe(0);
    }
}
