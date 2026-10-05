using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

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
}
