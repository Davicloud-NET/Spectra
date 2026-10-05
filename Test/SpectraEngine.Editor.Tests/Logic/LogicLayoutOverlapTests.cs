using SpectraEngine.Editor.Shell.Logic;
using System;
using System.Collections.Generic;
using System.Linq;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>
/// The rule the layout exists for: nothing with text in it touches anything
/// else with text in it. Checked on a few hundred levels made from fixed
/// seeds, so a failure names a level that can be built again.
/// </summary>
public sealed class LogicLayoutOverlapTests
{
    private const int Levels = 300;

    [Fact]
    public void The_vault_level_keeps_the_rule()
    {
        LogicScene scene = Arrange(Vault());

        LogicSceneCheck.FirstTextProblem(scene).ShouldBeNull();
        LogicSceneCheck.FirstCardCrossing(scene).ShouldBeNull();
    }

    [Fact]
    public void No_text_box_touches_another_in_any_random_level()
    {
        for (int seed = 1; seed <= Levels; seed++)
        {
            LogicScene scene = Arrange(Graph(LogicRandomLevel.Entities(seed)));

            scene.Cards.Count.ShouldBeGreaterThan(0, $"seed {seed} made a level with no wires");
            LogicSceneCheck.FirstTextProblem(scene).ShouldBeNull($"seed {seed}");
        }
    }

    [Fact]
    public void No_wire_runs_across_a_card_it_does_not_end_on_in_any_random_level()
    {
        for (int seed = 1; seed <= Levels; seed++)
        {
            LogicScene scene = Arrange(Graph(LogicRandomLevel.Entities(seed)));

            LogicSceneCheck.FirstCardCrossing(scene).ShouldBeNull($"seed {seed}");
        }
    }

    [Fact]
    public void The_rule_holds_with_state_rows_expanded_cards_and_narrow_lanes()
    {
        for (int seed = 1; seed <= Levels; seed++)
        {
            LogicGraph graph = Graph(LogicRandomLevel.Entities(seed));
            var options = new LogicLayoutOptions
            {
                ShowsState = seed % 2 == 0,
                ExpandedCards = graph.Cards.Where(card => card.Index % 3 == 0).Select(card => card.NodeId).ToHashSet(),
                MinimumLaneWidth = seed % 5 * 20,
            };

            LogicScene scene = Arrange(graph, options, graph.Cards[0].NodeId);

            LogicSceneCheck.FirstTextProblem(scene).ShouldBeNull($"seed {seed}");
        }
    }

    [Fact]
    public void The_rule_holds_around_a_selection_and_with_labels_a_caller_put_on()
    {
        for (int seed = 1; seed <= Levels; seed++)
        {
            LogicGraph graph = Graph(LogicRandomLevel.Entities(seed));
            var selection = new HashSet<Guid> { Wired(graph, seed).NodeId };

            LogicScopedGraph scoped = new LogicScope { Mode = LogicScopeMode.AroundSelection, Steps = seed % 4 }
                .Apply(graph, selection)
                .WithLabels(edge => new LogicLabel(new string('m', edge.WireIndices[0] * 9), false));

            LogicScene scene = LogicLayout.Arrange(scoped, selection, new LogicLayoutOptions(), new FixedWidthRuler());

            LogicSceneCheck.FirstTextProblem(scene).ShouldBeNull($"seed {seed}");
        }
    }

    [Theory]
    [InlineData(LogicScopeMode.WholeLevel)]
    [InlineData(LogicScopeMode.AroundSelection)]
    public void The_rule_holds_with_selected_entities_that_have_no_wires_among_the_cards(LogicScopeMode mode)
    {
        int shown = 0;

        for (int seed = 1; seed <= Levels; seed++)
        {
            LogicGraph graph = Graph(LogicRandomLevel.Entities(seed));

            // Every entity with no wires, and one that has some.
            HashSet<Guid> selection = graph.Cards.Where(card => !card.IsWired).Select(card => card.NodeId).ToHashSet();
            selection.Add(Wired(graph, seed).NodeId);

            LogicScopedGraph scoped = new LogicScope { Mode = mode, Steps = seed % 4 }.Apply(graph, selection);
            var options = new LogicLayoutOptions { ShowsState = seed % 2 == 0 };

            LogicScene scene = LogicLayout.Arrange(scoped, selection, options, new FixedWidthRuler());

            shown += scene.Cards.Count(card => !card.Card.IsWired);
            LogicSceneCheck.FirstTextProblem(scene).ShouldBeNull($"seed {seed}");
            LogicSceneCheck.FirstCardCrossing(scene).ShouldBeNull($"seed {seed}");
        }

        shown.ShouldBeGreaterThan(Levels, "the levels should have entities with no wires to show");
    }

    [Fact]
    public void The_random_levels_cover_the_hard_cases()
    {
        // If the generator stops making these, the tests above prove less than they say.
        var seen = new HashSet<string>();
        int largest = 0;

        for (int seed = 1; seed <= Levels; seed++)
        {
            LogicGraph graph = Graph(LogicRandomLevel.Entities(seed));
            largest = Math.Max(largest, graph.Cards.Count);

            foreach (LogicCard card in graph.Cards.Where(card => card.IsStub))
                seen.Add(card.Stub.ToString());

            if (graph.Edges.Any(edge => edge.IsLoop))
                seen.Add("loop");

            if (graph.Edges.Any(edge => edge.WireIndices.Count > 1))
                seen.Add("shared edge");

            if (graph.Edges.Any(edge => edge.Label.Text.Length > 30))
                seen.Add("long label");

            if (graph.UnwiredCount > 0)
                seen.Add("entity with no wires");

            LogicScene scene = Arrange(graph);

            if (scene.Edges.Any(edge => !edge.Edge.IsLoop && edge.End.X < edge.Start.X))
                seen.Add("wire running back");

            if (scene.Edges.Any(edge => edge.End.X > edge.Start.X && edge.Segments.Count >= 3))
                seen.Add("wire skipping a column");
        }

        seen.ShouldBe(
            [
                "Activator", "MissingName", "MissingPrefix", "NoTarget", "loop", "shared edge",
                "long label", "entity with no wires", "wire running back", "wire skipping a column",
            ],
            ignoreOrder: true);
        largest.ShouldBeGreaterThan(50);
    }

    // One of the cards a wire touches, another for each seed.
    private static LogicCard Wired(LogicGraph graph, int seed) =>
        graph.Cards[seed % (graph.Cards.Count - graph.UnwiredCount)];
}
