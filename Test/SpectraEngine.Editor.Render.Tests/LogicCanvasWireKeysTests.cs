using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;

using static SpectraEngine.Editor.Tests.Logic.LogicFixture;
using static SpectraEngine.Editor.Tests.Logic.LogicPlayFixture;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The selected wire on the Logic view's graph, through a headless window:
/// what selects a wire and what lets it go, what Delete and the wire's own
/// menu remove, and who has the keyboard afterwards.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class LogicCanvasWireKeysTests(RibbonSession session)
{
    private static readonly Point Ground = new(6, 6);

    private static Point OnZoneWire(LogicViewHarness graph) =>
        graph.Scene.Edge("StartZone", "StartDoor").Segments[0].At(0.5);

    [Fact]
    public void A_click_on_a_wire_selects_it()
    {
        On("whole", graph =>
        {
            graph.Click(OnZoneWire(graph));

            graph.Model.Wiring.IsSelected(graph.Scene.Edge("StartZone", "StartDoor")).ShouldBeTrue();
        });
    }

    [Fact]
    public void A_click_on_a_wires_label_selects_the_wire()
    {
        On("whole", graph =>
        {
            graph.Click(graph.Scene.Edge("OpenVault", "VaultDoor").LabelBounds.ShouldNotBeNull().Center);

            graph.Model.Wiring.Selected.ShouldNotBeNull().NodeId.ShouldBe(OpenVault);
        });
    }

    [Fact]
    public void A_click_on_a_card_lets_the_selected_wire_go()
    {
        On("whole", graph =>
        {
            graph.Click(OnZoneWire(graph));

            graph.Click(graph.Scene.Card("Presses").Header.Center);

            graph.Model.Wiring.Selected.ShouldBeNull();
        });
    }

    [Fact]
    public void A_click_on_empty_ground_lets_the_selected_wire_go()
    {
        On("whole", graph =>
        {
            graph.Click(OnZoneWire(graph));

            graph.Click(Ground);

            graph.Model.Wiring.Selected.ShouldBeNull();
        });
    }

    [Theory]
    [InlineData(PhysicalKey.Delete)]
    [InlineData(PhysicalKey.Backspace)]
    public void With_a_wire_selected_the_key_removes_the_wire_and_never_reaches_the_window(PhysicalKey key)
    {
        On("whole", graph =>
        {
            int reached = 0;
            graph.Window.AddHandler(InputElement.KeyDownEvent, (_, _) => reached++, handledEventsToo: false);
            graph.Click(graph.Scene.Edge("OpenVault", "Lift").Segments[0].At(0.5));

            graph.Window.KeyPressQwerty(key, RawInputModifiers.None);

            (Guid sender, EntityConnection[] wires) = graph.Wired.ShouldHaveSingleItem();
            sender.ShouldBe(OpenVault);
            wires.ShouldBe([Wire("OnTrigger", "VaultDoor", "Open", times: 1), Wire("OnTrigger", "VaultDor", "Close")]);
            reached.ShouldBe(0, "the key ends at the canvas");
            graph.Model.Wiring.Selected.ShouldBeNull();
        });
    }

    [Fact]
    public void Delete_on_the_canvas_with_a_wire_selected_is_marked_handled()
    {
        On("whole", graph =>
        {
            graph.Click(graph.Scene.Edge("OpenVault", "Lift").Segments[0].At(0.5));
            var press = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete };

            graph.Canvas.RaiseEvent(press);

            press.Handled.ShouldBeTrue();
            graph.Wired.Count.ShouldBe(1);
        });
    }

    [Fact]
    public void With_no_wire_selected_the_key_is_left_to_the_window()
    {
        On("whole", graph =>
        {
            int reached = 0;
            graph.Window.AddHandler(InputElement.KeyDownEvent, (_, _) => reached++, handledEventsToo: false);
            graph.Click(graph.Scene.Card("Presses").Header.Center);

            graph.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);

            reached.ShouldBe(1);
            graph.Wired.ShouldBeEmpty();
        });
    }

    [Fact]
    public void The_selected_wire_is_let_go_when_the_keyboard_leaves_the_canvas()
    {
        On("whole", graph =>
        {
            graph.Click(OnZoneWire(graph));

            FilterBox(graph).Focus();

            graph.Model.Wiring.Selected.ShouldBeNull();
        });
    }

    [Fact]
    public void Delete_after_the_keyboard_left_the_canvas_removes_no_wire()
    {
        On("whole", graph =>
        {
            graph.Click(OnZoneWire(graph));
            FilterBox(graph).Focus();

            graph.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);

            graph.Wired.ShouldBeEmpty();
        });
    }

    [Fact]
    public void A_right_click_on_a_wire_selects_it_and_offers_to_remove_it()
    {
        On("whole", graph =>
        {
            graph.Click(OnZoneWire(graph), button: MouseButton.Right);

            graph.Model.Wiring.IsSelected(graph.Scene.Edge("StartZone", "StartDoor")).ShouldBeTrue();
            graph.Selected.ShouldBe([(StartZone, false)]);
            Texts(graph.Canvas.ShownMenu.ShouldNotBeNull()).ShouldBe(["Remove wire"]);
        });
    }

    [Fact]
    public void The_line_of_a_wires_menu_removes_the_wire()
    {
        On("whole", graph =>
        {
            graph.Click(OnZoneWire(graph), button: MouseButton.Right);
            ContextMenu menu = graph.Canvas.ShownMenu.ShouldNotBeNull();

            menu.Items.OfType<MenuItem>().Single().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Close();

            (Guid sender, EntityConnection[] wires) = graph.Wired.ShouldHaveSingleItem();
            sender.ShouldBe(StartZone);
            wires.ShouldBeEmpty();
        });
    }

    [Fact]
    public void A_right_click_on_a_card_or_on_empty_ground_opens_no_menu()
    {
        On("whole", graph =>
        {
            graph.Click(graph.Scene.Card("Presses").Header.Center, button: MouseButton.Right);
            graph.Click(Ground, button: MouseButton.Right);

            graph.Canvas.ShownMenu.ShouldBeNull();
            graph.Selected.ShouldBeEmpty();
        });
    }

    [Fact]
    public void A_wire_stays_selected_while_its_menu_has_the_keyboard()
    {
        On("whole", graph =>
        {
            graph.Click(OnZoneWire(graph));

            graph.Click(OnZoneWire(graph), button: MouseButton.Right);

            graph.Canvas.IsFocused.ShouldBeFalse();
            graph.Model.Wiring.IsSelected(graph.Scene.Edge("StartZone", "StartDoor")).ShouldBeTrue();
        });
    }

    [Fact]
    public void A_wires_menu_closed_with_nothing_picked_lets_the_wire_go()
    {
        On("whole", graph =>
        {
            graph.Click(OnZoneWire(graph), button: MouseButton.Right);

            graph.Canvas.ShownMenu.ShouldNotBeNull().Close();

            graph.Model.Wiring.Selected.ShouldBeNull();
            graph.Wired.ShouldBeEmpty();
        });
    }

    // A real menu is a window of its own, and a click that closes it reaches
    // what it is on before the menu goes. Here the box takes the keyboard
    // first by hand, as it does then.
    [Fact]
    public void A_wires_menu_that_closes_leaves_the_keyboard_where_it_is()
    {
        On("whole", graph =>
        {
            TextBox filter = FilterBox(graph);
            graph.Click(OnZoneWire(graph), button: MouseButton.Right);
            ContextMenu menu = graph.Canvas.ShownMenu.ShouldNotBeNull();

            filter.Focus();
            menu.Close();

            filter.IsFocused.ShouldBeTrue();
        });
    }

    [Fact]
    public void A_dropped_wires_menu_that_closes_leaves_the_keyboard_where_it_is()
    {
        On("whole", graph =>
        {
            TextBox filter = FilterBox(graph);
            Point lift = graph.Scene.Card("Lift").Header.Center;
            graph.Drag(graph.Scene.Card("StartZone").Header.Center, lift);
            graph.Window.MouseUp(graph.ToWindow(lift), MouseButton.Left);
            ContextMenu menu = graph.Canvas.ShownMenu.ShouldNotBeNull();

            filter.Focus();
            menu.Close();

            filter.IsFocused.ShouldBeTrue();
        });
    }

    [Fact]
    public void A_level_that_starts_closes_a_wires_menu()
    {
        On("whole", graph =>
        {
            graph.Click(OnZoneWire(graph), button: MouseButton.Right);
            ContextMenu menu = graph.Canvas.ShownMenu.ShouldNotBeNull();

            graph.Model.Apply(Snapshot(Level(VaultEntities()), VaultPlaying()));
            Dispatcher.UIThread.RunJobs();

            menu.IsOpen.ShouldBeFalse();
            graph.Canvas.ShownMenu.ShouldBeNull();
        });
    }

    [Fact]
    public void A_wire_that_leaves_the_level_closes_its_menu()
    {
        On("whole", graph =>
        {
            graph.Click(OnZoneWire(graph), button: MouseButton.Right);
            ContextMenu menu = graph.Canvas.ShownMenu.ShouldNotBeNull();

            LogicEntityInfo[] entities = VaultEntities();
            entities[8] = entities[8] with { Wires = [] };
            graph.Model.Apply(Snapshot(Level(entities)));
            Dispatcher.UIThread.RunJobs();

            menu.IsOpen.ShouldBeFalse();
        });
    }

    [Fact]
    public void The_line_of_a_menu_opened_on_one_wire_does_not_remove_another()
    {
        On("whole", graph =>
        {
            graph.Click(OnZoneWire(graph), button: MouseButton.Right);
            MenuItem line = graph.Canvas.ShownMenu.ShouldNotBeNull().Items.OfType<MenuItem>().Single();

            graph.Model.Wiring.Select(graph.Scene.Edge("OpenVault", "Lift"));
            line.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            graph.Wired.ShouldBeEmpty();
        });
    }

    [Fact]
    public void While_a_level_plays_a_click_on_a_wire_selects_no_wire()
    {
        On("playing", graph =>
        {
            graph.Click(OnZoneWire(graph));

            graph.Model.Wiring.Selected.ShouldBeNull();
        });
    }

    [Fact]
    public void While_a_level_plays_a_right_click_on_a_wire_opens_no_menu()
    {
        On("playing", graph =>
        {
            graph.Click(OnZoneWire(graph), button: MouseButton.Right);

            graph.Canvas.ShownMenu.ShouldBeNull();
            graph.Selected.ShouldBeEmpty();
        });
    }

    private static TextBox FilterBox(LogicViewHarness graph) =>
        graph.View.FindControl<TextBox>("FilterBox").ShouldNotBeNull();

    private static string[] Texts(ItemsControl menu) =>
    [
        .. menu.Items.OfType<MenuItem>().Select(line => line.Header.ShouldBeOfType<TextBlock>().Text ?? "")
    ];

    private void On(string state, Action<LogicViewHarness> body) => LogicViewHarness.On(session, state, body);
}
