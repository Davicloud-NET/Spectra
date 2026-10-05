using Microsoft.Extensions.Logging;
using SpectraEngine.Core.ConsoleSystem;

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
}
