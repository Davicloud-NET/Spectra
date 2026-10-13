using Avalonia;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using System;
using System.Linq;
using static SpectraEngine.Editor.Tests.Logic.LogicScriptFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>
/// What the Logic view does when the connections of a running level's
/// scripts come and go: what is laid out again, where the graph then sits,
/// how far a script reaches near the selection, and what is said of a cut.
/// </summary>
public sealed class LogicScriptChangeTests
{
    private static readonly LogicPanZoom Chosen = new(new Vector(-120, 35), 1.5);

    private readonly LogicGraphInfo _level = Level();

    private LogicViewModel Model(LogicScopeMode mode)
    {
        var model = new LogicViewModel(new FixedWidthRuler())
        {
            Schemas = LogicFixture.Catalog,
            Mode = mode,
            ViewSize = new Size(1123, 800),
        };

        model.Apply(Snapshot(_level));
        return model;
    }

    private static string[] Cards(LogicViewModel model) =>
        [.. model.Scene.ShouldNotBeNull().Cards.Select(card => card.Card.Name).Order()];

    [Fact]
    public void With_the_scripts_key_off_what_scripts_connect_lays_nothing_out_again()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        model.ShowsScripts = false;
        model.Apply(Snapshot(_level, Playing(Designed())));
        LogicScene scene = model.Scene.ShouldNotBeNull();
        int redrawn = 0;
        model.Redraw += () => redrawn++;

        model.Apply(Snapshot(_level, Playing(Running(Sends(Main, Door, "Open", Doors, 57)))));
        model.Apply(Snapshot(_level, Playing(Designed())));

        model.Scene.ShouldBeSameAs(scene);
        redrawn.ShouldBe(0);

        model.ShowsScripts = true;
        model.Scene.ShouldNotBeNull().Cards.Count(card => card.Card.IsScript).ShouldBe(2);
    }

    [Fact]
    public void Near_the_selection_a_connection_a_script_makes_keeps_the_place_the_user_chose()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        model.Apply(Snapshot(_level, Playing(Running(Sends(Main, Door, "Open", Doors, 57))), Door));
        Size before = model.Scene.ShouldNotBeNull().Size;
        model.View = Chosen;

        model.Apply(Snapshot(_level, Playing(Designed()), Door));

        model.Scene.ShouldNotBeNull().Size.ShouldNotBe(before, "the graph grew by a card");
        model.View.ShouldBe(Chosen);
    }

    [Fact]
    public void Near_the_selection_a_connection_that_ends_keeps_the_place_the_user_chose()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        model.Apply(Snapshot(_level, Playing(Designed()), Door));
        model.View = Chosen;

        model.Apply(Snapshot(_level, Playing(Running(Sends(Main, Door, "Open", Doors, 57))), Door));

        Cards(model).ShouldNotContain("Zone7");
        model.View.ShouldBe(Chosen);
    }

    [Fact]
    public void A_graph_nobody_moved_is_fitted_again_when_a_script_connects()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        model.Apply(Snapshot(_level, Playing(Running(Sends(Main, Door, "Open", Doors, 57))), Door));
        LogicPanZoom fitted = model.View;

        model.Apply(Snapshot(_level, Playing(Designed()), Door));

        LogicScene scene = model.Scene.ShouldNotBeNull();
        model.View.ShouldBe(LogicPanZoom.Placed(scene.Size, model.ViewSize, scene.Card("Door7").Bounds));
        model.View.ShouldNotBe(fitted);
    }

    [Fact]
    public void Another_selection_is_fitted_also_when_a_script_connects_in_the_same_snapshot()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        model.Apply(Snapshot(_level, Playing(Running(Sends(Main, Door, "Open", Doors, 57))), Door));
        model.View = Chosen;

        model.Apply(Snapshot(_level, Playing(Designed()), Button));

        model.View.ShouldNotBe(Chosen);
    }

    [Fact]
    public void Near_the_selection_a_script_costs_no_step()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        model.Steps = 2;

        model.Apply(Snapshot(_level, Playing(Designed()), Door));

        // The zone is one wire from the door by way of Main, and Beep comes with the button.
        Cards(model).ShouldBe(["Beep", "Door7", "KeyButton", "Keypad", "Main", "Zone7"]);

        model.Steps = 1;

        Cards(model).ShouldBe(["Door7", "Keypad", "Main", "Zone7"]);
    }

    [Fact]
    public void A_press_on_a_dashed_wire_that_ends_after_the_level_stopped_selects_nothing()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        model.Apply(Snapshot(_level, Playing(Designed())));
        LogicSceneEdge dashed = model.Scene.ShouldNotBeNull().Edges.First(edge => edge.Edge.IsScript);

        model.Apply(Snapshot(_level));
        model.Wiring.CanEdit.ShouldBeTrue();
        model.Wiring.Select(dashed);

        model.Wiring.Selected.ShouldBeNull();
    }

    [Fact]
    public void With_nothing_on_show_there_is_no_legend_for_the_empty_state_to_stand_on()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);

        model.Apply(Snapshot(_level, Playing(Designed())));

        model.OffersWholeLevel.ShouldBeTrue();
        model.EmptyText.ShouldNotBeEmpty();
        model.ScriptsHint.ShouldBeEmpty();
    }

    [Fact]
    public void Scripts_that_made_more_wires_than_the_view_was_given_are_said_in_the_status_row()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        var cut = new LogicScriptWiring { Wires = Designed().Wires, IsTruncated = true };

        model.Apply(Snapshot(_level, Playing(cut)));
        model.Status.Truncated.ShouldBe("Scripts made more than 2,000 wires. The rest are not shown.");

        model.ShowsScripts = false;
        model.Status.Truncated.ShouldBeEmpty();
    }

    [Fact]
    public void A_cards_tooltip_names_the_scripts_inside_it_which_the_card_may_cut_short()
    {
        LogicGraph graph = Graph(running: null);
        LogicCard button = graph.CardOf(Button).ShouldNotBeNull();
        LogicCard door = graph.CardOf(Door).ShouldNotBeNull();

        LogicViewText.Sentence(button).ShouldBe($"KeyButton, {button.DisplayName}. 1 script inside: Beep.");
        LogicViewText.Sentence(door).ShouldBe($"Door7, {door.DisplayName}");
    }
}
