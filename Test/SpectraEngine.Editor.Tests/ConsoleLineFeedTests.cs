using Microsoft.Extensions.Logging;
using SpectraEngine.Core.ConsoleSystem;
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

    private static List<(OutputSeverity Severity, string Text)> Rows(OutputLog output) =>
        [.. output.Entries.Select(entry => (entry.Severity, entry.Text))];
}
