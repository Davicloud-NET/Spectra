using System.Text;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.CrashCases;

// A Luau state with the standard libraries and the probe as a global, made
// from the shim's own calls with no host around it. The crash cases and the
// tests of the shim both set a state up through this.
internal sealed unsafe class ProbeState : IDisposable
{
    internal ProbeState()
    {
        L = Shim.sl_newstate();
        if (L == 0)
            throw new InvalidOperationException("Luau made no state.");

        Require(Shim.sl_openlibs(L));
        Require(HostProbe.Push(L));
        Require(SetGlobal("probe"u8));
    }

    internal nint L { get; private set; }

    // The thread that holds what the last thread to end returned.
    internal nint Results => Shim.sl_results(L);

    // The serial the next outermost call into Luau gets.
    internal uint NextCall => Shim.CallSerial(L) + 1;

    // Runs the source as a chunk named "test". Its results, or the error value
    // when the status is not Ok, are left on the stack.
    internal LuaStatus Run(string source, int results = 0)
    {
        LuaStatus status = LuaChunk.Load(L, "=test", Encoding.UTF8.GetBytes(source));
        return status == LuaStatus.Ok ? Shim.sl_call(L, 0, results) : status;
    }

    // Starts a thread or goes on with it. The top `count` values of the main
    // stack go with it.
    internal LuaStatus Resume(nint thread, int count = 0)
    {
        int results;
        return Shim.sl_resume(thread, L, count, &results);
    }

    // Takes the value on top.
    internal LuaStatus SetGlobal(ReadOnlySpan<byte> name)
    {
        fixed (byte* key = name)
            return Shim.sl_setfield(L, Lua.GlobalsIndex, key, (nuint)name.Length);
    }

    internal LuaStatus GetGlobal(ReadOnlySpan<byte> name)
    {
        fixed (byte* key = name)
            return Shim.sl_getfield(L, Lua.GlobalsIndex, key, (nuint)name.Length);
    }

    // Pops the top value. Null when it was not a string.
    internal string? PopText()
    {
        string? text = LuaText.Read(L, -1);
        Lua.Pop(L, 1);
        return text;
    }

    internal double PopNumber()
    {
        double number = Lua.lua_tonumberx(L, -1, null);
        Lua.Pop(L, 1);
        return number;
    }

    // Throws with the error value when a setup step failed.
    internal void Require(LuaStatus status)
    {
        if (status != LuaStatus.Ok)
            throw new InvalidOperationException($"{status}: {PopText()}");
    }

    // Lets go of the state without closing it, when another thread still runs it.
    internal void Abandon() => L = 0;

    public void Dispose()
    {
        if (L == 0)
            return;

        Shim.sl_close(L);
        L = 0;
    }
}
