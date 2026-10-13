using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

using SpectraEngine.Editor.Shell.Logic;
using SpectraEngine.Editor.Tests.Logic;

using static SpectraEngine.Editor.Tests.Logic.LogicScriptFixture;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The Logic view with a level's scripts, through a headless window: the
/// Scripts key, the legend, the cards of scripts and their dashed wires, and
/// what a double click on a script's card asks for.
/// </summary>
// How it looks is for a person, in the sheets LogicSheetTests writes. This
// holds what is on show and that nothing stands on anything else.
[Collection(RibbonSessionCollection.Name)]
public sealed class LogicScriptViewTests(RibbonSession session)
{
    private static readonly Color ScriptWire = Color.Parse("#A79BE0");
    private static readonly Color LitScriptWire = Color.Parse("#D2CAFF");
    private static readonly Color ScriptHead = Color.Parse("#2E2B3A");

    [Fact]
    public void While_editing_the_legend_says_that_scripts_connect_when_the_level_runs()
    {
        LogicViewHarness.On(session, "scripts-editing", graph =>
        {
            Border legend = graph.View.FindControl<Border>("Legend").ShouldNotBeNull();

            legend.IsEffectivelyVisible.ShouldBeTrue();
            Words(legend).ShouldBe(
            [
                "Wired in the level",
                "Made by a script, while the level runs",
                "Scripts connect when the level runs. Press Play to see what they are wired to.",
            ]);
            graph.Scene.Cards.ShouldAllBe(card => !card.Card.IsScript);
            graph.Scene.Card("KeyButton").ScriptsNote.ShouldBe("1 script inside: Beep");
        });
    }

    [Fact]
    public void While_the_level_runs_the_legend_says_what_a_script_card_is()
    {
        LogicViewHarness.On(session, "scripts-playing", graph =>
        {
            Border legend = graph.View.FindControl<Border>("Legend").ShouldNotBeNull();

            legend.IsEffectivelyVisible.ShouldBeTrue();
            Words(legend)[^1].ShouldBe("A script shows as a name. Double-click it to open the code.");
            graph.Scene.Cards.Count(card => card.Card.IsScript).ShouldBe(2);
        });
    }

    [Theory]
    [InlineData("scripts-editing")]
    [InlineData("scripts-playing")]
    [InlineData("scripts-busy")]
    public void The_legend_stands_in_the_room_kept_for_it_and_on_no_card_of_a_fitted_graph(string state)
    {
        LogicViewHarness.On(session, state, graph =>
        {
            Border legend = graph.View.FindControl<Border>("Legend").ShouldNotBeNull();
            Rect plate = Within(legend, graph.Canvas);

            (graph.Canvas.Bounds.Height - plate.Y).ShouldBeLessThanOrEqualTo(LogicViewFit.LegendRoom);
            graph.Model.ViewSize.Height.ShouldBe(graph.Canvas.Bounds.Height - LogicViewFit.LegendRoom);

            graph.Model.Fit();
            foreach (LogicSceneCard card in graph.Scene.Cards)
            {
                var onScreen = new Rect(
                    graph.Model.View.ToView(card.Bounds.TopLeft), graph.Model.View.ToView(card.Bounds.BottomRight));
                onScreen.Intersects(plate).ShouldBeFalse($"{card.Card.Name} at {onScreen} is under the legend at {plate}");
            }
        });
    }

    [Theory]
    [InlineData("scripts-editing")]
    [InlineData("scripts-playing")]
    public void The_legend_shows_whole_or_not_at_all_at_any_width_and_height(string state)
    {
        var ruler = new LogicTextRuler();

        session.On(() =>
        {
            (LogicView view, Window window) = LogicSheetTests.Open(LogicSheetTests.Drive(state, 1200, 500), 1200, 500);

            try
            {
                Border legend = view.FindControl<Border>("Legend").ShouldNotBeNull();
                Control graph = view.FindControl<LogicCanvas>("Graph").ShouldNotBeNull();

                for (double width = 1200; width >= 180; width -= 20)
                {
                    foreach (double height in new[] { 500.0, 320.0, 200.0, 90.0 })
                    {
                        window.Width = width;
                        window.Height = height;
                        Dispatcher.UIThread.RunJobs();

                        if (!legend.IsVisible)
                            continue;

                        string where = $"{state} at {width} by {height}";
                        Rect plate = Within(legend, graph);
                        plate.X.ShouldBeGreaterThanOrEqualTo(0, where);
                        plate.Y.ShouldBeGreaterThanOrEqualTo(0, where);
                        plate.Right.ShouldBeLessThanOrEqualTo(graph.Bounds.Width, where);
                        plate.Bottom.ShouldBeLessThanOrEqualTo(graph.Bounds.Height, where);
                        (graph.Bounds.Height - plate.Y).ShouldBeLessThanOrEqualTo(LogicViewFit.LegendRoom, where);

                        foreach (TextBlock text in legend.GetVisualDescendants().OfType<TextBlock>())
                        {
                            text.Bounds.Width.ShouldBeGreaterThanOrEqualTo(
                                ruler.Width(text.Text ?? "", LogicTextStyle.Label) - 0.75, $"'{text.Text}', {where}");
                        }
                    }
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void A_level_with_no_scripts_and_a_view_with_scripts_off_show_no_legend()
    {
        LogicViewHarness.On(session, "whole", graph =>
            graph.View.FindControl<Border>("Legend").ShouldNotBeNull().IsVisible.ShouldBeFalse());

        LogicViewHarness.On(session, "scripts-off", graph =>
        {
            graph.View.FindControl<Border>("Legend").ShouldNotBeNull().IsVisible.ShouldBeFalse();
            graph.Model.ViewSize.ShouldBe(graph.Canvas.Bounds.Size);
        });
    }

    [Fact]
    public void A_press_on_the_scripts_key_hides_what_scripts_do_and_a_second_shows_it_again()
    {
        LogicViewHarness.On(session, "scripts-playing", graph =>
        {
            Button key = graph.View.FindControl<Border>("ScriptsKey").ShouldNotBeNull()
                .GetVisualDescendants().OfType<Button>().ShouldHaveSingleItem();
            Point at = key.TranslatePoint(new Point(key.Bounds.Width / 2, key.Bounds.Height / 2), graph.Window)
                .ShouldNotBeNull();

            key.Classes.ShouldContain("active");

            graph.Window.MouseDown(at, MouseButton.Left);
            graph.Window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            graph.Model.ShowsScripts.ShouldBeFalse();
            key.Classes.ShouldNotContain("active");
            graph.Scene.Cards.ShouldAllBe(card => !card.Card.IsScript);
            graph.View.FindControl<Border>("Legend").ShouldNotBeNull().IsVisible.ShouldBeFalse();

            graph.Window.MouseDown(at, MouseButton.Left);
            graph.Window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            graph.Model.ShowsScripts.ShouldBeTrue();
            graph.Scene.Cards.Count(card => card.Card.IsScript).ShouldBe(2);
        });
    }

    [Fact]
    public void A_double_click_on_a_script_asks_for_its_code_and_on_an_entity_for_a_frame()
    {
        LogicViewHarness.On(session, "scripts-playing", graph =>
        {
            var opened = new List<Guid>();
            graph.View.OpenScriptRequested += opened.Add;

            DoubleClick(graph, graph.Scene.Card("Main").Header.Center);
            DoubleClick(graph, graph.Scene.Card("Keypad").Header.Center);

            opened.ShouldBe([Main]);
            graph.Framed.ShouldBe([Keypad]);
            graph.Selected.Select(asked => asked.Entity).ShouldBe([Main, Keypad]);
        });
    }

    [Fact]
    public void A_click_on_a_dashed_wire_selects_no_wire_and_offers_nothing_to_remove()
    {
        LogicViewHarness.On(session, "scripts-playing", graph =>
        {
            LogicSceneEdge dashed = graph.Scene.Edges.First(edge => edge.Edge.IsScript && edge.Edge.From.IsScript);

            graph.Click(dashed.LabelBounds.ShouldNotBeNull().Center);
            graph.Click(dashed.LabelBounds.ShouldNotBeNull().Center, button: MouseButton.Right);

            graph.Model.Wiring.Selected.ShouldBeNull();
            graph.Canvas.ShownMenu.ShouldBeNull();
            graph.Selected.ShouldContain((Main, false));
        });
    }

    [Fact]
    public void The_status_row_and_the_events_say_what_scripts_did()
    {
        LogicViewHarness.On(session, "scripts-playing", graph =>
        {
            Control status = graph.View.FindControl<DockPanel>("StatusRow").ShouldNotBeNull();
            Words(status).ShouldBe(
            [
                "4 entities",
                "2 scripts",
                "2 wires in the level",
                "3 made by scripts",
                "Dashed wires are made by scripts and last only while the level runs.",
            ]);

            Control strip = graph.View.FindControl<Control>("EventStrip").ShouldNotBeNull();
            string[] events = Words(strip);
            events.ShouldContain("Zone7.OnTrigger → Main");
            events.ShouldContain("DoorController, line 42");
            events.ShouldContain("Main → Door7.Open");
            events.ShouldContain("DoorController, line 57");
        });
    }

    [Fact]
    public void Dashed_wires_and_script_cards_are_drawn_only_while_the_level_runs()
    {
        session.On(() =>
        {
            (double editingWire, double editingHead, _) = Painted("scripts-editing");
            (double playingWire, double playingHead, double lit) = Painted("scripts-playing");
            (double offWire, double offHead, double offLit) = Painted("scripts-off");

            // While editing the legend's sample is all there is of the colour.
            editingHead.ShouldBe(0);
            playingHead.ShouldBeGreaterThan(1000, "two script cards have a header each");
            playingWire.ShouldBeGreaterThan(4 * editingWire, "three dashed wires are far more than the legend's sample");
            lit.ShouldBeGreaterThan(100, "the wire that just carried something is lit");

            (offWire + offHead + offLit).ShouldBe(0, "with scripts off nothing of them is drawn, the legend included");
        });
    }

    // How many pixels of the graph are in the colours scripts are drawn in.
    private static (double Wire, double Head, double Lit) Painted(string state)
    {
        (LogicView view, Window window) = LogicSheetTests.Open(LogicSheetTests.Drive(state, 1123, 880), 1123, 880);

        try
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            WriteableBitmap frame = window.GetLastRenderedFrame().ShouldNotBeNull();
            return (Count(frame, ScriptWire), Count(frame, ScriptHead), Count(frame, LitScriptWire));
        }
        finally
        {
            window.Close();
        }
    }

    private static int Count(WriteableBitmap frame, Color colour)
    {
        using ILockedFramebuffer buffer = frame.Lock();
        uint wanted = ((uint)colour.A << 24) | ((uint)colour.R << 16) | ((uint)colour.G << 8) | colour.B;
        bool isRgba = buffer.Format == PixelFormat.Rgba8888;
        if (isRgba)
            wanted = ((uint)colour.A << 24) | ((uint)colour.B << 16) | ((uint)colour.G << 8) | colour.R;

        int count = 0;
        unsafe
        {
            for (int y = 0; y < buffer.Size.Height; y++)
            {
                uint* row = (uint*)((byte*)buffer.Address + (y * buffer.RowBytes));
                for (int x = 0; x < buffer.Size.Width; x++)
                    count += row[x] == wanted ? 1 : 0;
            }
        }

        return count;
    }

    private static void DoubleClick(LogicViewHarness graph, Point scene)
    {
        Point at = graph.ToWindow(scene);
        graph.Window.MouseDown(at, MouseButton.Left);
        graph.Window.MouseUp(at, MouseButton.Left);
        graph.Window.MouseDown(at, MouseButton.Left);
        graph.Window.MouseUp(at, MouseButton.Left);
    }

    private static Rect Within(Control part, Control of) =>
        new(part.TranslatePoint(default, of).ShouldNotBeNull(), part.Bounds.Size);

    // The words on show under a control, in the order they are laid out.
    private static string[] Words(Control under) =>
    [
        .. under.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text))
            .Select(text => text.Text ?? ""),
    ];
}
