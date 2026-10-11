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
        notice.Position.ShouldBe($"{PathOf(script)}:2", "the notice stands where the last dropped line was printed");
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
