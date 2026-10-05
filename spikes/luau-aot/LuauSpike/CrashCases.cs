using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LuauSpike;

// Scenarios that are expected to kill the process, plus the safe form of the
// same scenario. The parent runs each one as a child and reads the exit code.
internal static unsafe partial class CrashCases
{
    internal const int Survived = 0;
    internal const int SurvivedButSkippedFinally = 4;
    private const int Rounds = 200;

    private static int s_finallyRuns;
    private static bool s_trace;
    private static bool s_collect;

    internal static int Run(string name)
    {
        if (OperatingSystem.IsWindows())
            SetErrorMode(0x0001 | 0x0002 | 0x8000); // no crash dialogs

        return name switch
        {
            "raise-in-managed-frame" => Raise(throughShim: false, collect: true),
            "raise-in-managed-frame-no-gc" => Raise(throughShim: false, collect: false),
            "raise-through-shim" => Raise(throughShim: true, collect: true),
            "managed-exception-escapes" => EscapingException(),
            "unprotected-raise" => UnprotectedRaise(),
            _ => 2,
        };
    }

    // A host function raises a Luau error, the script catches it with pcall,
    // then calls a second host function that collects garbage.
    private static int Raise(bool throughShim, bool collect)
    {
        s_trace = !throughShim;
        s_collect = collect;
        nint L = LuaHost.NewState();
        int module = Scripts.LoadModule(L, "crash");
        string[] canaries = MakeCanaries();

        for (int round = 1; round <= Rounds; round++)
        {
            LuaStack.PushMember(L, module, "raiseAndContinue"u8);
            fixed (byte* name = "raise"u8)
            {
                if (throughShim)
                    Shim.sl_pushhostfunction(L, &RaiseThroughShim, name);
                else
                    Lua.lua_pushcclosurek(L, &RaiseDirectly, name, 0, 0);
            }
            fixed (byte* name = "churn"u8)
                Shim.sl_pushhostfunction(L, &Churn, name);

            LuaStack.Call(L, 2, 2);
            if (s_trace)
                Console.WriteLine("step: back in managed code");
            bool ok = Lua.lua_toboolean(L, -2) != 0;
            string? message = LuaStack.ToText(L, -1);
            Lua.Pop(L, 2);

            if (ok || message is null || !message.Contains("raised by the host", StringComparison.Ordinal))
            {
                Console.WriteLine($"CASE wrong result in round {round}: ok={ok} message={message}");
                return 3;
            }

            for (int i = 0; i < canaries.Length; i++)
            {
                if (canaries[i] != "canary " + i)
                {
                    Console.WriteLine($"CASE canary {i} damaged in round {round}");
                    return 5;
                }
            }

            Console.WriteLine($"round {round} finally={s_finallyRuns}");
        }

        Console.WriteLine($"CASE survived {Rounds} rounds, finally ran {s_finallyRuns} times");
        return s_finallyRuns == Rounds ? Survived : SurvivedButSkippedFinally;
    }

    private static int EscapingException()
    {
        nint L = LuaHost.NewState();
        int module = Scripts.LoadModule(L, "crash");
        LuaStack.PushMember(L, module, "callProtected"u8);
        fixed (byte* name = "throws"u8)
            Lua.lua_pushcclosurek(L, &ThrowsUncaught, name, 0, 0);
        LuaStack.Call(L, 1, 2);
        Console.WriteLine("CASE survived a managed exception leaving a host function");
        return Survived;
    }

    // Reads a field through an __index that raises, with no protected call active.
    private static int UnprotectedRaise()
    {
        nint L = LuaHost.NewState();
        int module = Scripts.LoadModule(L, "crash");
        LuaStack.PushMember(L, module, "trap"u8);
        fixed (byte* key = "anything"u8)
            Lua.lua_getfield(L, -1, key);
        Console.WriteLine("CASE survived an unprotected raise");
        return Survived;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] MakeCanaries()
    {
        string[] canaries = new string[64];
        for (int i = 0; i < canaries.Length; i++)
            canaries[i] = "canary " + i;
        return canaries;
    }

    // The unsafe form: lua_error unwinds out of this managed frame.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int RaiseDirectly(nint L)
    {
        try
        {
            LuaStack.PushString(L, "raised by the host");
            if (s_collect)
                Console.WriteLine("step: calling lua_error from managed code");
            Lua.lua_error(L);
            return 0;
        }
        finally
        {
            s_finallyRuns++;
        }
    }

    // The safe form: return an error code and let the shim raise.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int RaiseThroughShim(nint L)
    {
        try
        {
            return HostFunctions.Fail(L, "raised by the host");
        }
        finally
        {
            s_finallyRuns++;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Churn(nint L)
    {
        if (!s_collect)
            return 0;

        if (s_trace)
            Console.WriteLine("step: second host function entered");

        object[] garbage = new object[256];
        for (int i = 0; i < 2048; i++)
            garbage[i & 255] = new byte[128 + (i & 63)];

        if (s_trace)
            Console.WriteLine("step: collecting");
        GC.Collect();
        GC.KeepAlive(garbage);

        if (s_trace)
            Console.WriteLine("step: collected");
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ThrowsUncaught(nint L) => throw new InvalidOperationException("nobody catches this");

    [LibraryImport("kernel32")]
    private static partial uint SetErrorMode(uint mode);
}
