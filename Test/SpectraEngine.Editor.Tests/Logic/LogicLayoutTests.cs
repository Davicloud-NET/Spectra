using Avalonia;
using SpectraEngine.Editor.Shell.Logic;
using System;
using System.Collections.Generic;
using System.Linq;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>The vault level laid out: where its cards, wires and labels go.</summary>
public sealed class LogicLayoutTests
{
    private static readonly LogicScene Scene = Arrange(Vault());

    [Fact]
    public void Cards_stand_left_to_right_by_cause()
    {
        double buttons = Scene.Card("ButtonA").Bounds.X;
        double counter = Scene.Card("Presses").Bounds.X;
        double relay = Scene.Card("OpenVault").Bounds.X;
        double door = Scene.Card("VaultDoor").Bounds.X;

        Scene.Card("ButtonB").Bounds.X.ShouldBe(buttons);
        buttons.ShouldBeLessThan(counter);
        counter.ShouldBeLessThan(relay);
        relay.ShouldBeLessThan(door);

        Scene.Card("Lift").Bounds.X.ShouldBe(door);
        Scene.Card("VaultDor").Bounds.X.ShouldBe(door);
    }

    [Fact]
    public void A_card_nothing_sends_to_stands_one_column_before_its_receiver()
    {
        Scene.Card("LiftButton").Bounds.X.ShouldBe(Scene.Card("OpenVault").Bounds.X);
    }

    [Fact]
    public void Columns_are_a_card_and_a_lane_apart()
    {
        double step = LogicMetrics.CardWidth + LogicMetrics.LaneWidth;

        Scene.Card("ButtonA").Bounds.X.ShouldBe(LogicMetrics.ScenePadding);
        Scene.Card("Presses").Bounds.X.ShouldBe(LogicMetrics.ScenePadding + step);
        Scene.Card("OpenVault").Bounds.X.ShouldBe(LogicMetrics.ScenePadding + 2 * step);
        Scene.Card("VaultDoor").Bounds.X.ShouldBe(LogicMetrics.ScenePadding + 3 * step);
    }

    [Fact]
    public void A_group_that_shares_no_wire_with_the_first_stands_under_it()
    {
        double firstGroupBottom = Scene.Cards
            .Where(card => card.Card.Name is not ("StartZone" or "StartDoor"))
            .Max(card => card.Bounds.Bottom);

        Scene.Card("StartZone").Bounds.Y.ShouldBeGreaterThan(firstGroupBottom);
        Scene.Card("StartZone").Bounds.X.ShouldBe(LogicMetrics.ScenePadding);
        Scene.Card("StartDoor").Bounds.X.ShouldBeGreaterThan(Scene.Card("StartZone").Bounds.X);
    }

    [Fact]
    public void A_card_lists_its_wired_ports_and_notes_the_outputs_it_leaves_out()
    {
        LogicSceneCard button = Scene.Card("ButtonA");
        button.Ports.Select(port => port.Name).ShouldBe(["OnPressed"]);
        button.Note.ShouldBe("2 more outputs");
        button.Bounds.Height.ShouldBe(42 + 22 + 20 + 6);

        LogicSceneCard door = Scene.Card("VaultDoor");
        door.Ports.Select(port => port.Name).ShouldBe(["Open"]);
        door.Note.ShouldBe("4 outputs, none wired");

        LogicSceneCard lift = Scene.Card("Lift");
        lift.Ports.Select(port => (port.Name, port.IsOutput))
            .ShouldBe([("Open", false), ("Close", false), ("OnFullyOpen", true)]);
        lift.Note.ShouldBe("1 more output");

        LogicSceneCard relay = Scene.Card("OpenVault");
        relay.Note.ShouldBe("");
        relay.NoteRow.ShouldBeNull();
        relay.Bounds.Height.ShouldBe(42 + 2 * 22 + 6);
    }

    [Fact]
    public void A_cards_rows_stack_under_its_header()
    {
        LogicSceneCard lift = Scene.Card("Lift");
        Rect card = lift.Bounds;

        card.Width.ShouldBe(LogicMetrics.CardWidth);
        lift.Header.ShouldBe(new Rect(card.X, card.Y, 184, 42));
        lift.StateRow.ShouldBeNull();

        lift.Ports[0].Row.ShouldBe(new Rect(card.X, card.Y + 42, 184, 22));
        lift.Ports[2].Row.ShouldBe(new Rect(card.X, card.Y + 42 + 44, 184, 22));
        lift.NoteRow.ShouldBe(new Rect(card.X, card.Y + 42 + 66, 184, 20));
        card.Height.ShouldBe(42 + 66 + 20 + 6);
    }

    [Fact]
    public void An_input_meets_its_wire_on_the_left_edge_and_an_output_on_the_right()
    {
        LogicSceneCard lift = Scene.Card("Lift");

        lift.PortNamed("Open", isOutput: false)!.Value.Anchor
            .ShouldBe(new Point(lift.Bounds.X, lift.Bounds.Y + 42 + 11));
        lift.PortNamed("OnFullyOpen", isOutput: true)!.Value.Anchor
            .ShouldBe(new Point(lift.Bounds.Right, lift.Bounds.Y + 42 + 44 + 11));
        lift.PortNamed("OnFullyClosed", isOutput: true).ShouldBeNull();
    }

    [Fact]
    public void A_stub_is_placed_like_any_card_and_keeps_its_kind()
    {
        LogicSceneCard stub = Scene.Card("VaultDor");

        stub.Stub.ShouldBe(LogicStubKind.MissingName);
        stub.Ports.Select(port => port.Name).ShouldBe(["Close"]);
        stub.Note.ShouldBe("");
        Scene.Edge("OpenVault", "VaultDor").Verdict.ShouldBe(LogicVerdict.TargetMissing);
    }

    [Fact]
    public void Asked_for_state_rows_every_entity_gets_one_and_a_stub_does_not()
    {
        LogicScene playing = Arrange(Vault(), new LogicLayoutOptions { ShowsState = true });
        LogicSceneCard relay = playing.Card("OpenVault");

        relay.StateRow.ShouldBe(new Rect(relay.Bounds.X, relay.Bounds.Y + 42, 184, 20));
        relay.Ports[0].Row.Y.ShouldBe(relay.Bounds.Y + 62);
        relay.Bounds.Height.ShouldBe(Scene.Card("OpenVault").Bounds.Height + 20);

        playing.Card("VaultDor").StateRow.ShouldBeNull();
    }

    [Fact]
    public void An_expanded_card_lists_every_port_its_class_declares()
    {
        var options = new LogicLayoutOptions { ExpandedCards = new HashSet<Guid> { ButtonA } };
        LogicSceneCard button = Arrange(Vault(), options).Card("ButtonA");

        button.IsExpanded.ShouldBeTrue();
        button.Ports.Select(port => port.Name).ShouldBe(["Use", "Press", "OnPressed", "OnIn", "OnOut"]);
        button.Ports.Count(port => port.Port.IsWired).ShouldBe(1);
        button.Note.ShouldBe("");
        button.Bounds.Height.ShouldBe(42 + 5 * 22 + 6);
    }

    [Fact]
    public void A_label_sits_in_the_middle_of_the_lane_before_its_receiver()
    {
        LogicSceneEdge once = Scene.Edge("OpenVault", "VaultDoor");
        Rect label = once.LabelBounds.ShouldNotBeNull();

        double laneLeft = Scene.Card("OpenVault").Bounds.Right;
        double laneRight = Scene.Card("VaultDoor").Bounds.X;

        // Four characters at seven pixels, and nine of padding on each side.
        label.Width.ShouldBe(46);
        label.Height.ShouldBe(LogicMetrics.LabelHeight);
        label.Center.X.ShouldBe((laneLeft + laneRight) / 2);
        once.Label.ShouldBe(new LogicLabel("once", false));
    }

    [Fact]
    public void A_wire_with_a_label_is_drawn_through_it_in_two_pieces()
    {
        LogicSceneEdge wire = Scene.Edge("ButtonA", "Presses");
        Rect label = wire.LabelBounds.ShouldNotBeNull();

        wire.Segments.Count.ShouldBe(2);
        wire.Segments[0].End.ShouldBe(label.Center);
        wire.Segments[1].Start.ShouldBe(label.Center);
        wire.Start.ShouldBe(Scene.Card("ButtonA").PortNamed("OnPressed", true)!.Value.Anchor);
        wire.End.ShouldBe(Scene.Card("Presses").PortNamed("Add", false)!.Value.Anchor);
    }

    [Fact]
    public void A_wire_without_a_label_is_one_piece_from_port_to_port()
    {
        LogicSceneEdge wire = Scene.Edge("Presses", "OpenVault");

        wire.LabelBounds.ShouldBeNull();
        wire.Segments.Count.ShouldBe(1);
        wire.Start.ShouldBe(Scene.Card("Presses").PortNamed("OnHitMax", true)!.Value.Anchor);
        wire.End.ShouldBe(Scene.Card("OpenVault").PortNamed("Trigger", false)!.Value.Anchor);
    }

    [Fact]
    public void Two_labels_in_one_lane_keep_their_distance()
    {
        Rect first = Scene.Edge("ButtonA", "Presses").LabelBounds.ShouldNotBeNull();
        Rect second = Scene.Edge("ButtonB", "Presses").LabelBounds.ShouldNotBeNull();

        first.X.ShouldBe(second.X);
        Math.Abs(first.Y - second.Y).ShouldBeGreaterThanOrEqualTo(LogicMetrics.LabelHeight + LogicMetrics.TextGap);
    }

    [Fact]
    public void The_lifts_wire_to_itself_loops_under_the_card_with_room_for_its_label()
    {
        LogicSceneCard lift = Scene.Card("Lift");
        LogicSceneEdge loop = Scene.Edge("Lift", "Lift");
        Rect label = loop.LabelBounds.ShouldNotBeNull();

        loop.Start.X.ShouldBe(lift.Bounds.Right);
        loop.End.X.ShouldBe(lift.Bounds.X);
        LogicSceneCheck.Crosses(loop, lift.Bounds).ShouldBeFalse();

        label.Y.ShouldBeGreaterThanOrEqualTo(lift.Bounds.Bottom + LogicMetrics.TextGap);
        label.Center.X.ShouldBe(lift.Bounds.Center.X);
        loop.Segments.ShouldContain(piece => piece.End == label.Center);

        IEnumerable<LogicSceneCard> below = Scene.Cards.Where(card =>
            card.Bounds.X == lift.Bounds.X && card.Bounds.Y > lift.Bounds.Y);

        foreach (LogicSceneCard card in below)
            card.Bounds.Y.ShouldBeGreaterThanOrEqualTo(label.Bottom + LogicMetrics.TextGap);
    }

    [Fact]
    public void A_selection_in_every_group_leaves_the_groups_where_they_stand()
    {
        // Near the selection every group on show holds a selected card. So a
        // change of mode that shows the same cards has nothing to lay out again.
        LogicScene selected = Arrange(Vault(), null, OpenVault, StartZone);

        selected.Size.ShouldBe(Scene.Size);
        foreach (LogicSceneCard card in Scene.Cards)
            selected.Card(card.Card.Name).Bounds.ShouldBe(card.Bounds, card.Card.Name);
    }

    [Fact]
    public void The_group_holding_a_selected_card_comes_first()
    {
        LogicScene scene = Arrange(Vault(), null, StartDoor);

        scene.Card("StartZone").Bounds.Y.ShouldBe(LogicMetrics.ScenePadding);
        scene.Card("ButtonA").Bounds.Y.ShouldBeGreaterThan(scene.Card("StartDoor").Bounds.Bottom);
    }

    [Fact]
    public void The_scene_is_as_large_as_what_it_holds_plus_its_padding()
    {
        double right = Scene.Cards.Max(card => card.Bounds.Right);
        double bottom = Scene.Cards.Max(card => card.Bounds.Bottom);

        Scene.Size.Width.ShouldBe(right + LogicMetrics.ScenePadding);
        Scene.Size.Height.ShouldBe(bottom + LogicMetrics.ScenePadding);
        Scene.CardOf(Presses).ShouldBeSameAs(Scene.Card("Presses"));
        Scene.CardOf(PlayerStart).ShouldBeNull();
    }

    [Fact]
    public void A_scope_that_shows_nothing_lays_out_to_an_empty_scene()
    {
        var selection = new HashSet<Guid>();
        LogicScopedGraph nothing = new LogicScope { Mode = LogicScopeMode.AroundSelection }.Apply(Vault(), selection);

        LogicScene scene = LogicLayout.Arrange(nothing, selection, new LogicLayoutOptions(), new FixedWidthRuler());

        scene.Cards.ShouldBeEmpty();
        scene.Edges.ShouldBeEmpty();
        scene.Size.ShouldBe(default);
    }
}
