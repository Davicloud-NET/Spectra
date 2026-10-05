using SpectraEngine.Editor.Shell.Logic;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>What the menu of a dropped wire offers.</summary>
public sealed class LogicWireMenuTests
{
    private static readonly LogicGraph Level = Graph(
        Entity(1, "Zone", "trigger_multiple", Wire("OnTrigger", "Door", "Open")),
        Entity(2, "Door", "func_door"),
        Entity(3, "Thing", "prop_of_no_schema", Wire("OnBroken", "Door", "Close")),
        Entity(4, "Start", "info_player_start", Wire("OnSpawn", "Thing", "Shake")));

    private static string[] Texts(IReadOnlyList<LogicWireMenuItem> items) => [.. items.Select(item => item.Text)];

    [Fact]
    public void A_drag_from_an_output_offers_the_inputs_of_the_receivers_class()
    {
        LogicWireMenu menu = LogicWireMenu.For(Level.Card("Zone"), "OnStartTouch", Level.Card("Door"));

        menu.Title.ShouldBe("Wire Zone.OnStartTouch to Door");
        Texts(menu.Items).ShouldBe(["Open", "Close", "Toggle"]);
        menu.Items.ShouldAllBe(item => item.MakesWire && item.Output == "OnStartTouch" && item.Input == item.Text);
    }

    [Fact]
    public void A_drag_from_the_card_offers_the_senders_outputs_and_each_opens_the_inputs()
    {
        LogicWireMenu menu = LogicWireMenu.For(Level.Card("Zone"), null, Level.Card("Door"));

        menu.Title.ShouldBe("Wire Zone to Door");
        Texts(menu.Items).ShouldBe(["OnStartTouch", "OnEndTouch", "OnTrigger"]);

        LogicWireMenuItem endTouch = menu.Items[1];
        endTouch.MakesWire.ShouldBeFalse();
        Texts(endTouch.Items).ShouldBe(["Open", "Close", "Toggle"]);
        endTouch.Items.ShouldAllBe(item => item.MakesWire && item.Output == "OnEndTouch" && item.Input == item.Text);
    }

    [Fact]
    public void A_receiver_whose_class_has_no_schema_gets_one_line_that_leaves_the_input_empty()
    {
        LogicWireMenu menu = LogicWireMenu.For(Level.Card("Zone"), "OnTrigger", Level.Card("Thing"));

        LogicWireMenuItem only = menu.Items.ShouldHaveSingleItem();
        only.Text.ShouldBe("No input: prop_of_no_schema lists none");
        only.MakesWire.ShouldBeTrue();
        only.Output.ShouldBe("OnTrigger");
        only.Input.ShouldBe("");
    }

    [Fact]
    public void A_sender_whose_class_has_no_schema_gets_one_line_that_leaves_the_output_empty()
    {
        LogicWireMenu menu = LogicWireMenu.For(Level.Card("Thing"), null, Level.Card("Door"));

        LogicWireMenuItem only = menu.Items.ShouldHaveSingleItem();
        only.Text.ShouldBe("No output: prop_of_no_schema lists none");
        only.MakesWire.ShouldBeFalse();
        Texts(only.Items).ShouldBe(["Open", "Close", "Toggle"]);
        only.Items.ShouldAllBe(item => item.Output == "");
    }

    [Fact]
    public void A_name_only_a_wire_spells_is_not_offered()
    {
        LogicWireMenu menu = LogicWireMenu.For(Level.Card("Thing"), null, Level.Card("Thing"));

        // OnBroken and Shake are on the card, but no class lists them.
        LogicWireMenuItem output = menu.Items.ShouldHaveSingleItem();
        output.Items.ShouldHaveSingleItem().Text.ShouldBe("No input: prop_of_no_schema lists none");
    }

    [Fact]
    public void A_class_that_is_known_and_lists_nothing_is_said_by_its_display_name()
    {
        LogicWireMenu from = LogicWireMenu.For(Level.Card("Start"), null, Level.Card("Door"));
        LogicWireMenu to = LogicWireMenu.For(Level.Card("Zone"), "OnTrigger", Level.Card("Start"));

        from.Items.ShouldHaveSingleItem().Text.ShouldBe("No output: Player start lists none");
        to.Items.ShouldHaveSingleItem().Text.ShouldBe("No input: Player start lists none");
    }
}
