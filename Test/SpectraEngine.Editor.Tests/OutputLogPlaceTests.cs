using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// A line in the output that names a place: how the place reads, when two
/// lines fold into a count, how an error's calls show, and what a click asks for.
/// </summary>
public sealed class OutputLogPlaceTests
{
    private const string Door = "Scripts/level one/door.luau";

    private static readonly ConsolePlace[] NoCalls = [];

    [Fact]
    public void A_place_reads_as_the_files_name_and_the_line_and_its_tip_has_the_whole_path()
    {
        var log = new OutputLog();

        log.Append(OutputSeverity.Info, "hello", new ConsolePlace(Door, 12), NoCalls);

        OutputEntry entry = log.Entries.ShouldHaveSingleItem();
        entry.Text.ShouldBe("hello");
        entry.HasPlace.ShouldBeTrue();
        entry.PlaceLabel.ShouldBe("door.luau:12");
        entry.PlaceTip.ShouldBe("Open Scripts/level one/door.luau at line 12");
        entry.Stack.ShouldBeNull();
    }

    [Fact]
    public void A_place_that_knows_no_line_reads_as_the_files_name_alone()
    {
        var log = new OutputLog();

        log.Append(OutputSeverity.Error, "it is not in the level's content", new ConsolePlace(Door, 0), NoCalls);

        OutputEntry entry = log.Entries.ShouldHaveSingleItem();
        entry.PlaceLabel.ShouldBe("door.luau");
        entry.PlaceTip.ShouldBe("Open Scripts/level one/door.luau");
    }

    [Fact]
    public void A_line_with_no_place_has_nothing_to_open()
    {
        var log = new OutputLog();
        var asked = new List<ConsolePlace>();
        log.PlaceRequested += asked.Add;

        log.Append(OutputSeverity.Info, "Saved the level");

        OutputEntry entry = log.Entries.ShouldHaveSingleItem();
        entry.HasPlace.ShouldBeFalse();
        entry.PlaceLabel.ShouldBeEmpty();
        entry.PlaceTip.ShouldBeNull();
        entry.Stack.ShouldBeNull();

        entry.Open();
        asked.ShouldBeEmpty();
    }

    [Fact]
    public void Two_lines_fold_when_their_text_and_their_place_are_the_same()
    {
        var log = new OutputLog();

        log.Append(OutputSeverity.Info, "tick", new ConsolePlace(Door, 4), NoCalls);
        log.Append(OutputSeverity.Info, "tick", new ConsolePlace(Door, 4), NoCalls);
        log.Append(OutputSeverity.Info, "tick", new ConsolePlace(Door, 4), NoCalls);

        OutputEntry entry = log.Entries.ShouldHaveSingleItem();
        entry.CountLabel.ShouldBe("x3");
        entry.PlaceLabel.ShouldBe("door.luau:4");
    }

    [Fact]
    public void The_same_text_from_another_line_or_another_file_or_no_place_does_not_fold()
    {
        var log = new OutputLog();

        log.Append(OutputSeverity.Info, "tick", new ConsolePlace(Door, 4), NoCalls);
        log.Append(OutputSeverity.Info, "tick", new ConsolePlace(Door, 9), NoCalls);
        log.Append(OutputSeverity.Info, "tick", new ConsolePlace("Scripts/lamp.luau", 9), NoCalls);
        log.Append(OutputSeverity.Info, "tick");
        log.Append(OutputSeverity.Info, "tick", new ConsolePlace(Door, 4), NoCalls);

        log.Entries.Select(entry => (entry.PlaceLabel, entry.Count)).ShouldBe(
        [
            ("door.luau:4", 1),
            ("door.luau:9", 1),
            ("lamp.luau:9", 1),
            ("", 1),
            ("door.luau:4", 1),
        ]);
    }

    [Fact]
    public void The_same_error_folds_only_when_it_came_the_same_way()
    {
        var log = new OutputLog();
        ConsolePlace[] fromPress = [new(Door, 30, "press")];
        ConsolePlace[] fromTouch = [new(Door, 44, "touch")];

        log.Append(OutputSeverity.Error, "it broke", new ConsolePlace(Door, 12), fromPress);
        log.Append(OutputSeverity.Error, "it broke", new ConsolePlace(Door, 12), [new(Door, 30, "press")]);
        log.Append(OutputSeverity.Error, "it broke", new ConsolePlace(Door, 12), fromTouch);

        log.Entries.Select(entry => entry.Count).ShouldBe([2, 1]);
        log.ErrorCount.ShouldBe(2);
    }

    [Fact]
    public void A_short_stack_shows_every_call_and_has_nothing_to_fold()
    {
        var log = new OutputLog();

        log.Append(OutputSeverity.Error, "it broke", new ConsolePlace(Door, 12), Calls(OutputStack.ShownAtFirst + 1));

        OutputStack stack = log.Entries.ShouldHaveSingleItem().Stack.ShouldNotBeNull();
        stack.Shown.Count.ShouldBe(4, "a row that says 'one more' would cost the row it hides");
        stack.CanFold.ShouldBeFalse();
        stack.Shown[0].Label.ShouldBe("door.luau:101 in step1");
        stack.Shown[0].Tip.ShouldBe("Open Scripts/level one/door.luau at line 101");
    }

    [Fact]
    public void A_long_stack_shows_its_first_calls_and_the_rest_when_asked()
    {
        var log = new OutputLog();
        log.Append(OutputSeverity.Error, "it broke", new ConsolePlace(Door, 12), Calls(9));
        OutputStack stack = log.Entries.ShouldHaveSingleItem().Stack.ShouldNotBeNull();
        var changed = new List<string?>();
        stack.PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        stack.Shown.Select(frame => frame.Place.Line).ShouldBe([101, 102, 103]);
        stack.CanFold.ShouldBeTrue();
        stack.FoldLabel.ShouldBe("Show 6 more calls");

        stack.Toggle();

        stack.Shown.Count.ShouldBe(9);
        stack.FoldLabel.ShouldBe("Show fewer calls");
        changed.ShouldContain(nameof(OutputStack.Shown));
        changed.ShouldContain(nameof(OutputStack.FoldLabel));

        stack.Toggle();

        stack.Shown.Count.ShouldBe(OutputStack.ShownAtFirst);
    }

    [Fact]
    public void A_call_with_no_function_name_reads_as_its_place_alone()
    {
        var log = new OutputLog();

        log.Append(OutputSeverity.Error, "it broke", new ConsolePlace(Door, 12), [new ConsolePlace(Door, 30)]);

        log.Entries[0].Stack.ShouldNotBeNull().Shown.ShouldHaveSingleItem().Label.ShouldBe("door.luau:30");
    }

    [Fact]
    public void An_error_with_calls_under_it_is_one_line_of_the_heading()
    {
        var log = new OutputLog();

        log.Append(OutputSeverity.Info, "hello", new ConsolePlace(Door, 3), NoCalls);
        log.Append(OutputSeverity.Error, "it broke", new ConsolePlace(Door, 12), Calls(9));

        log.HistoryLabel.ShouldBe("Shell output, 2 of 500 lines");
    }

    [Fact]
    public void Opening_a_line_asks_for_its_place_and_opening_a_call_asks_for_the_calls()
    {
        var log = new OutputLog();
        var asked = new List<ConsolePlace>();
        log.PlaceRequested += asked.Add;
        log.Append(OutputSeverity.Error, "it broke", new ConsolePlace(Door, 12), [new(Door, 30, "press")]);
        OutputEntry entry = log.Entries[0];

        entry.Open();
        entry.Stack.ShouldNotBeNull().Shown[0].Open();

        asked.ShouldBe([new ConsolePlace(Door, 12), new ConsolePlace(Door, 30, "press")]);
    }

    [Fact]
    public void Lines_that_fall_off_the_top_are_counted()
    {
        var log = new OutputLog();

        for (int i = 0; i < OutputLog.Capacity + 7; i++)
            log.Append(OutputSeverity.Info, $"line {i}", new ConsolePlace(Door, i + 1), NoCalls);

        log.Entries.Count.ShouldBe(OutputLog.Capacity);
        log.FallenOff.ShouldBe(7);
        log.HistoryLabel.ShouldBe("Shell output, 500 of 500 lines");
    }

    private static ConsolePlace[] Calls(int count) =>
        [.. Enumerable.Range(1, count).Select(i => new ConsolePlace(Door, 100 + i, $"step{i}"))];
}
