using System;
using System.Diagnostics;
using System.Threading;

namespace LuauSpike.Checks;

// Check 7: a script that never returns, stopped from another thread.
internal static unsafe class InterruptChecks
{
    private const int WatchdogMilliseconds = 50;
    private const int CountIterations = 50_000_000;

    internal static void Run(Report report)
    {
        nint L = LuaHost.NewState();
        try
        {
            int module = Scripts.LoadModule(L, "runaway");
            report.Run("7a", "stop a runaway loop", () => Stop(report, "7a", "stop a runaway loop", L, module, "spin"u8));
            report.Run("7b", "stop a loop that uses pcall", () => Stop(report, "7b", "stop a loop that uses pcall", L, module, "stubborn"u8));
            report.Run("7", "interrupt overhead", () => Overhead(report, L, module));
        }
        finally
        {
            Shim.sl_close(L);
        }
    }

    private static void Stop(Report report, string id, string what, nint L, int module, ReadOnlySpan<byte> function)
    {
        LuaStack.PushMember(L, module, function);

        long armedAt = 0;
        Thread watchdog = new(() =>
        {
            Thread.Sleep(WatchdogMilliseconds);
            armedAt = Stopwatch.GetTimestamp();
            Shim.sl_stop_arm(L);
        });

        long start = Stopwatch.GetTimestamp();
        watchdog.Start();
        int status = Lua.lua_pcall(L, 0, 0, 0);
        long returned = Stopwatch.GetTimestamp();
        watchdog.Join();
        Shim.sl_stop_disarm(L);

        string? message = LuaStack.ToText(L, -1);
        Lua.Pop(L, 1);

        // The state must still work after the stop.
        double total = Count(L, module, 1000);

        bool passed = status == Lua.ErrRun && message is not null && message.EndsWith("script stopped by host", StringComparison.Ordinal) && total == 500500;
        report.Check(id, what, passed,
            $"ran {Bench.Nanoseconds(returned - start) / 1e6:F1} ms, returned {Bench.Nanoseconds(returned - armedAt) / 1e3:F0} us after the stop request with \"{message}\"; the state still runs scripts");
    }

    private static double Count(nint L, int module, double n)
    {
        LuaStack.PushMember(L, module, "count"u8);
        Lua.lua_pushnumber(L, n);
        LuaStack.Call(L, 1, 1);
        double total = Lua.ToNumber(L, -1);
        Lua.Pop(L, 1);
        return total;
    }

    private static void Overhead(Report report, nint L, int module)
    {
        double without = Bench.BestNanosecondsPerOperation(Bench.Rounds, CountIterations, () => Count(L, module, CountIterations));

        long callsBefore = Shim.sl_interrupt_calls(L);
        Shim.sl_interrupt_install(L, 1);
        double with = Bench.BestNanosecondsPerOperation(Bench.Rounds, CountIterations, () => Count(L, module, CountIterations));
        Shim.sl_interrupt_install(L, 0);
        long calls = Shim.sl_interrupt_calls(L) - callsBefore;

        report.Note("7", "loop iteration, no interrupt", $"{without:F2} ns");
        report.Note("7", "loop iteration, interrupt set", $"{with:F2} ns ({calls / Bench.Rounds:N0} handler calls per {CountIterations:N0} iterations)");
    }
}
