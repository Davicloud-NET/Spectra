using Microsoft.Extensions.Logging;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What scripts report, as the console's lines: the text, and beside it the
/// place, an error's stack and the node of a script that did not start. None
/// of this needs the Luau library.
/// </summary>
public sealed class ConsoleScriptSinkTests
{
    private const string Door = "Scripts/door.luau";

    [Fact]
    public void A_print_a_warning_and_an_error_keep_their_text_and_carry_their_place_beside_it()
    {
        var output = new ConsoleOutput();
        var sink = new ConsoleScriptSink(output);

        sink.Report(new ScriptMessage(ScriptSeverity.Print, "hello", Door, 3));
        sink.Report(new ScriptMessage(ScriptSeverity.Warning, "careful", Door, 4));
        sink.Report(new ScriptMessage(ScriptSeverity.Error, "it broke", Door, 12));

        output.Drain().Select(line => (line.Severity, line.Text, line.Place)).ShouldBe(
        [
            (LogLevel.Information, "hello", new ConsolePlace(Door, 3)),
            (LogLevel.Warning, "careful", new ConsolePlace(Door, 4)),
            (LogLevel.Error, "it broke", new ConsolePlace(Door, 12)),
        ]);
    }

    [Fact]
    public void A_message_with_no_script_is_a_line_with_no_place()
    {
        var output = new ConsoleOutput();
        var sink = new ConsoleScriptSink(output);

        sink.Report(new ScriptMessage(ScriptSeverity.Error, "The script runtime failed: no state", string.Empty, 0));

        ConsoleLine line = output.Drain().ShouldHaveSingleItem();
        line.Place.ShouldBeNull();
        line.Stack.ShouldBeEmpty();
        line.PlainText.ShouldBe("The script runtime failed: no state");
    }

    [Fact]
    public void A_message_that_knows_its_file_and_not_its_line_has_a_place_with_no_line()
    {
        var output = new ConsoleOutput();
        var sink = new ConsoleScriptSink(output);

        sink.Report(new ScriptMessage(ScriptSeverity.Error, "it is not in the level's content", Door, 0));

        ConsoleLine line = output.Drain().ShouldHaveSingleItem();
        line.Place.ShouldBe(new ConsolePlace(Door, 0));
        line.PlainText.ShouldBe("Scripts/door.luau: it is not in the level's content");
    }

    [Fact]
    public void An_error_is_one_line_with_its_callers_under_it()
    {
        var output = new ConsoleOutput();
        var sink = new ConsoleScriptSink(output);
        ScriptFrame[] stack = [new(Door, 12, "open"), new(Door, 30, "press"), new("Scripts/lobby.luau", 4, null)];

        sink.Report(new ScriptMessage(ScriptSeverity.Error, "it broke", Door, 12, stack));

        // The line names where the first frame was, so that one is not said twice.
        ConsoleLine line = output.Drain().ShouldHaveSingleItem();
        line.Place.ShouldBe(new ConsolePlace(Door, 12));
        line.Stack.ShouldBe([new ConsolePlace(Door, 30, "press"), new ConsolePlace("Scripts/lobby.luau", 4)]);
    }

    [Fact]
    public void The_frame_that_raised_stays_when_the_line_names_its_caller()
    {
        var output = new ConsoleOutput();
        var sink = new ConsoleScriptSink(output);
        ScriptFrame[] stack = [new(Door, 12, "open"), new(Door, 30, null)];

        // What error(message, 2) gives: the position is the caller's.
        sink.Report(new ScriptMessage(ScriptSeverity.Error, "no such door", Door, 30, stack));

        ConsoleLine line = output.Drain().ShouldHaveSingleItem();
        line.Place.ShouldBe(new ConsolePlace(Door, 30));
        line.Stack.ShouldBe([new ConsolePlace(Door, 12, "open"), new ConsolePlace(Door, 30)]);
    }

    [Fact]
    public void A_script_that_did_not_start_names_its_node_on_the_line()
    {
        var output = new ConsoleOutput();
        var sink = new ConsoleScriptSink(output);
        Guid node = Guid.NewGuid();

        sink.Report(new ScriptMessage(ScriptSeverity.Error, "it did not start", Door, 2) { ProblemNode = node });
        sink.Report(new ScriptMessage(ScriptSeverity.Error, "it broke while running", Door, 9));

        output.Drain().Select(line => line.ProblemNode).ShouldBe([node, Guid.Empty]);
    }

    [Fact]
    public void In_a_log_a_line_reads_with_its_file_and_line_in_front()
    {
        var output = new ConsoleOutput();
        var sink = new ConsoleScriptSink(output);

        sink.Report(new ScriptMessage(ScriptSeverity.Print, "hello", Door, 3));

        output.Drain().ShouldHaveSingleItem().PlainText.ShouldBe("Scripts/door.luau:3: hello");
    }
}
