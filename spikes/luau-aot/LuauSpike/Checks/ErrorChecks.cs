using System;
using System.Diagnostics;
using System.Text;

namespace LuauSpike.Checks;

// Check 4: errors in both directions, and what happens without the shim.
internal static unsafe class ErrorChecks
{
    internal static void Run(Report report)
    {
        nint L = LuaHost.NewState();
        try
        {
            int module = Scripts.LoadModule(L, "basic");
            report.Run("4a", "Luau error caught in C#", () => LuauErrorReachesHost(report, L, module));
            report.Run("4b", "C# failure seen by the script", () => HostFailureReachesScript(report, L, module));
            report.Run("4c", "raising read made safe", () => ProtectedFieldRead(report, L, module));
        }
        finally
        {
            Shim.sl_close(L);
        }

        report.Run("4d", "raise through shim, GC stress", () =>
        {
            ChildResult child = RunChild("raise-through-shim");
            report.Check("4d", "raise through shim, GC stress", child.ExitCode == CrashCases.Survived, child.Describe());
        });

        // These three pass when the process dies or misbehaves: that is the hazard the shim removes.
        ExpectHazard(report, "4e", "raise from a managed frame", "raise-in-managed-frame");
        report.Run("4e", "same, no GC before returning", () =>
            report.Note("4e", "same, no GC before returning", RunChild("raise-in-managed-frame-no-gc").Describe()));
        ExpectHazard(report, "4f", "managed exception escapes", "managed-exception-escapes");
        ExpectHazard(report, "4g", "raise with no protected call", "unprotected-raise");
    }

    private static void LuauErrorReachesHost(Report report, nint L, int module)
    {
        LuaStack.PushMember(L, module, "fail"u8);
        int status = Lua.lua_pcall(L, 0, 0, 0);
        string? message = LuaStack.ToText(L, -1);
        Lua.Pop(L, 1);

        bool passed = status == Lua.ErrRun && message is not null && message.StartsWith("basic:", StringComparison.Ordinal)
            && message.EndsWith("boom from Luau", StringComparison.Ordinal);
        report.Check("4a", "Luau error caught in C#", passed, $"lua_pcall returned {status} with \"{message}\"");
    }

    private static void HostFailureReachesScript(Report report, nint L, int module)
    {
        LuaStack.PushMember(L, module, "tryHostFailure"u8);
        LuaStack.Call(L, 0, 2);
        bool ok = Lua.lua_toboolean(L, -2) != 0;
        string? caught = LuaStack.ToText(L, -1);
        Lua.Pop(L, 2);

        LuaStack.PushMember(L, module, "hostFailure"u8);
        int status = Lua.lua_pcall(L, 0, 0, 0);
        string? uncaught = LuaStack.ToText(L, -1);
        Lua.Pop(L, 1);

        LuaStack.PushMember(L, module, "badArgument"u8);
        int badStatus = Lua.lua_pcall(L, 0, 0, 0);
        string? badArgument = LuaStack.ToText(L, -1);
        Lua.Pop(L, 1);

        LuaStack.PushMember(L, module, "badTypedArgument"u8);
        int typedStatus = Lua.lua_pcall(L, 0, 0, 0);
        string? badTyped = LuaStack.ToText(L, -1);
        Lua.Pop(L, 1);

        bool passed = !ok && caught is not null && caught.EndsWith("managed failure", StringComparison.Ordinal)
            && status == Lua.ErrRun && uncaught is not null && uncaught.EndsWith("managed failure", StringComparison.Ordinal)
            && badStatus == Lua.ErrRun && badArgument is not null && badArgument.Contains("argument 1 must be a number", StringComparison.Ordinal)
            && typedStatus == Lua.ErrRun && badTyped is not null && badTyped.Contains("number expected", StringComparison.Ordinal);
        report.Check("4b", "C# failure seen by the script", passed,
            $"pcall in the script got \"{caught}\"; uncaught it reached C# as \"{uncaught}\"; bad arguments: \"{badArgument}\" and \"{badTyped}\"");
    }

    private static void ProtectedFieldRead(Report report, nint L, int module)
    {
        LuaStack.PushMember(L, module, "trap"u8);
        int status = LuaStack.GetField(L, -1, "anything"u8);
        string? message = LuaStack.ToText(L, -1);
        Lua.Pop(L, 2);

        report.Check("4c", "raising read made safe", status == Lua.ErrRun && message is not null && message.EndsWith("no field anything", StringComparison.Ordinal),
            $"sl_getfield returned {status} with \"{message}\"");
    }

    private static void ExpectHazard(Report report, string id, string what, string caseName)
    {
        report.Run(id, what, () =>
        {
            ChildResult child = RunChild(caseName);
            report.Check(id, what + ", no shim", child.ExitCode != CrashCases.Survived, child.Describe());
        });
    }

    private static ChildResult RunChild(string caseName)
    {
        ProcessStartInfo start = new(Environment.ProcessPath ?? throw new InvalidOperationException("no process path"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("--case=" + caseName);
        start.ArgumentList.Add("--scripts=" + Scripts.Folder);

        StringBuilder output = new();
        string lastLine = "";
        using Process process = new() { StartInfo = start };
        DataReceivedEventHandler collect = (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;
            lock (output)
            {
                output.AppendLine(e.Data);
                lastLine = e.Data.Trim();
            }
        };
        process.OutputDataReceived += collect;
        process.ErrorDataReceived += collect;

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool exited = process.WaitForExit(60_000);
        if (!exited)
            process.Kill(entireProcessTree: true);
        process.WaitForExit();

        lock (output)
            return new ChildResult(caseName, process.ExitCode, !exited, lastLine);
    }

    private readonly record struct ChildResult(string Case, int ExitCode, bool TimedOut, string LastLine)
    {
        internal string Describe()
        {
            string outcome = TimedOut ? "hung and was killed"
                : ExitCode == CrashCases.Survived ? "child exited 0"
                : ExitCode == CrashCases.SurvivedButSkippedFinally ? "child survived but its finally blocks never ran"
                : $"child died, exit code {ExitCode} (0x{ExitCode:X8})";
            return $"{outcome}; last output: \"{LastLine}\"";
        }
    }
}
