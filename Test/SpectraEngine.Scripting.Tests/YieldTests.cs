using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SpectraEngine.Scripting.CrashCases;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Tests;

/// <summary>A host function that yields the script which called it.</summary>
public sealed unsafe class YieldTests
{
    [Fact]
    public void A_host_function_yields_its_thread_and_a_resume_hands_it_a_value()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        nint thread = StartThread(state, "local answer = wait_for(41)\nreturn answer + 1");

        LuaStatus yielded = Shim.sl_resume(thread, 0, 0);

        yielded.ShouldBe(LuaStatus.Yield);
        Lua.lua_gettop(thread).ShouldBe(1);
        Lua.lua_tonumberx(thread, 1, null).ShouldBe(41);

        Lua.Pop(thread, 1);
        Lua.lua_pushnumber(thread, 100);
        LuaStatus finished = Shim.sl_resume(thread, 0, 1);

        finished.ShouldBe(LuaStatus.Ok);
        Lua.lua_tonumberx(thread, -1, null).ShouldBe(101);
    }

    [Fact]
    public void A_yield_under_a_plain_call_comes_back_as_an_error()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        Register(state);

        LuaStatus status = state.Run("return wait_for(1)", results: 1);

        status.ShouldBe(LuaStatus.ErrorRun);
        string? message = state.PopText();
        message.ShouldNotBeNull();
        message.ShouldContain("attempt to yield");
    }

    [Fact]
    public void An_error_after_a_resume_comes_back_as_the_status_of_the_resume()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        nint thread = StartThread(state, "wait_for(0)\nerror('late')");
        Shim.sl_resume(thread, 0, 0).ShouldBe(LuaStatus.Yield);
        Lua.Pop(thread, 1);

        LuaStatus status = Shim.sl_resume(thread, 0, 0);

        status.ShouldBe(LuaStatus.ErrorRun);
        LuaText.Read(thread, -1).ShouldBe("waiting:2: late");
    }

    // A thread of its own with the source loaded and not yet started.
    private static nint StartThread(ProbeState state, string source)
    {
        Register(state);
        nint thread = Lua.lua_newthread(state.L);
        LuaChunk.Load(thread, "=waiting", Encoding.UTF8.GetBytes(source)).ShouldBe(LuaStatus.Ok);
        return thread;
    }

    private static void Register(ProbeState state)
    {
        fixed (byte* name = "wait_for"u8)
            state.Require(Shim.sl_pushhostfunction(state.L, &WaitFor, name));
        state.Require(state.SetGlobal("wait_for"u8));
    }

    // Yields its one argument to whoever resumed the thread.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int WaitFor(nint L)
    {
        Lua.lua_settop(L, 1);
        return Shim.HostYield - 1;
    }
}
