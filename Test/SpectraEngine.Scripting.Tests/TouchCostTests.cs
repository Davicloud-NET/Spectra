using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What listening for a touch costs a tick. A level with a thousand parts a
/// script listens to pays for the ones near the player, and none of it may
/// allocate. The times are a measurement that runs when asked for, and are
/// then written beside the test binary, in touch-costs.txt.
/// </summary>
public sealed class TouchCostTests
{
    private const int WarmTicks = 120;
    private const int Ticks = 600;
    private const int TimedTicks = 50_000;
    private const int Runs = 7;

    // Far enough apart that the player is near one part at a time.
    private const float Spacing = 4f;

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1000)]
    public void A_tick_with_so_many_parts_listened_to_allocates_nothing(int parts)
    {
        using ScriptLevel level = Level(parts, isListening: true);
        level.Play().Tick(WarmTicks);
        level.Entities.Listeners.TouchListenerCount.ShouldBe(2 * parts);
        level.Entities.Touches.PartTouchCount.ShouldBe(1, "the player stands in the first of them");

        // The least of a few runs: the runtime compiles a method again once
        // it has run often, and that one time is no cost of a tick.
        long allocated = long.MaxValue;
        for (int run = 0; run < 3; run++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            level.Tick(Ticks);
            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        level.Errors.ShouldBeEmpty();
        allocated.ShouldBe(0, $"{Ticks} ticks with {parts} parts listened to allocated");
    }

    // A measurement and no test: it has nothing to fail on, so it runs only
    // when asked for, with -explicit only.
    [Fact(Explicit = true)]
    public void The_cost_of_a_tick_with_parts_listened_to_is_written_down()
    {
        var table = new StringBuilder();
        table.AppendLine("parts   nobody listens   all listened to   listening costs");
        foreach (int parts in new[] { 1, 100, 1000 })
        {
            double unheard = Nanoseconds(parts, isListening: false);
            double heard = Nanoseconds(parts, isListening: true);
            table.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{parts,5}   {unheard,11:0} ns   {heard,13:0} ns   {heard - unheard,12:0} ns"));
        }

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "touch-costs.txt"), table.ToString());
    }

    // The time of one tick of the whole play session, the best of a few runs.
    private static double Nanoseconds(int parts, bool isListening)
    {
        using ScriptLevel level = Level(parts, isListening);
        level.Play().Tick(WarmTicks);

        double best = double.MaxValue;
        for (int run = 0; run < Runs; run++)
        {
            long started = Stopwatch.GetTimestamp();
            level.Tick(TimedTicks);
            best = Math.Min(best, Stopwatch.GetElapsedTime(started).TotalNanoseconds / TimedTicks);
        }

        level.Errors.ShouldBeEmpty();
        return best;
    }

    // Zones in a square grid on the ground, the first around the player's
    // start. They are not solid: the mover builds the planes of every solid
    // part near the player on each tick, and that is no cost of a touch.
    private static ScriptLevel Level(int parts, bool isListening)
    {
        var level = new ScriptLevel();
        SceneNode zones = level.Scene.Root.CreateChild("Zones");
        int side = (int)MathF.Ceiling(MathF.Sqrt(parts));
        for (int i = 0; i < parts; i++)
        {
            var center = new Vector3(i % side * Spacing, 1f, i / side * Spacing);
            TouchParts.Box(level, $"Zone{i}", center, new Vector3(1f), isSolid: false, parent: zones);
        }

        // The same script either way, so the two levels differ in nothing
        // but who listens.
        string listen = isListening
            ? "zone.Touched:Connect(function() end) zone.TouchEnded:Connect(function() end)"
            : "local _ = zone.Touched";

        level.AddScript(zones, $"""
            for _, zone in script.Parent:GetChildren() do
                if zone:IsA("BasePart") then
                    {listen}
                end
            end
            """);

        return level;
    }
}
