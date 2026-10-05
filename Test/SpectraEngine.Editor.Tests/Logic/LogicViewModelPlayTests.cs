using Avalonia;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using System;
using System.Collections.Generic;
using System.Linq;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;
using static SpectraEngine.Editor.Tests.Logic.LogicPlayFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>The Logic view's model while the vault level runs.</summary>
public sealed class LogicViewModelPlayTests
{
    private readonly LogicGraphInfo _level = Level(VaultEntities());
    private readonly FixedWidthRuler _ruler = new();

    private LogicViewModel Model()
    {
        var model = new LogicViewModel(_ruler)
        {
            Schemas = Catalog,
            Mode = LogicScopeMode.WholeLevel,
            ViewSize = new Size(1123, 800),
        };

        model.Apply(Snapshot(_level));
        return model;
    }

    private static LogicWireFace Face(LogicViewModel model, string from, string to) =>
        model.Wires.Single(wire => wire.Edge.Edge.From.Name == from && wire.Edge.Edge.To.Name == to);

    [Fact]
    public void Starting_a_level_lays_out_once_with_a_state_row_on_every_entity()
    {
        LogicViewModel model = Model();
        LogicScene editing = model.Scene.ShouldNotBeNull();

        model.Apply(Snapshot(_level, VaultPlaying()));
        LogicScene playing = model.Scene.ShouldNotBeNull();

        playing.ShouldNotBeSameAs(editing);
        editing.Cards.ShouldAllBe(card => card.StateRow == null);
        playing.Cards.ShouldAllBe(card => card.Card.IsStub == (card.StateRow == null));
        model.IsPlaying.ShouldBeTrue();
        model.TickText.ShouldBe("812");
        model.Hint.ShouldBe("Wires light up as they fire. Stop to edit.");
    }

    [Fact]
    public void Every_wire_of_a_running_level_has_room_for_anything_it_may_come_to_say()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level, VaultPlaying()));

        double widest = Enumerable.Range('0', 10)
            .SelectMany(digit => LogicWireState.LongestTexts((char)digit))
            .Max(text => _ruler.Width(text, LogicTextStyle.Label));

        foreach (LogicSceneEdge edge in model.Scene.ShouldNotBeNull().Edges)
        {
            edge.LabelBounds.ShouldNotBeNull().Width
                .ShouldBeGreaterThanOrEqualTo(widest + 2 * LogicMetrics.LabelPadding);
        }
    }

    [Fact]
    public void Each_wire_says_what_it_is_doing_and_otherwise_what_it_was_authored_with()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level, VaultPlaying()));

        (LogicWireLook, string) Says(string from, string to) =>
            (Face(model, from, to).State.Look, Face(model, from, to).Text);

        Says("ButtonA", "Presses").ShouldBe((LogicWireLook.Fired, "1 time"));
        Says("Presses", "OpenVault").ShouldBe((LogicWireLook.Firing, "now"));
        Says("OpenVault", "VaultDoor").ShouldBe((LogicWireLook.Firing, "now"));
        Says("OpenVault", "Lift").ShouldBe((LogicWireLook.Waiting, "in 1.4 s"));
        Says("OpenVault", "VaultDor").ShouldBe((LogicWireLook.Broken, "missed 1"));
        Says("Lift", "Lift").ShouldBe((LogicWireLook.Plain, "after 3 s"));
        Says("StartZone", "StartDoor").ShouldBe((LogicWireLook.Plain, ""));

        Face(model, "OpenVault", "Lift").Authored.Text.ShouldBe("after 2 s");
        Face(model, "ButtonA", "Presses").IsMono.ShouldBeFalse();
    }

    [Fact]
    public void Nothing_moves_while_the_level_runs_however_high_the_counts_go()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level, VaultPlaying()));
        LogicScene scene = model.Scene.ShouldNotBeNull();

        model.Apply(Snapshot(_level, new LogicPlayInfo
        {
            Tick = 90000,
            Time = 1500f,
            Wires =
            [
                Fired(ButtonA, 0, 1000, 100),
                Fired(ButtonB, 0, 123456, 100),
                new() { NodeId = OpenVault, Wire = 2, Fired = 5000, LastFiredTick = 100, Missed = 5000 },
            ],
        }));

        model.Scene.ShouldBeSameAs(scene);
        Face(model, "ButtonA", "Presses").Text.ShouldBe("999+ times");
        Face(model, "ButtonB", "Presses").Text.ShouldBe("999+ times");
        Face(model, "OpenVault", "VaultDor").Text.ShouldBe("missed 999+");
    }

    [Fact]
    public void Stopping_the_level_lays_out_again_without_the_state_rows()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level, VaultPlaying()));
        LogicScene playing = model.Scene.ShouldNotBeNull();

        model.Apply(Snapshot(_level));

        model.Scene.ShouldNotBeSameAs(playing);
        model.Scene.ShouldNotBeNull().Cards.ShouldAllBe(card => card.StateRow == null);
        model.IsPlaying.ShouldBeFalse();
        model.TickText.ShouldBe("");
        model.Events.ShouldBeEmpty();
        Face(model, "ButtonA", "Presses").Text.ShouldBe("\"1\"");
        Face(model, "ButtonA", "Presses").IsMono.ShouldBeTrue();
    }

    [Fact]
    public void A_card_shows_the_line_of_state_its_entity_last_gave()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level, VaultPlaying()));

        model.TryGetState(VaultDoor, out LogicEntityState door).ShouldBeTrue();
        door.ShouldBe(new LogicEntityState(VaultDoor, "opening", "14 of 39 ticks"));
        model.TryGetState(PlayerStart, out _).ShouldBeFalse();
    }

    [Fact]
    public void State_lines_redraw_when_they_read_differently_and_not_when_a_new_list_says_the_same()
    {
        LogicViewModel model = Model();
        LogicPlayInfo first = VaultPlaying();
        model.Apply(Snapshot(_level, first));

        int redraws = 0;
        model.Redraw += () => redraws++;

        // The engine reads the lines again every few ticks into a new list.
        model.Apply(Snapshot(_level, Again(first, [.. first.States])));
        redraws.ShouldBe(0);

        LogicEntityState[] moved = [.. first.States];
        moved[4] = new LogicEntityState(VaultDoor, "opening", "15 of 39 ticks");
        model.Apply(Snapshot(_level, Again(first, moved)));

        redraws.ShouldBe(1);
        model.TryGetState(VaultDoor, out LogicEntityState door).ShouldBeTrue();
        door.Value.ShouldBe("15 of 39 ticks");
    }

    [Fact]
    public void A_waiting_wire_redraws_on_every_snapshot_as_its_dot_travels()
    {
        LogicViewModel model = Model();
        LogicPlayInfo first = VaultPlaying();
        model.Apply(Snapshot(_level, first));
        double before = Face(model, "OpenVault", "Lift").State.Travel.ShouldNotBeNull();

        int redraws = 0;
        model.Redraw += () => redraws++;
        model.Apply(Snapshot(_level, new LogicPlayInfo
        {
            Tick = first.Tick + 1,
            Time = first.Time + 0.02f,
            Wires = first.Wires,
            States = first.States,
            Recent = first.Recent,
        }));

        redraws.ShouldBe(1);
        Face(model, "OpenVault", "Lift").State.Travel.ShouldNotBeNull().ShouldBeGreaterThan(before);
        model.TickText.ShouldBe("813");
    }

    [Fact]
    public void The_three_newest_events_are_lines_oldest_first_and_a_failure_says_why()
    {
        LogicViewModel model = Model();
        LogicPlayInfo play = VaultPlaying();
        model.Apply(Snapshot(_level, play));

        model.Events.Select(line => (line.Tick, line.Route, line.Reason)).ShouldBe(
        [
            ("812", "Presses.OnHitMax → OpenVault.Trigger", ""),
            ("812", "OpenVault.OnTrigger → VaultDoor.Open", ""),
            ("812", "OpenVault.OnTrigger → VaultDor.Close", "nothing is named VaultDor"),
        ]);
        model.Events[2].IsFailure.ShouldBeTrue();

        LogicEventLine kept = model.Events[2];
        LogicEventInfo refused = Event(4, EntityTraceKind.InputRefused, "LiftButton", "OnPressed", "Door", "Opn");
        model.Apply(Snapshot(_level, new LogicPlayInfo
        {
            Tick = play.Tick,
            Time = play.Time,
            Wires = play.Wires,
            States = play.States,
            Recent = [.. play.Recent, refused],
        }));

        model.Events.Count.ShouldBe(3);
        model.Events[1].ShouldBeSameAs(kept);
        model.Events[2].Reason.ShouldBe("Door has no input Opn");
    }

    [Fact]
    public void A_refusal_shows_on_the_edge_whose_target_refused_and_a_miss_on_the_wire()
    {
        // One wire reaches a door, which opens, and a timer, which cannot.
        LogicGraphInfo level = Level(
            Entity(1, "Relay", "logic_relay", Wire("OnTrigger", "Thing*", "Open"), Wire("OnTrigger", "Nobody", "Open")),
            Entity(2, "ThingDoor", "func_door"),
            Entity(3, "ThingClock", "logic_timer"));

        var model = new LogicViewModel(_ruler) { Schemas = Catalog, Mode = LogicScopeMode.WholeLevel };
        model.Apply(Snapshot(level, Playing(
            new LogicWireActivity { NodeId = Id(1), Wire = 0, Fired = 3, LastFiredTick = 10, Refused = 3 },
            new LogicWireActivity { NodeId = Id(1), Wire = 1, Fired = 3, LastFiredTick = 10, Missed = 3 })));

        Face(model, "Relay", "ThingClock").Text.ShouldBe("refused 3");
        Face(model, "Relay", "ThingClock").State.Look.ShouldBe(LogicWireLook.Broken);
        Face(model, "Relay", "ThingDoor").Text.ShouldBe("3 times");
        Face(model, "Relay", "ThingDoor").State.Look.ShouldBe(LogicWireLook.Fired);
        Face(model, "Relay", "Nobody").Text.ShouldBe("missed 3");
    }

    [Fact]
    public void A_refusal_the_schemas_did_not_foresee_shows_on_every_edge_of_the_wire()
    {
        LogicGraphInfo level = Level(
            Entity(1, "Relay", "logic_relay", Wire("OnTrigger", "Thing*", "Open")),
            Entity(2, "ThingDoor", "func_door"),
            Entity(3, "ThingGate", "func_door"));

        var model = new LogicViewModel(_ruler) { Schemas = Catalog, Mode = LogicScopeMode.WholeLevel };
        model.Apply(Snapshot(level, Playing(
            new LogicWireActivity { NodeId = Id(1), Wire = 0, Fired = 2, LastFiredTick = 10, Refused = 1 })));

        Face(model, "Relay", "ThingDoor").Text.ShouldBe("refused 1");
        Face(model, "Relay", "ThingGate").Text.ShouldBe("refused 1");
    }

    [Fact]
    public void A_label_with_no_words_in_it_is_picked_as_its_wire_or_not_at_all()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level, Playing()));
        model.View = LogicPanZoom.Identity;

        // StartZone's wire has no authored label and has done nothing.
        LogicSceneEdge quiet = model.Scene.ShouldNotBeNull().Edge("StartZone", "StartDoor");
        Rect room = quiet.LabelBounds.ShouldNotBeNull();

        model.HitTest(room.Center).Kind.ShouldBe(LogicHitKind.Edge);
        model.HitTest(new Point(room.Center.X, room.Y + 1)).ShouldBe(LogicHit.None);

        LogicSceneEdge worded = model.Scene.Edge("Lift", "Lift");
        model.HitTest(worded.LabelBounds.ShouldNotBeNull().Center).Kind.ShouldBe(LogicHitKind.Label);
    }

    [Fact]
    public void A_wire_that_runs_through_the_room_kept_for_another_wires_label_is_picked_there()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level, VaultPlaying()));
        model.View = LogicPanZoom.Identity;

        // Both wires leave the relay's one output. The room kept for the
        // door wire's label starts beside it, and is wider than "now".
        LogicScene scene = model.Scene.ShouldNotBeNull();
        LogicSceneEdge toLift = scene.Edge("OpenVault", "Lift");
        LogicSceneEdge toDoor = scene.Edge("OpenVault", "VaultDoor");
        Rect room = toDoor.LabelBounds.ShouldNotBeNull();

        Point crossing = Enumerable.Range(1, 60)
            .Select(step => toLift.Segments[0].At(step / 120.0))
            .First(room.Contains);

        LogicHit hit = model.HitTest(crossing);

        hit.Kind.ShouldBe(LogicHitKind.Edge);
        hit.Edge.ShouldBeSameAs(toLift);
        model.HitTest(room.Center).Edge.ShouldBeSameAs(toDoor);
    }

    [Fact]
    public void A_label_is_picked_where_its_words_are_drawn_and_beside_them_its_wire_is()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level, VaultPlaying()));
        model.View = LogicPanZoom.Identity;

        // The lift's wire to itself says "after 3 s" in room kept for more.
        Rect room = model.Scene.ShouldNotBeNull().Edge("Lift", "Lift").LabelBounds.ShouldNotBeNull();
        double words = _ruler.Width("after 3 s", LogicTextStyle.Label) + 2 * LogicMetrics.LabelPadding;

        room.Width.ShouldBeGreaterThan(words + 8);
        model.HitTest(new Point(room.Center.X - words / 2 + 2, room.Y + 2)).Kind.ShouldBe(LogicHitKind.Label);
        model.HitTest(new Point(room.X + 2, room.Center.Y)).Kind.ShouldBe(LogicHitKind.Edge);
    }

    private static LogicPlayInfo Again(LogicPlayInfo play, IReadOnlyList<LogicEntityState> states) => new()
    {
        Tick = play.Tick,
        Time = play.Time,
        Wires = play.Wires,
        States = states,
        Recent = play.Recent,
    };
}
