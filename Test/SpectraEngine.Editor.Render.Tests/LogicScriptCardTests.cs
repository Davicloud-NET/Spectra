using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;

using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using SpectraEngine.Editor.Tests.Logic;

using static SpectraEngine.Editor.Tests.Logic.LogicScriptFixture;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// Two things of the Logic view's scripts that need the real theme and a
/// real pointer: a dashed wire seen from far out, and a double click on the
/// script of a node that is an entity too.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class LogicScriptCardTests(RibbonSession session)
{
    [Fact]
    public void Far_out_a_dashed_wire_keeps_a_gap_between_its_dashes()
    {
        session.On(() =>
        {
            var pens = new LogicWirePens();

            foreach (double zoom in new[] { 1.0, 0.5, 0.25, 0.1 })
            {
                pens.SetZoom(zoom);

                foreach (LogicWireLook look in new[] { LogicWireLook.Script, LogicWireLook.ScriptFiring })
                {
                    IPen pen = pens.Pen(look);
                    IReadOnlyList<double> dashes = pen.DashStyle.ShouldNotBeNull().Dashes.ShouldNotBeNull();

                    // A round cap takes half the pen's width off each end of a gap.
                    double gapOnScreen = (dashes[1] - 1) * pen.Thickness * zoom;
                    gapOnScreen.ShouldBeGreaterThan(0.5, $"{look} at a zoom of {zoom}");
                }
            }
        });
    }

    [Fact]
    public void A_double_click_tells_the_script_of_a_node_from_the_entity_on_the_same_node()
    {
        session.On(() =>
        {
            // The door carries a script of its own, which hears the door.
            LogicViewModel model = LogicSheetTests.Drive("scripts-playing", 1123, 880);
            LogicScriptInfo[] scripts = [.. Scripts(), new LogicScriptInfo(Door, "Door7", Door)];
            model.Apply(Snapshot(
                Level(LogicScriptFixture.Entities(), scripts),
                Playing(Running(Hears(Door, Door, "OnFullyOpen", "Scripts/Door.luau", 2)))));

            (LogicView view, Window window) = LogicSheetTests.Open(model, 1123, 880);

            try
            {
                var graph = new LogicViewHarness(view, window);
                var opened = new List<Guid>();
                view.OpenScriptRequested += opened.Add;

                DoubleClick(graph, graph.Scene.Cards.Single(card => card.Card.IsScript).Header.Center);

                opened.ShouldBe([Door]);
                graph.Framed.ShouldBeEmpty();

                LogicSceneCard entity = graph.Scene.Cards.Single(card => card.Card.Name == "Door7" && card.Card.IsEntity);
                DoubleClick(graph, entity.Header.Center);

                opened.ShouldBe([Door]);
                graph.Framed.ShouldBe([Door]);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void DoubleClick(LogicViewHarness graph, Point scene)
    {
        Point at = graph.ToWindow(scene);
        graph.Window.MouseDown(at, MouseButton.Left);
        graph.Window.MouseUp(at, MouseButton.Left);
        graph.Window.MouseDown(at, MouseButton.Left);
        graph.Window.MouseUp(at, MouseButton.Left);
    }
}
