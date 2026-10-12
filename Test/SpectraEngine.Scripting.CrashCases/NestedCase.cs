using System.Text;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.CrashCases;

// A host function runs another script and waits for it, as require runs a
// module. The script that asked is in the middle of a call, so a managed
// frame of the host lies on the stack under the script it started. That one
// ends in each way a script can: it returns, fails, waits, and is stopped by
// the watchdog. The collector runs inside it and after it.
internal static class NestedCase
{
    private const int Rounds = 40;
    private const string Failed = "it failed";
    private const string Waited = "it waited";

    private const string Asker = """
        local inside = ...
        churn()
        local worked, got = pcall(run, inside)
        churn()
        return worked, tostring(got)
        """;

    // What the script inside does, and what the one that asked gets for it.
    private static readonly (string Inside, bool Works, string Gives)[] _ways =
    [
        ("churn() return 6 * 7", true, "42"),
        ("churn() local door = nil return door.open", false, Failed),
        ("churn() coroutine.yield() return 1", true, Waited),
        ("churn() while true do end", false, Failed),
    ];

    internal static int Run()
    {
        CountingSink sink = new();
        using LuauHost host = Open(sink);
        string[] canaries = Stress.MakeCanaries();

        for (int round = 1; round <= Rounds; round++)
        {
            foreach ((string inside, bool works, string gives) in _ways)
            {
                string? failure = Ask(host, inside, works, gives) ?? Stress.FindDamage(canaries);
                if (failure is not null)
                {
                    Console.WriteLine($"CASE failed in round {round}: {failure}");
                    return CaseExit.Failed;
                }

                host.Collect();
                GC.Collect();
            }
        }

        // Two of the four report an error: the one that fails and the one that is stopped.
        Console.WriteLine($"CASE ran {Rounds * _ways.Length} scripts inside a host function, {sink.Errors} errors reported");
        return sink.Errors == Rounds * 2 ? CaseExit.Passed : CaseExit.Failed;
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
            && host.PushFunction(stack, "run"u8, call => RunInside(host, call))
            && stack.SetGlobal("run"u8);
        if (!isSet)
            throw new InvalidOperationException("The host functions could not be set.");

        host.Seal();
        host.CallLimit = TimeSpan.FromMilliseconds(20);
        return host;
    }

    // Null when the script that asked got what it should and was left alone.
    private static string? Ask(LuauHost host, string inside, bool works, string gives)
    {
        LuauScript? script = host.Load("asker.luau", Encoding.UTF8.GetBytes(Asker));
        if (script is null)
            return "the asking script did not compile";
        if (script.Start([inside]) != LuauResume.Finished)
            return $"the asking script did not run to its end after '{inside}'";
        if (script.IsSwitchedOff)
            return $"the asking script was switched off for what '{inside}' did";

        LuauStack values = script.Thread.Values;
        string got = values.GetText(2) ?? string.Empty;
        return values.GetBoolean(1) == works && got.Contains(gives, StringComparison.Ordinal)
            ? null
            : $"'{inside}' gave the asking script '{got}'";
    }

    // Loads the text it is handed as a script of its own and starts it, from
    // inside the call of the script that asked.
    private static int RunInside(LuauHost host, LuauStack stack)
    {
        if (!stack.TryGetUtf8(1, out ReadOnlySpan<byte> text) || host.Load("inside.luau", text) is not { } script)
            return stack.Fail("the script to run did not load");

        LuauResume outcome = script.Start();
        Stress.Churn();
        switch (outcome)
        {
            case LuauResume.Finished:
                using (LuauReference? kept = host.Keep(script.Thread.Values, 1))
                    return kept is not null && kept.Push(stack) ? 1 : stack.Fail("the value was not kept");
            case LuauResume.Yielded:
                script.Thread.Cancel();
                return stack.Push(Waited) ? 1 : LuauStack.Raise;
            default:
                return stack.Fail(Failed);
        }
    }
}
