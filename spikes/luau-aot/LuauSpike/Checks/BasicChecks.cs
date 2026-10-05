using System;
using System.IO;

namespace LuauSpike.Checks;

// Checks 1 to 3: run a script, call into it, be called from it.
internal static unsafe class BasicChecks
{
    internal static void Run(Report report)
    {
        nint L = LuaHost.NewState();
        try
        {
            report.Run("1a", "compile source and run", () => CompileAndRun(report, L));
            report.Run("1b", "load precompiled bytecode", () => LoadPrecompiled(report, L));

            int module = Scripts.LoadModule(L, "basic");
            report.Run("2", "call Luau from C#", () => CallLuau(report, L, module));
            report.Run("3", "call C# from Luau", () => CallHost(report, L, module));
        }
        finally
        {
            Shim.sl_close(L);
        }
    }

    private static void CompileAndRun(Report report, nint L)
    {
#if LUAU_COMPILER
        byte[] source = Scripts.ReadSource("answer");
        byte[] bytecode = Scripts.Compile(source);
        bool loaded = Scripts.LoadBytecode(L, "answer", bytecode);
        LuaStack.Call(L, 0, 1);
        double answer = Lua.ToNumber(L, -1);
        Lua.Pop(L, 1);

        bool badLoaded = Scripts.LoadBytecode(L, "bad", Scripts.Compile("return +"u8));
        string? syntaxError = LuaStack.ToText(L, -1);
        Lua.Pop(L, 1);

        bool passed = loaded && answer == 42 && !badLoaded && syntaxError is not null && syntaxError.StartsWith("bad:1:", StringComparison.Ordinal);
        report.Check("1a", "compile source and run", passed,
            $"{source.Length} B source, {bytecode.Length} B bytecode, returned {answer}; a syntax error came back as \"{syntaxError}\"");
#else
        report.Skip("1a", "compile source and run", "no compiler in this build");
#endif
    }

    private static void LoadPrecompiled(Report report, nint L)
    {
#if LUAU_COMPILER
        string path = Path.Combine(Path.GetTempPath(), $"luau-spike-{Environment.ProcessId}.luauc");
        File.WriteAllBytes(path, Scripts.Compile(Scripts.ReadSource("answer")));
        byte[] bytecode = File.ReadAllBytes(path);
        File.Delete(path);
        string origin = "written to a file and read back; the compiler is linked in this build";
#else
        byte[] bytecode = Scripts.ReadBytecode("answer");
        string origin = "answer.luauc, no compiler linked";
#endif
        bool loaded = Scripts.LoadBytecode(L, "answer", bytecode);
        LuaStack.Call(L, 0, 1);
        double answer = Lua.ToNumber(L, -1);
        Lua.Pop(L, 1);

        report.Check("1b", "load precompiled bytecode", loaded && answer == 42, $"{bytecode.Length} B, returned {answer}; {origin}");
    }

    private static void CallLuau(Report report, nint L, int module)
    {
        LuaStack.PushMember(L, module, "add"u8);
        Lua.lua_pushnumber(L, 2);
        Lua.lua_pushnumber(L, 40);
        LuaStack.Call(L, 2, 1);
        double sum = Lua.ToNumber(L, -1);
        Lua.Pop(L, 1);

        LuaStack.PushMember(L, module, "describe"u8);
        LuaStack.PushString(L, "crate");
        Lua.lua_pushnumber(L, 3);
        LuaStack.Call(L, 2, 2);
        string? text = LuaStack.ToText(L, -2);
        double doubled = Lua.ToNumber(L, -1);
        Lua.Pop(L, 2);

        report.Check("2", "call Luau from C#", sum == 42 && text == "crate x3" && doubled == 6 && Lua.lua_gettop(L) == 0,
            $"add(2, 40) = {sum}; describe(\"crate\", 3) = \"{text}\", {doubled}");
    }

    private static void CallHost(Report report, nint L, int module)
    {
        LuaStack.PushMember(L, module, "callHost"u8);
        Lua.lua_pushnumber(L, 2);
        Lua.lua_pushnumber(L, 40);
        LuaStack.Call(L, 2, 1);
        double sum = Lua.ToNumber(L, -1);
        Lua.Pop(L, 1);

        LuaStack.PushMember(L, module, "greet"u8);
        LuaStack.PushString(L, "Spectra");
        LuaStack.Call(L, 1, 2);
        string? greeting = LuaStack.ToText(L, -2);
        double doubled = Lua.ToNumber(L, -1);
        Lua.Pop(L, 2);

        report.Check("3", "call C# from Luau", sum == 42 && greeting == "hello Spectra" && doubled == 42,
            $"host_add(2, 40) = {sum}; host_greet(\"Spectra\", 21) = \"{greeting}\", {doubled}");
    }
}
