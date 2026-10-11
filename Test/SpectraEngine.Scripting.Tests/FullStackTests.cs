using SpectraEngine.Scripting.CrashCases;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A frame that holds as many values as Luau allows. Luau raises from the next
/// push. A status form answers instead, and the frame keeps what it held.
/// </summary>
public sealed unsafe class FullStackTests
{
    [Fact]
    public void A_string_too_many_is_refused_with_a_status()
    {
        NativeLuau.Require();
        using ProbeState state = new();

        int pushed = 0;
        LuaStatus status = LuaStatus.Ok;
        while (status == LuaStatus.Ok && pushed <= Lua.MaxCStack)
        {
            status = LuaText.Push(state.L, "one more");
            if (status == LuaStatus.Ok)
                pushed++;
        }

        status.ShouldBe(LuaStatus.ErrorRun);
        pushed.ShouldBeInRange(Lua.MaxCStack - 8, Lua.MaxCStack);
        state.PopText().ShouldBe("stack overflow");
        Lua.lua_gettop(state.L).ShouldBe(pushed);
    }

    [Fact]
    public void A_state_runs_scripts_again_once_a_full_frame_is_emptied()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        Fill(state);
        Shim.sl_pushnumber(state.L, 0).ShouldBe(LuaStatus.ErrorRun);

        Lua.Pop(state.L, Lua.lua_gettop(state.L));
        LuaStatus status = state.Run("return 1", results: 1);

        status.ShouldBe(LuaStatus.Ok);
        state.PopNumber().ShouldBe(1);
    }

    [Fact]
    public void A_number_too_many_is_refused_and_the_frame_keeps_its_values()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        Fill(state);

        LuaStatus status = Shim.sl_pushnumber(state.L, -1);

        ShouldBeRefusedWithTheFrameWhole(state, status);
    }

    [Fact]
    public void A_string_on_a_full_frame_is_refused_and_the_frame_keeps_its_values()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        Fill(state);

        LuaStatus status = LuaText.Push(state.L, "one more");

        ShouldBeRefusedWithTheFrameWhole(state, status);
    }

    [Fact]
    public void A_thread_on_a_full_frame_is_refused_and_the_frame_keeps_its_values()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        Fill(state);
        nint thread = 1;

        LuaStatus status = Shim.sl_newthread(state.L, &thread);

        thread.ShouldBe(0);
        ShouldBeRefusedWithTheFrameWhole(state, status);
    }

    [Fact]
    public void A_read_on_a_full_frame_is_refused_and_the_frame_keeps_its_values()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        state.Run("return { speed = 5 }", results: 1).ShouldBe(LuaStatus.Ok);
        Fill(state);

        LuaStatus status;
        fixed (byte* key = "speed"u8)
            status = Shim.sl_getfield(state.L, 1, key, 5);

        ShouldBeRefusedWithTheFrameWhole(state, status);
    }

    [Fact]
    public void Text_of_a_value_on_a_full_frame_is_refused_and_the_frame_keeps_its_values()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        Fill(state);

        LuaStatus status = Shim.sl_tostring(state.L, 1);

        ShouldBeRefusedWithTheFrameWhole(state, status);
    }

    // Pushes numbers until the frame holds MaxCStack values. Each number is
    // the count before it, so the last one is MaxCStack - 1.
    private static void Fill(ProbeState state)
    {
        for (int count = Lua.lua_gettop(state.L); count < Lua.MaxCStack; count++)
            Shim.sl_pushnumber(state.L, count).ShouldBe(LuaStatus.Ok);
    }

    private static void ShouldBeRefusedWithTheFrameWhole(ProbeState state, LuaStatus status)
    {
        status.ShouldBe(LuaStatus.ErrorRun);
        state.PopText().ShouldBe("stack overflow");
        Lua.lua_gettop(state.L).ShouldBe(Lua.MaxCStack);
        Lua.lua_tonumberx(state.L, -1, null).ShouldBe(Lua.MaxCStack - 1);
    }
}
