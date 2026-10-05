using Avalonia;
using SpectraEngine.Editor.Shell.Logic;
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>What the rows round the Logic view's graph show as the view narrows.</summary>
public sealed class LogicViewFitTests
{
    private const string LongNote = "PlayerStart and 3 more entities have no wires and are not shown.";
    private const string ShortNote = "4 entities have no wires.";

    private static readonly FixedWidthRuler Ruler = new();

    private static readonly LogicStatus Busy =
        new("9 entities", "9 wires", "1 wire goes nowhere", LongNote, "") { UnwiredShort = ShortNote };

    private static readonly LogicStatus Quiet = new("2 entities", "1 wire", "", "", "");

    private static LogicViewFit Fit(double width, bool isPlaying = false, double height = 600) =>
        LogicViewFit.For(new Size(width, height), isPlaying, Busy, Ruler);

    // What a part of the status row takes under the fake ruler, with its gap.
    private static double Part(string text) => text.Length * FixedWidthRuler.CharacterWidth + 14;

    [Fact]
    public void A_wide_view_shows_everything_and_the_pill_only_while_a_level_runs()
    {
        Fit(1600, isPlaying: true).ShouldBe(LogicViewFit.Everything);
        Fit(1600).ShouldBe(LogicViewFit.Everything with { ShowsPlaying = false, ShowsTick = false, ShowsEvents = false });
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
    public void The_status_row_keeps_the_link_longest_and_shows_a_sentence_whole_or_not_at_all()
    {
        double fixedParts = 20 + Part(Busy.Entities) + Part(Busy.Wires) + Part(Busy.GoingNowhere);

        LogicViewFit wide = Fit(1200);
        LogicViewFit holdsTheLong = Fit(fixedParts + Part(LongNote));
        LogicViewFit holdsTheShort = Fit(fixedParts + Part(LongNote) - 1);
        LogicViewFit holdsNeither = Fit(fixedParts + Part(ShortNote) - 1);
        LogicViewFit narrow = Fit(200);

        (wide.ShowsCounts && wide.ShowsNotes && wide.ShowsHint).ShouldBeTrue();
        wide.UsesShortNotes.ShouldBeFalse();

        (holdsTheLong.ShowsNotes && !holdsTheLong.UsesShortNotes).ShouldBeTrue();
        (holdsTheShort.ShowsNotes && holdsTheShort.UsesShortNotes).ShouldBeTrue();
        holdsTheShort.ShowsHint.ShouldBeFalse();
        (holdsNeither.ShowsCounts && !holdsNeither.ShowsNotes && !holdsNeither.UsesShortNotes).ShouldBeTrue();
        (narrow.ShowsCounts || narrow.ShowsNotes || narrow.ShowsHint).ShouldBeFalse();
    }

    [Fact]
    public void A_status_row_with_less_to_say_has_room_for_the_hint_sooner()
    {
        LogicViewFit.For(new Size(600, 500), false, Quiet, Ruler).ShowsHint.ShouldBeTrue();
        LogicViewFit.For(new Size(600, 500), false, Busy, Ruler).ShowsHint.ShouldBeFalse();
        LogicViewFit.For(new Size(180, 500), false, Quiet, Ruler).ShowsCounts.ShouldBeTrue();
    }

    [Fact]
    public void Counts_that_run_to_thousands_take_the_room_they_need_from_the_hint()
    {
        var large = new LogicStatus("4,000 entities", "12,345 wires", "", "", "");
        double counts = 20 + Part(large.Entities) + Part(large.Wires);
        double hint = Part(LogicViewText.EditingHintShort);

        LogicViewFit.For(new Size(counts + hint, 500), false, large, Ruler).ShowsHint.ShouldBeTrue();
        LogicViewFit.For(new Size(counts + hint - 1, 500), false, large, Ruler).ShowsHint.ShouldBeFalse();
    }

    [Fact]
    public void The_hint_loses_its_second_sentence_before_it_goes()
    {
        double counts = 20 + Part(Quiet.Entities) + Part(Quiet.Wires);
        double whole = Part(LogicViewText.EditingHint);
        double first = Part(LogicViewText.EditingHintShort);

        LogicViewFit holdsAll = LogicViewFit.For(new Size(counts + whole, 500), false, Quiet, Ruler);
        LogicViewFit holdsTheFirst = LogicViewFit.For(new Size(counts + first, 500), false, Quiet, Ruler);
        LogicViewFit holdsNone = LogicViewFit.For(new Size(counts + first - 1, 500), false, Quiet, Ruler);

        (holdsAll.ShowsHint && !holdsAll.UsesShortHint).ShouldBeTrue();
        (holdsTheFirst.ShowsHint && holdsTheFirst.UsesShortHint).ShouldBeTrue();
        (holdsNone.ShowsHint || holdsNone.UsesShortHint).ShouldBeFalse();
    }

    [Fact]
    public void The_hint_of_a_running_level_shows_whole_or_not_at_all()
    {
        double counts = 20 + Part(Quiet.Entities) + Part(Quiet.Wires);
        double hint = Part(LogicViewText.PlayingHint);

        LogicViewFit.For(new Size(counts + hint, 500), true, Quiet, Ruler).ShowsHint.ShouldBeTrue();

        LogicViewFit narrower = LogicViewFit.For(new Size(counts + hint - 1, 500), true, Quiet, Ruler);
        (narrower.ShowsHint || narrower.UsesShortHint).ShouldBeFalse();
    }

    [Fact]
    public void What_the_last_edit_did_stands_in_the_place_of_the_notes()
    {
        LogicStatus edited = Busy with { News = "Wired LiftButton.OnPressed to Lift.Open." };

        LogicViewFit fit = LogicViewFit.For(new Size(1600, 500), false, edited, Ruler);

        fit.ShowsNotes.ShouldBeFalse();
        (fit.ShowsCounts && fit.ShowsHint).ShouldBeTrue();
    }

    [Fact]
    public void The_counts_give_way_to_what_the_last_edit_did()
    {
        LogicStatus edited = Quiet with { News = "Wired LiftButton.OnPressed to Lift.Open." };
        double counts = Part(edited.Entities) + Part(edited.Wires);
        double news = 20 + Part(edited.News);

        LogicViewFit.For(new Size(news + counts, 500), false, edited, Ruler).ShowsCounts.ShouldBeTrue();
        LogicViewFit.For(new Size(news + counts - 1, 500), false, edited, Ruler).ShowsCounts.ShouldBeFalse();
    }

    [Fact]
    public void The_heights_the_fit_reckons_with_are_the_theme_s()
    {
        LogicViewFit.Rows.ShouldBe(Token("SpectraLogicToolbarHeight") + Token("SpectraStatusBarHeight"));
        LogicViewFit.EventStrip.ShouldBe(Token("SpectraLogicEventStripHeight"));
    }

    [Fact]
    public void The_event_strip_shows_only_while_a_level_runs_and_the_graph_keeps_room()
    {
        Fit(480, isPlaying: true, height: 500).ShowsEvents.ShouldBeTrue();
        Fit(480, isPlaying: true, height: 200).ShowsEvents.ShouldBeFalse();
        Fit(480, isPlaying: false, height: 500).ShowsEvents.ShouldBeFalse();
    }

    // Tokens.axaml is read as text: this project loads no theme.
    private static double Token(string key)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Spectra.slnx")))
            folder = folder.Parent;

        string tokens = File.ReadAllText(Path.Combine(
            folder.ShouldNotBeNull().FullName, "SpectraEngine.Editor", "Theme", "Tokens.axaml"));

        Match match = Regex.Match(tokens, $@"<x:Double\s+x:Key=""{key}"">\s*(?<value>[0-9.]+)\s*</x:Double>");
        match.Success.ShouldBeTrue(key);

        return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }

    private static int Count(params bool[] shown)
    {
        int count = 0;
        foreach (bool one in shown)
            count += one ? 1 : 0;

        return count;
    }
}
