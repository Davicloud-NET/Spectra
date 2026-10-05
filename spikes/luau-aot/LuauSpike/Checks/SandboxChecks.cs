using System;

namespace LuauSpike.Checks;

// Check 8: luaL_sandbox on the state, and one environment per script.
internal static unsafe class SandboxChecks
{
    private const long MemoryLimit = 8 * 1024 * 1024;

    internal static void Run(Report report)
    {
        report.Run("8a", "sandbox and script environments", () => Environments(report));
        report.Run("8b", "memory limit", () => MemoryCap(report));
    }

    // The allocator refuses to grow past a limit. Luau turns that into an error
    // raised from its own frames, so a protected call returns it.
    private static void MemoryCap(Report report)
    {
        nint L = LuaHost.NewState();
        try
        {
            int module = Scripts.LoadModule(L, "runaway");
            Shim.sl_set_memory_limit(L, MemoryLimit);
            LuaStack.PushMember(L, module, "hoard"u8);
            int status = Lua.lua_pcall(L, 0, 0, 0);
            string? message = LuaStack.ToText(L, -1);
            long atFailure = Shim.LiveBytes(L);
            Lua.Pop(L, 1);
            Shim.sl_set_memory_limit(L, 0);

            Lua.lua_gc(L, Lua.GcCollect, 0);
            long afterCollect = Shim.LiveBytes(L);

            LuaStack.PushMember(L, module, "count"u8);
            Lua.lua_pushnumber(L, 1000);
            LuaStack.Call(L, 1, 1);
            double total = Lua.ToNumber(L, -1);
            Lua.Pop(L, 1);

            bool passed = status == Lua.ErrMem && atFailure <= MemoryLimit && afterCollect < atFailure / 4 && total == 500500;
            report.Check("8b", "memory limit", passed,
                $"a script that allocates forever stopped with status {status} \"{message}\" at {atFailure:N0} B under a {MemoryLimit:N0} B limit; {afterCollect:N0} B after a collection; the state still runs scripts");
        }
        finally
        {
            Shim.sl_close(L);
        }
    }

    private static void Environments(Report report)
    {
        nint L = LuaHost.NewState(sandbox: true);
        try
        {
            byte[] bytecode = Scripts.ReadBytecode("sandbox");

            // One thread per script. luaL_sandboxthread gives it its own
            // globals table that reads through to the frozen shared one.
            nint scriptA = Lua.lua_newthread(L);
            Lua.luaL_sandboxthread(scriptA);
            nint scriptB = Lua.lua_newthread(L);
            Lua.luaL_sandboxthread(scriptB);

            Outcome firstA = RunIn(scriptA, bytecode);
            Outcome secondA = RunIn(scriptA, bytecode);
            Outcome firstB = RunIn(scriptB, bytecode);

            int leaked = LuaStack.GetField(L, Lua.GlobalsIndex, "shared_counter"u8);
            bool sharedGlobalsUntouched = leaked == Lua.Ok && Lua.lua_type(L, -1) == Lua.TNil;
            Lua.Pop(L, 1);

            // A script loaded straight into the frozen state cannot create a global at all.
            bool loaded = Scripts.LoadBytecode(L, "sandbox", bytecode);
            int unsandboxedStatus = Lua.lua_pcall(L, 0, 0, 0);
            string? unsandboxedError = LuaStack.ToText(L, -1);
            Lua.Pop(L, 1);
            Lua.Pop(L, 2);

            bool isolated = firstA is { SawCounterBefore: false, Counter: 1 }
                && secondA is { SawCounterBefore: true, Counter: 2 }
                && firstB is { SawCounterBefore: false, Counter: 1 };
            bool frozen = !firstA.ChangedLibrary && !firstA.ChangedGlobals && firstA.Upper == "OK" && firstB.Upper == "OK";
            bool mainFrozen = loaded && unsandboxedStatus == Lua.ErrRun
                && unsandboxedError is not null && unsandboxedError.Contains("readonly", StringComparison.Ordinal);

            report.Check("8a", "sandbox and script environments", isolated && frozen && sharedGlobalsUntouched && mainFrozen && firstA.ShadowedPrint,
                $"script A kept its own global across two runs ({firstA.Counter}, {secondA.Counter}), script B never saw it ({firstB.Counter}); "
                + $"writing string.upper or _G failed; a script may shadow print in its own environment; outside an environment a global write fails with \"{unsandboxedError}\"");
        }
        finally
        {
            Shim.sl_close(L);
        }
    }

    private static Outcome RunIn(nint thread, byte[] bytecode)
    {
        if (!Scripts.LoadBytecode(thread, "sandbox", bytecode))
            throw new InvalidOperationException(LuaStack.ToText(thread, -1));

        LuaStack.Call(thread, 0, 6);
        Outcome outcome = new(
            SawCounterBefore: Lua.lua_type(thread, -6) != Lua.TNil,
            Counter: Lua.ToNumber(thread, -5),
            ChangedLibrary: Lua.lua_toboolean(thread, -4) != 0,
            ChangedGlobals: Lua.lua_toboolean(thread, -3) != 0,
            ShadowedPrint: Lua.lua_toboolean(thread, -2) != 0,
            Upper: LuaStack.ToText(thread, -1));
        Lua.Pop(thread, 6);
        return outcome;
    }

    private readonly record struct Outcome(bool SawCounterBefore, double Counter, bool ChangedLibrary, bool ChangedGlobals, bool ShadowedPrint, string? Upper);
}
