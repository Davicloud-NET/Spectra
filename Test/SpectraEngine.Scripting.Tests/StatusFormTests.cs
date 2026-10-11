using SpectraEngine.Scripting.CrashCases;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The shim's calls that would raise in Luau's own API. Each answers with a
/// status and leaves the error value where the result would be.
/// </summary>
public sealed unsafe class StatusFormTests
{
    [Fact]
    public void Reading_a_field_pushes_its_value()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        state.Run("return { speed = 5 }", results: 1).ShouldBe(LuaStatus.Ok);

        LuaStatus status = GetField(state, -1, "speed"u8);

        status.ShouldBe(LuaStatus.Ok);
        state.PopNumber().ShouldBe(5);
        Lua.lua_type(state.L, -1).ShouldBe(LuaType.Table);
    }

    [Fact]
    public void An_error_in_a_metamethod_of_a_read_comes_back_as_a_status()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        state.Run("return setmetatable({}, { __index = function(_, key) error('no field ' .. key) end })", results: 1)
            .ShouldBe(LuaStatus.Ok);

        LuaStatus status = GetField(state, -1, "speed"u8);

        status.ShouldBe(LuaStatus.ErrorRun);
        state.PopText().ShouldBe("test:1: no field speed");
        Lua.lua_type(state.L, -1).ShouldBe(LuaType.Table);
    }

    [Fact]
    public void Writing_a_field_takes_the_value()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        state.Run("return {}", results: 1).ShouldBe(LuaStatus.Ok);
        Lua.lua_pushnumber(state.L, 7);

        LuaStatus status = SetField(state, -2, "speed"u8);

        status.ShouldBe(LuaStatus.Ok);
        Lua.lua_gettop(state.L).ShouldBe(1);
        GetField(state, -1, "speed"u8).ShouldBe(LuaStatus.Ok);
        state.PopNumber().ShouldBe(7);
    }

    [Fact]
    public void A_frozen_table_refuses_a_write_with_a_status()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        state.Run("return table.freeze({})", results: 1).ShouldBe(LuaStatus.Ok);
        Lua.lua_pushnumber(state.L, 7);

        LuaStatus status = SetField(state, -2, "speed"u8);

        status.ShouldBe(LuaStatus.ErrorRun);
        string? message = state.PopText();
        message.ShouldNotBeNull();
        message.ShouldContain("readonly");
        Lua.lua_gettop(state.L).ShouldBe(1);
    }

    [Fact]
    public void Text_of_a_value_goes_through_its_metamethod()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        state.Run("return setmetatable({}, { __tostring = function() return 'a door' end })", results: 1)
            .ShouldBe(LuaStatus.Ok);

        LuaStatus status = Shim.sl_tostring(state.L, -1);

        status.ShouldBe(LuaStatus.Ok);
        state.PopText().ShouldBe("a door");
    }

    [Fact]
    public void An_error_in_a_tostring_metamethod_comes_back_as_a_status()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        state.Run("return setmetatable({}, { __tostring = function() error('no name') end })", results: 1)
            .ShouldBe(LuaStatus.Ok);

        LuaStatus status = Shim.sl_tostring(state.L, -1);

        status.ShouldBe(LuaStatus.ErrorRun);
        state.PopText().ShouldBe("test:1: no name");
    }

    [Fact]
    public void A_table_is_made()
    {
        NativeLuau.Require();
        using ProbeState state = new();

        LuaStatus status = Shim.sl_createtable(state.L, 8, 2);

        status.ShouldBe(LuaStatus.Ok);
        Lua.lua_type(state.L, -1).ShouldBe(LuaType.Table);
    }

    [Fact]
    public void A_table_of_negative_size_is_refused_with_a_status()
    {
        NativeLuau.Require();
        using ProbeState state = new();

        LuaStatus status = Shim.sl_createtable(state.L, -1, 0);

        status.ShouldBe(LuaStatus.ErrorRun);
        state.PopText().ShouldBe("a table size is negative");
    }

    [Fact]
    public void Userdata_is_made_with_its_tag_and_its_block()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        void* block;

        LuaStatus status = Shim.sl_newuserdata(state.L, sizeof(int), tag: 3, &block);

        status.ShouldBe(LuaStatus.Ok);
        ((nint)block).ShouldNotBe(0);
        Lua.lua_type(state.L, -1).ShouldBe(LuaType.Userdata);
    }

    [Fact]
    public void A_userdata_tag_past_the_limit_is_refused_with_a_status()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        void* block;

        LuaStatus status = Shim.sl_newuserdata(state.L, sizeof(int), Lua.UserdataTagLimit, &block);

        status.ShouldBe(LuaStatus.ErrorRun);
        ((nint)block).ShouldBe(0);
        state.PopText().ShouldBe("userdata tag 128 is out of range");
    }

    // Luau raises from a push once one frame holds MaxCStack values. The status
    // forms have to answer instead, or the raise would cross the caller.
    [Fact]
    public void A_full_stack_is_reported_as_a_status()
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

        Lua.lua_settop(state.L, 0);
        state.Run("return 1", results: 1).ShouldBe(LuaStatus.Ok);
    }

    private static LuaStatus GetField(ProbeState state, int index, ReadOnlySpan<byte> key)
    {
        fixed (byte* name = key)
            return Shim.sl_getfield(state.L, index, name);
    }

    private static LuaStatus SetField(ProbeState state, int index, ReadOnlySpan<byte> key)
    {
        fixed (byte* name = key)
            return Shim.sl_setfield(state.L, index, name);
    }
}
