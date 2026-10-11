using Microsoft.Extensions.Logging;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

/// <summary>The engine console's lines arriving in the output panel.</summary>
public sealed class ConsoleLineFeedTests
{
    [Fact]
    public void Lines_append_in_order_with_their_severity()
    {
        var feed = new ConsoleLineFeed();
        var output = new OutputLog();

        feed.Feed(
            [
                new ConsoleLine(0, LogLevel.Information, "ent 12 send relay.OnTrigger -> door.Open"),
                new ConsoleLine(1, LogLevel.Warning, "ent 12 miss relay.OnTrigger -> dor1.Close"),
                new ConsoleLine(2, LogLevel.Error, "no command named 'noclip'. Type help."),
            ],
            output);

        Assert.Equal(
            [
                (OutputSeverity.Info, "ent 12 send relay.OnTrigger -> door.Open"),
                (OutputSeverity.Warning, "ent 12 miss relay.OnTrigger -> dor1.Close"),
                (OutputSeverity.Error, "no command named 'noclip'. Type help."),
            ],
            Rows(output));
    }

    [Fact]
    public void Lines_from_one_snapshot_follow_the_last_without_a_warning()
    {
        var feed = new ConsoleLineFeed();
        var output = new OutputLog();

        feed.Feed([new ConsoleLine(0, LogLevel.Information, "one")], output);
        feed.Feed([], output);
        feed.Feed([new ConsoleLine(1, LogLevel.Information, "two")], output);

        Assert.Equal([(OutputSeverity.Info, "one"), (OutputSeverity.Info, "two")], Rows(output));
    }

    [Fact]
    public void A_gap_in_the_sequence_is_reported_once_with_the_count()
    {
        var feed = new ConsoleLineFeed();
        var output = new OutputLog();
        feed.Feed([new ConsoleLine(0, LogLevel.Information, "before")], output);

        feed.Feed(
            [
                new ConsoleLine(4, LogLevel.Information, "after"),
                new ConsoleLine(5, LogLevel.Information, "and on"),
            ],
            output);

        List<(OutputSeverity Severity, string Text)> rows = Rows(output);
        Assert.Equal(4, rows.Count);
        Assert.Equal((OutputSeverity.Info, "before"), rows[0]);
        Assert.Equal(OutputSeverity.Warning, rows[1].Severity);
        Assert.StartsWith("3 console line(s) are missing here", rows[1].Text);
        Assert.Equal((OutputSeverity.Info, "after"), rows[2]);
        Assert.Equal((OutputSeverity.Info, "and on"), rows[3]);
    }

    [Fact]
    public void A_new_session_starts_the_count_again()
    {
        var feed = new ConsoleLineFeed();
        var output = new OutputLog();
        feed.Feed(
            [
                new ConsoleLine(0, LogLevel.Information, "old one"),
                new ConsoleLine(1, LogLevel.Information, "old two"),
            ],
            output);

        feed.Reset();
        feed.Feed([new ConsoleLine(0, LogLevel.Information, "new one")], output);
        feed.Feed([new ConsoleLine(1, LogLevel.Information, "new two")], output);

        Assert.Equal(
            [
                (OutputSeverity.Info, "old one"),
                (OutputSeverity.Info, "old two"),
                (OutputSeverity.Info, "new one"),
                (OutputSeverity.Info, "new two"),
            ],
            Rows(output));
    }

    [Fact]
    public void A_feed_nobody_reset_still_shows_the_next_engines_lines()
    {
        var feed = new ConsoleLineFeed();
        var output = new OutputLog();
        feed.Feed([new ConsoleLine(7, LogLevel.Information, "old")], output);
        output.Clear();

        // Lower numbers than expected: shown, never taken for lines already seen.
        feed.Feed([new ConsoleLine(0, LogLevel.Information, "new")], output);

        Assert.Equal([(OutputSeverity.Info, "new")], Rows(output));
    }

    [Fact]
    public void A_scripts_line_arrives_with_its_place_and_a_commands_line_with_none()
    {
        // The way of a line: the sink, the console, a snapshot, this feed.
        var console = new ConsoleOutput();
        var sink = new ConsoleScriptSink(console);
        var feed = new ConsoleLineFeed();
        var output = new OutputLog();

        sink.Report(new ScriptMessage(ScriptSeverity.Print, "Spinner turns on a script", "Scripts/spinner.luau", 5));
        console.Print("sound_volume: 0.");
        sink.Report(new ScriptMessage(
            ScriptSeverity.Error, "it broke", "Scripts/door.luau", 12,
            [new ScriptFrame("Scripts/door.luau", 12, "open"), new ScriptFrame("Scripts/door.luau", 30, null)]));
        feed.Feed(Snapshot(console), output);

        output.Entries.Select(entry => (entry.Severity, entry.Text, entry.PlaceLabel)).ShouldBe(
        [
            (OutputSeverity.Info, "Spinner turns on a script", "spinner.luau:5"),
            (OutputSeverity.Info, "sound_volume: 0.", ""),
            (OutputSeverity.Error, "it broke", "door.luau:12"),
        ]);
        output.Entries[1].HasPlace.ShouldBeFalse();
        output.Entries[2].Stack.ShouldNotBeNull().Shown.ShouldHaveSingleItem().Label.ShouldBe("door.luau:30");
    }

    [Fact]
    public void Lines_the_engine_dropped_at_the_end_of_a_burst_are_reported_with_that_snapshot()
    {
        const int Lost = 6;
        var console = new ConsoleOutput();
        var feed = new ConsoleLineFeed();
        var output = new OutputLog();

        // No later line shows the jump in the numbers. The count does.
        for (int i = 0; i < ConsoleOutput.Capacity + Lost; i++)
            console.Print("the same line");
        feed.Feed(Snapshot(console), output);

        output.Entries.Select(entry => (entry.Severity, entry.Text, entry.Count)).ShouldBe(
        [
            (OutputSeverity.Info, "the same line", ConsoleOutput.Capacity),
            (OutputSeverity.Warning,
                "6 console line(s) are missing here: more were printed than the editor kept up with.", 1),
        ]);

        console.Print("and on");
        feed.Feed(Snapshot(console), output);

        output.Entries.Count.ShouldBe(3, "what was reported is not reported again");
        output.Entries[^1].Text.ShouldBe("and on");
    }

    [Fact]
    public void A_snapshot_with_more_lines_than_the_output_holds_says_how_many_went_by_unread()
    {
        var console = new ConsoleOutput();
        var feed = new ConsoleLineFeed();
        var output = new OutputLog();
        output.Append(OutputSeverity.Info, "from before");

        for (int i = 0; i < ConsoleOutput.Capacity; i++)
            console.Warn($"warning {i}");
        feed.Feed(Snapshot(console), output);

        // 1024 came into room for 500. The line from before went first.
        OutputEntry notice = output.Entries[^1];
        notice.Severity.ShouldBe(OutputSeverity.Warning);
        notice.Text.ShouldBe(
            "524 line(s) came and went before they could be read: the output keeps 500. The run log has them all.");
        output.Entries[^2].Text.ShouldBe($"warning {ConsoleOutput.Capacity - 1}");
        output.Entries.Count.ShouldBe(OutputLog.Capacity);
    }

    [Fact]
    public void A_snapshot_that_fits_says_nothing_about_lines_going_by()
    {
        var console = new ConsoleOutput();
        var feed = new ConsoleLineFeed();
        var output = new OutputLog();
        for (int i = 0; i < OutputLog.Capacity; i++)
            output.Append(OutputSeverity.Info, $"old {i}");

        for (int i = 0; i < 40; i++)
            console.Print($"new {i}");
        feed.Feed(Snapshot(console), output);

        output.Entries[^1].Text.ShouldBe("new 39");
        output.WarningCount.ShouldBe(0);
    }

    private static FrameSnapshot Snapshot(ConsoleOutput console) => new()
    {
        ConsoleLines = console.Drain(),
        ConsoleLinesWritten = console.Written,
    };

    private static List<(OutputSeverity Severity, string Text)> Rows(OutputLog output) =>
        [.. output.Entries.Select(entry => (entry.Severity, entry.Text))];
}
