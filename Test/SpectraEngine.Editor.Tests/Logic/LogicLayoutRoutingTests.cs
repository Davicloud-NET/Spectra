using Avalonia;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using System;
using System.Collections.Generic;
using System.Linq;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>How wires get from card to card: cycles, wires that skip a column, lanes and groups.</summary>
public sealed class LogicLayoutRoutingTests
{
    private static LogicEntityInfo Relay(int id, string name, params string[] targets) => Entity(
        id, name, "logic_relay",
        [.. targets.Select(target => Wire("OnTrigger", target, "Trigger"))]);

    private static double LowestPoint(LogicSceneEdge edge) =>
        edge.Segments.Max(piece => Math.Max(piece.Start.Y, piece.End.Y));

    [Fact]
    public void A_cycle_is_cut_at_the_card_that_comes_first_by_name()
    {
        LogicScene scene = Arrange(Graph(Relay(1, "Beta", "Alpha"), Relay(2, "Alpha", "Beta")));

        scene.Card("Alpha").Bounds.X.ShouldBeLessThan(scene.Card("Beta").Bounds.X);
        scene.Edge("Alpha", "Beta").Segments.Count.ShouldBe(1);

        LogicSceneEdge back = scene.Edge("Beta", "Alpha");
        back.Start.X.ShouldBe(scene.Card("Beta").Bounds.Right);
        back.End.X.ShouldBe(scene.Card("Alpha").Bounds.X);
        LowestPoint(back).ShouldBeGreaterThan(scene.Cards.Max(card => card.Bounds.Bottom));
    }

    [Fact]
    public void A_cycle_entered_from_outside_is_cut_where_it_is_entered()
    {
        // Alpha comes first by name, but Start is what sets the pair off, through Beta.
        LogicScene scene = Arrange(Graph(
            Relay(1, "Alpha", "Beta"),
            Relay(2, "Beta", "Alpha"),
            Relay(3, "Start", "Beta")));

        scene.Card("Start").Bounds.X.ShouldBeLessThan(scene.Card("Beta").Bounds.X);
        scene.Card("Beta").Bounds.X.ShouldBeLessThan(scene.Card("Alpha").Bounds.X);
        scene.Edge("Alpha", "Beta").Segments.Count.ShouldBeGreaterThan(2);
    }

    [Fact]
    public void A_wire_running_back_carries_its_label_on_the_run_under_the_group()
    {
        LogicScene scene = Arrange(Graph(
            Relay(1, "Alpha", "Beta"),
            Entity(2, "Beta", "logic_relay", Wire("OnTrigger", "Alpha", "Trigger", delay: 5f))));

        LogicSceneEdge back = scene.Edge("Beta", "Alpha");
        Rect label = back.LabelBounds.ShouldNotBeNull();

        label.Y.ShouldBeGreaterThanOrEqualTo(scene.Cards.Max(card => card.Bounds.Bottom) + LogicMetrics.TextGap);
        back.Segments.ShouldContain(piece => piece.End == label.Center);
        LowestPoint(back).ShouldBe(label.Center.Y);
    }

    [Fact]
    public void Wires_running_back_get_a_run_each_so_their_labels_cannot_meet()
    {
        LogicScene scene = Arrange(Graph(
            Relay(1, "Alpha", "Beta"),
            Entity(
                2, "Beta", "logic_relay",
                Wire("OnTrigger", "Gamma", "Trigger"),
                Wire("OnTrigger", "Alpha", "Enable", delay: 1f)),
            Entity(3, "Gamma", "logic_relay", Wire("OnTrigger", "Alpha", "Disable", delay: 2f))));

        Rect near = scene.Edge("Beta", "Alpha").LabelBounds.ShouldNotBeNull();
        Rect far = scene.Edge("Gamma", "Alpha").LabelBounds.ShouldNotBeNull();

        // The shorter wire runs nearer the cards, and the longer passes under it.
        (far.Y - near.Y).ShouldBe(LogicMetrics.LabelHeight + LogicMetrics.TextGap);
        LogicSceneCheck.FirstTextProblem(scene).ShouldBeNull();
        LogicSceneCheck.FirstCardCrossing(scene).ShouldBeNull();
    }

    [Fact]
    public void A_wire_that_skips_a_column_passes_between_the_cards_there()
    {
        LogicScene scene = Arrange(Graph(
            Relay(1, "Alpha", "Beta", "Gamma"),
            Relay(2, "Beta", "Gamma"),
            Relay(3, "Gamma")));

        Rect beta = scene.Card("Beta").Bounds;
        LogicSceneEdge skipping = scene.Edge("Alpha", "Gamma");

        scene.Card("Gamma").Bounds.X.ShouldBeGreaterThan(beta.X);
        skipping.Segments.Count.ShouldBe(3);

        // The middle piece runs level, from one side of Beta's column to the other.
        LogicCubic across = skipping.Segments[1];
        across.Start.X.ShouldBe(beta.X);
        across.End.X.ShouldBe(beta.Right);
        across.Start.Y.ShouldBe(across.End.Y);
        (across.Start.Y < beta.Y || across.Start.Y > beta.Bottom).ShouldBeTrue();

        LogicSceneCheck.Crosses(skipping, beta).ShouldBeFalse();
    }

    [Fact]
    public void A_wire_to_self_by_token_loops_under_its_card()
    {
        LogicScene scene = Arrange(Graph(
            Entity(1, "Relay", "logic_relay", Wire("OnTrigger", "!self", "Disable"))));

        LogicSceneCard card = scene.Cards.ShouldHaveSingleItem();
        LogicSceneEdge loop = scene.Edges.ShouldHaveSingleItem();

        loop.LabelBounds.ShouldBeNull();
        LowestPoint(loop).ShouldBe(card.Bounds.Bottom + LogicMetrics.LoopDrop);
        scene.Size.Height.ShouldBeGreaterThanOrEqualTo(LowestPoint(loop) + LogicMetrics.ScenePadding);
    }

    [Fact]
    public void A_prefix_wire_is_drawn_to_every_card_it_matches()
    {
        LogicScene scene = Arrange(Graph(
            Entity(1, "Relay", "logic_relay", Wire("OnTrigger", "Door*", "Open", times: 1)),
            Entity(2, "DoorNorth", "func_door"),
            Entity(3, "DoorSouth", "func_door")));

        scene.Edges.Count.ShouldBe(2);
        scene.Edges.Select(edge => edge.Wire).Distinct().ShouldHaveSingleItem();
        scene.Edges.ShouldAllBe(edge => edge.Label.Text == "once" && edge.LabelBounds != null);
        LogicSceneCheck.FirstTextProblem(scene).ShouldBeNull();
    }

    [Fact]
    public void The_activators_stub_stands_after_the_cards_that_send_to_it()
    {
        LogicScene scene = Arrange(Graph(
            Entity(1, "Zone", "trigger_multiple", Wire("OnTrigger", "!activator", "Kill"))));

        LogicSceneCard stub = scene.Card(LogicText.ActivatorName);
        stub.Stub.ShouldBe(LogicStubKind.Activator);
        stub.Bounds.X.ShouldBeGreaterThan(scene.Card("Zone").Bounds.Right);
    }

    [Fact]
    public void A_wide_label_widens_its_own_lane_and_no_other()
    {
        string parameter = new('x', 30);
        LogicScene scene = Arrange(Graph(
            Entity(1, "Alpha", "logic_relay", Wire("OnTrigger", "Beta", "Trigger", parameter)),
            Relay(2, "Beta", "Gamma"),
            Relay(3, "Gamma")));

        Rect label = scene.Edge("Alpha", "Beta").LabelBounds.ShouldNotBeNull();
        double firstLane = scene.Card("Beta").Bounds.X - scene.Card("Alpha").Bounds.Right;
        double secondLane = scene.Card("Gamma").Bounds.X - scene.Card("Beta").Bounds.Right;

        label.Width.ShouldBe(32 * FixedWidthRuler.CharacterWidth + 2 * LogicMetrics.LabelPadding);
        firstLane.ShouldBe(label.Width + 2 * LogicMetrics.LaneMargin);
        secondLane.ShouldBe(LogicMetrics.LaneWidth);
    }

    [Fact]
    public void The_least_lane_width_is_the_callers_to_set()
    {
        LogicScene scene = Arrange(
            Graph(Relay(1, "Alpha", "Beta"), Relay(2, "Beta")),
            new LogicLayoutOptions { MinimumLaneWidth = 200 });

        (scene.Card("Beta").Bounds.X - scene.Card("Alpha").Bounds.Right).ShouldBe(200);
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    public void A_lane_width_that_is_no_width_is_refused(double width)
    {
        LogicGraph graph = Graph(Relay(1, "Alpha", "Beta"), Relay(2, "Beta"));

        Should.Throw<ArgumentOutOfRangeException>(
            () => Arrange(graph, new LogicLayoutOptions { MinimumLaneWidth = width }));
    }

    [Fact]
    public void A_long_label_on_a_wire_to_self_gets_a_column_wide_enough_to_hold_it()
    {
        string parameter = new('x', 40);
        LogicScene scene = Arrange(Graph(
            Relay(1, "Alpha", "Beta"),
            Entity(
                2, "Beta", "logic_relay",
                Wire("OnTrigger", "Beta", "Trigger", parameter),
                Wire("OnTrigger", "Gamma", "Trigger")),
            Relay(3, "Gamma")));

        Rect label = scene.Edge("Beta", "Beta").LabelBounds.ShouldNotBeNull();

        label.Width.ShouldBeGreaterThan(LogicMetrics.CardWidth);
        label.Center.X.ShouldBe(scene.Card("Beta").Bounds.Center.X);
        LogicSceneCheck.FirstTextProblem(scene).ShouldBeNull();
    }

    [Fact]
    public void Larger_groups_come_before_smaller_ones_and_equal_ones_go_by_name()
    {
        LogicScene scene = Arrange(Graph(
            Relay(1, "Bravo1", "Bravo2"),
            Relay(2, "Bravo2"),
            Relay(3, "Alpha1", "Alpha2"),
            Relay(4, "Alpha2"),
            Relay(5, "Zulu1", "Zulu2"),
            Relay(6, "Zulu2", "Zulu3"),
            Relay(7, "Zulu3")));

        scene.Card("Zulu1").Bounds.Y.ShouldBeLessThan(scene.Card("Alpha1").Bounds.Y);
        scene.Card("Alpha1").Bounds.Y.ShouldBeLessThan(scene.Card("Bravo1").Bounds.Y);
    }

    [Fact]
    public void The_same_level_listed_in_another_order_gets_the_same_picture()
    {
        LogicEntityInfo[] entities = VaultEntities();
        LogicScene first = Arrange(Graph(entities));
        LogicScene second = Arrange(Graph([.. entities.Reverse()]));

        Picture(second).ShouldBe(Picture(first));
    }

    [Fact]
    public void Random_levels_get_the_same_picture_in_any_order_and_on_any_run()
    {
        for (int seed = 1; seed <= 60; seed++)
        {
            LogicEntityInfo[] entities = LogicRandomLevel.Entities(seed);
            List<string> first = Picture(Arrange(Graph(entities)));

            Picture(Arrange(Graph(entities))).ShouldBe(first, $"seed {seed}, laid out twice");
            Picture(Arrange(Graph(LogicRandomLevel.Shuffled(entities, seed))))
                .ShouldBe(first, $"seed {seed}, entities in another order");
        }
    }

    // Every rectangle and every curve, named by what it belongs to.
    private static List<string> Picture(LogicScene scene)
    {
        var lines = new List<string> { $"scene {scene.Size}" };

        foreach (LogicSceneCard card in scene.Cards)
        {
            lines.Add($"card {card.Card.Name} {card.Card.NodeId:N} {card.Bounds}");
            lines.AddRange(card.Ports.Select(port => $"  port {port.Name} {port.Row} {port.Anchor}"));
        }

        foreach (LogicSceneEdge edge in scene.Edges)
        {
            LogicEdge wire = edge.Edge;
            lines.Add($"wire {wire.From.Name} {wire.From.NodeId:N} {wire.WireIndices[0]} > {wire.To.Name} {wire.To.NodeId:N}");
            lines.Add($"  label '{edge.Label.Text}' {edge.LabelBounds}");
            lines.AddRange(edge.Segments.Select(piece => $"  {piece.Start} {piece.Control1} {piece.Control2} {piece.End}"));
        }

        return lines;
    }
}
