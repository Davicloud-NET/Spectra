using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// How much scripts may print in one tick, what gets through whatever the
/// count, and what a tick says about the lines it dropped.
/// </summary>
public sealed class PrintCapTests
{
    private const int Limit = PrintCap.MaxPrintsPerTick;

    [Fact]
    public void A_flood_of_prints_stops_at_the_limit_and_the_tick_says_how_many_it_dropped()
    {
        using var level = new ScriptLevel();
        SceneNode script = AddScript(level, """
            for i = 1, 250 do
                print("line " .. i)
            end
            """);

        level.Play().Tick();

        level.Printed.Length.ShouldBe(Limit);
        level.Printed[^1].ShouldBe($"line {Limit}");

        ScriptMessage notice = level.Sink.Messages[^1];
        notice.Severity.ShouldBe(ScriptSeverity.Warning);
        notice.Text.ShouldBe(
            "150 more lines were printed in this tick and dropped. Scripts may print 100 lines a tick.");
        notice.Position.ShouldBe($"{PathOf(script)}:2", "the notice stands where the script printed last");
    }

    [Fact]
    public void The_notice_stands_at_the_script_that_flooded_and_not_at_the_one_whose_line_was_dropped_last()
    {
        using var level = new ScriptLevel();
        SceneNode flood = AddScript(level, """
            for i = 1, 5000 do
                print("flood " .. i)
            end
            """);
        SceneNode quiet = AddScript(level, "print('ready')");

        level.Play().Tick();

        level.Printed.ShouldNotContain("ready", "the flood used the tick's prints up before the second script ran");
        ScriptMessage notice = level.Sink.Messages[^1];
        notice.Text.ShouldBe(PrintCap.Describe(4901));
        notice.Position.ShouldBe($"{PathOf(flood)}:2");
        notice.Script.ShouldNotBe(PathOf(quiet));
    }

    [Fact]
    public void Scripts_share_the_ticks_prints_and_the_notice_names_the_one_that_used_them()
    {
        using var level = new ScriptLevel();
        SceneNode busy = AddScript(level, $$"""
            for i = 1, {{Limit}} do
                print("busy")
            end
            """);
        AddScript(level, "print('one')\nprint('two')\nprint('three')");

        level.Play().Tick();

        level.Printed.Length.ShouldBe(Limit);
        level.Printed.ShouldAllBe(line => line == "busy");
        ScriptMessage notice = level.Sink.Messages[^1];
        notice.Text.ShouldBe(PrintCap.Describe(3));
        notice.Position.ShouldBe($"{PathOf(busy)}:2", "none of its own lines were dropped, and it left no room");
    }

    [Fact]
    public void The_script_that_printed_the_most_is_found_again_in_each_tick()
    {
        using var level = new ScriptLevel();
        SceneNode first = AddScript(level, $$"""
            for i = 1, {{Limit + 10}} do
                print("first")
            end
            """);
        SceneNode second = AddScript(level, $$"""
            task.wait()
            for i = 1, {{Limit + 20}} do
                print("second")
            end
            """);

        level.Play().Tick(2);

        ScriptMessage[] notices = [.. level.Sink.Messages.Where(message => message.Severity == ScriptSeverity.Warning)];
        notices.Select(notice => (notice.Text, notice.Position)).ShouldBe(
        [
            (PrintCap.Describe(10), $"{PathOf(first)}:2"),
            (PrintCap.Describe(20), $"{PathOf(second)}:3"),
        ]);
    }

    [Fact]
    public void A_warning_and_an_error_in_the_middle_of_a_flood_get_through()
    {
        using var level = new ScriptLevel();
        SceneNode script = AddScript(level, """
            for i = 1, 150 do
                print("before " .. i)
            end
            warn("careful")
            for i = 1, 50 do
                print("between " .. i)
            end
            error("broken")
            """);

        level.Play().Tick();

        level.Printed.Length.ShouldBe(Limit);
        level.Printed.ShouldAllBe(line => line.StartsWith("before "));

        ScriptMessage[] loud = [.. level.Sink.Messages.Where(message => message.Severity != ScriptSeverity.Print)];
        loud.Select(message => (message.Severity, message.Text, message.Line)).ShouldBe(
        [
            (ScriptSeverity.Warning, "careful", 4),
            (ScriptSeverity.Error, "broken", 8),
            (ScriptSeverity.Warning, PrintCap.Describe(100), 6),
        ]);
        loud[^1].Script.ShouldBe(PathOf(script));
    }

    [Fact]
    public void A_tick_that_stays_at_the_limit_says_nothing()
    {
        using var level = new ScriptLevel();
        AddScript(level, $"for i = 1, {Limit} do print(i) end");

        level.Play().Tick();

        level.Printed.Length.ShouldBe(Limit);
        level.Sink.Texts(ScriptSeverity.Warning).ShouldBeEmpty();
    }

    [Fact]
    public void The_next_tick_starts_its_count_again()
    {
        using var level = new ScriptLevel();
        AddScript(level, $$"""
            game:GetService("RunService").Heartbeat:Connect(function()
                for i = 1, {{Limit + 1}} do
                    print("tick")
                end
            end)
            """);

        level.Play().Tick(3);

        level.Printed.Length.ShouldBe(3 * Limit);
        level.Sink.Texts(ScriptSeverity.Warning).ShouldBe(
        [
            "1 more line was printed in this tick and dropped. Scripts may print 100 lines a tick.",
            "1 more line was printed in this tick and dropped. Scripts may print 100 lines a tick.",
            "1 more line was printed in this tick and dropped. Scripts may print 100 lines a tick.",
        ]);
    }

    [Fact]
    public void A_host_that_never_ends_a_tick_hears_of_the_dropped_lines_when_the_next_one_prints()
    {
        using var level = new ScriptLevel();
        AddScript(level, $$"""
            while true do
                for i = 1, {{Limit + 5}} do
                    print("loop")
                end
                task.wait()
            end
            """);
        level.Play();

        // The entities alone, with no play session around them to end the tick.
        level.Entities.Tick(ScriptLevel.Dt);
        level.Sink.Messages.Count.ShouldBe(Limit);

        level.Entities.Tick(ScriptLevel.Dt);

        level.Sink.Messages[Limit].Severity.ShouldBe(ScriptSeverity.Warning);
        level.Sink.Messages[Limit].Text.ShouldBe(PrintCap.Describe(5));
        level.Printed.Length.ShouldBe(2 * Limit);
    }

    private static SceneNode AddScript(ScriptLevel level, string text) =>
        level.AddScript(level.Part("Part", new Vector3(0f, 2f, 0f)), text);

    private static string PathOf(SceneNode script) => script.Script.ShouldNotBeNull().Path;
}
