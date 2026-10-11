using System.Text;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.CrashCases;

// Scripts the compiler refuses. For the last two Luau throws a C++ exception
// and catches it itself. In a native build that happens inside the
// executable, with the C++ runtime the publish linked in.
internal static class CompileCase
{
    internal static int Run()
    {
        using ProbeState state = new();

        string? failure =
            Refuse(state, "local door = 1\nlocal = 5", "test:2: ")
            ?? Refuse(state, "return " + new string('(', 1200) + "1" + new string(')', 1200), "recursion depth")
            ?? Refuse(state, TooManyLocals(), "Out of local registers");
        if (failure is not null)
        {
            Console.WriteLine($"CASE failed: {failure}");
            return CaseExit.Failed;
        }

        if (state.Run("return probe('ab', 2)", results: 2) != LuaStatus.Ok || state.PopNumber() != 4)
        {
            Console.WriteLine("CASE failed: the state does not run a script after the refused ones");
            return CaseExit.Failed;
        }

        Console.WriteLine("CASE the compiler refused three scripts with a message each");
        return CaseExit.Passed;
    }

    // Null when the script was refused with a message that holds the text.
    private static string? Refuse(ProbeState state, string source, string expected)
    {
        LuaStatus status = state.Run(source);
        if (status == LuaStatus.Ok)
            return $"a script that should not compile ran: {Start(source)}";

        string? message = state.PopText();
        return message is not null && message.Contains(expected, StringComparison.Ordinal)
            ? null
            : $"'{Start(source)}' was refused with {status}: {message}";
    }

    // One function holds 200 locals at most. A local set from a global counts,
    // where a local set to a number is folded away.
    private static string TooManyLocals()
    {
        StringBuilder source = new();
        for (int i = 0; i < 300; i++)
            source.Append("local a").Append(i).Append(" = probe\n");
        return source.Append("return a0").ToString();
    }

    private static string Start(string source)
    {
        string line = source.ReplaceLineEndings(" ");
        return line.Length <= 24 ? line : line[..24] + "...";
    }
}
