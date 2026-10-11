using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.CrashCases;

// A host function fails, the script catches the error with pcall, and a second
// host function then collects garbage. Through the shim that goes on for as
// long as you like. With the raise in the managed frame the collector walks a
// stack that is gone, and a native build dies there or soon after.
internal static unsafe class RaiseCase
{
    private const int Rounds = 200;
    private const string RaisedMessage = "raised by the host";

    private const string Script = """
        return function(raise, churn)
            local ok, message = pcall(raise)
            churn()
            local thrown, why = pcall(probe, "x", -1)
            churn()
            return ok, message, thrown, why
        end
        """;

    private static int _finallyRuns;
    private static bool _isTracing;

    internal static int Run(bool throughShim)
    {
        _finallyRuns = 0;
        _isTracing = !throughShim;

        using ProbeState state = new();
        state.Require(state.Run(Script, results: 1));
        state.Require(state.SetGlobal("round"u8));
        string[] canaries = MakeCanaries();

        for (int round = 1; round <= Rounds; round++)
        {
            string? failure = PlayRound(state, throughShim) ?? FindDamage(canaries);
            if (failure is not null)
            {
                Console.WriteLine($"CASE failed in round {round}: {failure}");
                return throughShim ? CaseExit.Failed : CaseExit.BrokenByTheWrongWay;
            }

            Lua.lua_gc(state.L, LuaGc.Collect, 0);
        }

        Console.WriteLine($"CASE survived {Rounds} rounds, finally ran {_finallyRuns} times");
        if (!throughShim)
            return CaseExit.SurvivedTheWrongWay;
        return _finallyRuns == Rounds ? CaseExit.Passed : CaseExit.Failed;
    }

    // Null when the round went as it should.
    private static string? PlayRound(ProbeState state, bool throughShim)
    {
        nint L = state.L;
        delegate* unmanaged[Cdecl]<nint, int> raise = &RaiseThroughShim;
        if (!throughShim)
            raise = &RaiseFromManagedFrame;

        state.Require(state.GetGlobal("round"u8));
        fixed (byte* name = "raise"u8)
            state.Require(Shim.sl_pushhostfunction(L, raise, name));
        fixed (byte* name = "churn"u8)
            state.Require(Shim.sl_pushhostfunction(L, &Churn, name));

        LuaStatus status = Shim.sl_call(L, 2, 4);
        if (status != LuaStatus.Ok)
            return $"the round ended with {status}: {state.PopText()}";

        // The results are ok, message, thrown, why. The last one is on top.
        string? why = state.PopText();
        bool probeFailed = Lua.lua_toboolean(L, -1) == 0;
        Lua.Pop(L, 1);
        string? message = state.PopText();
        bool raiseFailed = Lua.lua_toboolean(L, -1) == 0;
        Lua.Pop(L, 1);

        if (!raiseFailed || message is null || !message.EndsWith(RaisedMessage, StringComparison.Ordinal))
            return $"the host error came back as ok={!raiseFailed}, message={message}";
        if (!probeFailed || why is null)
            return $"the exception in the probe came back as ok={!probeFailed}, message={why}";
        return null;
    }

    // Strings that the collector frees or moves when it walks a broken stack.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] MakeCanaries()
    {
        string[] canaries = new string[64];
        for (int i = 0; i < canaries.Length; i++)
            canaries[i] = "canary " + i;
        return canaries;
    }

    private static string? FindDamage(string[] canaries)
    {
        for (int i = 0; i < canaries.Length; i++)
        {
            if (canaries[i] != "canary " + i)
                return $"canary {i} was damaged";
        }

        return null;
    }

    // The right way: return the error code and let the shim raise.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int RaiseThroughShim(nint L)
    {
        try
        {
            return LuaText.Fail(L, RaisedMessage);
        }
        finally
        {
            _finallyRuns++;
        }
    }

    // The wrong way: lua_error jumps out of this frame.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int RaiseFromManagedFrame(nint L)
    {
        try
        {
            if (LuaText.Push(L, RaisedMessage) != LuaStatus.Ok)
                return Shim.HostError;

            Console.WriteLine("step: calling lua_error from a managed frame");
            WrongWay.lua_error(L);
            return 0;
        }
        finally
        {
            _finallyRuns++;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Churn(nint L)
    {
        if (_isTracing)
            Console.WriteLine("step: collecting garbage");

        object[] garbage = new object[256];
        for (int i = 0; i < 2048; i++)
            garbage[i & 255] = new byte[128 + (i & 63)];

        GC.Collect();
        GC.KeepAlive(garbage);

        if (_isTracing)
            Console.WriteLine("step: collected");
        return 0;
    }
}
