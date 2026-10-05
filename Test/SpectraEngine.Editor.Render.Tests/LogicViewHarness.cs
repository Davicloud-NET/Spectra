using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;

using SpectraEngine.Core.Entities;
using SpectraEngine.Editor.Shell.Logic;

namespace SpectraEngine.Editor.Render.Tests;

// One open Logic view, what it was asked for, and points in its scene as
// points in its window.
internal sealed class LogicViewHarness
{
    public LogicViewHarness(LogicView view, Window window)
    {
        View = view;
        Window = window;
        Canvas = view.FindControl<LogicCanvas>("Graph").ShouldNotBeNull();
        Model = view.Model.ShouldNotBeNull();

        view.SelectRequested += (entity, adds) => Selected.Add((entity, adds));
        view.FrameRequested += Framed.Add;
        view.WiringRequested += (entity, wires) => Wired.Add((entity, [.. wires]));
    }

    public LogicView View { get; }

    public Window Window { get; }

    public LogicCanvas Canvas { get; }

    public LogicViewModel Model { get; }

    public LogicScene Scene => Model.Scene.ShouldNotBeNull();

    public List<(Guid Entity, bool Adds)> Selected { get; } = [];

    public List<Guid> Framed { get; } = [];

    public List<(Guid Entity, EntityConnection[] Wires)> Wired { get; } = [];

    // Opens the vault level in a state LogicSheetTests names, runs the body
    // and closes the window again.
    public static void On(RibbonSession session, string state, Action<LogicViewHarness> body)
    {
        session.On(() =>
        {
            (LogicView view, Window window) = LogicSheetTests.Open(LogicSheetTests.Drive(state, 1123, 880), 1123, 880);

            try
            {
                body(new LogicViewHarness(view, window));
            }
            finally
            {
                window.Close();
            }
        });
    }

    public Point ToWindow(Point scene) =>
        Canvas.TranslatePoint(Model.View.ToView(scene), Window).ShouldNotBeNull();

    public void Click(
        Point scene,
        RawInputModifiers modifiers = RawInputModifiers.None,
        MouseButton button = MouseButton.Left)
    {
        Point at = ToWindow(scene);
        Window.MouseDown(at, button, modifiers);
        Window.MouseUp(at, button, modifiers);
    }

    // Presses on one point of the scene and moves to another. The button stays down.
    public void Drag(Point from, Point to)
    {
        Window.MouseDown(ToWindow(from), MouseButton.Left);
        Window.MouseMove(ToWindow(to));
    }
}
