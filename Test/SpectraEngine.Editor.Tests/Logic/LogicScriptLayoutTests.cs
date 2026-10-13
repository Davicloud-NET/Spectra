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
/// Where the layout puts the cards of scripts and their wires: by the rules
/// of every other card, and with nothing that holds text touching anything
/// else that does.
/// </summary>
public sealed class LogicScriptLayoutTests
{
    private const int Levels = 200;

    private static readonly FixedWidthRuler Ruler = new();

    [Fact]
    public void A_script_card_is_its_header_alone()
    {
        LogicScene scene = Arrange(Graph(Designed()));

        foreach (string name in new[] { "Main", "Beep" })
        {
            LogicSceneCard card = scene.Card(name);
            card.Bounds.Height.ShouldBe(LogicMetrics.HeaderHeight);
            card.Bounds.Width.ShouldBe(LogicMetrics.CardWidth);
            card.StateRow.ShouldBeNull();
            card.Ports.ShouldBeEmpty();
            card.NoteRow.ShouldBeNull();
        }
    }

    [Fact]
    public void A_script_stands_in_the_column_between_what_it_hears_and_what_it_sends_to()
    {
        LogicScene scene = Arrange(Graph(Designed()));

        scene.Card("Zone7").Bounds.Right.ShouldBeLessThan(scene.Card("Main").Bounds.X);
        scene.Card("Main").Bounds.Right.ShouldBeLessThan(scene.Card("Door7").Bounds.X);
        scene.Card("KeyButton").Bounds.Right.ShouldBeLessThan(scene.Card("Beep").Bounds.X);

        // The same column as the keypad, which the button reaches by a wire.
        scene.Card("Beep").Bounds.X.ShouldBe(scene.Card("Keypad").Bounds.X);
    }

    [Fact]
    public void A_wire_meets_a_script_at_the_middle_of_its_header_and_an_entity_at_the_port()
    {
        LogicScene scene = Arrange(Graph(Designed()));
        Rect main = scene.Card("Main").Bounds;
        double middle = main.Y + LogicMetrics.HeaderHeight / 2;

        LogicSceneEdge heard = scene.Edge("Zone7", "Main");
        heard.End.ShouldBe(new Point(main.X, middle));
        heard.Start.ShouldBe(scene.Card("Zone7").PortNamed("OnTrigger", isOutput: true).ShouldNotBeNull().Anchor);

        LogicSceneEdge sent = scene.Edge("Main", "Door7");
        sent.Start.ShouldBe(new Point(main.Right, middle));
        sent.End.ShouldBe(scene.Card("Door7").PortNamed("Open", isOutput: false).ShouldNotBeNull().Anchor);
    }

    [Fact]
    public void The_designed_level_keeps_the_rule_on_text_while_it_runs()
    {
        LogicScene scene = Arrange(Graph(Designed()));

        LogicSceneCheck.FirstTextProblem(scene).ShouldBeNull();
        LogicSceneCheck.FirstCardCrossing(scene).ShouldBeNull();
        scene.Edges.Count(edge => edge.Edge.IsScript).ShouldBe(3);
    }

    [Fact]
    public void A_label_has_room_for_the_whole_line_of_code_however_long_the_file_is_named()
    {
        const string longFile = "Scripts/TheControllerOfEveryDoorOnTheUpperFloorOfTheVault.luau";
        LogicScene scene = Arrange(Graph(Running(
            Hears(Main, Zone, "OnTrigger", longFile, 1042),
            Sends(Main, Door, "Open", longFile, 2057))));

        foreach (LogicSceneEdge edge in scene.Edges.Where(edge => edge.Edge.IsScript))
        {
            double words = Ruler.Width(edge.Label.Text, LogicTextStyle.Label);
            edge.LabelBounds.ShouldNotBeNull().Width.ShouldBeGreaterThanOrEqualTo(words + 2 * LogicMetrics.LabelPadding);
        }

        LogicSceneCheck.FirstTextProblem(scene).ShouldBeNull();
        LogicSceneCheck.FirstCardCrossing(scene).ShouldBeNull();
    }

    [Fact]
    public void A_lane_keeps_room_for_dashes_on_both_sides_of_a_scripts_label()
    {
        LogicScene scene = Arrange(Graph(Designed()));

        foreach (LogicSceneEdge edge in scene.Edges.Where(edge => edge.Edge.IsScript))
        {
            Rect label = edge.LabelBounds.ShouldNotBeNull();
            double words = Ruler.Width(edge.Label.Text, LogicTextStyle.Label);
            double lane = scene.Card(edge.Edge.To.Name).Bounds.X - scene.Card(edge.Edge.From.Name).Bounds.Right;

            lane.ShouldBeGreaterThanOrEqualTo(words + 2 * LogicMetrics.ScriptLaneMargin, edge.Label.Text);
            (label.X - edge.Start.X).ShouldBeGreaterThan(LogicMetrics.LaneMargin, edge.Label.Text);
            (edge.End.X - label.Right).ShouldBeGreaterThan(LogicMetrics.LaneMargin, edge.Label.Text);
        }
    }

    [Fact]
    public void A_script_that_hears_an_entity_and_sends_to_it_keeps_the_rule()
    {
        LogicScene scene = Arrange(Graph(Running(
            Hears(Beep, Door, "OnFullyOpen", BeepFile, 4),
            Sends(Beep, Door, "Close", BeepFile, 5),
            Hears(Main, Button, "OnPressed", Doors, 9),
            Sends(Main, Button, "Press", Doors, 10))));

        LogicSceneCheck.FirstTextProblem(scene).ShouldBeNull();
        LogicSceneCheck.FirstCardCrossing(scene).ShouldBeNull();
    }

    [Fact]
    public void Two_scripts_on_one_output_and_many_wires_of_one_script_keep_the_rule()
    {
        LogicScene scene = Arrange(Graph(Running(
            Hears(Main, Button, "OnPressed", Doors, 9),
            Hears(Beep, Button, "OnPressed", BeepFile, 3),
            Hears(Main, Zone, "OnTrigger", Doors, 42),
            Sends(Main, Door, "Open", Doors, 57),
            Sends(Main, Door, "Close", Doors, 61),
            Sends(Main, Keypad, "Trigger", Doors, 70),
            Sends(Beep, Keypad, "Toggle", BeepFile, 8))));

        LogicSceneCheck.FirstTextProblem(scene).ShouldBeNull();
        LogicSceneCheck.FirstCardCrossing(scene).ShouldBeNull();
    }

    [Fact]
    public void The_line_that_names_the_scripts_inside_has_a_row_of_its_own_under_the_ports()
    {
        LogicScene editing = LogicFixture.Arrange(Graph(running: null));
        LogicSceneCard button = editing.Card("KeyButton");

        button.ScriptsNote.ShouldBe("1 script inside: Beep");
        Rect row = button.ScriptsNoteRow.ShouldNotBeNull();
        row.Height.ShouldBe(LogicMetrics.NoteRowHeight);
        row.Y.ShouldBe(button.NoteRow.ShouldNotBeNull().Bottom);
        row.Bottom.ShouldBeLessThanOrEqualTo(button.Bounds.Bottom);
        row.Y.ShouldBeGreaterThanOrEqualTo(button.Ports[^1].Row.Bottom);

        editing.Card("Door7").ScriptsNoteRow.ShouldBeNull();
        LogicSceneCheck.FirstTextProblem(editing).ShouldBeNull();
    }

    [Fact]
    public void The_picture_does_not_depend_on_the_order_the_engine_lists_the_wires_in()
    {
        LogicScriptWire[] wires = [.. Designed().Wires, Sends(Beep, Keypad, "Toggle", BeepFile, 8)];
        LogicScene one = Arrange(Graph(Running(wires)));

        Array.Reverse(wires);
        LogicScene other = Arrange(Graph(Running(wires)));

        other.Size.ShouldBe(one.Size);
        foreach (LogicSceneCard card in one.Cards)
            other.Card(card.Card.Name).Bounds.ShouldBe(card.Bounds);

        foreach (LogicSceneEdge edge in one.Edges)
        {
            LogicSceneEdge same = other.Edges.Single(candidate =>
                candidate.Edge.From.Name == edge.Edge.From.Name
                && candidate.Edge.To.Name == edge.Edge.To.Name
                && candidate.Edge.Input == edge.Edge.Input
                && candidate.Edge.Output == edge.Edge.Output);

            same.LabelBounds.ShouldBe(edge.LabelBounds);
            same.Segments.ShouldBe(edge.Segments);
        }
    }

    [Fact]
    public void No_text_box_touches_another_whatever_scripts_hear_and_send_in_a_random_level()
    {
        for (int seed = 1; seed <= Levels; seed++)
        {
            LogicScene scene = Arrange(RandomGraph(seed));

            scene.Cards.Count(card => card.Card.IsScript).ShouldBeGreaterThan(0, $"seed {seed}");
            LogicSceneCheck.FirstTextProblem(scene).ShouldBeNull($"seed {seed}");
            LogicSceneCheck.FirstCardCrossing(scene).ShouldBeNull($"seed {seed}");
        }
    }

    // A random level of wires with a few scripts, each hearing and sending
    // at random among its entities.
    private static LogicGraph RandomGraph(int seed)
    {
        var random = new Random(seed);
        LogicEntityInfo[] entities = LogicRandomLevel.Entities(seed);

        var scripts = new LogicScriptInfo[1 + random.Next(4)];
        for (int i = 0; i < scripts.Length; i++)
            scripts[i] = new LogicScriptInfo(LogicFixture.Id(1000 + i), $"Script{i}", Guid.Empty);

        var wires = new List<LogicScriptWire>();
        foreach (LogicScriptInfo script in scripts)
        {
            int count = 1 + random.Next(5);
            for (int i = 0; i < count; i++)
            {
                LogicEntityInfo entity = entities[random.Next(entities.Length)];
                string file = $"Scripts/{(random.Next(2) == 0 ? script.Name : "ASharedModuleWithALongName")}.luau";
                int line = 1 + random.Next(900);
                wires.Add(random.Next(2) == 0
                    ? Hears(script.NodeId, entity.NodeId, Port(random, entity, isOutput: true), file, line)
                    : Sends(script.NodeId, entity.NodeId, Port(random, entity, isOutput: false), file, line));
            }
        }

        return LogicGraph.Build(Level(entities, scripts), LogicFixture.Catalog, Running([.. wires]));
    }

    // A port the entity's class has, since a script can name no other. A
    // class nobody knows takes any name.
    private static string Port(Random random, LogicEntityInfo entity, bool isOutput)
    {
        if (!LogicFixture.Catalog.TryGetSchema(entity.ClassName, out EntitySchema? schema))
            return isOutput ? "OnMystery" : "Mystery";

        IReadOnlyList<string> names = isOutput ? schema.OutputNames : schema.InputNames;
        return names[random.Next(names.Count)];
    }
}
