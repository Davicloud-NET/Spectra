using SpectraEngine.Editor.Shell.Logic;
using System;
using System.Collections.Generic;
using System.Linq;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>Which cards a view shows: the whole level, or what is near the selection.</summary>
public sealed class LogicScopeTests
{
    private static LogicScopedGraph Around(int steps, params Guid[] selected) =>
        new LogicScope { Mode = LogicScopeMode.AroundSelection, Steps = steps }
            .Apply(Vault(), new HashSet<Guid>(selected));

    private static LogicScopedGraph Whole(params Guid[] selected) =>
        new LogicScope().Apply(Vault(), new HashSet<Guid>(selected));

    private static string[] Names(LogicScopedGraph scoped) =>
        [.. scoped.Cards.Select(card => card.Name).Order(StringComparer.Ordinal)];

    [Fact]
    public void The_whole_level_shows_every_wired_card_whatever_is_selected()
    {
        LogicScopedGraph scoped = Whole();

        scoped.Cards.Count.ShouldBe(10);
        scoped.Edges.Count.ShouldBe(9);
        scoped.EmptyReason.ShouldBe(LogicEmptyReason.None);
        Names(Whole(OpenVault, Id(999))).ShouldBe(Names(scoped));
    }

    [Fact]
    public void In_the_whole_level_an_entity_with_no_wires_shows_while_it_is_selected()
    {
        LogicScopedGraph scoped = Whole(Exit);

        scoped.Cards.Count.ShouldBe(11);
        scoped.Cards[^1].Name.ShouldBe("Exit");
        scoped.Cards.Select(card => card.Name).ShouldNotContain("PlayerStart");
        scoped.Edges.Count.ShouldBe(9);
    }

    [Fact]
    public void The_counts_are_what_a_status_line_needs()
    {
        Whole().Counts.ShouldBe(new LogicCounts(
            Cards: 10,
            Entities: 9,
            UnwiredCards: 0,
            Wires: 9,
            WiresGoingNowhere: 1,
            HiddenUnwiredEntities: 4,
            IsTruncated: false));
    }

    [Fact]
    public void An_entity_with_no_wires_that_shows_is_not_counted_among_the_ones_that_do_not()
    {
        LogicScopedGraph scoped = Whole(PlayerStart, SideDoor);

        scoped.Counts.ShouldBe(new LogicCounts(
            Cards: 12,
            Entities: 11,
            UnwiredCards: 2,
            Wires: 9,
            WiresGoingNowhere: 1,
            HiddenUnwiredEntities: 2,
            IsTruncated: false));
    }

    [Fact]
    public void The_first_entity_with_no_wires_that_is_not_shown_is_named()
    {
        Whole().FirstHiddenUnwiredName.ShouldBe("PlayerStart");
        Whole(PlayerStart, SideDoor).FirstHiddenUnwiredName.ShouldBe("Exit");
        Whole(PlayerStart, Exit, SideDoor, Clock).FirstHiddenUnwiredName.ShouldBe("");
    }

    [Fact]
    public void No_steps_shows_the_selected_entity_alone()
    {
        LogicScopedGraph scoped = Around(0, OpenVault);

        Names(scoped).ShouldBe(["OpenVault"]);
        scoped.Edges.ShouldBeEmpty();
        scoped.Counts.Wires.ShouldBe(0);
    }

    [Fact]
    public void One_step_follows_wires_both_ways_and_takes_stubs_along()
    {
        LogicScopedGraph scoped = Around(1, OpenVault);

        Names(scoped).ShouldBe(["Lift", "OpenVault", "Presses", "VaultDoor", "VaultDor"]);

        // Lift's wire to itself comes along with Lift.
        scoped.Edges.Count.ShouldBe(5);
        scoped.Counts.WiresGoingNowhere.ShouldBe(1);
    }

    [Fact]
    public void Two_steps_reach_what_is_wired_to_the_neighbours()
    {
        Names(Around(2, OpenVault)).ShouldBe(
        [
            "ButtonA", "ButtonB", "Lift", "LiftButton", "OpenVault", "Presses", "VaultDoor", "VaultDor",
        ]);
    }

    [Fact]
    public void Cards_near_any_selected_entity_show()
    {
        Names(Around(0, ButtonA, StartDoor)).ShouldBe(["ButtonA", "StartDoor"]);
        Names(Around(1, ButtonA, StartDoor)).ShouldBe(["ButtonA", "Presses", "StartDoor", "StartZone"]);
    }

    [Fact]
    public void With_nothing_selected_it_says_so()
    {
        LogicScopedGraph scoped = Around(2);

        scoped.Cards.ShouldBeEmpty();
        scoped.EmptyReason.ShouldBe(LogicEmptyReason.NoEntitySelected);
    }

    [Fact]
    public void Near_the_selection_an_entity_with_no_wires_shows_alone()
    {
        LogicScopedGraph scoped = Around(2, PlayerStart);

        Names(scoped).ShouldBe(["PlayerStart"]);
        scoped.Edges.ShouldBeEmpty();
        scoped.EmptyReason.ShouldBe(LogicEmptyReason.None);
    }

    [Fact]
    public void Near_the_selection_entities_with_and_without_wires_show_together()
    {
        Names(Around(1, StartZone, PlayerStart, SideDoor))
            .ShouldBe(["PlayerStart", "SideDoor", "StartDoor", "StartZone"]);
    }

    [Fact]
    public void A_selection_with_no_entity_in_it_shows_nothing_and_says_so()
    {
        LogicScopedGraph scoped = Around(2, Id(999));

        scoped.Cards.ShouldBeEmpty();
        scoped.EmptyReason.ShouldBe(LogicEmptyReason.NoEntitySelected);
    }

    [Fact]
    public void Selected_nodes_that_are_not_cards_are_ignored_beside_ones_that_are()
    {
        Names(Around(0, Id(999), Lift)).ShouldBe(["Lift"]);
    }

    [Fact]
    public void A_stub_shows_or_not_as_before_when_an_entity_with_no_wires_is_selected()
    {
        Whole(PlayerStart).Cards.Count(card => card.IsStub).ShouldBe(1);
        Around(2, PlayerStart).Cards.ShouldAllBe(card => !card.IsStub);
        Around(1, PlayerStart, OpenVault).Cards.Count(card => card.IsStub).ShouldBe(1);
    }

    [Theory]
    [InlineData(LogicScopeMode.WholeLevel)]
    [InlineData(LogicScopeMode.AroundSelection)]
    public void A_level_with_no_wires_and_no_entity_selected_says_so(LogicScopeMode mode)
    {
        LogicGraph graph = Graph(Entity(1, "Door", "func_door"));

        LogicScopedGraph scoped = new LogicScope { Mode = mode }.Apply(graph, new HashSet<Guid> { Id(999) });

        scoped.Cards.ShouldBeEmpty();
        scoped.EmptyReason.ShouldBe(LogicEmptyReason.LevelHasNoWires);
        scoped.Counts.HiddenUnwiredEntities.ShouldBe(1);
    }

    [Theory]
    [InlineData(LogicScopeMode.WholeLevel)]
    [InlineData(LogicScopeMode.AroundSelection)]
    public void In_a_level_with_no_wires_the_selected_entities_show(LogicScopeMode mode)
    {
        LogicGraph graph = Graph(Entity(1, "Door", "func_door"), Entity(2, "Gate", "func_door"));

        LogicScopedGraph scoped = new LogicScope { Mode = mode }.Apply(graph, new HashSet<Guid> { Id(2) });

        Names(scoped).ShouldBe(["Gate"]);
        scoped.EmptyReason.ShouldBe(LogicEmptyReason.None);
        scoped.Counts.HiddenUnwiredEntities.ShouldBe(1);
    }

    [Theory]
    [InlineData("button", new[] { "ButtonA", "ButtonB", "LiftButton" })]
    [InlineData("MATH_", new[] { "Presses" })]
    [InlineData("linear mover", new[] { "Lift" })]
    [InlineData("  door ", new[] { "StartDoor", "VaultDoor" })]
    public void A_filter_matches_name_class_or_display_name_in_any_case(string filter, string[] matches)
    {
        LogicScopedGraph scoped = new LogicScope { Filter = filter }.Apply(Vault(), new HashSet<Guid>());

        scoped.Cards.Count.ShouldBe(10, "a filter never removes a card");
        scoped.Cards.Where(card => !scoped.IsDimmed(card)).Select(card => card.Name)
            .Order(StringComparer.Ordinal).ShouldBe(matches);
    }

    [Fact]
    public void An_edge_is_dimmed_only_when_both_its_ends_are()
    {
        LogicGraph graph = Vault();
        LogicScopedGraph scoped = new LogicScope { Filter = "presses" }.Apply(graph, new HashSet<Guid>());

        scoped.IsDimmed(graph.Edge("ButtonA", "Presses")).ShouldBeFalse();
        scoped.IsDimmed(graph.Edge("Presses", "OpenVault")).ShouldBeFalse();
        scoped.IsDimmed(graph.Edge("OpenVault", "Lift")).ShouldBeTrue();
    }

    [Fact]
    public void An_empty_filter_dims_nothing()
    {
        LogicScopedGraph scoped = new LogicScope { Filter = "   " }.Apply(Vault(), new HashSet<Guid>());

        scoped.Cards.ShouldAllBe(card => !scoped.IsDimmed(card));
    }

    [Fact]
    public void Edges_can_be_given_other_labels_without_touching_the_graph()
    {
        LogicGraph graph = Vault();
        LogicScopedGraph scoped = new LogicScope().Apply(graph, new HashSet<Guid>());

        LogicScopedGraph playing = scoped.WithLabels(edge =>
            edge.To.Name == "Lift" && !edge.IsLoop ? new LogicLabel("now", false) : LogicLabel.None);

        playing.Edges.Count(edge => edge.Label.Text == "now").ShouldBe(2);
        playing.Edges.Count(edge => edge.Label.IsEmpty).ShouldBe(7);
        playing.Cards.ShouldBe(scoped.Cards);
        graph.Edge("OpenVault", "Lift").Label.Text.ShouldBe("after 2 s");
    }
}
