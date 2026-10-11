using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.CrashCases;

// A script calls the host function, works on what it got and hands the
// results back: the ordinary round trip, in a native build.
internal static class CallCase
{
    internal static int Run()
    {
        using ProbeState state = new();
        LuaStatus status = state.Run("local text, length = probe('ab', 3) return text .. '!', length * 2", results: 2);
        if (status != LuaStatus.Ok)
        {
            Console.WriteLine($"CASE failed with {status}: {state.PopText()}");
            return CaseExit.Failed;
        }

        double length = state.PopNumber();
        string? text = state.PopText();
        if (text != "ababab!" || length != 12)
        {
            Console.WriteLine($"CASE failed: the script returned '{text}' and {length}");
            return CaseExit.Failed;
        }

        Console.WriteLine($"CASE the script returned '{text}' and {length}");
        return CaseExit.Passed;
    }
}
