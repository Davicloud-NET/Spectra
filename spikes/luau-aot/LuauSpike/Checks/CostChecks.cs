using System;
using System.IO;

namespace LuauSpike.Checks;

// Check 9: what a call costs in each direction, compile and load time, memory.
internal static unsafe class CostChecks
{
    private const int HostCalls = 2_000_000;
    private const int ScriptCalls = 3_000_000;
    private const int Raises = 100_000;
    private const int ManagedThrows = 20_000;
    private const int Environments = 1000;

    internal static void Run(Report report)
    {
        nint L = LuaHost.NewState();
        try
        {
            int module = Scripts.LoadModule(L, "bench");
            report.Run("9a", "transition cost", () => Transition(report, L));
            report.Run("9b", "C# to Luau", () => HostToLuau(report, L, module));
            report.Run("9c", "Luau to C#", () => LuauToHost(report, L, module));
            report.Run("9d", "error cost", () => Errors(report, L, module));
        }
        finally
        {
            Shim.sl_close(L);
        }

        report.Run("9e", "compile and load", () => CompileAndLoad(report));
        report.Run("9f", "memory", () => Memory(report));
        report.Note("9g", "executable size", $"{new FileInfo(Environment.ProcessPath ?? "").Length:N0} bytes ({Program.LinkFlavor})");
    }

    private static void Transition(Report report, nint L)
    {
        const int calls = 20_000_000;
        int sink = 0;
        double normal = Bench.BestNanosecondsPerOperation(Bench.Rounds, calls, () => sink += GetTopLoop(L, calls));
        double suppressed = Bench.BestNanosecondsPerOperation(Bench.Rounds, calls, () => sink += GetTopFastLoop(L, calls));

        report.Note("9a", "one P/Invoke (lua_gettop)", $"{normal:F2} ns, {suppressed:F2} ns with SuppressGCTransition (sink {sink})");
    }

    private static int GetTopLoop(nint L, int calls)
    {
        int sink = 0;
        for (int i = 0; i < calls; i++)
            sink += Lua.lua_gettop(L);
        return sink;
    }

    private static int GetTopFastLoop(nint L, int calls)
    {
        int sink = 0;
        for (int i = 0; i < calls; i++)
            sink += Lua.lua_gettop_fast(L);
        return sink;
    }

    private static double ChattyLoop(nint L, int add, int calls)
    {
        double total = 0;
        for (int i = 0; i < calls; i++)
        {
            Lua.lua_rawgeti(L, Lua.RegistryIndex, add);
            Lua.lua_pushnumber(L, i);
            Lua.lua_pushnumber(L, 2);
            Lua.lua_pcall(L, 2, 1, 0);
            total += Lua.lua_tonumberx(L, -1, null);
            Lua.lua_settop(L, -2);
        }
        return total;
    }

    private static double ChattyFastLoop(nint L, int add, int calls)
    {
        double total = 0;
        for (int i = 0; i < calls; i++)
        {
            Lua.lua_rawgeti_fast(L, Lua.RegistryIndex, add);
            Lua.lua_pushnumber_fast(L, i);
            Lua.lua_pushnumber_fast(L, 2);
            Lua.lua_pcall(L, 2, 1, 0);
            total += Lua.lua_tonumberx_fast(L, -1, null);
            Lua.lua_settop_fast(L, -2);
        }
        return total;
    }

    private static double BatchedLoop(nint L, int add, int calls)
    {
        double total = 0;
        for (int i = 0; i < calls; i++)
        {
            double result;
            Shim.sl_call_numbers(L, add, i, 2, &result);
            total += result;
        }
        return total;
    }

    private static void HostToLuau(Report report, nint L, int module)
    {
        LuaStack.PushMember(L, module, "add"u8);
        int add = Lua.lua_ref(L, -1);
        Lua.Pop(L, 1);

        double total = 0;
        double chatty = Bench.BestNanosecondsPerOperation(Bench.Rounds, HostCalls, () => total += ChattyLoop(L, add, HostCalls));
        double suppressed = Bench.BestNanosecondsPerOperation(Bench.Rounds, HostCalls, () => total += ChattyFastLoop(L, add, HostCalls));
        double batched = Bench.BestNanosecondsPerOperation(Bench.Rounds, HostCalls, () => total += BatchedLoop(L, add, HostCalls));

        bool balanced = Lua.lua_gettop(L) == 0;
        report.Check("9b", "C# to Luau, add(a, b)", balanced,
            $"{chatty:F1} ns with six P/Invokes; {suppressed:F1} ns with SuppressGCTransition on five of them; {batched:F1} ns as one shim call (checksum {total:E3})");
    }

    private static void LuauToHost(Report report, nint L, int module)
    {
        double empty = Loop(L, module, "empty"u8, default, ScriptCalls);
        double luau = Loop(L, module, "call"u8, "add"u8, ScriptCalls, fromModule: true);
        double native = Loop(L, module, "call"u8, "native_add"u8, ScriptCalls);
        double direct = Loop(L, module, "call"u8, "host_add_direct"u8, ScriptCalls);
        double generic = Loop(L, module, "call"u8, "host_add"u8, ScriptCalls);
        double fast = Loop(L, module, "call"u8, "host_add_fast"u8, ScriptCalls);
        double typed = Loop(L, module, "call"u8, "host_add_typed"u8, ScriptCalls);

        report.Check("9c", "Luau to C#, f(1, 2)", Lua.lua_gettop(L) == 0,
            $"typed {typed:F1} ns; generic {generic:F1} ns; generic with SuppressGCTransition {fast:F1} ns; generic without the trampoline {direct:F1} ns");
        report.Note("9c", "the same loop, for scale", $"native C function {native:F1} ns; Luau function {luau:F1} ns; empty loop {empty:F2} ns per iteration");
    }

    private static void Errors(Report report, nint L, int module)
    {
        double protect = Loop(L, module, "protect"u8, default, Raises);
        double raise = Loop(L, module, "raise"u8, default, Raises);
        double reject = Loop(L, module, "raiseHost"u8, "host_reject"u8, Raises);
        double thrown = Loop(L, module, "raiseHost"u8, "host_throw"u8, ManagedThrows);

        report.Note("9d", "pcall, no error", $"{protect:F0} ns");
        report.Note("9d", "pcall catching error()", $"{raise:F0} ns");
        report.Note("9d", "pcall catching a host error code", $"{reject:F0} ns");
        report.Note("9d", "pcall catching a C# exception", $"{thrown:F0} ns");
    }

    // Times module[loop](argument, count). The argument is a global, or a module member.
    private static double Loop(nint L, int module, ReadOnlySpan<byte> loop, ReadOnlySpan<byte> argument, int count, bool fromModule = false)
    {
        long best = long.MaxValue;
        for (int round = 0; round < Bench.Rounds; round++)
        {
            LuaStack.PushMember(L, module, loop);
            int arguments = 1;
            if (!argument.IsEmpty)
            {
                if (fromModule)
                    LuaStack.PushMember(L, module, argument);
                else if (LuaStack.GetField(L, Lua.GlobalsIndex, argument) != Lua.Ok)
                    throw new InvalidOperationException(LuaStack.ToText(L, -1));
                arguments = 2;
            }

            Lua.lua_pushnumber(L, count);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            LuaStack.Call(L, arguments, 0);
            best = Math.Min(best, System.Diagnostics.Stopwatch.GetTimestamp() - start);
        }

        return Bench.Nanoseconds(best) / count;
    }

    private static void CompileAndLoad(Report report)
    {
#if LUAU_COMPILER
        byte[] source = Scripts.ReadSource("gameplay_200");
        int lines = 0;
        foreach (byte b in source)
            lines += b == (byte)'\n' ? 1 : 0;

        byte[] bytecode = Scripts.Compile(source);
        double compile = Bench.MedianMicroseconds(201, () => Scripts.Compile(source));
        double compileOptimized = Bench.MedianMicroseconds(201, () => Scripts.Compile(source, optimizationLevel: 2));

        nint L = LuaHost.NewState();
        try
        {
            double load = Bench.MedianMicroseconds(201, () =>
            {
                Scripts.LoadBytecode(L, "gameplay_200", bytecode);
                Lua.Pop(L, 1);
            });
            double loadAndRun = Bench.MedianMicroseconds(201, () =>
            {
                Scripts.LoadBytecode(L, "gameplay_200", bytecode);
                LuaStack.Call(L, 0, 0);
            });

            report.Check("9e", "compile and load, 200 lines", lines == 200,
                $"{source.Length:N0} B of source, {lines} lines, {bytecode.Length:N0} B of bytecode; compile {compile:F0} us (O1) or {compileOptimized:F0} us (O2); "
                + $"luau_load {load:F0} us; load and run the top level {loadAndRun:F0} us; medians of 201 runs");
        }
        finally
        {
            Shim.sl_close(L);
        }
#else
        report.Skip("9e", "compile and load, 200 lines", "no compiler in this build");
#endif
    }

    private static void Memory(Report report)
    {
        nint L = Shim.sl_newstate();
        try
        {
            long empty = Shim.LiveBytes(L);
            Lua.luaL_openlibs(L);
            long withLibraries = Shim.LiveBytes(L);
            HostFunctions.Register(L);
            Lua.luaL_sandbox(L);
            long ready = Shim.LiveBytes(L);

            Lua.lua_createtable(L, Environments, 0);
            Lua.lua_gc(L, Lua.GcCollect, 0);
            long before = Shim.LiveBytes(L);
            for (int i = 1; i <= Environments; i++)
            {
                nint thread = Lua.lua_newthread(L);
                Lua.luaL_sandboxthread(thread);
                Lua.lua_rawseti(L, -2, i);
            }
            Lua.lua_gc(L, Lua.GcCollect, 0);
            long perEnvironment = (Shim.LiveBytes(L) - before) / Environments;
            Lua.Pop(L, 1);

            long heapKilobytes = Lua.lua_gc(L, Lua.GcCount, 0);
            report.Note("9f", "memory of a state", $"{empty:N0} B after lua_newstate; {withLibraries:N0} B with the standard libraries; {ready:N0} B with host functions and the sandbox");
            report.Note("9f", "memory per script environment", $"{perEnvironment:N0} B for a thread with luaL_sandboxthread, mean of {Environments} (Luau heap now {heapKilobytes} KB)");
        }
        finally
        {
            Shim.sl_close(L);
        }
    }
}
