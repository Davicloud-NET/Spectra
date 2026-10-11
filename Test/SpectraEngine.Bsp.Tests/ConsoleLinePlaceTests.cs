using Microsoft.Extensions.Logging;
using SpectraEngine.Core.ConsoleSystem;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A console line that says where it came from: what the output keeps of it,
/// and how it reads where nobody can click.
/// </summary>
public sealed class ConsoleLinePlaceTests
{
    private const string Door = "Scripts/door.luau";

    [Fact]
    public void A_line_written_with_a_place_keeps_it_and_takes_the_next_number()
    {
        var output = new ConsoleOutput();
        ConsolePlace[] stack = [new(Door, 30, "press")];
        Guid node = Guid.NewGuid();

        output.Print("first");
        output.Write(new ConsoleLine(99, LogLevel.Error, "it broke")
        {
            Place = new ConsolePlace(Door, 12),
            Stack = stack,
            ProblemNode = node,
        });

        IReadOnlyList<ConsoleLine> lines = output.Drain();
        lines[0].ShouldBe(new ConsoleLine(0, LogLevel.Information, "first"));
        lines[1].Sequence.ShouldBe(1, "the output numbers a line, whatever number it came with");
        lines[1].Severity.ShouldBe(LogLevel.Error);
        lines[1].Text.ShouldBe("it broke");
        lines[1].Place.ShouldBe(new ConsolePlace(Door, 12));
        lines[1].Stack.ShouldBe(stack);
        lines[1].ProblemNode.ShouldBe(node);
    }

    [Fact]
    public void A_line_a_command_printed_has_no_place_no_stack_and_no_node()
    {
        var output = new ConsoleOutput();

        output.Warn("a quote was not closed");

        ConsoleLine line = output.Drain().ShouldHaveSingleItem();
        line.Place.ShouldBeNull();
        line.Stack.ShouldBeEmpty();
        line.ProblemNode.ShouldBe(Guid.Empty);
        line.PlainText.ShouldBe("a quote was not closed");
    }

    [Fact]
    public void A_line_nobody_built_has_an_empty_stack()
    {
        default(ConsoleLine).Stack.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(12, "Scripts/door.luau:12: it broke")]
    [InlineData(0, "Scripts/door.luau: it broke")]
    public void As_plain_text_a_line_has_its_place_in_front(int line, string expected)
    {
        var placed = new ConsoleLine(0, LogLevel.Error, "it broke") { Place = new ConsolePlace(Door, line) };

        placed.PlainText.ShouldBe(expected);
    }

    [Theory]
    [InlineData("open", "Scripts/door.luau:12 in open")]
    [InlineData(null, "Scripts/door.luau:12")]
    public void A_frame_reads_as_its_position_and_the_function_when_it_has_a_name(string? function, string expected)
    {
        new ConsolePlace(Door, 12, function).Described.ShouldBe(expected);
    }

    [Fact]
    public void The_count_of_written_lines_includes_the_ones_the_output_could_not_hold()
    {
        const int Lost = 6;
        var output = new ConsoleOutput();

        for (int i = 0; i < ConsoleOutput.Capacity + Lost; i++)
            output.Print("line");

        output.Drain().Count.ShouldBe(ConsoleOutput.Capacity);
        output.Written.ShouldBe(ConsoleOutput.Capacity + Lost);
    }

    [Fact]
    public void An_error_after_a_flood_of_other_lines_is_kept_with_its_number()
    {
        const int Lost = 300;
        var output = new ConsoleOutput();

        // warn() in a loop, and then the error the loop led to.
        for (int i = 0; i < ConsoleOutput.Capacity + Lost; i++)
            output.Warn("careful");
        output.Write(new ConsoleLine(0, LogLevel.Error, "it broke") { Place = new ConsolePlace(Door, 12) });
        output.Print("after");

        IReadOnlyList<ConsoleLine> kept = output.Drain();
        kept.Count.ShouldBe(ConsoleOutput.Capacity + 1);
        kept[^1].Text.ShouldBe("it broke");
        kept[^1].Sequence.ShouldBe(ConsoleOutput.Capacity + Lost, "the jump in the numbers is how many lines went");
        kept[^1].Place.ShouldBe(new ConsolePlace(Door, 12));
    }

    [Fact]
    public void A_flood_of_errors_is_cut_too_when_their_own_room_is_full()
    {
        var output = new ConsoleOutput();

        for (int i = 0; i < ConsoleOutput.Capacity + ConsoleOutput.ErrorRoom + 50; i++)
            output.Error("it broke");

        output.Drain().Count.ShouldBe(ConsoleOutput.Capacity + ConsoleOutput.ErrorRoom);
        output.Written.ShouldBe(ConsoleOutput.Capacity + ConsoleOutput.ErrorRoom + 50);
    }
}
