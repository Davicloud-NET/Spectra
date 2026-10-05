using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Hosting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>Running a typed line: what runs, what is refused and what is printed.</summary>
public sealed class SpectraConsoleTests
{
    private static string[] Run(SpectraConsole console, string line)
    {
        console.Execute(line, default);
        return [.. console.Output.Drain().Select(printed => printed.Text)];
    }

    [Fact]
    public void An_unknown_command_is_an_error_that_names_itself()
    {
        var console = new SpectraConsole();

        console.Execute("noclip on", default);

        ConsoleLine line = console.Output.Drain().ShouldHaveSingleItem();
        line.Severity.ShouldBe(LogLevel.Error);
        line.Text.ShouldBe("no command named 'noclip'. Type help.");
    }

    [Fact]
    public void A_command_that_throws_is_reported_and_the_next_one_still_runs()
    {
        var console = new SpectraConsole();
        console.Commands.Add(new ConCommand(
            "boom", "boom", "Throws.", (in ConArgs _) => throw new InvalidOperationException("the fuse was lit")));

        console.Execute("boom; echo still here", default);

        IReadOnlyList<ConsoleLine> lines = console.Output.Drain();
        lines.Count.ShouldBe(2);
        lines[0].Severity.ShouldBe(LogLevel.Error);
        lines[0].Text.ShouldBe("boom failed. InvalidOperationException: the fuse was lit");
        lines[1].ShouldBe(new ConsoleLine(1, LogLevel.Information, "still here"));
    }

    [Fact]
    public void A_line_that_starts_with_the_script_sigil_is_refused()
    {
        var console = new SpectraConsole();

        console.Execute("  > print(1); echo ran", default);

        ConsoleLine line = console.Output.Drain().ShouldHaveSingleItem();
        line.Severity.ShouldBe(LogLevel.Error);
        line.Text.ShouldContain("Luau is not built yet");
    }

    [Fact]
    public void Help_lists_every_command_in_the_table()
    {
        var console = new SpectraConsole();
        console.Commands.Add(new ConCommand(
            "noclip", "noclip [on | off]", "Flies through walls.", (in ConArgs _) => { }));

        string[] rows = Run(console, "help");

        rows.Length.ShouldBe(console.Commands.Commands.Count);
        foreach (ConCommand command in console.Commands.Commands)
            rows.ShouldContain($"{command.Name}  {command.Help}");
        rows.ShouldContain("noclip  Flies through walls.");
    }

    [Fact]
    public void Help_for_one_command_says_how_to_use_it()
    {
        var console = new SpectraConsole();

        Run(console, "help ECHO").ShouldBe(["echo <text>", "Prints its arguments."]);
    }

    [Fact]
    public void Help_for_a_command_that_does_not_exist_says_so()
    {
        var console = new SpectraConsole();

        console.Execute("help noclip", default);

        ConsoleLine line = console.Output.Drain().ShouldHaveSingleItem();
        line.Severity.ShouldBe(LogLevel.Error);
        line.Text.ShouldBe("help: no command named 'noclip'.");
    }

    [Fact]
    public void Echo_prints_its_arguments_with_one_space_between_them()
    {
        var console = new SpectraConsole();

        Run(console, "ECHO   \"a;  b\"   c // not this").ShouldBe(["a;  b c"]);
    }

    [Fact]
    public void A_quote_left_open_warns_and_the_command_still_runs()
    {
        var console = new SpectraConsole();

        console.Execute("echo \"a b", default);

        IReadOnlyList<ConsoleLine> lines = console.Output.Drain();
        lines.Count.ShouldBe(2);
        lines[0].Severity.ShouldBe(LogLevel.Warning);
        lines[0].Text.ShouldEndWith("echo \"a b");
        lines[1].Text.ShouldBe("a b");
    }

    [Fact]
    public void A_line_with_no_command_prints_nothing()
    {
        var console = new SpectraConsole();

        Run(console, "").ShouldBeEmpty();
        Run(console, " ;; // only a comment").ShouldBeEmpty();
    }

    [Fact]
    public void A_line_too_long_for_the_stack_runs_the_same()
    {
        var console = new SpectraConsole();
        string word = new('x', 400);

        Run(console, $"echo {word} \"{word}\"; echo end").ShouldBe([$"{word} {word}", "end"]);
    }

    [Fact]
    public void Lines_that_overflow_the_output_keep_their_sequence_numbers()
    {
        const int Lost = 3;
        var console = new SpectraConsole();
        for (int i = 0; i < ConsoleOutput.Capacity + Lost; i++)
            console.Execute("echo flood", default);

        IReadOnlyList<ConsoleLine> kept = console.Output.Drain();
        console.Execute("echo after", default);
        ConsoleLine next = console.Output.Drain().ShouldHaveSingleItem();

        kept.Count.ShouldBe(ConsoleOutput.Capacity);
        kept[0].Sequence.ShouldBe(0);
        kept[kept.Count - 1].Sequence.ShouldBe(ConsoleOutput.Capacity - 1);
        next.Sequence.ShouldBe(ConsoleOutput.Capacity + Lost);
    }

    [Fact]
    public void Draining_hands_the_lines_over_and_starts_empty()
    {
        var console = new SpectraConsole();
        console.Execute("echo one", default);

        IReadOnlyList<ConsoleLine> first = console.Output.Drain();
        console.Execute("echo two", default);

        first.ShouldHaveSingleItem().Text.ShouldBe("one");
        console.Output.Count.ShouldBe(1);
        console.Output.Drain().ShouldHaveSingleItem().Text.ShouldBe("two");
        console.Output.Drain().ShouldBeEmpty();
    }

    [Fact]
    public void Draining_runs_queued_lines_and_marks_the_host_dirty()
    {
        var console = new SpectraConsole();
        EngineHost host = QuietHost();
        host.SubmitConsoleLine("echo one; echo two");
        host.SubmitConsoleLine("echo three");

        // The submit asked for a snapshot of its own. Let that one go first.
        host.PublishFrame(TimeSpan.FromMilliseconds(1), Build).ShouldNotBeNull();
        host.PublishFrame(TimeSpan.FromMilliseconds(2), Build).ShouldBeNull();

        console.Drain(host, default);

        Printed(console).ShouldBe(["one", "two", "three"]);
        host.PublishFrame(TimeSpan.FromMilliseconds(3), Build).ShouldNotBeNull();
    }

    [Fact]
    public void A_drain_with_nothing_to_run_leaves_the_host_alone()
    {
        var console = new SpectraConsole();
        EngineHost host = QuietHost();

        console.Drain(host, default);

        Printed(console).ShouldBeEmpty();
        host.PublishFrame(TimeSpan.FromMilliseconds(1), Build).ShouldBeNull();
    }

    [Fact]
    public void Wait_holds_the_rest_of_the_line_for_a_later_drain()
    {
        var console = new SpectraConsole();
        EngineHost host = QuietHost();
        host.SubmitConsoleLine("echo before; wait; echo after");
        host.SubmitConsoleLine("echo next line");

        console.Drain(host, default);
        Printed(console).ShouldBe(["before"]);

        // The held half runs ahead of the line that was queued behind it.
        console.Drain(host, default);
        Printed(console).ShouldBe(["after", "next line"]);
    }

    [Fact]
    public void Wait_counts_drains()
    {
        var console = new SpectraConsole();
        EngineHost host = QuietHost();
        host.SubmitConsoleLine("wait 3; echo done");

        console.Drain(host, default);
        console.Drain(host, default);
        console.Drain(host, default);
        Printed(console).ShouldBeEmpty();

        console.Drain(host, default);
        Printed(console).ShouldBe(["done"]);
    }

    [Fact]
    public void A_wait_at_the_end_of_a_line_holds_the_lines_behind_it()
    {
        var console = new SpectraConsole();
        EngineHost host = QuietHost();
        host.SubmitConsoleLine("wait 2");
        host.SubmitConsoleLine("echo later");

        console.Drain(host, default);
        console.Drain(host, default);
        Printed(console).ShouldBeEmpty();

        console.Drain(host, default);
        Printed(console).ShouldBe(["later"]);
    }

    [Fact]
    public void A_wait_run_directly_keeps_the_rest_for_the_next_drain()
    {
        var console = new SpectraConsole();
        EngineHost host = QuietHost();

        console.Execute("echo now; wait; echo then", default);
        Printed(console).ShouldBe(["now"]);

        console.Drain(host, default);
        Printed(console).ShouldBe(["then"]);
    }

    [Theory]
    [InlineData("wait soon")]
    [InlineData("wait 0")]
    [InlineData("wait -1")]
    [InlineData("wait 1.5")]
    public void A_wait_that_is_not_a_frame_count_is_an_error_and_holds_nothing(string wait)
    {
        var console = new SpectraConsole();

        console.Execute($"{wait}; echo ran", default);

        IReadOnlyList<ConsoleLine> lines = console.Output.Drain();
        lines.Count.ShouldBe(2);
        lines[0].Severity.ShouldBe(LogLevel.Error);
        lines[0].Text.ShouldStartWith("wait: ");
        lines[1].Text.ShouldBe("ran");
    }

    [Fact]
    public void A_wait_over_the_limit_waits_the_limit_and_says_so()
    {
        var console = new SpectraConsole();
        EngineHost host = QuietHost();
        host.SubmitConsoleLine("wait 100000; echo done");

        console.Drain(host, default);

        ConsoleLine warning = console.Output.Drain().ShouldHaveSingleItem();
        warning.Severity.ShouldBe(LogLevel.Warning);
        warning.Text.ShouldContain($"{SpectraConsole.MaxWaitFrames}");

        for (int i = 1; i < SpectraConsole.MaxWaitFrames; i++)
            console.Drain(host, default);
        Printed(console).ShouldBeEmpty();

        console.Drain(host, default);
        Printed(console).ShouldBe(["done"]);
    }

    [Fact]
    public void A_drain_stops_at_its_command_limit_and_keeps_the_rest()
    {
        const int Limit = SpectraConsole.MaxCommandsPerDrain;
        var console = new SpectraConsole();
        EngineHost host = QuietHost();
        host.SubmitConsoleLine(string.Join("; ", Enumerable.Range(0, Limit + 3).Select(i => $"echo {i}")));
        host.SubmitConsoleLine("echo last");

        console.Drain(host, default);
        string[] first = Printed(console);
        first.Length.ShouldBe(Limit);
        first[^1].ShouldBe($"{Limit - 1}");

        console.Drain(host, default);
        Printed(console).ShouldBe([$"{Limit}", $"{Limit + 1}", $"{Limit + 2}", "last"]);
    }

    private static string[] Printed(SpectraConsole console) =>
        [.. console.Output.Drain().Select(printed => printed.Text)];

    // Published once already, and with an interval nothing here reaches, so a
    // later snapshot goes out only when something marked the host dirty.
    private static EngineHost QuietHost()
    {
        var host = new EngineHost(NullLogger.Instance) { SnapshotInterval = TimeSpan.FromHours(1) };
        host.PublishFrame(TimeSpan.Zero, Build);
        return host;
    }

    private static FrameSnapshot Build(FrameSnapshotBuilder builder) => new() { FrameNumber = builder.FrameNumber };
}
