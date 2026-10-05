using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using SpectraEngine.Editor.Shell.Logic;

using static SpectraEngine.Editor.Tests.Logic.LogicFixture;
using static SpectraEngine.Editor.Tests.Logic.LogicPlayFixture;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The pointer on the Logic view's graph, through a headless window: what a
/// click, a double click, a drag, the wheel and a rest over a wire do.
/// </summary>
// How it feels is for a person. This holds what each gesture asks for.
[Collection(RibbonSessionCollection.Name)]
public sealed class LogicCanvasTests(RibbonSession session)
{
    private static readonly Point Ground = new(6, 6);

    [Fact]
    public void A_click_on_a_card_asks_for_its_entity_and_Ctrl_adds_it()
    {
        On("whole", graph =>
        {
            // Two cards: a second click on the first would be a double click.
            graph.Click(graph.Scene.Card("Presses").Header.Center);
            graph.Click(graph.Scene.Card("StartDoor").Header.Center, RawInputModifiers.Control);

            graph.Selected.ShouldBe([(Presses, false), (StartDoor, true)]);
            graph.Framed.ShouldBeEmpty();
        });
    }

    [Fact]
    public void A_click_on_a_port_row_asks_for_the_card_it_is_on()
    {
        On("whole", graph =>
        {
            graph.Click(graph.Scene.Card("Lift").PortNamed("Close", isOutput: false).ShouldNotBeNull().Row.Center);

            graph.Selected.ShouldBe([(Lift, false)]);
        });
    }

    [Fact]
    public void A_click_on_a_wire_or_its_label_asks_for_the_sender()
    {
        On("whole", graph =>
        {
            LogicSceneEdge wire = graph.Scene.Edge("OpenVault", "VaultDoor");
            LogicSceneEdge bare = graph.Scene.Edge("StartZone", "StartDoor");

            graph.Click(wire.LabelBounds.ShouldNotBeNull().Center);
            graph.Click(bare.Segments[0].At(0.5));

            graph.Selected.ShouldBe([(OpenVault, false), (StartZone, false)]);
        });
    }

    [Fact]
    public void A_click_on_the_card_of_a_name_nothing_has_and_on_empty_ground_asks_for_nothing()
    {
        On("whole", graph =>
        {
            graph.Click(graph.Scene.Card("VaultDor").Header.Center);
            graph.Click(Ground);

            graph.Selected.ShouldBeEmpty();
        });
    }

    [Fact]
    public void A_double_click_on_a_card_selects_it_once_and_asks_to_frame_it()
    {
        On("whole", graph =>
        {
            Point card = graph.ToWindow(graph.Scene.Card("VaultDoor").Header.Center);

            graph.Window.MouseDown(card, MouseButton.Left);
            graph.Window.MouseUp(card, MouseButton.Left);
            graph.Window.MouseDown(card, MouseButton.Left);
            graph.Window.MouseUp(card, MouseButton.Left);

            graph.Selected.ShouldBe([(VaultDoor, false)]);
            graph.Framed.ShouldBe([VaultDoor]);
        });
    }

    [Fact]
    public void A_double_click_frames_the_card_it_began_on_though_the_first_click_moved_the_graph()
    {
        On("around-relay", graph =>
        {
            Point door = graph.ToWindow(graph.Scene.Card("VaultDoor").Header.Center);
            Point onCanvas = graph.Window.TranslatePoint(door, graph.Canvas).ShouldNotBeNull();

            graph.Window.MouseDown(door, MouseButton.Left);
            graph.Window.MouseUp(door, MouseButton.Left);

            // The engine answers the first click before the second press
            // comes. Near the selection that is another graph, fitted anew.
            graph.Model.Apply(Snapshot(Level(VaultEntities()), null, VaultDoor));
            Dispatcher.UIThread.RunJobs();
            graph.Model.HitTest(onCanvas).Card.ShouldNotBeSameAs(graph.Scene.Card("VaultDoor"));

            graph.Window.MouseDown(door, MouseButton.Left);
            graph.Window.MouseUp(door, MouseButton.Left);

            graph.Selected.ShouldBe([(VaultDoor, false)]);
            graph.Framed.ShouldBe([VaultDoor]);
        });
    }

    [Fact]
    public void A_press_on_the_graph_takes_the_keyboard_from_the_filter_box()
    {
        On("whole", graph =>
        {
            TextBox filter = graph.View.FindControl<TextBox>("FilterBox").ShouldNotBeNull();
            filter.Focus();
            filter.IsFocused.ShouldBeTrue();

            graph.Click(graph.Scene.Card("Presses").Header.Center);

            filter.IsFocused.ShouldBeFalse();
            graph.Canvas.IsFocused.ShouldBeTrue();
        });
    }

    [Fact]
    public void A_wheel_notch_during_a_drag_stays_and_the_drag_goes_on_from_there()
    {
        On("whole", graph =>
        {
            Point from = graph.ToWindow(Ground);

            graph.Window.MouseDown(from, MouseButton.Left);
            graph.Window.MouseMove(from + new Vector(30, 12));
            graph.Window.MouseWheel(from + new Vector(30, 12), new Vector(0, 1));
            LogicPanZoom zoomed = graph.Model.View;

            graph.Window.MouseMove(from + new Vector(50, 22));
            graph.Window.MouseUp(from + new Vector(50, 22), MouseButton.Left);

            zoomed.Zoom.ShouldBeGreaterThan(LogicPanZoom.Fit(graph.Scene.Size, graph.Model.ViewSize).Zoom);
            graph.Model.View.ShouldBe(zoomed.MovedBy(new Vector(20, 10)));
        });
    }

    [Fact]
    public void A_level_that_starts_is_fitted_into_the_room_the_event_strip_leaves()
    {
        On("around-relay", graph =>
        {
            double before = graph.Canvas.Bounds.Height;

            graph.Model.Apply(Snapshot(Level(VaultEntities()), VaultPlaying(), OpenVault));
            Dispatcher.UIThread.RunJobs();

            graph.Canvas.Bounds.Height.ShouldBe(before - LogicViewFit.EventStrip);
            graph.Model.ViewSize.ShouldBe(graph.Canvas.Bounds.Size);
            graph.Model.View.ShouldBe(LogicPanZoom.Fit(graph.Scene.Size, graph.Canvas.Bounds.Size));
        });
    }

    [Fact]
    public void A_drag_on_empty_ground_pans_and_selects_nothing()
    {
        On("whole", graph =>
        {
            LogicPanZoom before = graph.Model.View;
            Point from = graph.ToWindow(Ground);

            graph.Window.MouseDown(from, MouseButton.Left);
            graph.Window.MouseMove(from + new Vector(30, 12));
            graph.Window.MouseMove(from + new Vector(64, -40));
            graph.Window.MouseUp(from + new Vector(64, -40), MouseButton.Left);

            graph.Model.View.ShouldBe(before.MovedBy(new Vector(64, -40)));
            graph.Selected.ShouldBeEmpty();
        });
    }

    [Fact]
    public void A_drag_that_starts_on_a_card_neither_pans_nor_selects()
    {
        On("whole", graph =>
        {
            LogicPanZoom before = graph.Model.View;
            Point from = graph.ToWindow(graph.Scene.Card("Presses").Header.Center);

            graph.Window.MouseDown(from, MouseButton.Left);
            graph.Window.MouseMove(from + new Vector(40, 25));
            graph.Window.MouseUp(from + new Vector(40, 25), MouseButton.Left);

            graph.Model.View.ShouldBe(before);
            graph.Selected.ShouldBeEmpty();
        });
    }

    [Fact]
    public void The_middle_button_pans_from_anywhere()
    {
        On("whole", graph =>
        {
            LogicPanZoom before = graph.Model.View;
            Point from = graph.ToWindow(graph.Scene.Card("Presses").Header.Center);

            graph.Window.MouseDown(from, MouseButton.Middle);
            graph.Window.MouseMove(from + new Vector(-50, 20));
            graph.Window.MouseUp(from + new Vector(-50, 20), MouseButton.Middle);

            graph.Model.View.ShouldBe(before.MovedBy(new Vector(-50, 20)));
            graph.Selected.ShouldBeEmpty();
        });
    }

    [Fact]
    public void The_wheel_zooms_about_the_pointer()
    {
        On("whole", graph =>
        {
            Point held = graph.Scene.Card("OpenVault").Bounds.Center;
            Point pointer = graph.ToWindow(held);
            double before = graph.Model.View.Zoom;

            graph.Window.MouseWheel(pointer, new Vector(0, 1));
            graph.Window.MouseWheel(pointer, new Vector(0, 1));
            Point after = graph.ToWindow(held);

            graph.Model.View.Zoom.ShouldBe(before * LogicDrawMetrics.WheelZoom * LogicDrawMetrics.WheelZoom, 1e-9);
            Math.Abs(after.X - pointer.X).ShouldBeLessThanOrEqualTo(1);
            Math.Abs(after.Y - pointer.Y).ShouldBeLessThanOrEqualTo(1);

            graph.Window.MouseWheel(pointer, new Vector(0, -1));
            graph.Model.View.Zoom.ShouldBe(before * LogicDrawMetrics.WheelZoom, 1e-9);
        });
    }

    [Fact]
    public void A_wire_under_the_pointer_says_what_it_does_and_a_card_says_its_name()
    {
        On("whole", graph =>
        {
            LogicSceneEdge wire = graph.Scene.Edge("OpenVault", "VaultDoor");

            graph.Window.MouseMove(graph.ToWindow(wire.LabelBounds.ShouldNotBeNull().Center));
            ToolTip.GetTip(graph.Canvas).ShouldBe("OpenVault.OnTrigger sends Open to VaultDoor, once.");

            graph.Window.MouseMove(graph.ToWindow(graph.Scene.Card("Presses").Header.Center));
            ToolTip.GetTip(graph.Canvas).ShouldBe("Presses, math_counter");

            graph.Window.MouseMove(graph.ToWindow(Ground));
            ToolTip.GetTip(graph.Canvas).ShouldBeNull();
        });
    }

    [Fact]
    public void A_view_moved_to_another_window_still_draws_its_model_and_answers_the_pointer()
    {
        On("whole", graph =>
        {
            var other = new Window { Width = 700, Height = 500 };
            graph.Window.Content = null;
            other.Content = graph.View;
            other.Show();
            Dispatcher.UIThread.RunJobs();

            try
            {
                Point card = graph.Scene.Card("Presses").Header.Center;
                Point inOther = graph.Canvas.TranslatePoint(graph.Model.View.ToView(card), other).ShouldNotBeNull();

                other.MouseDown(inOther, MouseButton.Left);
                other.MouseUp(inOther, MouseButton.Left);

                graph.Model.ViewSize.ShouldBe(graph.Canvas.Bounds.Size);
                graph.Selected.ShouldBe([(Presses, false)]);

                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                LogicSheetTests.DistinctColours(other.GetLastRenderedFrame().ShouldNotBeNull())
                    .ShouldBeGreaterThan(8, "the view drew nothing in the window it moved to");
            }
            finally
            {
                other.Close();
            }
        });
    }

    private void On(string state, Action<LogicViewHarness> body) => LogicViewHarness.On(session, state, body);
}
