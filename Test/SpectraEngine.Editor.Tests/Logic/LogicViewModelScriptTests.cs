using Avalonia;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using System;
using System.Collections.Generic;
using System.Linq;
using static SpectraEngine.Editor.Tests.Logic.LogicScriptFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>
/// The Logic view's model and a level's scripts: what it says of them while
/// the level is edited, what it draws while the level runs, and that Stop
/// and the Scripts key take all of it away again.
/// </summary>
public sealed class LogicViewModelScriptTests
{
    private const long Tick = LogicPlayFixture.Tick;

    private readonly LogicGraphInfo _level = Level();

    private LogicViewModel Model()
    {
        var model = new LogicViewModel(new FixedWidthRuler())
        {
            Schemas = LogicFixture.Catalog,
            Mode = LogicScopeMode.WholeLevel,
            ViewSize = new Size(1123, 800),
        };

        model.Apply(Snapshot(_level));
        return model;
    }

    private static string[] ScriptCards(LogicViewModel model) =>
        [.. model.Scene.ShouldNotBeNull().Cards.Where(card => card.Card.IsScript).Select(card => card.Card.Name)];

    private static LogicWireFace Face(LogicViewModel model, string from, string to) =>
        model.Wires.Single(wire => wire.Edge.Edge.From.Name == from && wire.Edge.Edge.To.Name == to);

    [Fact]
    public void While_editing_the_view_names_the_scripts_inside_and_says_that_nothing_more_is_known()
    {
        LogicViewModel model = Model();

        ScriptCards(model).ShouldBeEmpty();
        model.Scene.ShouldNotBeNull().Card("KeyButton").ScriptsNote.ShouldBe("1 script inside: Beep");
        model.ScriptsHint.ShouldBe("Scripts connect when the level runs. Press Play to see what they are wired to.");
        model.Status.Entities.ShouldBe("3 entities");
        model.Status.Wires.ShouldBe("2 wires in the level");
        model.Status.Scripts.ShouldBeEmpty();
        model.Status.ScriptWires.ShouldBeEmpty();
    }

    [Fact]
    public void A_level_with_no_scripts_gets_no_legend_and_counts_its_wires_as_before()
    {
        var model = new LogicViewModel(new FixedWidthRuler()) { Mode = LogicScopeMode.WholeLevel };

        model.Apply(Snapshot(Level(LogicScriptFixture.Entities())));

        model.ScriptsHint.ShouldBeEmpty();
        model.Status.Wires.ShouldBe("2 wires");
    }

    [Fact]
    public void While_the_level_runs_each_script_that_hears_or_sends_has_a_card_and_dashed_wires()
    {
        LogicViewModel model = Model();

        model.Apply(Snapshot(_level, Playing(Designed())));

        ScriptCards(model).Order().ShouldBe(["Beep", "Main"]);
        model.Wires.Count(wire => wire.Edge.Edge.IsScript).ShouldBe(3);
        model.Wires.Where(wire => wire.Edge.Edge.IsScript).ShouldAllBe(wire => wire.State.Look == LogicWireLook.Script);
        model.Status.Entities.ShouldBe("4 entities");
        model.Status.Scripts.ShouldBe("2 scripts");
        model.Status.Wires.ShouldBe("2 wires in the level");
        model.Status.ScriptWires.ShouldBe("3 made by scripts");
        model.Hint.ShouldBe("Dashed wires are made by scripts and last only while the level runs.");
        model.ScriptsHint.ShouldBe("A script shows as a name. Double-click it to open the code.");
        LogicSceneCheck.FirstTextProblem(model.Scene.ShouldNotBeNull()).ShouldBeNull();
    }

    [Fact]
    public void A_dashed_wire_always_says_its_line_of_code_and_lights_for_a_moment_when_it_carries_something()
    {
        LogicViewModel model = Model();
        LogicScriptWiring wiring = Designed();
        model.Apply(Snapshot(_level, Playing(wiring)));
        LogicScene scene = model.Scene.ShouldNotBeNull();

        model.Apply(Snapshot(_level, Playing(wiring, Tick, default, new LogicScriptActivity(1, Tick), default)));
        LogicWireFace lit = Face(model, "Main", "Door7");

        lit.State.Look.ShouldBe(LogicWireLook.ScriptFiring);
        lit.Text.ShouldBe("DoorController, line 57");
        Face(model, "Zone7", "Main").State.Look.ShouldBe(LogicWireLook.Script);

        model.Apply(Snapshot(
            _level,
            Playing(wiring, Tick + LogicWireState.FiringTicks, default, new LogicScriptActivity(1, Tick), default)));

        Face(model, "Main", "Door7").State.Look.ShouldBe(LogicWireLook.Script);
        Face(model, "Main", "Door7").Text.ShouldBe("DoorController, line 57");
        model.Scene.ShouldBeSameAs(scene, "what a wire carries moves nothing");
    }

    [Fact]
    public void A_wire_of_the_level_goes_on_saying_what_it_does_beside_the_dashed_ones()
    {
        LogicViewModel model = Model();
        LogicPlayInfo play = new()
        {
            Tick = Tick,
            Wires = [LogicPlayFixture.Fired(Button, 0, 3, Tick - 100)],
            Scripts = Designed(),
            ScriptActivity = new LogicScriptActivity[3],
        };

        model.Apply(Snapshot(_level, play));

        Face(model, "KeyButton", "Keypad").Text.ShouldBe("3 times");
        Face(model, "KeyButton", "Beep").Text.ShouldBe("Beep, line 3");
    }

    [Fact]
    public void A_connection_that_ends_takes_its_wire_away_and_the_card_with_its_last_wire()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level, Playing(Designed())));

        model.Apply(Snapshot(_level, Playing(Running(
            Hears(Main, Zone, "OnTrigger", Doors, 42),
            Sends(Main, Door, "Open", Doors, 57)))));

        ScriptCards(model).ShouldBe(["Main"]);
        model.Status.Scripts.ShouldBe("1 script");
        model.Status.ScriptWires.ShouldBe("2 made by scripts");
        model.Scene.ShouldNotBeNull().Card("KeyButton").ScriptsNote.ShouldBe("1 script inside: Beep");

        model.Apply(Snapshot(_level, Playing(Running(Sends(Main, Door, "Open", Doors, 57)))));

        model.Status.ScriptWires.ShouldBe("1 made by a script");
        model.Scene.ShouldNotBeNull().Cards.ShouldNotContain(card => card.Card.Name == "Zone7");
    }

    [Fact]
    public void With_the_scripts_key_off_the_view_is_the_levels_wiring_and_says_nothing_of_scripts()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level, Playing(Designed())));

        model.ShowsScripts = false;

        ScriptCards(model).ShouldBeEmpty();
        model.Wires.ShouldAllBe(wire => !wire.Edge.Edge.IsScript);
        model.Scene.ShouldNotBeNull().Cards.ShouldAllBe(card => card.ScriptsNote.Length == 0);
        model.Scene.ShouldNotBeNull().Cards.Select(card => card.Card.Name).Order()
            .ShouldBe(["Door7", "KeyButton", "Keypad"]);
        model.Status.Wires.ShouldBe("2 wires");
        model.Status.Scripts.ShouldBeEmpty();
        model.Hint.ShouldBe("Wires light up as they fire. Stop to edit.");
        model.ScriptsHint.ShouldBeEmpty();

        model.ShowsScripts = true;

        ScriptCards(model).Order().ShouldBe(["Beep", "Main"]);
        model.Status.ScriptWires.ShouldBe("3 made by scripts");
    }

    [Fact]
    public void Stopping_the_level_takes_the_script_cards_and_the_dashed_wires_away()
    {
        LogicViewModel model = Model();
        LogicScene editing = model.Scene.ShouldNotBeNull();
        model.Apply(Snapshot(_level, Playing(Designed())));

        model.Apply(Snapshot(_level));

        ScriptCards(model).ShouldBeEmpty();
        model.Wires.ShouldAllBe(wire => !wire.Edge.Edge.IsScript);
        model.Status.Entities.ShouldBe("3 entities");
        model.Status.Scripts.ShouldBeEmpty();
        model.ScriptsHint.ShouldBe("Scripts connect when the level runs. Press Play to see what they are wired to.");
        model.Scene.ShouldNotBeNull().Size.ShouldBe(editing.Size);
        model.Scene.ShouldNotBeNull().Cards.Select(card => card.Bounds).ShouldBe(editing.Cards.Select(card => card.Bounds));
    }

    [Fact]
    public void A_session_that_ends_while_its_level_runs_leaves_nothing_of_its_scripts()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level, Playing(Designed())));

        model.Reset();
        model.Scene.ShouldBeNull();

        // The session that comes back after a crash plays nothing yet.
        model.Schemas = LogicFixture.Catalog;
        model.Apply(Snapshot(_level));

        ScriptCards(model).ShouldBeEmpty();
        model.IsPlaying.ShouldBeFalse();
        model.ShowsScripts.ShouldBeTrue("what the user chose stays");
    }

    [Fact]
    public void The_engine_is_asked_for_the_state_of_entities_and_never_of_a_script()
    {
        LogicViewModel model = Model();

        model.Apply(Snapshot(_level, Playing(Designed())));

        model.ShownEntityIds.ShouldBe([Zone, Button, Keypad, Door], ignoreOrder: true);
    }

    [Fact]
    public void The_same_wiring_from_the_engine_lays_nothing_out_again()
    {
        LogicViewModel model = Model();
        LogicScriptWiring wiring = Designed();
        model.Apply(Snapshot(_level, Playing(wiring)));
        LogicScene scene = model.Scene.ShouldNotBeNull();

        model.Apply(Snapshot(_level, Playing(wiring, Tick + 1)));
        model.Apply(Snapshot(_level, Playing(wiring, Tick + 2, new(1, Tick + 2), default, default)));

        model.Scene.ShouldBeSameAs(scene);
    }

    [Fact]
    public void Near_a_selected_script_the_view_shows_its_card_ringed_and_what_it_reaches()
    {
        LogicViewModel model = Model();
        model.Mode = LogicScopeMode.AroundSelection;
        model.Steps = 1;

        model.Apply(Snapshot(_level, Playing(Designed()), Main));

        LogicScene scene = model.Scene.ShouldNotBeNull();
        scene.Cards.Select(card => card.Card.Name).Order().ShouldBe(["Door7", "Main", "Zone7"]);
        model.IsSelected(scene.Card("Main").Card).ShouldBeTrue();
    }

    [Fact]
    public void What_a_script_heard_and_sent_is_listed_with_its_line_of_code()
    {
        LogicViewModel model = Model();
        LogicPlayInfo play = Listing(
            Heard(1, 1198, "KeyButton", "OnPressed", Beep, "Beep", BeepFile, 3),
            Heard(2, 1204, "Zone7", "OnTrigger", Main, "Main", Doors, 42),
            Sent(3, 1204, Main, "Main", "Door7", "Open", Doors, 57));

        model.Apply(Snapshot(_level, play));

        model.Events.Select(line => $"{line.Tick} {line.Route} | {line.Place}").ShouldBe(
        [
            "1198 KeyButton.OnPressed → Beep | Beep, line 3",
            "1204 Zone7.OnTrigger → Main | DoorController, line 42",
            "1204 Main → Door7.Open | DoorController, line 57",
        ]);
        model.Events.ShouldAllBe(line => line.HasPlace && !line.IsFailure);
    }

    [Fact]
    public void With_the_scripts_key_off_the_events_are_the_wires_alone()
    {
        LogicViewModel model = Model();
        LogicPlayInfo play = Listing(
            LogicPlayFixture.Event(1, EntityTraceKind.InputDelivered, "KeyButton", "OnPressed", "Keypad", "Trigger"),
            Heard(2, 1198, "KeyButton", "OnPressed", Beep, "Beep", BeepFile, 3),
            Heard(3, 1204, "Zone7", "OnTrigger", Main, "Main", Doors, 42),
            Sent(4, 1204, Main, "Main", "Door7", "Open", Doors, 57));
        model.Apply(Snapshot(_level, play));
        model.Events.Count.ShouldBe(3);
        model.Events.ShouldAllBe(line => line.HasPlace);

        var raised = new List<string?>();
        model.PropertyChanged += (_, change) => raised.Add(change.PropertyName);
        model.ShowsScripts = false;

        model.Events.ShouldHaveSingleItem().Route.ShouldBe("KeyButton.OnPressed → Keypad.Trigger");
        raised.ShouldContain(nameof(LogicViewModel.Events));
        raised.ShouldContain(nameof(LogicViewModel.ShowsScripts));
    }

    [Fact]
    public void A_script_that_sent_to_an_entity_that_left_is_a_line_that_says_so()
    {
        LogicEventLine line = LogicEventLine.From(
            Sent(1, 40, Main, "Main", "Door7", "Open", Doors, 57) with { Kind = EntityTraceKind.TargetMissing });

        line.IsFailure.ShouldBeTrue();
        line.Reason.ShouldBe("Door7 is gone");
        line.Place.ShouldBe("DoorController, line 57");
    }

    // The designed level running, with these as its newest events.
    private static LogicPlayInfo Listing(params LogicEventInfo[] recent)
    {
        LogicScriptWiring scripts = Designed();
        return new LogicPlayInfo
        {
            Tick = Tick,
            Scripts = scripts,
            ScriptActivity = new LogicScriptActivity[scripts.Wires.Count],
            Recent = recent,
        };
    }

    private static LogicEventInfo Heard(
        long number, long tick, string source, string output, Guid script, string name, string file, int line) => new()
    {
        Number = number,
        Tick = tick,
        Kind = EntityTraceKind.OutputFired,
        SourceName = source,
        Output = output,
        TargetName = "",
        Input = "",
        ScriptId = script,
        ScriptName = name,
        ScriptFile = file,
        ScriptLine = line,
    };

    private static LogicEventInfo Sent(
        long number, long tick, Guid script, string name, string target, string input, string file, int line) => new()
    {
        Number = number,
        Tick = tick,
        Kind = EntityTraceKind.InputDelivered,
        SourceName = "",
        Output = "",
        TargetName = target,
        Input = input,
        ScriptId = script,
        ScriptName = name,
        ScriptFile = file,
        ScriptLine = line,
    };
}
