using System;

namespace LuauSpike.Checks;

// Check 10: Luau.CodeGen, the optional native code generator.
internal static unsafe class CodeGenChecks
{
    internal static void Run(Report report)
    {
#if LUAU_CODEGEN && LUAU_COMPILER
        report.Run("10", "native code generation", () => Compare(report));
#else
        report.Skip("10", "native code generation", "Luau.CodeGen is not in this build");
#endif
    }

#if LUAU_CODEGEN && LUAU_COMPILER
    private static void Compare(Report report)
    {
        if (Lua.luau_codegen_supported() == 0)
        {
            report.Check("10", "native code generation", false, "luau_codegen_supported returned 0 on this machine");
            return;
        }

        // Same bytecode for both states. Type information lets the code generator specialize.
        byte[] bytecode = Scripts.Compile(Scripts.ReadSource("codegen"), optimizationLevel: 2, debugLevel: 1, typeInfoLevel: 1);

        nint interpreted = LuaHost.NewState();
        nint native = LuaHost.NewState(nativeCode: true);
        try
        {
            int plainModule = LoadModule(interpreted, bytecode, compileNative: false);
            int nativeModule = LoadModule(native, bytecode, compileNative: true);

            bool plainIsNative = IsNative(interpreted, plainModule);
            bool nativeIsNative = IsNative(native, nativeModule);

            (double plainMandelbrot, double plainInside) = Time(interpreted, plainModule, "mandelbrot"u8, 256, 256);
            (double nativeMandelbrot, double nativeInside) = Time(native, nativeModule, "mandelbrot"u8, 256, 256);
            (double plainSieve, double plainPrimes) = Time(interpreted, plainModule, "sieve"u8, 2_000_000, 0);
            (double nativeSieve, double nativePrimes) = Time(native, nativeModule, "sieve"u8, 2_000_000, 0);

            // An error raised under 64 native frames has to unwind through code Luau generated at run time.
            LuaStack.PushMember(native, nativeModule, "raiseThroughNative"u8);
            Lua.lua_pushnumber(native, 64);
            LuaStack.Call(native, 1, 4);
            bool luauOk = Lua.lua_toboolean(native, -4) != 0;
            string? luauMessage = LuaStack.ToText(native, -3);
            bool hostOk = Lua.lua_toboolean(native, -2) != 0;
            string? hostMessage = LuaStack.ToText(native, -1);
            Lua.Pop(native, 4);
            bool raised = !luauOk && luauMessage is not null && luauMessage.EndsWith("deep", StringComparison.Ordinal)
                && !hostOk && hostMessage is not null && hostMessage.EndsWith("rejected", StringComparison.Ordinal);

            bool passed = !plainIsNative && nativeIsNative && plainInside == nativeInside && plainPrimes == nativePrimes && raised;
            report.Check("10", "native code generation", passed,
                $"functions run as native code: {nativeIsNative}; results match the interpreter: {plainInside == nativeInside && plainPrimes == nativePrimes}; "
                + $"errors raised under 64 native frames were caught: \"{luauMessage}\" and \"{hostMessage}\"");
            report.Note("10", "mandelbrot 256x256, 256 steps", $"interpreter {plainMandelbrot:F1} ms, native {nativeMandelbrot:F1} ms, {plainMandelbrot / nativeMandelbrot:F1}x");
            report.Note("10", "sieve to 2,000,000", $"interpreter {plainSieve:F1} ms, native {nativeSieve:F1} ms, {plainSieve / nativeSieve:F1}x");
            Gameplay(report, interpreted, native);
        }
        finally
        {
            Shim.sl_close(interpreted);
            Shim.sl_close(native);
        }
    }

    // The 200-line script is tables, closures and strings, not arithmetic.
    private static void Gameplay(Report report, nint interpreted, nint native)
    {
        const int calls = 20_000;
        byte[] bytecode = Scripts.Compile(Scripts.ReadSource("gameplay_200"), optimizationLevel: 2, debugLevel: 1, typeInfoLevel: 1);

        double generate = Bench.MedianMicroseconds(51, () =>
        {
            Scripts.LoadBytecode(native, "gameplay_200", bytecode);
            Lua.luau_codegen_compile(native, -1);
            Lua.Pop(native, 1);
        });
        double loadOnly = Bench.MedianMicroseconds(51, () =>
        {
            Scripts.LoadBytecode(native, "gameplay_200", bytecode);
            Lua.Pop(native, 1);
        });

        int plainModule = LoadModule(interpreted, bytecode, compileNative: false, chunkName: "gameplay_200");
        int nativeModule = LoadModule(native, bytecode, compileNative: true, chunkName: "gameplay_200");
        double plain = Bench.BestNanosecondsPerOperation(Bench.Rounds, calls, () => CallDemo(interpreted, plainModule, calls));
        double fast = Bench.BestNanosecondsPerOperation(Bench.Rounds, calls, () => CallDemo(native, nativeModule, calls));

        report.Note("10", "200-line gameplay script", $"demo() interpreter {plain / 1000:F2} us, native {fast / 1000:F2} us, {plain / fast:F2}x; "
            + $"generating native code for the script takes {generate - loadOnly:F0} us on top of a {loadOnly:F0} us load (medians of 51)");
    }

    private static void CallDemo(nint L, int module, int calls)
    {
        for (int i = 0; i < calls; i++)
        {
            LuaStack.PushMember(L, module, "demo"u8);
            LuaStack.Call(L, 0, 0);
        }
    }

    private static int LoadModule(nint L, byte[] bytecode, bool compileNative, string chunkName = "codegen")
    {
        if (!Scripts.LoadBytecode(L, chunkName, bytecode))
            throw new InvalidOperationException(LuaStack.ToText(L, -1));
        if (compileNative)
            Lua.luau_codegen_compile(L, -1);

        LuaStack.Call(L, 0, 1);
        int reference = Lua.lua_ref(L, -1);
        Lua.Pop(L, 1);
        return reference;
    }

    private static bool IsNative(nint L, int module)
    {
        LuaStack.PushMember(L, module, "isNative"u8);
        LuaStack.Call(L, 0, 1);
        bool result = Lua.lua_toboolean(L, -1) != 0;
        Lua.Pop(L, 1);
        return result;
    }

    // Best of Bench.Rounds, in milliseconds, with the function's result.
    private static (double Milliseconds, double Result) Time(nint L, int module, ReadOnlySpan<byte> function, double a, double b)
    {
        double result = 0;
        long best = long.MaxValue;
        for (int round = 0; round < Bench.Rounds; round++)
        {
            LuaStack.PushMember(L, module, function);
            Lua.lua_pushnumber(L, a);
            Lua.lua_pushnumber(L, b);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            LuaStack.Call(L, 2, 1);
            best = Math.Min(best, System.Diagnostics.Stopwatch.GetTimestamp() - start);
            result = Lua.ToNumber(L, -1);
            Lua.Pop(L, 1);
        }

        return (Bench.Nanoseconds(best) / 1e6, result);
    }
#endif
}
