using Avalonia;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using System;
using System.Collections.Generic;
using System.Linq;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;
using static SpectraEngine.Editor.Tests.Logic.LogicPlayFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>
/// What the Logic view's model rebuilds when, and what it tells the view
/// about the vault level while it is edited.
/// </summary>
public sealed class LogicViewModelTests
{
    private readonly LogicGraphInfo _level = Level(VaultEntities());

    private static LogicViewModel Model(LogicScopeMode mode = LogicScopeMode.WholeLevel) =>
        new(new FixedWidthRuler()) { Schemas = Catalog, Mode = mode, ViewSize = new Size(1123, 800) };

    private static int Redraws(LogicViewModel model, Action act)
    {
        int redraws = 0;
        void Count() => redraws++;

        model.Redraw += Count;
        act();
        model.Redraw -= Count;
        return redraws;
    }

    [Fact]
    public void A_model_starts_near_the_selection_two_steps_out_with_nothing_to_show()
    {
        var model = new LogicViewModel(new FixedWidthRuler());

        model.Mode.ShouldBe(LogicScopeMode.AroundSelection);
        model.IsAroundSelection.ShouldBeTrue();
        model.Steps.ShouldBe(2);
        model.Filter.ShouldBe("");
        model.View.ShouldBe(LogicPanZoom.Identity);
        model.Scene.ShouldBeNull();
        model.EmptyText.ShouldBe("");
        model.Status.ShouldBe(LogicStatus.None);
    }

    [Fact]
    public void A_snapshot_with_the_same_wiring_rebuilds_and_redraws_nothing()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level));

        LogicGraph? graph = model.Graph;
        LogicScopedGraph? shown = model.Shown;
        LogicScene? scene = model.Scene;

        int redraws = Redraws(model, () => model.Apply(Snapshot(_level)));

        scene.ShouldNotBeNull().Cards.Count.ShouldBe(10);
        model.Graph.ShouldBeSameAs(graph);
        model.Shown.ShouldBeSameAs(shown);
        model.Scene.ShouldBeSameAs(scene);
        redraws.ShouldBe(0);
    }

    [Fact]
    public void Other_wiring_rebuilds_the_graph_and_everything_made_from_it()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level));
        LogicGraph? graph = model.Graph;
        LogicScene? scene = model.Scene;

        model.Apply(Snapshot(Level(VaultEntities())));

        model.Graph.ShouldNotBeSameAs(graph);
        model.Scene.ShouldNotBeSameAs(scene);
    }

    [Fact]
    public void Other_schemas_rebuild_the_graph()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level));
        LogicGraph? graph = model.Graph;

        model.Schemas = null;

        model.Graph.ShouldNotBeSameAs(graph);
        model.Graph.ShouldNotBeNull().Card("Presses").IsKnownClass.ShouldBeFalse();
    }

    [Fact]
    public void In_the_whole_level_a_selection_moves_nothing_and_rings_its_card()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level));
        LogicScopedGraph? shown = model.Shown;
        LogicScene scene = model.Scene.ShouldNotBeNull();

        int redraws = Redraws(model, () => model.Apply(Snapshot(_level, null, OpenVault)));

        model.Shown.ShouldBeSameAs(shown);
        model.Scene.ShouldBeSameAs(scene);
        redraws.ShouldBe(1);
        scene.Cards.Where(card => model.IsSelected(card.Card)).Select(card => card.Card.Name).ShouldBe(["OpenVault"]);
    }

    [Fact]
    public void The_wires_that_touch_a_selected_card_are_in_focus_and_a_broken_one_stays_broken()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level, null, OpenVault));

        string[] Ends(LogicWireLook look) =>
        [
            .. model.Wires.Where(wire => wire.State.Look == look)
                .Select(wire => $"{wire.Edge.Edge.From.Name}>{wire.Edge.Edge.To.Name}")
                .Order(StringComparer.Ordinal),
        ];

        Ends(LogicWireLook.Focus).ShouldBe(["OpenVault>Lift", "OpenVault>VaultDoor", "Presses>OpenVault"]);
        Ends(LogicWireLook.Broken).ShouldBe(["OpenVault>VaultDor"]);
        model.Wires.Count(wire => wire.State.Look == LogicWireLook.Plain).ShouldBe(5);
    }

    [Fact]
    public void Selecting_something_that_has_no_card_redraws_nothing()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level));

        Redraws(model, () => model.Apply(Snapshot(_level, null, PlayerStart))).ShouldBe(0);
    }

    [Fact]
    public void Near_the_selection_another_selection_shows_and_lays_out_other_cards()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        model.Apply(Snapshot(_level, null, StartZone));
        LogicScene first = model.Scene.ShouldNotBeNull();

        model.Apply(Snapshot(_level, null, LiftButton));
        LogicScene second = model.Scene.ShouldNotBeNull();

        first.Cards.Select(card => card.Card.Name).Order().ShouldBe(["StartDoor", "StartZone"]);
        second.Cards.Select(card => card.Card.Name).Order().ShouldBe(["Lift", "LiftButton", "OpenVault"]);
        second.ShouldNotBeSameAs(first);
    }

    [Fact]
    public void Near_the_selection_a_selection_that_shows_the_same_cards_lays_out_nothing()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        model.Steps = 1;
        model.Apply(Snapshot(_level, null, StartZone));
        LogicScene scene = model.Scene.ShouldNotBeNull();

        // A brush selected with it has no card and changes nothing on show.
        model.Apply(Snapshot(_level, null, StartZone, Id(900)));

        model.Scene.ShouldBeSameAs(scene);
    }

    [Fact]
    public void The_filter_dims_what_it_leaves_out_and_moves_nothing()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level));
        LogicScene scene = model.Scene.ShouldNotBeNull();
        LogicScopedGraph? shown = model.Shown;

        int redraws = Redraws(model, () => model.Filter = "presses");

        model.Scene.ShouldBeSameAs(scene);
        model.Shown.ShouldNotBeSameAs(shown);
        redraws.ShouldBe(1);

        scene.Cards.Where(card => !model.IsDimmed(card.Card)).Select(card => card.Card.Name).ShouldBe(["Presses"]);
        model.IsDimmed(scene.Edge("ButtonA", "Presses").Edge).ShouldBeFalse();
        model.IsDimmed(scene.Edge("OpenVault", "Lift").Edge).ShouldBeTrue();
    }

    [Fact]
    public void Steps_show_more_or_less_near_the_selection_and_change_nothing_in_the_whole_level()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        model.Apply(Snapshot(_level, null, ButtonA));
        int two = model.Scene.ShouldNotBeNull().Cards.Count;

        model.Steps = 1;
        int one = model.Scene.ShouldNotBeNull().Cards.Count;

        model.Mode = LogicScopeMode.WholeLevel;
        LogicScopedGraph? whole = model.Shown;
        model.Steps = 4;

        one.ShouldBeLessThan(two);
        model.Shown.ShouldBeSameAs(whole);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 4)]
    [InlineData(40, 6)]
    public void Steps_stay_between_one_and_six(int asked, int taken)
    {
        LogicViewModel model = Model();
        var changed = new List<string?>();
        model.PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        model.Steps = asked;

        model.Steps.ShouldBe(taken);
        changed.ShouldContain(nameof(LogicViewModel.Steps));
    }

    [Fact]
    public void A_step_count_out_of_range_that_changes_nothing_still_tells_the_field_that_shows_it()
    {
        LogicViewModel model = Model();
        model.Steps = 6;
        int told = 0;
        model.PropertyChanged += (_, change) => told += change.PropertyName == nameof(LogicViewModel.Steps) ? 1 : 0;

        model.Steps = 9;
        model.Steps = 6;

        told.ShouldBe(1);
    }

    [Fact]
    public void The_entities_on_show_are_listed_without_the_stubs_and_announced_when_they_change()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        var seen = new List<int>();
        model.ShownEntitiesChanged += () => seen.Add(model.Scene?.Cards.Count ?? -1);

        model.Apply(Snapshot(_level, null, OpenVault));
        model.Apply(Snapshot(_level, null, OpenVault));
        model.Filter = "door";

        // The handler found the scene those entities are in already laid out.
        seen.ShouldBe([model.Scene.ShouldNotBeNull().Cards.Count]);
        model.ShownEntityIds.ShouldNotContain(Guid.Empty);
        model.ShownEntityIds.ShouldContain(OpenVault);
        model.ShownEntityIds.Count.ShouldBe(model.Scene.Cards.Count(card => !card.Card.IsStub));

        model.Apply(Snapshot(_level, null, StartZone));

        seen.Count.ShouldBe(2);
        model.ShownEntityIds.ShouldBe([StartZone, StartDoor]);
    }

    [Fact]
    public void Wiring_that_goes_away_is_not_announced_and_wiring_that_comes_back_is()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        model.Apply(Snapshot(_level, null, StartZone));

        int announced = 0;
        model.ShownEntitiesChanged += () => announced++;

        // The engine was told the view is hidden and sends no wiring. A
        // request sent now would have it send wiring again.
        model.Apply(Snapshot(null, null, StartZone));

        model.ShownEntityIds.ShouldBeEmpty();
        announced.ShouldBe(0);

        // Shown again, the same entities have to be asked for again.
        model.Apply(Snapshot(_level, null, StartZone));

        model.ShownEntityIds.ShouldBe([StartZone, StartDoor]);
        announced.ShouldBe(1);
    }

    [Fact]
    public void The_status_row_counts_what_is_on_show_and_names_what_is_wrong()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level));

        model.Status.ShouldBe(new LogicStatus(
            "9 entities",
            "9 wires",
            "1 wire goes nowhere",
            "PlayerStart and 3 more entities have no wires and are not shown.",
            "")
        {
            // Where the link about the wire that goes nowhere leads.
            GoingNowhereSender = OpenVault,
            UnwiredShort = "4 entities have no wires.",
        });
        model.Hint.ShouldBe("Double-click a card to frame it in the viewport.");
        model.EmptyText.ShouldBe("");
    }

    [Fact]
    public void Near_a_selection_the_status_row_does_not_list_the_rest_of_the_level()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        model.Apply(Snapshot(_level, null, StartZone));

        model.Status.ShouldBe(new LogicStatus("2 entities", "1 wire", "", "", ""));
        model.Status.GoingNowhereSender.ShouldBeNull();
    }

    [Fact]
    public void A_level_that_was_cut_short_says_how_much_of_it_is_shown()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(new LogicGraphInfo { Entities = VaultEntities(), TotalEntities = 5200 }));

        model.Status.Truncated.ShouldBe("Showing the first 13 of 5,200 entities.");
        LogicViewText.Truncated(4000, 5200).ShouldBe("Showing the first 4,000 of 5,200 entities.");
    }

    [Fact]
    public void With_nothing_selected_the_view_says_so_and_offers_the_whole_level()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        model.Apply(Snapshot(_level));

        model.EmptyText.ShouldBe("Select an entity to see what it is wired to, or show the whole level.");
        model.OffersWholeLevel.ShouldBeTrue();
        model.Status.ShouldBe(LogicStatus.None);

        model.WholeLevelCommand.Execute(null);

        model.IsWholeLevel.ShouldBeTrue();
        model.EmptyText.ShouldBe("");
        model.OffersWholeLevel.ShouldBeFalse();
    }

    [Fact]
    public void A_selection_without_wires_and_a_level_without_wires_each_get_their_own_sentence()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);

        model.Apply(Snapshot(_level, null, PlayerStart));
        model.EmptyText.ShouldBe("Nothing is wired to or from the selection.");
        model.OffersWholeLevel.ShouldBeFalse();

        model.Apply(Snapshot(Level(Entity(1, "PlayerStart", "info_player_start")), null, PlayerStart));
        model.EmptyText.ShouldBe(
            "No entity in this level is wired yet. Select one and press Add under Sends in the Properties panel.");
    }

    [Fact]
    public void A_wire_in_words_names_both_ends_what_it_was_authored_with_and_what_is_wrong()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level));
        LogicScene scene = model.Scene.ShouldNotBeNull();

        string Sentence(string from, string to) => model.FaceOf(scene.Edge(from, to)).ShouldNotBeNull().Sentence;

        Sentence("OpenVault", "VaultDoor").ShouldBe("OpenVault.OnTrigger sends Open to VaultDoor, once.");
        Sentence("Presses", "OpenVault").ShouldBe("Presses.OnHitMax sends Trigger to OpenVault.");
        Sentence("OpenVault", "VaultDor").ShouldBe(
            "OpenVault.OnTrigger sends Close to VaultDor. Nothing is named VaultDor.");
    }

    [Fact]
    public void A_point_is_looked_up_through_the_pan_and_zoom()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level));
        LogicScene scene = model.Scene.ShouldNotBeNull();
        model.View = new LogicPanZoom(new Vector(40, -25), 0.5);

        LogicSceneCard card = scene.Card("Presses");
        LogicSceneEdge labelled = scene.Edge("OpenVault", "VaultDoor");

        model.HitTest(model.View.ToView(card.Header.Center)).Card.ShouldBeSameAs(card);
        model.HitTest(model.View.ToView(labelled.LabelBounds.ShouldNotBeNull().Center)).Kind.ShouldBe(LogicHitKind.Label);
        model.HitTest(new Point(-500, -500)).ShouldBe(LogicHit.None);
    }

    [Fact]
    public void Far_out_a_label_is_not_drawn_and_a_point_on_it_is_on_its_wire()
    {
        LogicViewModel model = Model();
        model.Apply(Snapshot(_level));
        LogicSceneEdge labelled = model.Scene.ShouldNotBeNull().Edge("OpenVault", "VaultDoor");
        Rect label = labelled.LabelBounds.ShouldNotBeNull();
        model.View = new LogicPanZoom(default, 0.3);

        LogicHit hit = model.HitTest(model.View.ToView(label.Center));

        hit.Kind.ShouldBe(LogicHitKind.Edge);
        hit.Edge.ShouldBeSameAs(labelled);
    }
}
