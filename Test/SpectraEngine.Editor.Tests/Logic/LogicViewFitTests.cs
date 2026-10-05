using Avalonia;
using SpectraEngine.Editor.Shell.Logic;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>What the rows round the Logic view's graph show as the view narrows.</summary>
public sealed class LogicViewFitTests
{
    private static readonly LogicStatus Busy = new(
        "9 entities",
        "9 wires",
        "1 wire goes nowhere",
        "PlayerStart and 3 more entities have no wires and are not shown.",
        "");

    private static readonly LogicStatus Quiet = new("2 entities", "1 wire", "", "", "");

    private static LogicViewFit Fit(double width, bool isPlaying = false, double height = 600) =>
        LogicViewFit.For(new Size(width, height), isPlaying, Busy);

    [Fact]
    public void A_wide_view_shows_everything_and_the_pill_only_while_a_level_runs()
    {
        Fit(1123, isPlaying: true).ShouldBe(LogicViewFit.Everything);
        Fit(1123).ShouldBe(LogicViewFit.Everything with { ShowsPlaying = false, ShowsTick = false, ShowsEvents = false });
    }

    [Fact]
    public void The_filter_box_gives_up_room_before_anything_is_hidden()
    {
        LogicViewFit wide = Fit(700);
        LogicViewFit narrower = Fit(600);

        narrower.ShowsLabels.ShouldBeTrue();
        wide.FilterWidth(700).ShouldBe(LogicViewFit.MostFilterWidth);
        narrower.FilterWidth(600).ShouldBeLessThan(LogicViewFit.MostFilterWidth);
        narrower.FilterWidth(600).ShouldBeGreaterThanOrEqualTo(LogicViewFit.LeastFilterWidth);
    }

    [Fact]
    public void Then_the_labels_go_and_the_controls_stay()
    {
        LogicViewFit fit = Fit(480);

        fit.ShowsLabels.ShouldBeFalse();
        fit.UsesShortNames.ShouldBeFalse();
        (fit.ShowsFilter && fit.ShowsSteps && fit.ShowsZoomKeys).ShouldBeTrue();
    }

    [Fact]
    public void A_running_level_at_480_keeps_every_control_by_shortening_the_mode_names()
    {
        LogicViewFit fit = Fit(480, isPlaying: true);

        fit.ShowsTick.ShouldBeFalse();
        fit.UsesShortNames.ShouldBeTrue();
        (fit.ShowsPlaying && fit.ShowsFilter && fit.ShowsSteps && fit.ShowsZoomKeys).ShouldBeTrue();
    }

    [Fact]
    public void Each_narrower_view_shows_no_more_than_the_one_before()
    {
        foreach (bool isPlaying in new[] { false, true })
        {
            int before = int.MaxValue;
            for (double width = 1200; width >= 100; width -= 4)
            {
                LogicViewFit fit = Fit(width, isPlaying);
                int shown = Count(
                    fit.ShowsLabels, fit.ShowsTick, !fit.UsesShortNames, fit.ShowsFilter,
                    fit.ShowsZoomKeys, fit.ShowsSteps, fit.ShowsPlaying);

                shown.ShouldBeLessThanOrEqualTo(before, $"at {width}");
                before = shown;
            }
        }
    }

    [Fact]
    public void What_is_shown_fits_the_width_down_to_the_narrowest_pane()
    {
        foreach (bool isPlaying in new[] { false, true })
        {
            for (double width = 1200; width >= 180; width -= 4)
            {
                LogicViewFit fit = Fit(width, isPlaying);
                double needed = fit.ToolbarWidth + (fit.ShowsFilter ? fit.FilterWidth(width) : 0);

                needed.ShouldBeLessThanOrEqualTo(width, $"at {width}");
            }
        }
    }

    [Fact]
    public void The_narrowest_pane_has_the_two_mode_keys_and_nothing_else()
    {
        Fit(180, isPlaying: true, height: 90).ShouldBe(default(LogicViewFit) with { UsesShortNames = true });
    }

    [Fact]
    public void A_filter_box_too_narrow_for_its_placeholder_is_told_apart()
    {
        Fit(1123).FilterWidth(1123).ShouldBeGreaterThanOrEqualTo(LogicViewFit.LongPlaceholderWidth);
        Fit(480).FilterWidth(480).ShouldBeLessThan(LogicViewFit.LongPlaceholderWidth);
    }

    [Fact]
    public void The_status_row_keeps_the_link_longest_and_shows_a_sentence_only_with_room_to_read_it()
    {
        LogicViewFit wide = Fit(1123);
        LogicViewFit middle = Fit(480);
        LogicViewFit narrow = Fit(200);

        (wide.ShowsCounts && wide.ShowsNotes && wide.ShowsHint).ShouldBeTrue();
        (middle.ShowsCounts && middle.ShowsNotes).ShouldBeTrue();
        middle.ShowsHint.ShouldBeFalse();
        (narrow.ShowsCounts || narrow.ShowsNotes || narrow.ShowsHint).ShouldBeFalse();
    }

    [Fact]
    public void A_status_row_with_less_to_say_has_room_for_the_hint_sooner()
    {
        LogicViewFit.For(new Size(480, 500), false, Quiet).ShowsHint.ShouldBeTrue();
        LogicViewFit.For(new Size(480, 500), false, Busy).ShowsHint.ShouldBeFalse();
        LogicViewFit.For(new Size(180, 500), false, Quiet).ShowsCounts.ShouldBeTrue();
    }

    [Fact]
    public void The_event_strip_shows_only_while_a_level_runs_and_the_graph_keeps_room()
    {
        Fit(480, isPlaying: true, height: 500).ShowsEvents.ShouldBeTrue();
        Fit(480, isPlaying: true, height: 200).ShowsEvents.ShouldBeFalse();
        Fit(480, isPlaying: false, height: 500).ShowsEvents.ShouldBeFalse();
    }

    private static int Count(params bool[] shown)
    {
        int count = 0;
        foreach (bool one in shown)
            count += one ? 1 : 0;

        return count;
    }
}
