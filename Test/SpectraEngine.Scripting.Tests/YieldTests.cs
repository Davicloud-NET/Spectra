using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SpectraEngine.Scripting.CrashCases;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Tests;

/// <summary>A host function that yields the script which called it, at the level of the shim.</summary>
public sealed unsafe class YieldTests
{
    [Fact]
    public void A_host_function_yields_its_thread_and_a_resume_hands_it_a_value()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        nint thread = StartThread(state, "local answer = wait_for(41)\nreturn answer + 1");

        LuaStatus yielded = state.Resume(thread);

        yielded.ShouldBe(LuaStatus.Yield);
        Lua.lua_gettop(thread).ShouldBe(1);
        Lua.lua_tonumberx(thread, 1, null).ShouldBe(41);

        Shim.sl_pushnumber(state.L, 100).ShouldBe(LuaStatus.Ok);
        LuaStatus finished = state.Resume(thread, count: 1);

        finished.ShouldBe(LuaStatus.Ok);
        Lua.lua_tonumberx(state.Results, -1, null).ShouldBe(101);
    }

    // A thread that ended with values on its stack would read as one that
    // has not started, and a second resume would call its first result.
    [Fact]
    public void What_a_thread_returned_moves_off_its_stack()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        nint thread = StartThread(state, "return 'a', 'b'");

        int results;
        LuaStatus finished = Shim.sl_resume(thread, state.L, 0, &results);

        finished.ShouldBe(LuaStatus.Ok);
        results.ShouldBe(2);
        Lua.lua_gettop(thread).ShouldBe(0);
        LuaText.Read(state.Results, 1).ShouldBe("a");
        LuaText.Read(state.Results, 2).ShouldBe("b");
    }

    [Fact]
    public void A_thread_that_is_not_waiting_is_refused_and_the_values_for_it_are_taken()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        nint thread = StartThread(state, "error('late')");
        state.Resume(thread).ShouldBe(LuaStatus.ErrorRun);
        Shim.sl_pushnumber(state.L, 1).ShouldBe(LuaStatus.Ok);
        int before = Lua.lua_gettop(state.L);

        LuaStatus status = state.Resume(thread, count: 1);

        status.ShouldBe(LuaStatus.Refused);
        Lua.lua_gettop(state.L).ShouldBe(before - 1);
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
        state.Resume(thread).ShouldBe(LuaStatus.Yield);

        LuaStatus status = state.Resume(thread);

        status.ShouldBe(LuaStatus.ErrorRun);
        LuaText.Read(thread, -1).ShouldBe("waiting:2: late");
    }

    // A protected call on a thread that has yielded moves its stack base, so
    // the values it yielded would no longer be where the host reads them.
    [Fact]
    public void A_yielded_thread_refuses_a_call_that_runs_protected()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        nint thread = StartThread(state, "local answer = wait_for(7, 41)\nreturn answer + 1");
        state.Resume(thread).ShouldBe(LuaStatus.Yield);

        LuaStatus status = LuaText.Push(thread, "text");

        status.ShouldBe(LuaStatus.ErrorRun);
        LuaText.Read(thread, -1).ShouldBe("the thread is suspended");
        Lua.Pop(thread, 1);
        Lua.lua_gettop(thread).ShouldBe(1);
        Lua.lua_tonumberx(thread, 1, null).ShouldBe(41);
    }

    [Fact]
    public void A_yielded_thread_resumes_after_it_refused_a_call()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        nint thread = StartThread(state, "local answer = wait_for(7, 41)\nreturn answer + 1");
        state.Resume(thread).ShouldBe(LuaStatus.Yield);
        Shim.sl_tostring(thread, -1).ShouldBe(LuaStatus.ErrorRun);

        Shim.sl_pushnumber(state.L, 100).ShouldBe(LuaStatus.Ok);
        LuaStatus finished = state.Resume(thread, count: 1);

        finished.ShouldBe(LuaStatus.Ok);
        Lua.lua_tonumberx(state.Results, -1, null).ShouldBe(101);
    }

    // The way a string gets to a thread that has yielded: made on the main
    // thread, where a protected call may run, and moved.
    [Fact]
    public void A_string_reaches_a_yielded_thread_by_being_moved_there()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        nint thread = StartThread(state, "local answer = wait_for(1)\nreturn answer .. '!'");
        state.Resume(thread).ShouldBe(LuaStatus.Yield);

        LuaText.Push(state.L, "moved").ShouldBe(LuaStatus.Ok);
        LuaStatus finished = state.Resume(thread, count: 1);

        finished.ShouldBe(LuaStatus.Ok);
        LuaText.Read(state.Results, -1).ShouldBe("moved!");
    }

    [Fact]
    public void A_thread_that_ended_in_an_error_refuses_a_call_that_runs_protected()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        nint thread = StartThread(state, "error('late')");
        state.Resume(thread).ShouldBe(LuaStatus.ErrorRun);

        LuaStatus status = Shim.sl_tostring(thread, -1);

        status.ShouldBe(LuaStatus.ErrorRun);
        LuaText.Read(thread, -1).ShouldBe("the thread ended with an error");
        Lua.Pop(thread, 1);
        LuaText.Read(thread, -1).ShouldBe("waiting:1: late");
    }

    // A thread of its own with the source loaded and not yet started. It
    // stays on the main stack, which keeps it alive.
    private static nint StartThread(ProbeState state, string source)
    {
        Register(state);
        nint thread;
        Shim.sl_newthread(state.L, &thread).ShouldBe(LuaStatus.Ok);
        LuaChunk.Load(thread, "=waiting", Encoding.UTF8.GetBytes(source)).ShouldBe(LuaStatus.Ok);
        return thread;
    }

    private static void Register(ProbeState state)
    {
        fixed (byte* name = "wait_for"u8)
            state.Require(Shim.sl_pushhostfunction(state.L, &WaitFor, name));
        state.Require(state.SetGlobal("wait_for"u8));
    }

    // Yields its last argument to whoever resumed the thread.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int WaitFor(nint L) => Shim.HostYield - 1;
}
