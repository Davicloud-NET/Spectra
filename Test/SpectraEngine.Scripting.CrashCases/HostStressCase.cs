using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.CrashCases;

// The ways the binding ends a script, one after the other and with the
// collector at work: an error in the script, an exception in a host function
// by both ways in, a stop by the watchdog and a stop by the memory budget.
// Each is a raise from native code, and none may cross a managed frame.
internal static unsafe class HostStressCase
{
    private const int Rounds = 40;
    private const string Thrown = "thrown by the host";

    private const string Failing = "churn()\nlocal door = nil\nreturn door.open";

    private const string Throwing = """
        local caught, why = pcall(explode)
        churn()
        local caughtFast, whyFast = pcall(explode_fast)
        churn()
        if caught or caughtFast then error('an exception was lost') end
        explode()
        """;

    private const string Spinning = "churn()\nwhile true do end";

    // Large pieces, so the budget is met long before the time limit is.
    private const string Hoarding = "local hoard = {}\nwhile true do hoard[#hoard + 1] = string.rep('x', 65536) .. #hoard end";

    internal static int Run()
    {
        CountingSink sink = new();
        using LuauHost host = Open(sink);
        string[] canaries = Stress.MakeCanaries();

        for (int round = 1; round <= Rounds; round++)
        {
            string? failure = PlayRound(host, sink) ?? Stress.FindDamage(canaries);
            if (failure is not null)
            {
                Console.WriteLine($"CASE failed in round {round}: {failure}");
                return CaseExit.Failed;
            }

            host.Collect();
            GC.Collect();
        }

        Console.WriteLine($"CASE survived {Rounds} rounds, {sink.Errors} errors reported");
        return sink.Errors == Rounds * 4 ? CaseExit.Passed : CaseExit.Failed;
    }

    private static LuauHost Open(CountingSink sink)
    {
        LuauHost host = new(sink);
        LuauStack stack = host.Stack;

        bool isSet = host.PushFunction(stack, "churn"u8, _ =>
            {
                Stress.Churn();
                return 0;
            })
            && stack.SetGlobal("churn"u8)
            && host.PushFunction(stack, "explode"u8, _ => throw new InvalidOperationException(Thrown))
            && stack.SetGlobal("explode"u8)
            && stack.PushFunction(&ExplodeFast, "explode_fast"u8)
            && stack.SetGlobal("explode_fast"u8);
        if (!isSet)
            throw new InvalidOperationException("The host functions could not be set.");

        host.Seal();
        host.CallLimit = TimeSpan.FromMilliseconds(20);
        host.MemoryBudget = 4 * 1024 * 1024;
        return host;
    }

    // Null when the round went as it should.
    private static string? PlayRound(LuauHost host, CountingSink sink) =>
        Fail(host, sink, Failing, "attempt to index nil", isSwitchedOff: false)
        ?? Fail(host, sink, Throwing, Thrown, isSwitchedOff: false)
        ?? Fail(host, sink, Spinning, "without a pause", isSwitchedOff: true)
        ?? Fail(host, sink, Hoarding, "hold more than", isSwitchedOff: true)
        ?? Finish(host);

    // Runs a script that must end in an error with this text.
    private static string? Fail(LuauHost host, CountingSink sink, string source, string expected, bool isSwitchedOff)
    {
        int before = sink.Errors;
        LuauScript? script = host.Load("stress.luau", Encoding.UTF8.GetBytes(source));
        if (script is null)
            return "a script did not compile: " + sink.Last.Text;

        LuauResume outcome = script.Start();
        if (outcome != LuauResume.Failed || sink.Errors != before + 1)
            return $"'{expected}' ended as {outcome} with {sink.Errors - before} errors reported";
        if (!sink.Last.Text.Contains(expected, StringComparison.Ordinal) || sink.Last.Line == 0)
            return $"'{expected}' was reported as '{sink.Last.Text}' at line {sink.Last.Line}";
        return script.IsSwitchedOff == isSwitchedOff ? null : $"'{expected}' left its script switched off: {script.IsSwitchedOff}";
    }

    // The host still runs a script after the four that failed.
    private static string? Finish(LuauHost host)
    {
        LuauScript? script = host.Load("after.luau", "churn()\nreturn 6 * 7"u8);
        if (script is null || script.Start() != LuauResume.Finished)
            return "the script after the failures did not run";

        return script.Thread.Values.TryGetNumber(1, out double answer) && answer == 42
            ? null
            : "the script after the failures returned something else";
    }

    // The fast way in: the function catches what it throws itself.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ExplodeFast(nint state)
    {
        LuauStack stack = new(state);
        try
        {
            throw new InvalidOperationException(Thrown);
        }
        catch (Exception e) // boundary: a managed exception must not reach a native frame
        {
            return stack.Fail(e);
        }
    }
}
