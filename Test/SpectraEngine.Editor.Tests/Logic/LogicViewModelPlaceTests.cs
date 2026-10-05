using Avalonia;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using System;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;
using static SpectraEngine.Editor.Tests.Logic.LogicPlayFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>
/// Where the Logic view's model puts the graph: when a new scene is fitted,
/// when it keeps the place the user chose, and what a session's end leaves.
/// </summary>
public sealed class LogicViewModelPlaceTests
{
    private static readonly Size Pane = new(700, 400);
    private static readonly LogicPanZoom Chosen = new(new Vector(-120, 35), 1.5);

    private readonly LogicGraphInfo _level = Level(VaultEntities());

    private static LogicViewModel Model(LogicScopeMode mode) =>
        new(new FixedWidthRuler()) { Schemas = Catalog, Mode = mode, ViewSize = Pane };

    private static LogicPanZoom Fitted(LogicViewModel model) =>
        LogicPanZoom.Fit(model.Scene.ShouldNotBeNull().Size, Pane);

    [Fact]
    public void The_first_scene_is_fitted_into_the_view()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);

        model.Apply(Snapshot(_level));

        model.View.ShouldBe(Fitted(model));
        model.View.Zoom.ShouldBeLessThan(1);
    }

    [Fact]
    public void A_scene_that_comes_before_the_view_has_a_size_is_fitted_once_it_has_one()
    {
        var model = new LogicViewModel(new FixedWidthRuler()) { Schemas = Catalog, Mode = LogicScopeMode.WholeLevel };
        model.Apply(Snapshot(_level));
        model.View.ShouldBe(LogicPanZoom.Identity);

        model.ViewSize = Pane;

        model.View.ShouldBe(Fitted(model));
    }

    [Fact]
    public void In_the_whole_level_a_new_scene_keeps_the_place_the_user_chose()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        model.Apply(Snapshot(_level));
        model.View = Chosen;

        // A wire was added: other wiring, the same cards and one more.
        LogicEntityInfo[] entities = VaultEntities();
        entities[12] = Entity(13, "Clock", "logic_timer", Wire("OnTimer", "SideDoor", "Toggle"));
        model.Apply(Snapshot(Level(entities)));
        model.Apply(Snapshot(Level(entities), VaultPlaying()));

        model.Scene.ShouldNotBeNull().Cards.Count.ShouldBe(12);
        model.View.ShouldBe(Chosen);
    }

    [Fact]
    public void In_the_whole_level_another_level_altogether_is_fitted()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        model.Apply(Snapshot(_level));
        model.View = Chosen;

        model.Apply(Snapshot(Level(
            Entity(40, "Lever", "func_button", Wire("OnPressed", "Gate", "Open")),
            Entity(41, "Gate", "func_door"))));

        model.View.ShouldBe(Fitted(model));
    }

    [Fact]
    public void Near_the_selection_every_new_scene_is_fitted()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        model.Apply(Snapshot(_level, null, StartZone));
        model.View.ShouldBe(Fitted(model));
        model.View = Chosen;

        model.Apply(Snapshot(_level, null, OpenVault));

        model.View.ShouldBe(Fitted(model));
    }

    [Fact]
    public void Near_the_selection_a_scene_that_did_not_change_keeps_its_place()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        model.Apply(Snapshot(_level, null, StartZone));
        model.View = Chosen;

        model.Filter = "zone";
        model.Apply(Snapshot(_level, null, StartZone, Id(900)));

        model.View.ShouldBe(Chosen);
    }

    [Fact]
    public void Another_mode_is_another_picture_and_is_fitted()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        model.Apply(Snapshot(_level, null, StartZone));
        model.View = Chosen;

        model.AroundSelectionCommand.Execute(null);

        model.Scene.ShouldNotBeNull().Cards.Count.ShouldBe(2);
        model.View.ShouldBe(Fitted(model));
    }

    [Fact]
    public void The_fit_and_actual_size_commands_place_the_graph()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        model.Apply(Snapshot(_level));
        LogicPanZoom fitted = model.View;
        Point middle = new(Pane.Width / 2, Pane.Height / 2);
        Point held = fitted.ToScene(middle);

        model.ActualSizeCommand.Execute(null);

        model.View.Zoom.ShouldBe(1);
        Math.Abs(model.View.ToView(held).X - middle.X).ShouldBeLessThanOrEqualTo(0.5);
        Math.Abs(model.View.ToView(held).Y - middle.Y).ShouldBeLessThanOrEqualTo(0.5);

        model.FitCommand.Execute(null);

        model.View.ShouldBe(fitted);
    }

    [Fact]
    public void Centring_on_an_entity_puts_its_card_in_the_middle_and_keeps_the_zoom()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        model.Apply(Snapshot(_level));
        model.View = Chosen;
        Point card = model.Scene.ShouldNotBeNull().Card("OpenVault").Bounds.Center;

        model.CenterOn(OpenVault);
        Point shown = model.View.ToView(card);

        model.View.Zoom.ShouldBe(Chosen.Zoom);
        Math.Abs(shown.X - Pane.Width / 2).ShouldBeLessThanOrEqualTo(0.5);
        Math.Abs(shown.Y - Pane.Height / 2).ShouldBeLessThanOrEqualTo(0.5);

        model.CenterOn(PlayerStart);
        model.View.ToView(card).ShouldBe(shown);
    }

    [Fact]
    public void A_session_that_ends_takes_the_level_and_leaves_what_the_user_chose()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        model.Steps = 4;
        model.Filter = "door";
        model.Apply(Snapshot(_level, VaultPlaying(), OpenVault));
        model.View = Chosen;

        int announced = 0;
        int redraws = 0;
        model.ShownEntitiesChanged += () => announced++;
        model.Redraw += () => redraws++;

        model.Reset();

        model.Mode.ShouldBe(LogicScopeMode.WholeLevel);
        model.Steps.ShouldBe(4);
        model.Filter.ShouldBe("door");
        model.View.ShouldBe(Chosen);

        model.Schemas.ShouldBeNull();
        model.Graph.ShouldBeNull();
        model.Shown.ShouldBeNull();
        model.Scene.ShouldBeNull();
        model.Wires.ShouldBeEmpty();
        model.IsPlaying.ShouldBeFalse();
        model.Events.ShouldBeEmpty();
        model.Status.ShouldBe(LogicStatus.None);
        model.EmptyText.ShouldBe("");
        model.GoingNowhereSender.ShouldBeNull();
        model.ShownEntityIds.ShouldBeEmpty();
        model.TryGetState(VaultDoor, out _).ShouldBeFalse();
        announced.ShouldBe(1);
        redraws.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_session_that_comes_back_with_the_same_cards_finds_them_where_they_were()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        model.Apply(Snapshot(_level));
        model.View = Chosen;

        model.Reset();
        int announced = 0;
        model.ShownEntitiesChanged += () => announced++;
        model.Schemas = Catalog;
        model.Apply(Snapshot(Level(VaultEntities())));

        model.Scene.ShouldNotBeNull().Cards.Count.ShouldBe(10);
        model.View.ShouldBe(Chosen);

        // The new session's engine has to be asked for their state again.
        announced.ShouldBe(1);
    }

    [Fact]
    public void A_session_that_comes_back_with_other_cards_is_fitted()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        model.Apply(Snapshot(_level));
        model.View = Chosen;

        model.Reset();
        model.Schemas = Catalog;
        LogicEntityInfo[] entities = VaultEntities();
        entities[12] = Entity(13, "Clock", "logic_timer", Wire("OnTimer", "SideDoor", "Toggle"));
        model.Apply(Snapshot(Level(entities)));

        model.View.ShouldBe(Fitted(model));
    }
}
