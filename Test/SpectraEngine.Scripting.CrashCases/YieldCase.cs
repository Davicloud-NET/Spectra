using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.CrashCases;

// A host function yields the script that called it, by both ways in and
// once from inside a pcall, and the host resumes it with values. The yield
// is made by the shim once the managed frame has returned. The collector
// runs while the script waits and while it runs.
internal static unsafe class YieldCase
{
    private const int Rounds = 200;

    private const string Script = """
        local word = wait('first')
        churn()
        local ok, number = pcall(wait_fast, 'second')
        churn()
        return word, ok, number
        """;

    private static LuauThread _waiting;
    private static string? _askedFor;

    internal static int Run()
    {
        using LuauHost host = Open();
        string[] canaries = Stress.MakeCanaries();

        for (int round = 1; round <= Rounds; round++)
        {
            string? failure = PlayRound(host, round) ?? Stress.FindDamage(canaries);
            if (failure is not null)
            {
                Console.WriteLine($"CASE failed in round {round}: {failure}");
                return CaseExit.Failed;
            }

            host.Collect();
        }

        Console.WriteLine($"CASE yielded and resumed {Rounds * 2} times");
        return CaseExit.Passed;
    }

    private static LuauHost Open()
    {
        LuauHost host = new(new CountingSink());
        LuauStack stack = host.Stack;

        bool isSet = host.PushFunction(stack, "churn"u8, _ =>
            {
                Stress.Churn();
                return 0;
            })
            && stack.SetGlobal("churn"u8)
            && host.PushFunction(stack, "wait"u8, call => Wait(host, call))
            && stack.SetGlobal("wait"u8)
            && stack.PushFunction(&WaitFast, "wait_fast"u8)
            && stack.SetGlobal("wait_fast"u8);
        if (!isSet)
            throw new InvalidOperationException("The host functions could not be set.");

        host.Seal();
        return host;
    }

    // Null when the round went as it should.
    private static string? PlayRound(LuauHost host, int round)
    {
        LuauScript? script = host.Load("waiting.luau", Encoding.UTF8.GetBytes(Script));
        if (script is null)
            return "the script did not compile";

        if (script.Start() != LuauResume.Yielded || _askedFor != "first" || _waiting != script.Thread)
            return $"the first wait did not yield, it asked for '{_askedFor}'";
        Stress.Churn();

        if (_waiting.Resume(["a word"]) != LuauResume.Yielded || _askedFor != "second")
            return $"the second wait did not yield, it asked for '{_askedFor}'";
        Stress.Churn();

        if (_waiting.Resume([round]) != LuauResume.Finished)
            return "the script did not end after its second wait";

        LuauStack values = script.Thread.Values;
        bool isRight = values.GetText(1) == "a word"
            && values.GetBoolean(2)
            && values.TryGetNumber(3, out double number)
            && number == round;
        return isRight ? null : "the script returned something else than it was resumed with";
    }

    // Remembers who waits and for what, then yields with no values.
    private static int Wait(LuauHost host, LuauStack stack)
    {
        _askedFor = stack.GetText(1);
        _waiting = host.ThreadOf(stack);
        return LuauStack.Yield();
    }

    // The fast way in. Under a pcall the thread that waits is still the script's.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int WaitFast(nint state)
    {
        LuauStack stack = new(state);
        try
        {
            return LuauHost.From(stack) is { } host ? Wait(host, stack) : stack.Fail("no host");
        }
        catch (Exception e) // boundary: a managed exception must not reach a native frame
        {
            return stack.Fail(e);
        }
    }
}
