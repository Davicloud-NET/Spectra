using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using SpectraEngine.Editor.Shell.Logic;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The rows round the Logic view's graph at every width a pane can have:
/// nothing in them is cut off, squeezed or pushed onto its neighbour.
/// </summary>
// Needs real Skia: LogicViewFit decides from widths measured in the shell's
// fonts, and this is what holds those numbers to the fonts.
[Collection(RibbonSessionCollection.Name)]
public sealed class LogicToolbarTests(RibbonSession session)
{
    private const double Widest = 1200;
    private const double Narrowest = 180;
    private const double Height = 500;

    // Layout rounds to half pixels at this scaling.
    private const double Slack = 0.75;

    [Theory]
    [InlineData("whole")]
    [InlineData("around-relay")]
    [InlineData("playing")]
    [InlineData("empty")]
    [InlineData("unwired")]
    public void Nothing_in_the_rows_is_cut_or_pushed_onto_its_neighbour_at_any_width(string state)
    {
        Sweep(state, (view, width) =>
        {
            Panel toolbar = view.FindControl<Grid>("Toolbar").ShouldNotBeNull();
            Panel status = view.FindControl<DockPanel>("StatusRow").ShouldNotBeNull();

            StandsApart(toolbar, $"the toolbar, {state} at {width}");
            StandsApart(status, $"the status row, {state} at {width}");

            foreach (Control part in toolbar.Children)
            {
                if (part.IsVisible && part.Name != "FilterBox")
                    Width(part).ShouldBeGreaterThanOrEqualTo(part.DesiredSize.Width - Slack, $"{part.Name}, {state} at {width}");
            }
        });
    }

    [Theory]
    [InlineData("whole")]
    [InlineData("playing")]
    [InlineData("unwired")]
    public void Every_sentence_under_the_graph_shows_whole_or_not_at_all_at_any_width(string state)
    {
        var ruler = new LogicTextRuler();

        Sweep(state, (view, width) =>
        {
            Control status = view.FindControl<DockPanel>("StatusRow").ShouldNotBeNull();
            foreach (TextBlock text in Shown(status))
                Holds(text, ruler.Width(text.Text ?? "", LogicTextStyle.Status), $"{state} at {width}");

            // A line of the event strip gives up its route first. What went
            // wrong stays whole for as long as the strip can hold it.
            Control strip = view.FindControl<Control>("EventStrip").ShouldNotBeNull();
            foreach (TextBlock text in Shown(strip).Where(text => text.Text == "nothing is named VaultDor"))
            {
                if (width >= 320)
                    Holds(text, ruler.Width(text.Text ?? "", LogicTextStyle.MonoLabel), $"{state} at {width}");
            }
        });
    }

    [Fact]
    public void At_480_the_status_row_says_in_fewer_words_what_it_has_no_room_to_say_in_full()
    {
        session.On(() =>
        {
            (LogicView view, Window window) = LogicSheetTests.Open(LogicSheetTests.Drive("whole", 480, Height), 480, Height);

            try
            {
                TextBlock note = view.FindControl<TextBlock>("UnwiredNote").ShouldNotBeNull();

                note.IsEffectivelyVisible.ShouldBeTrue();
                note.Text.ShouldBe("4 entities have no wires.");
                ToolTip.GetTip(note).ShouldBe("PlayerStart and 3 more entities have no wires and are not shown.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(Widest, "Drag from a card or an output onto a card to wire it. Ctrl-click another entity to give it a card too.")]
    [InlineData(1000, "Drag from a card or an output onto a card to wire it.")]
    public void With_a_card_that_has_no_wires_on_show_the_status_row_says_how_to_wire_it(double width, string hint)
    {
        session.On(() =>
        {
            (LogicView view, Window window) = LogicSheetTests.Open(
                LogicSheetTests.Drive("unwired", width, Height), width, Height);

            try
            {
                TextBlock text = view.FindControl<TextBlock>("HintText").ShouldNotBeNull();

                text.IsEffectivelyVisible.ShouldBeTrue();
                text.Text.ShouldBe(hint);
                view.FindControl<TextBlock>("UnwiredNote").ShouldNotBeNull().Text.ShouldBe(
                    "PlayerStart and 2 more entities have no wires and are not shown.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void A_running_level_says_so_in_the_event_strip_until_a_wire_has_fired()
    {
        session.On(() =>
        {
            (LogicView view, Window window) = LogicSheetTests.Open(
                LogicSheetTests.Drive("quiet-playing", 480, Height), 480, Height);
            (LogicView busy, Window other) = LogicSheetTests.Open(LogicSheetTests.Drive("playing", 480, Height), 480, Height);

            try
            {
                view.FindControl<TextBlock>("NoEvents").ShouldNotBeNull().IsEffectivelyVisible.ShouldBeTrue();
                busy.FindControl<TextBlock>("NoEvents").ShouldNotBeNull().IsVisible.ShouldBeFalse();
            }
            finally
            {
                window.Close();
                other.Close();
            }
        });
    }

    [Fact]
    public void A_view_with_no_model_shows_nothing_to_press()
    {
        session.On(() =>
        {
            var view = new LogicView();
            var window = new Window { Content = view, Width = 480, Height = Height };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            try
            {
                view.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible)
                    .ShouldBeEmpty();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData("whole")]
    [InlineData("playing")]
    public void A_filter_box_that_shows_is_wide_enough_to_type_in(string state)
    {
        Sweep(state, (view, width) =>
        {
            TextBox filter = view.FindControl<TextBox>("FilterBox").ShouldNotBeNull();
            if (!filter.IsVisible)
                return;

            filter.Bounds.Width.ShouldBeGreaterThanOrEqualTo(LogicViewFit.LeastFilterWidth - Slack, $"{state} at {width}");
            filter.Bounds.Width.ShouldBeLessThanOrEqualTo(LogicViewFit.MostFilterWidth + Slack, $"{state} at {width}");
        });
    }

    [Theory]
    [InlineData("whole")]
    [InlineData("playing")]
    public void At_480_the_toolbar_still_has_every_control(string state)
    {
        session.On(() =>
        {
            (LogicView view, Window window) = LogicSheetTests.Open(LogicSheetTests.Drive(state, 480, Height), 480, Height);

            try
            {
                foreach (string name in new[] { "ModeKeys", "StepsBox", "FilterBox", "ZoomKeys" })
                    view.FindControl<Control>(name).ShouldNotBeNull().IsVisible.ShouldBeTrue(name);

                view.FindControl<Control>("PlayingPill").ShouldNotBeNull().IsVisible.ShouldBe(state == "playing");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void At_180_the_toolbar_is_the_two_mode_keys_and_nothing_spills()
    {
        session.On(() =>
        {
            (LogicView view, Window window) = LogicSheetTests.Open(
                LogicSheetTests.Drive("playing", Narrowest, 90), Narrowest, 90);

            try
            {
                Panel toolbar = view.FindControl<Grid>("Toolbar").ShouldNotBeNull();

                toolbar.Children.Where(part => part.IsVisible).Select(part => part.Name).ShouldBe(["ModeKeys"]);
                StandsApart(toolbar, "the toolbar at 180");
                view.FindControl<Control>("EventStrip").ShouldNotBeNull().IsVisible.ShouldBeFalse();
            }
            finally
            {
                window.Close();
            }
        });
    }

    private void Sweep(string state, Action<LogicView, double> check)
    {
        session.On(() =>
        {
            (LogicView view, Window window) = LogicSheetTests.Open(
                LogicSheetTests.Drive(state, Widest, Height), Widest, Height);

            try
            {
                for (double width = Widest; width >= Narrowest; width -= 10)
                {
                    window.Width = width;
                    Dispatcher.UIThread.RunJobs();

                    view.Bounds.Width.ShouldBe(width);
                    check(view, width);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    // Every part that shows lies inside the row, and no two of them share a pixel.
    private static void StandsApart(Panel row, string where)
    {
        List<Control> shown = [.. row.Children.Where(part => part.IsVisible && part.Bounds.Width > 0)];
        shown.Sort((a, b) => a.Bounds.X.CompareTo(b.Bounds.X));

        double edge = 0;
        foreach (Control part in shown)
        {
            double left = part.Bounds.X - part.Margin.Left;
            double right = part.Bounds.Right + part.Margin.Right;

            left.ShouldBeGreaterThanOrEqualTo(edge - Slack, $"{part.Name ?? part.GetType().Name} in {where}");
            right.ShouldBeLessThanOrEqualTo(row.Bounds.Width + Slack, $"{part.Name ?? part.GetType().Name} in {where}");
            edge = right;
        }
    }

    private static double Width(Control part) => part.Bounds.Width + part.Margin.Left + part.Margin.Right;

    private static IEnumerable<TextBlock> Shown(Control within) => within.GetVisualDescendants()
        .OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text));

    // A text block that is narrower than its words has cut them.
    private static void Holds(TextBlock text, double words, string where) =>
        text.Bounds.Width.ShouldBeGreaterThanOrEqualTo(words - Slack, $"'{text.Text}', {where}");
}
