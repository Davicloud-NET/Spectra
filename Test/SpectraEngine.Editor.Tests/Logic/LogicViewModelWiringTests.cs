using Avalonia;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;
using static SpectraEngine.Editor.Tests.Logic.LogicPlayFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>
/// What the Logic view's model asks for when a wire is dragged onto a card
/// or the selected one is removed, and what it says and keeps afterwards.
/// </summary>
public sealed class LogicViewModelWiringTests
{
    private static readonly EntityConnection ZoneToDoor = Wire("OnTrigger", "StartDoor", "Open");
    private static readonly EntityConnection ZoneToLift = Wire("OnStartTouch", "Lift", "Open");

    private readonly List<(Guid Sender, EntityConnection[] Wires)> _asked = [];
    private readonly LogicViewModel _model;

    public LogicViewModelWiringTests()
    {
        _model = new LogicViewModel(new FixedWidthRuler())
        {
            Schemas = Catalog,
            Mode = LogicScopeMode.WholeLevel,
            ViewSize = new Size(1123, 800),
        };

        _model.Wiring.Requested += (sender, wires) => _asked.Add((sender, [.. wires]));
        _model.Apply(Snapshot(Level(VaultEntities())));
    }

    private LogicScene Scene => _model.Scene.ShouldNotBeNull();

    private LogicWiring Wiring => _model.Wiring;

    // The vault level with other wires on the start zone.
    private static LogicGraphInfo WithZoneWires(params EntityConnection[] wires)
    {
        LogicEntityInfo[] entities = VaultEntities();
        entities[8] = entities[8] with { Wires = wires };
        return Level(entities);
    }

    private Point InView(Point scene) => _model.View.ToView(scene);

    // Presses at a point of the scene and lets go in the middle of a card.
    private bool Drag(Point from, string onto)
    {
        Point start = InView(from);
        Point end = InView(Scene.Card(onto).Header.Center);

        Wiring.Press(start, _model.HitTest(start));
        Wiring.Move(InView(new Point(2, 2)), null);
        Wiring.Move(end, _model.HitTest(end).Card);
        return Wiring.Release();
    }

    private LogicWireMenuItem Line(params string[] path)
    {
        IReadOnlyList<LogicWireMenuItem> items = Wiring.Menu().ShouldNotBeNull().Items;
        LogicWireMenuItem line = items.Single(item => item.Text == path[0]);

        foreach (string text in path.Skip(1))
            line = line.Items.Single(item => item.Text == text);

        return line;
    }

    [Fact]
    public void A_wire_dropped_on_a_card_offers_the_senders_outputs_and_the_receivers_inputs()
    {
        Drag(Scene.Card("StartZone").Header.Center, "Lift").ShouldBeTrue();

        LogicWireMenu menu = Wiring.Menu().ShouldNotBeNull();

        menu.Title.ShouldBe("Wire StartZone to Lift");
        menu.Items.Select(item => item.Text).ShouldBe(["OnStartTouch", "OnEndTouch", "OnTrigger"]);
        menu.Items[0].Items.Select(item => item.Text).ShouldBe(["Open", "Close", "SetPosition"]);
        _asked.ShouldBeEmpty();
    }

    [Fact]
    public void A_wire_dragged_from_an_output_offers_only_the_receivers_inputs()
    {
        LogicScenePort trigger = Scene.Card("StartZone").PortNamed("OnTrigger", isOutput: true).ShouldNotBeNull();

        Drag(trigger.Row.Center, "Lift").ShouldBeTrue();

        LogicWireMenu menu = Wiring.Menu().ShouldNotBeNull();
        menu.Title.ShouldBe("Wire StartZone.OnTrigger to Lift");
        menu.Items.Select(item => item.Text).ShouldBe(["Open", "Close", "SetPosition"]);
    }

    [Fact]
    public void A_wire_dropped_on_one_of_two_entities_of_a_name_says_it_reaches_both()
    {
        _model.Apply(Snapshot(Level([.. VaultEntities(), Entity(14, "Lift", "func_door")])));
        LogicSceneCard lift = Scene.Cards.First(card => card.Card.Name == "Lift");
        Point start = InView(Scene.Card("StartZone").Header.Center);
        Point end = InView(lift.Header.Center);

        Wiring.Press(start, _model.HitTest(start));
        Wiring.Move(end, lift);
        Wiring.Release().ShouldBeTrue();

        Wiring.Menu().ShouldNotBeNull().Note.ShouldBe("2 entities are named Lift. The wire reaches each of them.");
    }

    [Fact]
    public void Far_out_a_drag_from_where_an_output_would_be_starts_from_the_card()
    {
        LogicScenePort trigger = Scene.Card("StartZone").PortNamed("OnTrigger", isOutput: true).ShouldNotBeNull();
        _model.View = new LogicPanZoom(default, 0.3);

        Drag(trigger.Row.Center, "Lift").ShouldBeTrue();

        Wiring.Gesture.Output.ShouldBeNull();
        Wiring.Menu().ShouldNotBeNull().Items.Select(item => item.Text)
            .ShouldBe(["OnStartTouch", "OnEndTouch", "OnTrigger"]);
    }

    [Fact]
    public void A_pick_asks_for_the_senders_wires_with_the_new_one_at_the_end()
    {
        Drag(Scene.Card("StartZone").Header.Center, "Lift");

        Guid? sender = Wiring.Pick(Line("OnStartTouch", "Open"));

        sender.ShouldBe(StartZone);
        (Guid asked, EntityConnection[] wires) = _asked.ShouldHaveSingleItem();
        asked.ShouldBe(StartZone);
        wires.ShouldBe([ZoneToDoor, ZoneToLift]);
        wires[1].ShouldBe(new EntityConnection("OnStartTouch", "Lift", "Open", "", 0f, EntityConnection.Infinite));
        Wiring.Gesture.Phase.ShouldBe(LogicWirePhase.Done);
    }

    [Fact]
    public void The_status_row_says_what_was_wired_once_the_level_shows_the_wire()
    {
        var raised = new List<string?>();
        _model.PropertyChanged += (_, change) => raised.Add(change.PropertyName);

        Drag(Scene.Card("StartZone").Header.Center, "Lift");
        Wiring.Pick(Line("OnStartTouch", "Open"));
        _model.Status.News.ShouldBe("");

        _model.Apply(Snapshot(WithZoneWires(ZoneToDoor, ZoneToLift)));

        _model.Status.News.ShouldBe("Wired StartZone.OnStartTouch to Lift.Open.");
        raised.ShouldContain(nameof(LogicViewModel.Status));
        Scene.Edge("StartZone", "Lift").Edge.Output.ShouldBe("OnStartTouch");
    }

    [Fact]
    public void The_news_goes_when_the_wiring_changes_again()
    {
        Drag(Scene.Card("StartZone").Header.Center, "Lift");
        Wiring.Pick(Line("OnStartTouch", "Open"));
        _model.Apply(Snapshot(WithZoneWires(ZoneToDoor, ZoneToLift)));

        _model.Apply(Snapshot(WithZoneWires(ZoneToDoor)));

        _model.Status.News.ShouldBe("");
    }

    [Fact]
    public void An_edit_the_level_never_shows_is_never_said()
    {
        Drag(Scene.Card("StartZone").Header.Center, "Lift");
        Wiring.Pick(Line("OnStartTouch", "Open"));

        _model.Apply(Snapshot(WithZoneWires(ZoneToDoor)));

        _model.Status.News.ShouldBe("");
    }

    [Fact]
    public void A_wire_the_sender_already_has_is_not_asked_for_and_the_row_says_so()
    {
        LogicScenePort pressed = Scene.Card("LiftButton").PortNamed("OnPressed", isOutput: true).ShouldNotBeNull();
        Drag(pressed.Row.Center, "Lift");

        Wiring.Pick(Line("Open")).ShouldBeNull();

        _asked.ShouldBeEmpty();
        _model.Status.News.ShouldBe("That wire is already there.");
        Wiring.Gesture.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void A_new_drag_clears_what_the_row_said()
    {
        LogicScenePort pressed = Scene.Card("LiftButton").PortNamed("OnPressed", isOutput: true).ShouldNotBeNull();
        Drag(pressed.Row.Center, "Lift");
        Wiring.Pick(Line("Open"));

        Drag(pressed.Row.Center, "Lift");

        _model.Status.News.ShouldBe("");
    }

    [Fact]
    public void A_menu_closed_with_nothing_picked_asks_for_nothing()
    {
        Drag(Scene.Card("StartZone").Header.Center, "Lift");

        Wiring.Cancel();

        Wiring.Menu().ShouldBeNull();
        Wiring.Gesture.Phase.ShouldBe(LogicWirePhase.Cancelled);
        _asked.ShouldBeEmpty();
    }

    [Fact]
    public void A_drag_redraws_as_the_wire_moves_and_when_it_is_given_up()
    {
        int redraws = 0;
        _model.Redraw += () => redraws++;
        Point zone = InView(Scene.Card("StartZone").Header.Center);

        Wiring.Press(zone, _model.HitTest(zone));
        redraws.ShouldBe(0);

        Wiring.Move(zone + new Vector(30, 0), null);
        Wiring.Move(zone + new Vector(60, 0), null);
        redraws.ShouldBe(2);

        Wiring.Release().ShouldBeFalse();
        redraws.ShouldBe(3);
    }

    [Fact]
    public void A_scene_laid_out_again_gives_a_drag_up()
    {
        Drag(Scene.Card("StartZone").Header.Center, "Lift");

        _model.Apply(Snapshot(WithZoneWires(ZoneToDoor)));

        Wiring.Gesture.Phase.ShouldBe(LogicWirePhase.Cancelled);
        Wiring.Menu().ShouldBeNull();
    }

    [Fact]
    public void A_selected_wire_is_known_by_its_sender_its_place_and_what_it_says()
    {
        LogicSceneEdge toLift = Scene.Edge("OpenVault", "Lift");

        Wiring.Select(toLift);

        Wiring.Selected.ShouldBe(new LogicSelectedWire(OpenVault, 1, "OnTrigger", "Lift", "Open"));
        Wiring.IsSelected(toLift).ShouldBeTrue();
        Wiring.IsSelected(Scene.Edge("OpenVault", "VaultDoor")).ShouldBeFalse();
    }

    [Fact]
    public void Selecting_a_wire_redraws_and_selecting_it_again_does_not()
    {
        int redraws = 0;
        _model.Redraw += () => redraws++;

        Wiring.Select(Scene.Edge("OpenVault", "Lift"));
        Wiring.Select(Scene.Edge("OpenVault", "Lift"));
        redraws.ShouldBe(1);

        Wiring.Select(null);
        redraws.ShouldBe(2);
        Wiring.Selected.ShouldBeNull();
    }

    [Fact]
    public void The_selected_wire_survives_a_redraw_a_selection_and_a_new_graph_that_still_has_it()
    {
        Wiring.Select(Scene.Edge("OpenVault", "Lift"));
        LogicSelectedWire? selected = Wiring.Selected;

        _model.Apply(Snapshot(Level(VaultEntities()), null, OpenVault));
        _model.Mode = LogicScopeMode.AroundSelection;
        _model.Apply(Snapshot(WithZoneWires(ZoneToDoor, ZoneToLift), null, OpenVault));

        Wiring.Selected.ShouldBe(selected);
        Wiring.IsSelected(Scene.Edge("OpenVault", "Lift")).ShouldBeTrue();
    }

    [Fact]
    public void The_selected_wire_is_dropped_when_a_new_graph_no_longer_has_it()
    {
        Wiring.Select(Scene.Edge("StartZone", "StartDoor"));
        int redraws = 0;
        _model.Redraw += () => redraws++;

        _model.Apply(Snapshot(WithZoneWires(ZoneToLift)));

        Wiring.Selected.ShouldBeNull();
        redraws.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void The_selected_wire_is_dropped_when_the_view_no_longer_shows_it()
    {
        Wiring.Select(Scene.Edge("StartZone", "StartDoor"));

        _model.Mode = LogicScopeMode.AroundSelection;
        _model.Apply(Snapshot(Level(VaultEntities()), null, ButtonA));

        Wiring.Selected.ShouldBeNull();
    }

    [Fact]
    public void An_edge_of_a_scene_that_is_gone_is_not_selected_once_its_place_holds_another_wire()
    {
        LogicSceneEdge toLift = Scene.Edge("OpenVault", "Lift");

        // The relay lost its first wire. Where the wire to the lift was, the
        // one to VaultDor is now.
        LogicEntityInfo[] entities = VaultEntities();
        entities[4] = entities[4] with { Wires = [.. entities[4].Wires.Skip(1)] };
        _model.Apply(Snapshot(Level(entities)));

        Wiring.Select(toLift);

        Wiring.Selected.ShouldBeNull();
    }

    [Fact]
    public void A_drag_that_begins_lets_the_selected_wire_go()
    {
        Wiring.Select(Scene.Edge("OpenVault", "Lift"));
        Point zone = InView(Scene.Card("StartZone").Header.Center);

        Wiring.Press(zone, _model.HitTest(zone));
        Wiring.Move(zone + new Vector(30, 0), null);

        Wiring.Selected.ShouldBeNull();
    }

    [Fact]
    public void Removing_the_selected_wire_asks_for_the_senders_list_without_it()
    {
        Wiring.Select(Scene.Edge("OpenVault", "Lift"));

        Wiring.RemoveSelected().ShouldBeTrue();

        (Guid sender, EntityConnection[] wires) = _asked.ShouldHaveSingleItem();
        sender.ShouldBe(OpenVault);
        wires.ShouldBe([Wire("OnTrigger", "VaultDoor", "Open", times: 1), Wire("OnTrigger", "VaultDor", "Close")]);
        Wiring.Selected.ShouldBeNull();
    }

    [Fact]
    public void The_status_row_says_what_was_removed_once_the_level_shows_it_gone()
    {
        Wiring.Select(Scene.Edge("StartZone", "StartDoor"));
        Wiring.RemoveSelected();

        _model.Apply(Snapshot(WithZoneWires()));

        _model.Status.News.ShouldBe("Removed StartZone.OnTrigger to StartDoor.Open.");
    }

    [Fact]
    public void An_edge_that_draws_two_wires_loses_the_newer_one_and_the_row_says_one_of_two()
    {
        EntityConnection later = ZoneToDoor with { Delay = 2f };
        _model.Apply(Snapshot(WithZoneWires(ZoneToDoor, ZoneToLift, later)));

        Wiring.Select(Scene.Edge("StartZone", "StartDoor"));
        Wiring.RemoveSelected().ShouldBeTrue();
        _model.Apply(Snapshot(WithZoneWires(ZoneToDoor, ZoneToLift)));

        _asked.ShouldHaveSingleItem().Wires.ShouldBe([ZoneToDoor, ZoneToLift]);
        _model.Status.News.ShouldBe("Removed 1 of 2 wires from StartZone.OnTrigger to StartDoor.Open.");
    }

    [Fact]
    public void With_no_wire_selected_nothing_is_removed()
    {
        Wiring.RemoveSelected().ShouldBeFalse();

        _asked.ShouldBeEmpty();
    }

    [Fact]
    public void A_level_that_starts_gives_a_drag_up()
    {
        Drag(Scene.Card("StartZone").Header.Center, "Lift");

        _model.Apply(Snapshot(Level(VaultEntities()), VaultPlaying()));

        Wiring.Gesture.IsActive.ShouldBeFalse();
        Wiring.Menu().ShouldBeNull();
    }

    [Fact]
    public void A_level_that_starts_drops_the_selected_wire()
    {
        Wiring.Select(Scene.Edge("StartZone", "StartDoor"));

        _model.Apply(Snapshot(Level(VaultEntities()), VaultPlaying()));

        Wiring.Selected.ShouldBeNull();
    }

    [Fact]
    public void While_a_level_plays_a_drag_starts_nothing()
    {
        _model.Apply(Snapshot(Level(VaultEntities()), VaultPlaying()));

        Drag(Scene.Card("StartZone").Header.Center, "Lift").ShouldBeFalse();

        Wiring.Gesture.Phase.ShouldBe(LogicWirePhase.Idle);
    }

    [Fact]
    public void While_a_level_plays_no_wire_is_selected()
    {
        _model.Apply(Snapshot(Level(VaultEntities()), VaultPlaying()));

        Wiring.Select(Scene.Edge("StartZone", "StartDoor"));

        Wiring.Selected.ShouldBeNull();
        Wiring.CanEdit.ShouldBeFalse();
    }

    [Fact]
    public void A_session_that_ends_forgets_the_selected_wire_and_the_news()
    {
        LogicScenePort pressed = Scene.Card("LiftButton").PortNamed("OnPressed", isOutput: true).ShouldNotBeNull();
        Drag(pressed.Row.Center, "Lift");
        Wiring.Pick(Line("Open"));
        Wiring.Select(Scene.Edge("StartZone", "StartDoor"));

        _model.Reset();

        Wiring.Selected.ShouldBeNull();
        _model.Status.News.ShouldBe("");
        Wiring.CanEdit.ShouldBeFalse();
    }
}
