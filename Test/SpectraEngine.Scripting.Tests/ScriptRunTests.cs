using SpectraEngine.Scripting.CrashCases;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Tests;

/// <summary>A script from source to result, and the ways it can fail.</summary>
public sealed class ScriptRunTests
{
    [Fact]
    public void A_state_opens_and_closes()
    {
        NativeLuau.Require();

        nint state = Shim.sl_newstate();

        state.ShouldNotBe(0);
        Shim.sl_memory(state).ShouldBeGreaterThan(0);
        Shim.sl_close(state);
    }

    [Fact]
    public void A_script_calls_the_host_function_and_gets_values_back()
    {
        NativeLuau.Require();
        using ProbeState state = new();

        LuaStatus status = state.Run("local text, length = probe('ab', 3) return text .. '!', length * 2", results: 2);

        status.ShouldBe(LuaStatus.Ok);
        state.PopNumber().ShouldBe(12);
        state.PopText().ShouldBe("ababab!");
    }

    [Fact]
    public void A_host_error_comes_back_as_a_status_with_its_message_and_line()
    {
        NativeLuau.Require();
        using ProbeState state = new();

        LuaStatus status = state.Run("local first = 1\nlocal text = probe('ab', 'many')\nreturn text");

        status.ShouldBe(LuaStatus.ErrorRun);
        state.PopText().ShouldBe("test:2: probe: argument 2 must be a number");
        Lua.lua_gettop(state.L).ShouldBe(0);
    }

    [Fact]
    public void An_exception_in_a_host_function_comes_back_as_an_error()
    {
        NativeLuau.Require();
        using ProbeState state = new();

        LuaStatus status = state.Run("return probe('ab', -1)");

        status.ShouldBe(LuaStatus.ErrorRun);
        string? message = state.PopText();
        message.ShouldNotBeNull();
        message.ShouldStartWith("test:1: ");
        message.ShouldContain("count");
    }

    [Fact]
    public void A_script_catches_a_host_error_and_goes_on()
    {
        NativeLuau.Require();
        using ProbeState state = new();

        LuaStatus status = state.Run("local ok, message = pcall(probe, 1, 2)\nreturn ok, message, probe('c', 2)", results: 3);

        status.ShouldBe(LuaStatus.Ok);
        state.PopText().ShouldBe("cc");
        state.PopText().ShouldBe("test:1: probe: argument 1 must be a string");
        Lua.lua_toboolean(state.L, -1).ShouldBe(0);
    }

    [Fact]
    public void An_error_in_a_script_comes_back_with_its_line()
    {
        NativeLuau.Require();
        using ProbeState state = new();

        LuaStatus status = state.Run("local door = nil\n\nreturn door.open");

        status.ShouldBe(LuaStatus.ErrorRun);
        state.PopText().ShouldBe("test:3: attempt to index nil with 'open'");
    }

    [Fact]
    public void A_compile_error_comes_back_as_a_message()
    {
        NativeLuau.Require();
        using ProbeState state = new();

        LuaStatus status = state.Run("local door = 1\nlocal = 5");

        status.ShouldNotBe(LuaStatus.Ok);
        string? message = state.PopText();
        message.ShouldNotBeNull();
        message.ShouldStartWith("test:2: ");
        Lua.lua_gettop(state.L).ShouldBe(0);
    }

    [Fact]
    public void A_state_still_runs_scripts_after_one_failed()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        state.Run("error('first')").ShouldBe(LuaStatus.ErrorRun);
        Lua.Pop(state.L, 1);

        LuaStatus status = state.Run("return 1 + 1", results: 1);

        status.ShouldBe(LuaStatus.Ok);
        state.PopNumber().ShouldBe(2);
    }

    // Luau's tools turn its feature flags on and a bare embedder gets them all
    // off. The integer library is behind one of them.
    [Fact]
    public void The_feature_flags_are_on_as_in_Luaus_own_tools()
    {
        NativeLuau.Require();
        using ProbeState state = new();

        LuaStatus status = state.Run("return type(integer)", results: 1);

        status.ShouldBe(LuaStatus.Ok);
        state.PopText().ShouldBe("table");
    }

    [Fact]
    public void The_allocator_counts_what_a_script_holds()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        long before = Shim.sl_memory(state.L);

        state.Run("kept = table.create(100000, 0)").ShouldBe(LuaStatus.Ok);

        (Shim.sl_memory(state.L) - before).ShouldBeGreaterThan(100000 * sizeof(double));
    }
}
