using Avalonia;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;
using static SpectraEngine.Editor.Tests.Logic.LogicPlayFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>
/// What the Logic view's model shows of a selected entity with no wires: its
/// card, what the rows round the graph say of it, and the wire a drag from it
/// or onto it asks for.
/// </summary>
public sealed class LogicViewModelUnwiredTests
{
    private readonly LogicGraphInfo _level = Level(VaultEntities());
    private readonly List<(Guid Sender, EntityConnection[] Wires)> _asked = [];

    private LogicViewModel Model(LogicScopeMode mode)
    {
        var model = new LogicViewModel(new FixedWidthRuler())
        {
            Schemas = Catalog,
            Mode = mode,
            ViewSize = new Size(1123, 800),
        };

        model.Wiring.Requested += (sender, wires) => _asked.Add((sender, [.. wires]));
        return model;
    }

    private static string[] Names(LogicViewModel model) =>
        [.. model.Scene.ShouldNotBeNull().Cards.Select(card => card.Card.Name)];

    // Presses on one card's header and lets go on another's.
    private static bool Drag(LogicViewModel model, string from, string onto)
    {
        LogicScene scene = model.Scene.ShouldNotBeNull();
        Point start = model.View.ToView(scene.Card(from).Header.Center);
        Point end = model.View.ToView(scene.Card(onto).Header.Center);

        model.Wiring.Press(start, model.HitTest(start));
        model.Wiring.Move(model.View.ToView(new Point(2, 2)), null);
        model.Wiring.Move(end, model.HitTest(end).Card);
        return model.Wiring.Release();
    }

    private static LogicWireMenuItem Line(LogicViewModel model, string output, string input) =>
        model.Wiring.Menu().ShouldNotBeNull()
            .Items.Single(item => item.Text == output)
            .Items.Single(item => item.Text == input);

    [Fact]
    public void Near_the_selection_an_entity_with_no_wires_shows_its_card_alone()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);

        model.Apply(Snapshot(_level, null, SideDoor));

        Names(model).ShouldBe(["SideDoor"]);
        model.IsSelected(model.Scene.ShouldNotBeNull().Card("SideDoor").Card).ShouldBeTrue();
        model.EmptyText.ShouldBe("");
        model.OffersWholeLevel.ShouldBeFalse();
    }

    [Fact]
    public void Near_the_selection_two_entities_with_no_wires_show_as_two_cards()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);

        model.Apply(Snapshot(_level, null, SideDoor, Clock));

        Names(model).ShouldBe(["Clock", "SideDoor"]);
        model.Wires.ShouldBeEmpty();
    }

    [Fact]
    public void In_the_whole_level_selecting_an_entity_with_no_wires_adds_its_card_and_moves_no_other()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        model.Apply(Snapshot(_level));
        LogicScene before = model.Scene.ShouldNotBeNull();

        model.Apply(Snapshot(_level, null, SideDoor));
        LogicScene with = model.Scene.ShouldNotBeNull();

        with.Cards.Count.ShouldBe(11);
        with.CardOf(SideDoor).ShouldNotBeNull();
        foreach (LogicSceneCard card in before.Cards)
            with.Card(card.Card.Name).Bounds.ShouldBe(card.Bounds, card.Card.Name);
    }

    [Fact]
    public void In_the_whole_level_deselecting_it_takes_its_card_away_and_moves_no_other()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        model.Apply(Snapshot(_level, null, SideDoor, PlayerStart));
        LogicScene with = model.Scene.ShouldNotBeNull();

        model.Apply(Snapshot(_level, null, OpenVault));
        LogicScene without = model.Scene.ShouldNotBeNull();

        without.Cards.Count.ShouldBe(10);
        foreach (LogicSceneCard card in without.Cards)
            with.Card(card.Card.Name).Bounds.ShouldBe(card.Bounds, card.Card.Name);
    }

    [Fact]
    public void In_the_whole_level_a_selection_that_shows_the_same_cards_lays_out_nothing()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        model.Apply(Snapshot(_level, null, SideDoor));
        LogicScopedGraph? shown = model.Shown;
        LogicScene? scene = model.Scene;

        // A wired entity and a brush selected with it change no card on show.
        model.Apply(Snapshot(_level, null, SideDoor, OpenVault, Id(900)));

        model.Shown.ShouldBeSameAs(shown);
        model.Scene.ShouldBeSameAs(scene);
    }

    [Theory]
    [InlineData(LogicScopeMode.WholeLevel)]
    [InlineData(LogicScopeMode.AroundSelection)]
    public void The_engine_is_asked_for_the_state_of_a_shown_entity_with_no_wires(LogicScopeMode mode)
    {
        LogicViewModel model = Model(mode);
        model.Apply(Snapshot(_level, null, StartZone));
        int announced = 0;
        model.ShownEntitiesChanged += () => announced++;

        model.Apply(Snapshot(_level, null, StartZone, SideDoor));

        model.ShownEntityIds.ShouldContain(SideDoor);
        model.ShownEntityIds[^1].ShouldBe(SideDoor);
        announced.ShouldBe(1);
    }

    [Fact]
    public void While_a_level_plays_a_card_with_no_wires_shows_the_state_the_engine_gave()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        var playing = new LogicPlayInfo { Tick = Tick, Time = Time, States = [new(SideDoor, "state", "closed")] };

        model.Apply(Snapshot(_level, playing, SideDoor));

        model.Scene.ShouldNotBeNull().Card("SideDoor").StateRow.ShouldNotBeNull();
        model.TryGetState(SideDoor, out LogicEntityState state).ShouldBeTrue();
        state.Value.ShouldBe("closed");
        model.Hint.ShouldBe(LogicViewText.PlayingHint);
    }

    [Fact]
    public void The_status_row_counts_the_card_and_says_how_to_wire_it()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);

        model.Apply(Snapshot(_level, null, SideDoor));

        model.Status.ShouldBe(new LogicStatus("1 entity", "0 wires", "", "", "")
        {
            Hint = "Drag from this card onto another to wire it. Select both to see both.",
            HintShort = "Drag from this card onto another to wire it.",
        });
        model.Hint.ShouldBe(model.Status.Hint);
    }

    [Fact]
    public void In_the_whole_level_the_status_row_leaves_a_shown_entity_out_of_the_ones_not_shown()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);

        model.Apply(Snapshot(_level, null, PlayerStart));

        model.Status.Entities.ShouldBe("10 entities");
        model.Status.Unwired.ShouldBe("Exit and 2 more entities have no wires and are not shown.");
        model.Status.UnwiredShort.ShouldBe("3 more entities have no wires.");
        model.Status.Hint.ShouldBe(LogicViewText.UnwiredHint);
    }

    [Fact]
    public void With_every_entity_without_wires_on_show_the_status_row_lists_none_as_not_shown()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);

        model.Apply(Snapshot(_level, null, PlayerStart, Exit, SideDoor, Clock));

        model.Status.Unwired.ShouldBe("");
        model.Status.UnwiredShort.ShouldBe("");
    }

    [Theory]
    [InlineData(1, 0, "1 entity has no wires.")]
    [InlineData(1, 2, "1 more entity has no wires.")]
    [InlineData(3, 1, "3 more entities have no wires.")]
    [InlineData(0, 1, "")]
    public void The_short_sentence_says_more_when_others_without_wires_are_shown(int hidden, int shown, string sentence)
    {
        LogicViewText.UnwiredShort(hidden, shown).ShouldBe(sentence);
    }

    [Fact]
    public void The_hint_goes_back_to_the_usual_one_when_no_card_without_wires_is_shown()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        model.Apply(Snapshot(_level, null, SideDoor));
        var raised = new List<string?>();
        model.PropertyChanged += (_, change) => raised.Add(change.PropertyName);

        model.Apply(Snapshot(_level, null, OpenVault));

        model.Hint.ShouldBe(LogicViewText.EditingHint);
        model.Status.HintShort.ShouldBe(LogicViewText.EditingHintShort);
        raised.ShouldContain(nameof(LogicViewModel.Hint));
    }

    [Theory]
    [InlineData(LogicScopeMode.WholeLevel)]
    [InlineData(LogicScopeMode.AroundSelection)]
    public void A_level_with_no_wires_shows_the_cards_of_the_selected_entities(LogicScopeMode mode)
    {
        LogicViewModel model = Model(mode);
        LogicGraphInfo bare = Level(Entity(1, "Door", "func_door"), Entity(2, "Gate", "func_door"), Entity(3, "Bell", "point_sound"));

        model.Apply(Snapshot(bare, null, Id(1), Id(3)));

        Names(model).ShouldBe(["Bell", "Door"]);
        model.EmptyText.ShouldBe("");
        model.ShownEntityIds.ShouldBe([Id(1), Id(3)]);
    }

    [Fact]
    public void A_selection_with_no_entity_in_it_says_to_select_one_and_offers_the_whole_level()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);

        model.Apply(Snapshot(_level, null, Id(900)));

        model.Scene.ShouldNotBeNull().Cards.ShouldBeEmpty();
        model.EmptyText.ShouldBe("Select an entity to see what it is wired to, or show the whole level.");
        model.OffersWholeLevel.ShouldBeTrue();
    }

    [Fact]
    public void A_drag_from_a_card_with_no_wires_asks_for_a_list_of_the_one_wire()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        model.Apply(Snapshot(_level, null, SideDoor));

        Drag(model, "SideDoor", "Lift").ShouldBeTrue();
        model.Wiring.Menu().ShouldNotBeNull().Items.Select(item => item.Text)
            .ShouldBe(["OnOpen", "OnClose", "OnFullyOpen", "OnFullyClosed"]);

        model.Wiring.Pick(Line(model, "OnOpen", "Close")).ShouldBe(SideDoor);

        (Guid sender, EntityConnection[] wires) = _asked.ShouldHaveSingleItem();
        sender.ShouldBe(SideDoor);
        wires.ShouldBe([Wire("OnOpen", "Lift", "Close")]);
    }

    [Fact]
    public void A_wire_dropped_on_a_card_with_no_wires_names_it_as_the_target()
    {
        LogicViewModel model = Model(LogicScopeMode.WholeLevel);
        model.Apply(Snapshot(_level, null, SideDoor));

        Drag(model, "StartZone", "SideDoor").ShouldBeTrue();
        model.Wiring.Menu().ShouldNotBeNull().Title.ShouldBe("Wire StartZone to SideDoor");
        model.Wiring.Pick(Line(model, "OnStartTouch", "Open"));

        (Guid sender, EntityConnection[] wires) = _asked.ShouldHaveSingleItem();
        sender.ShouldBe(StartZone);
        wires.ShouldBe([Wire("OnTrigger", "StartDoor", "Open"), Wire("OnStartTouch", "SideDoor", "Open")]);
    }

    [Fact]
    public void Two_cards_with_no_wires_are_wired_by_a_drag_from_one_onto_the_other()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        model.Apply(Snapshot(_level, null, SideDoor, Clock));

        Drag(model, "Clock", "SideDoor").ShouldBeTrue();
        model.Wiring.Pick(Line(model, "OnTimer", "Toggle"));

        (Guid sender, EntityConnection[] wires) = _asked.ShouldHaveSingleItem();
        sender.ShouldBe(Clock);
        wires.ShouldBe([Wire("OnTimer", "SideDoor", "Toggle")]);
    }

    [Fact]
    public void Once_the_level_shows_the_first_wire_both_ends_are_wired_cards_and_the_row_says_so()
    {
        LogicViewModel model = Model(LogicScopeMode.AroundSelection);
        model.Apply(Snapshot(_level, null, SideDoor, Clock));
        Drag(model, "Clock", "SideDoor");
        model.Wiring.Pick(Line(model, "OnTimer", "Toggle"));

        // The engine answers with the wire, and the view selected its sender.
        LogicEntityInfo[] entities = VaultEntities();
        entities[12] = entities[12] with { Wires = _asked[0].Wires };
        model.Apply(Snapshot(Level(entities), null, Clock));

        Names(model).ShouldBe(["Clock", "SideDoor"]);
        model.Scene.ShouldNotBeNull().Edge("Clock", "SideDoor").Edge.Input.ShouldBe("Toggle");
        model.Status.News.ShouldBe("Wired Clock.OnTimer to SideDoor.Toggle.");
        model.Hint.ShouldBe(LogicViewText.EditingHint);
    }
}
