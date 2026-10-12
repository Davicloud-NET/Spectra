using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What it costs a script to cross into the engine and back: a property
/// read, a method call, a Vector3 and a CFrame. None of them may allocate.
/// The times are a measurement that runs when asked for, and are then
/// written beside the test binary, in script-costs.txt.
/// </summary>
public sealed class ScriptCostTests
{
    private const int Rounds = 2000;
    private const int WarmTicks = 30;
    private const int Ticks = 100;
    private const int Runs = 3;

    private const string Nothing = "an empty loop";

    // What a script does in one round, by what it is called in the table.
    private static readonly (string Name, string Statement)[] Crossings =
    [
        (Nothing, ""),
        ("read a number of a CFrame", "local _ = f.X"),
        ("read a Vector3 of a CFrame", "local _ = f.Position"),
        ("read a Vector3 of a node", "local _ = part.Position"),
        ("read a CFrame of a node", "local _ = part.CFrame"),
        ("read a string of a node", "local _ = part.Name"),
        ("read a node of a node", "local _ = part.Parent"),
        ("read a signal of a service", "local _ = run.Heartbeat"),
        ("write a Vector3 to a node", "part.Position = a"),
        ("call a method of a Vector3", "local _ = a:Dot(b)"),
        ("call a method of a CFrame, CFrame out", "local _ = f:Inverse()"),
        ("call a method of a CFrame, Vector3 in and out", "local _ = f:PointToWorldSpace(a)"),
        ("call a method of a service", "local _ = game:GetService('RunService')"),
        ("read a method and do not call it", "local _ = f.Inverse"),
        ("call a method of a CFrame with a dot", "local _ = f.Inverse(f)"),
        ("call a method of a service with a dot", "local _ = game.GetService(game, 'RunService')"),
        ("call a library function, CFrame out", "local _ = CFrame.Angles(0, i, 0)"),
        ("multiply two CFrames", "local _ = f * g"),
        ("find a child by its name", "local _ = part:FindFirstChild('Script')"),
        ("read a child by its name", "local _ = part.Script"),
        ("ask a node what it is", "local _ = part:IsA('BasePart')"),
    ];

    public static TheoryData<string> Names => new(Crossings.Select(crossing => crossing.Name));

    [Theory]
    [MemberData(nameof(Names))]
    public void A_crossing_allocates_nothing_on_the_managed_heap(string name)
    {
        Measure(name).Allocated.ShouldBe(0, $"{Ticks * Rounds} rounds of '{name}' allocated");
    }

    // A measurement and no test: it has nothing to fail on, so it runs only
    // when asked for, with -explicit only.
    [Fact(Explicit = true)]
    public void The_cost_of_every_crossing_is_written_down()
    {
        double empty = Measure(Nothing).Nanoseconds;
        var table = new StringBuilder();
        foreach ((string name, _) in Crossings)
        {
            (double nanoseconds, long allocated) = Measure(name);
            table.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name,-48} {Math.Max(0, nanoseconds - empty),7:0} ns  {allocated / (double)(Ticks * Rounds),5:0.#} bytes"));
        }

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "script-costs.txt"), table.ToString());
    }

    // The time of one round and the bytes all rounds allocated.
    private static (double Nanoseconds, long Allocated) Measure(string name)
    {
        string statement = Crossings.Single(crossing => crossing.Name == name).Statement;

        using var level = new ScriptLevel(hasCharacter: false);
        SceneNode part = level.Part("Mover", new Vector3(0f, 2f, 0f));
        level.AddScript(part, $$"""
            local part = script.Parent
            local a = Vector3.new(1, 2, 3)
            local b = Vector3.new(-4, 0.5, 2)
            local f = CFrame.new(1, 2, 3) * CFrame.Angles(0.1, 0.2, 0.3)
            local g = CFrame.new(4, 5, 6)
            local run = game:GetService("RunService")
            run.Heartbeat:Connect(function()
                for i = 1, {{Rounds}} do
                    {{statement}}
                end
            end)
            """);

        level.Play().Tick(WarmTicks);

        // The best of a few runs. The runtime compiles a method again once it
        // has run often, and that one time is no cost of the crossing.
        double nanoseconds = double.MaxValue;
        long allocated = long.MaxValue;
        for (int run = 0; run < Runs; run++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            level.Tick(Ticks);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);

            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
            nanoseconds = Math.Min(nanoseconds, elapsed.TotalNanoseconds / (Ticks * Rounds));
        }

        level.Errors.ShouldBeEmpty();
        return (nanoseconds, allocated);
    }
}
