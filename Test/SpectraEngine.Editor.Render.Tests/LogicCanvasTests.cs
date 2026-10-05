using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using SpectraEngine.Editor.Shell.Logic;

using static SpectraEngine.Editor.Tests.Logic.LogicFixture;

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
            }
            finally
            {
                other.Close();
            }
        });
    }

    private void On(string state, Action<Graph> body)
    {
        session.On(() =>
        {
            (LogicView view, Window window) = LogicSheetTests.Open(LogicSheetTests.Drive(state, 1123, 880), 1123, 880);

            try
            {
                body(new Graph(view, window));
            }
            finally
            {
                window.Close();
            }
        });
    }

    // One open Logic view, what it was asked for, and points in its scene as
    // points in its window.
    private sealed class Graph
    {
        public Graph(LogicView view, Window window)
        {
            View = view;
            Window = window;
            Canvas = view.FindControl<LogicCanvas>("Graph").ShouldNotBeNull();
            Model = view.Model.ShouldNotBeNull();

            view.SelectRequested += (entity, adds) => Selected.Add((entity, adds));
            view.FrameRequested += Framed.Add;
        }

        public LogicView View { get; }

        public Window Window { get; }

        public LogicCanvas Canvas { get; }

        public LogicViewModel Model { get; }

        public LogicScene Scene => Model.Scene.ShouldNotBeNull();

        public List<(Guid Entity, bool Adds)> Selected { get; } = [];

        public List<Guid> Framed { get; } = [];

        public Point ToWindow(Point scene) =>
            Canvas.TranslatePoint(Model.View.ToView(scene), Window).ShouldNotBeNull();

        public void Click(Point scene, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Point at = ToWindow(scene);
            Window.MouseDown(at, MouseButton.Left, modifiers);
            Window.MouseUp(at, MouseButton.Left, modifiers);
        }
    }
}
