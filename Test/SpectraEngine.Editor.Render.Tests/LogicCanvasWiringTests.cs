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
/// Wiring on the Logic view's graph, through a headless window: what a drag
/// from a card, the menu it opens, a click on a wire and the keys ask for.
/// </summary>
// How the drag feels is for a person. This holds what each gesture asks for.
[Collection(RibbonSessionCollection.Name)]
public sealed class LogicCanvasWiringTests(RibbonSession session)
{
    private static readonly Point Ground = new(6, 6);

    [Fact]
    public void A_drag_from_a_card_to_a_card_waits_for_its_menu_with_the_two_cards()
    {
        On("whole", graph =>
        {
            LogicPanZoom before = graph.Model.View;
            Point lift = graph.Scene.Card("Lift").Header.Center;

            graph.Drag(graph.Scene.Card("StartZone").Header.Center, lift);
            graph.Window.MouseUp(graph.ToWindow(lift), MouseButton.Left);

            LogicWireGesture gesture = graph.Model.Wiring.Gesture;
            gesture.Phase.ShouldBe(LogicWirePhase.Dropped);
            gesture.From.ShouldBeSameAs(graph.Scene.Card("StartZone"));
            gesture.Target.ShouldBeSameAs(graph.Scene.Card("Lift"));
            gesture.Output.ShouldBeNull();

            Texts(graph.Canvas.ShownMenu.ShouldNotBeNull())
                .ShouldBe(["Wire StartZone to Lift", "OnStartTouch", "OnEndTouch", "OnTrigger"]);
            graph.Model.View.ShouldBe(before);
            graph.Selected.ShouldBeEmpty();
            graph.Wired.ShouldBeEmpty();
        });
    }

    [Fact]
    public void A_drag_from_an_output_offers_the_inputs_at_once()
    {
        On("whole", graph =>
        {
            LogicScenePort trigger = graph.Scene.Card("StartZone").PortNamed("OnTrigger", isOutput: true).ShouldNotBeNull();
            Point lift = graph.Scene.Card("Lift").Header.Center;

            graph.Drag(trigger.Row.Center, lift);
            graph.Window.MouseUp(graph.ToWindow(lift), MouseButton.Left);

            graph.Model.Wiring.Gesture.Output.ShouldBe("OnTrigger");
            Texts(graph.Canvas.ShownMenu.ShouldNotBeNull())
                .ShouldBe(["Wire StartZone.OnTrigger to Lift", "Open", "Close", "SetPosition"]);
        });
    }

    [Fact]
    public void A_pick_in_the_menu_asks_for_the_wire_and_for_its_sender_to_be_selected()
    {
        On("whole", graph =>
        {
            Point lift = graph.Scene.Card("Lift").Header.Center;
            graph.Drag(graph.Scene.Card("StartZone").Header.Center, lift);
            graph.Window.MouseUp(graph.ToWindow(lift), MouseButton.Left);

            ContextMenu menu = graph.Canvas.ShownMenu.ShouldNotBeNull();
            MenuItem open = Line(Line(menu, "OnStartTouch"), "Open");
            open.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Close();

            (Guid sender, EntityConnection[] wires) = graph.Wired.ShouldHaveSingleItem();
            sender.ShouldBe(StartZone);
            wires.ShouldBe([Wire("OnTrigger", "StartDoor", "Open"), Wire("OnStartTouch", "Lift", "Open")]);
            graph.Selected.ShouldBe([(StartZone, false)]);
            graph.Model.Wiring.Gesture.Phase.ShouldBe(LogicWirePhase.Done);
            graph.Canvas.ShownMenu.ShouldBeNull();
        });
    }

    [Fact]
    public void A_menu_closed_with_nothing_picked_gives_the_wire_up()
    {
        On("whole", graph =>
        {
            Point lift = graph.Scene.Card("Lift").Header.Center;
            graph.Drag(graph.Scene.Card("StartZone").Header.Center, lift);
            graph.Window.MouseUp(graph.ToWindow(lift), MouseButton.Left);

            graph.Canvas.ShownMenu.ShouldNotBeNull().Close();

            graph.Model.Wiring.Gesture.Phase.ShouldBe(LogicWirePhase.Cancelled);
            graph.Wired.ShouldBeEmpty();
            graph.Selected.ShouldBeEmpty();
        });
    }

    [Fact]
    public void A_level_laid_out_again_closes_the_menu_of_a_dropped_wire()
    {
        On("whole", graph =>
        {
            Point lift = graph.Scene.Card("Lift").Header.Center;
            graph.Drag(graph.Scene.Card("StartZone").Header.Center, lift);
            graph.Window.MouseUp(graph.ToWindow(lift), MouseButton.Left);
            ContextMenu menu = graph.Canvas.ShownMenu.ShouldNotBeNull();

            // Something else changed the wiring while the menu was open.
            graph.Model.Apply(Snapshot(Level(VaultEntities())));
            Dispatcher.UIThread.RunJobs();

            menu.IsOpen.ShouldBeFalse();
            graph.Canvas.ShownMenu.ShouldBeNull();
            graph.Model.Wiring.Gesture.Phase.ShouldBe(LogicWirePhase.Cancelled);
        });
    }

    [Fact]
    public void A_view_taken_out_of_its_window_gives_a_dropped_wire_up_and_closes_its_menu()
    {
        On("whole", graph =>
        {
            Point lift = graph.Scene.Card("Lift").Header.Center;
            graph.Drag(graph.Scene.Card("StartZone").Header.Center, lift);
            graph.Window.MouseUp(graph.ToWindow(lift), MouseButton.Left);
            ContextMenu menu = graph.Canvas.ShownMenu.ShouldNotBeNull();

            graph.Window.Content = null;
            Dispatcher.UIThread.RunJobs();

            menu.IsOpen.ShouldBeFalse();
            graph.Model.Wiring.Gesture.Phase.ShouldBe(LogicWirePhase.Cancelled);
        });
    }

    [Fact]
    public void A_view_taken_out_of_its_window_mid_drag_gives_the_wire_up()
    {
        On("whole", graph =>
        {
            graph.Drag(graph.Scene.Card("StartZone").Header.Center, graph.Scene.Card("Lift").Header.Center);

            graph.Window.Content = null;
            Dispatcher.UIThread.RunJobs();

            graph.Model.Wiring.Gesture.Phase.ShouldBe(LogicWirePhase.Cancelled);
        });
    }

    [Fact]
    public void A_wire_let_go_on_empty_ground_opens_no_menu_and_asks_for_nothing()
    {
        On("whole", graph =>
        {
            graph.Drag(graph.Scene.Card("StartZone").Header.Center, Ground);
            graph.Model.Wiring.Gesture.Phase.ShouldBe(LogicWirePhase.Dragging);

            graph.Window.MouseUp(graph.ToWindow(Ground), MouseButton.Left);

            graph.Model.Wiring.Gesture.Phase.ShouldBe(LogicWirePhase.Cancelled);
            graph.Canvas.ShownMenu.ShouldBeNull();
            graph.Wired.ShouldBeEmpty();
            graph.Selected.ShouldBeEmpty();
        });
    }

    [Fact]
    public void Escape_gives_a_drag_up_and_the_release_after_it_does_nothing()
    {
        On("whole", graph =>
        {
            Point lift = graph.Scene.Card("Lift").Header.Center;
            graph.Drag(graph.Scene.Card("StartZone").Header.Center, lift);

            graph.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            graph.Model.Wiring.Gesture.Phase.ShouldBe(LogicWirePhase.Cancelled);

            graph.Window.MouseUp(graph.ToWindow(lift), MouseButton.Left);

            graph.Canvas.ShownMenu.ShouldBeNull();
            graph.Wired.ShouldBeEmpty();
            graph.Selected.ShouldBeEmpty();
        });
    }

    [Fact]
    public void A_press_of_the_right_button_gives_a_drag_up()
    {
        On("whole", graph =>
        {
            Point lift = graph.ToWindow(graph.Scene.Card("Lift").Header.Center);
            graph.Drag(graph.Scene.Card("StartZone").Header.Center, graph.Scene.Card("Lift").Header.Center);

            graph.Window.MouseDown(lift, MouseButton.Right);
            graph.Model.Wiring.Gesture.Phase.ShouldBe(LogicWirePhase.Cancelled);

            graph.Window.MouseUp(lift, MouseButton.Right);
            graph.Window.MouseUp(lift, MouseButton.Left);

            graph.Canvas.ShownMenu.ShouldBeNull();
            graph.Wired.ShouldBeEmpty();
            graph.Selected.ShouldBeEmpty();
        });
    }

    [Fact]
    public void A_drag_from_a_card_lets_the_selected_wire_go()
    {
        On("whole", graph =>
        {
            graph.Click(graph.Scene.Edge("StartZone", "StartDoor").Segments[0].At(0.5));

            graph.Drag(graph.Scene.Card("Presses").Header.Center, Ground);

            graph.Model.Wiring.Selected.ShouldBeNull();
        });
    }

    [Fact]
    public void While_a_level_plays_a_drag_from_a_card_starts_nothing()
    {
        On("playing", graph =>
        {
            Point lift = graph.Scene.Card("Lift").Header.Center;

            graph.Drag(graph.Scene.Card("StartZone").Header.Center, lift);
            graph.Window.MouseUp(graph.ToWindow(lift), MouseButton.Left);

            graph.Model.Wiring.Gesture.Phase.ShouldBe(LogicWirePhase.Idle);
            graph.Canvas.ShownMenu.ShouldBeNull();
            graph.Wired.ShouldBeEmpty();
        });
    }

    [Fact]
    public void The_status_row_says_what_was_wired_and_the_sentence_fits_the_row()
    {
        On("whole", graph =>
        {
            Point lift = graph.Scene.Card("Lift").Header.Center;
            graph.Drag(graph.Scene.Card("StartZone").Header.Center, lift);
            graph.Window.MouseUp(graph.ToWindow(lift), MouseButton.Left);

            ContextMenu menu = graph.Canvas.ShownMenu.ShouldNotBeNull();
            Line(Line(menu, "OnStartTouch"), "Open").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Close();

            // The engine answers with the level as it is now.
            LogicEntityInfo[] entities = VaultEntities();
            entities[8] = entities[8] with { Wires = graph.Wired.ShouldHaveSingleItem().Wires };
            graph.Model.Apply(Snapshot(Level(entities), null, StartZone));
            Dispatcher.UIThread.RunJobs();

            TextBlock news = graph.View.FindControl<TextBlock>("NewsText").ShouldNotBeNull();
            news.IsVisible.ShouldBeTrue();
            news.Text.ShouldBe("Wired StartZone.OnStartTouch to Lift.Open.");
            news.Bounds.Width.ShouldBeGreaterThan(0);
            graph.View.FindControl<DockPanel>("Notes").ShouldNotBeNull().IsVisible.ShouldBeFalse();
        });
    }

    private static string[] Texts(ItemsControl menu) =>
    [
        .. menu.Items.OfType<MenuItem>().Select(line => line.Header.ShouldBeOfType<TextBlock>().Text ?? "")
    ];

    private static MenuItem Line(ItemsControl menu, string text) =>
        menu.Items.OfType<MenuItem>().Single(line => line.Header is TextBlock words && words.Text == text);

    private void On(string state, Action<LogicViewHarness> body) => LogicViewHarness.On(session, state, body);
}
