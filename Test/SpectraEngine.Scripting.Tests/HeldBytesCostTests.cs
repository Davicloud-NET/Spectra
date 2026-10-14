using System.Diagnostics;
using System.Globalization;
using System.Text;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What levels that destroy parts hold and take, written down. A measurement
/// and no test: it has nothing to fail on, so it runs only when asked for,
/// with -explicit only. <see cref="HeldBytesTests"/> holds the bounds.
/// </summary>
public sealed class HeldBytesCostTests
{
    private const int Ticks = 4000;
    private const string Long = "string.rep(\"x\", 65536)";
    private const string Short = "\"0123456789abcdef\"";

    // A Luau heap of about 35 MiB in 400000 small tables.
    private const string Ballast = """
        local ballast = table.create(400000)
        for index = 1, 400000 do
            ballast[index] = { index }
        end
        """;

    [Fact(Explicit = true)]
    public void What_destroyed_parts_cost_is_written_down()
    {
        var table = new StringBuilder();
        table.AppendLine(EveryTick("10 a tick, 64 KiB string", 10, Long));
        table.AppendLine(EveryTick("1 a tick, 64 KiB string", 1, Long));
        table.AppendLine(EveryTick("10 a tick, 16 byte string", 10, Short));
        table.AppendLine(EveryTick("none destroyed", 0, Short));
        table.AppendLine(EveryTick("10 a tick, 64 KiB, 35 MiB of Luau", 10, Long, Ballast));
        table.AppendLine(InOneCall("20000 in one call, 64 KiB string", 20000, Long));
        table.AppendLine(InOneCall("20000 in one call, 16 byte string", 20000, Short));
        table.AppendLine(FullCollection("a full collection, level alone", string.Empty));
        table.AppendLine(FullCollection("a full collection, 35 MiB of Luau", Ballast));
        table.AppendLine(LooseEveryTick("loose, 10 a tick, 64 KiB string", Long));
        table.AppendLine(LooseEveryTick("loose, 10 a tick, 64 KiB, 35 MiB of Luau", Long, Ballast));
        table.AppendLine(LooseInOneCall("loose, 20000 in one call, 64 KiB", Long));
        table.AppendLine(LooseInOneCall("loose, 20000 in one call, 16 bytes", Short));
        table.AppendLine(LiveAttributes("live, 64 KiB strings, 8 MiB budget"));

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "held-bytes.txt"), table.ToString());
    }

    // Parts made with an attribute and a tag and let go of in no level.
    private static string LooseEveryTick(string name, string value, string before = "")
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, before + $$"""

            local value = {{value}}
            game:GetService("RunService").Heartbeat:Connect(function()
                local kept = ballast
                for index = 1, 10 do
                    local part = Instance.new("Part")
                    part:SetAttribute("Blob", value)
                    part:AddTag("Spawned")
                end
            end)
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();
        long floor = GC.GetTotalMemory(true);

        long managed = 0;
        int loose = 0;
        long spent = 0;
        for (int tick = 0; tick < Ticks; tick++)
        {
            long started = Stopwatch.GetTimestamp();
            level.Tick();
            spent += Stopwatch.GetTimestamp() - started;

            loose = Math.Max(loose, runtime.LooseTreeCount);
            if (tick % 20 == 0)
                managed = Math.Max(managed, GC.GetTotalMemory(true) - floor);
        }

        level.Errors.ShouldBeEmpty();
        return LooseLine(name, loose, managed, spent / (double)Ticks, "a tick");
    }

    private static string LooseInOneCall(string name, string value)
    {
        const int Count = 20000;
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, $$"""
            task.wait()
            local value = {{value}}
            for index = 1, {{Count}} do
                local part = Instance.new("Part")
                part:SetAttribute("Blob", value)
                part:AddTag("Spawned")
            end
            print("done")
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();
        runtime.CallLimit = TimeSpan.FromMinutes(5);
        long floor = GC.GetTotalMemory(true);

        long managed = 0;
        int loose = 0;
        using var done = new CancellationTokenSource();
        var watcher = new Thread(() =>
        {
            while (!done.IsCancellationRequested)
            {
                managed = Math.Max(managed, GC.GetTotalMemory(true) - floor);
                loose = Math.Max(loose, runtime.LooseTreeCount);
                Thread.Sleep(5);
            }
        });
        watcher.Start();

        long started = Stopwatch.GetTimestamp();
        level.Tick(2);
        long spent = Stopwatch.GetTimestamp() - started;
        done.Cancel();
        watcher.Join();

        level.Printed.ShouldBe(["done"]);
        return LooseLine(name, loose, managed, spent / (double)Count, "a part");
    }

    // A script that sets 64 KiB strings on parts of the level, 4 on each
    // of 200, under a budget of 8 MiB.
    private static string LiveAttributes(string name)
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            task.wait()
            local parts = {}
            for index = 1, 200 do
                local part = Instance.new("Part")
                part.Parent = workspace
                parts[index] = part
            end
            for index, part in parts do
                for slot = 1, 4 do
                    part:SetAttribute("Blob" .. slot, string.rep(string.char(64 + slot), 65536))
                end
            end
            print("done")
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Runtime.ShouldNotBeNull();
        runtime.MemoryBudget = 8 * 1048576;
        level.Tick();
        long floor = GC.GetTotalMemory(true);

        long started = Stopwatch.GetTimestamp();
        level.Tick();
        long spent = Stopwatch.GetTimestamp() - started;
        long managed = GC.GetTotalMemory(true) - floor;

        string outcome = level.Printed.Length > 0 ? "ran to its end" : "was switched off";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name,-36} {outcome}  managed {managed / 1048576.0,7:0.0} MiB  Luau {runtime.LuauBytes / 1024.0,7:0} KiB" +
            $"  {Stopwatch.GetElapsedTime(0, spent).TotalMilliseconds,8:0.0} ms");
    }

    private static string LooseLine(string name, int loose, long managed, double stopwatchTicks, string per) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{name,-36} waiting {loose,6} trees  managed {managed / 1048576.0,7:0.0} MiB" +
            $"  {stopwatchTicks * 1e6 / Stopwatch.Frequency,8:0.0} us {per}");

    private static string EveryTick(string name, int perTick, string value, string before = "")
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, before + $$"""

            local value = {{value}}
            game:GetService("RunService").Heartbeat:Connect(function()
                local kept = ballast
                for index = 1, {{perTick}} do
                    local part = Instance.new("Part")
                    part:SetAttribute("Blob", value)
                    part:AddTag("Spawned")
                    part.Parent = workspace
                    part:Destroy()
                end
            end)
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();
        long floor = GC.GetTotalMemory(true);

        long managed = 0;
        long luau = 0;
        long spent = 0;
        for (int tick = 0; tick < Ticks; tick++)
        {
            long started = Stopwatch.GetTimestamp();
            level.Tick();
            spent += Stopwatch.GetTimestamp() - started;

            luau = Math.Max(luau, runtime.LuauBytes);
            if (tick % 20 == 0)
                managed = Math.Max(managed, GC.GetTotalMemory(true) - floor);
        }

        level.Errors.ShouldBeEmpty();
        return Line(name, runtime, managed, luau, spent / (double)Ticks, "a tick");
    }

    private static string InOneCall(string name, int count, string value)
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, $$"""
            task.wait()
            local value = {{value}}
            for index = 1, {{count}} do
                local part = Instance.new("Part")
                part:SetAttribute("Blob", value)
                part:AddTag("Spawned")
                part.Parent = workspace
                part:Destroy()
            end
            print("done")
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();
        runtime.CallLimit = TimeSpan.FromMinutes(5);
        long floor = GC.GetTotalMemory(true);

        // The call does not return in between, so another thread looks.
        long managed = 0;
        long luau = 0;
        using var done = new CancellationTokenSource();
        var watcher = new Thread(() =>
        {
            while (!done.IsCancellationRequested)
            {
                managed = Math.Max(managed, GC.GetTotalMemory(true) - floor);
                luau = Math.Max(luau, runtime.LuauBytes);
                Thread.Sleep(5);
            }
        });
        watcher.Start();

        long started = Stopwatch.GetTimestamp();
        level.Tick(2);
        long spent = Stopwatch.GetTimestamp() - started;
        done.Cancel();
        watcher.Join();

        level.Printed.ShouldBe(["done"]);
        return Line(name, runtime, managed, luau, spent / (double)count, "a part");
    }

    private static string FullCollection(string name, string before)
    {
        const int Rounds = 50;
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, before + "\ntask.wait(1000)\nprint(ballast)", "Main");
        LuauScriptRuntime runtime = level.Play().Tick(2).Runtime.ShouldNotBeNull();
        runtime.Collect();

        long started = Stopwatch.GetTimestamp();
        for (int round = 0; round < Rounds; round++)
            runtime.Collect();

        double microseconds = Stopwatch.GetElapsedTime(started).TotalMicroseconds / Rounds;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name,-36} {microseconds,8:0.0} us with {runtime.LuauBytes / 1024.0,8:0} KiB of Luau");
    }

    private static string Line(
        string name, LuauScriptRuntime runtime, long managed, long luau, double stopwatchTicks, string per) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{name,-36} waiting {runtime.PeakHeldDestroyedCount,6} parts, {runtime.PeakHeldDestroyedBytes / 1024.0,8:0} KiB by the estimate" +
            $"  managed {managed / 1048576.0,7:0.0} MiB  Luau {luau / 1024.0,7:0} KiB" +
            $"  cycles {runtime.HeldCollectionCount,5}  {stopwatchTicks * 1e6 / Stopwatch.Frequency,8:0.0} us {per}");
}
