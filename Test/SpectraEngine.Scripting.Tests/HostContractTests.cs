using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SpectraEngine.Scripting.CrashCases;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What the shim makes of the number a host function returns, also when the
/// function breaks the contract.
/// </summary>
public sealed unsafe class HostContractTests
{
    [Fact]
    public void An_error_value_that_is_not_text_reaches_the_script_as_it_is()
    {
        NativeLuau.Require();
        using ProbeState state = Register("fail_with_number", &FailWithNumber);

        LuaStatus status = state.Run("local ok, value = pcall(fail_with_number)\nreturn ok, value", results: 2);

        status.ShouldBe(LuaStatus.Ok);
        state.PopNumber().ShouldBe(42);
        Lua.lua_toboolean(state.L, -1).ShouldBe(0);
    }

    [Fact]
    public void A_failure_with_nothing_on_the_stack_still_has_a_message()
    {
        NativeLuau.Require();
        using ProbeState state = Register("fail_with_nothing", &FailWithNothing);

        LuaStatus status = state.Run("fail_with_nothing()");

        status.ShouldBe(LuaStatus.ErrorRun);
        state.PopText().ShouldBe("test:1: a host function failed without a message");
    }

    [Fact]
    public void A_count_larger_than_the_stack_is_an_error_and_not_a_read_past_it()
    {
        NativeLuau.Require();
        using ProbeState state = Register("return_too_many", &ReturnTooMany);

        LuaStatus status = state.Run("return return_too_many()", results: 1);

        status.ShouldBe(LuaStatus.ErrorRun);
        state.PopText().ShouldBe("test:1: a host function returned more values than it left on the stack");
    }

    [Fact]
    public void A_yield_of_more_values_than_the_stack_holds_is_an_error()
    {
        NativeLuau.Require();
        using ProbeState state = Register("yield_too_many", &YieldTooMany);

        LuaStatus status = state.Run("yield_too_many()");

        status.ShouldBe(LuaStatus.ErrorRun);
        state.PopText().ShouldBe("test:1: a host function yielded more values than it left on the stack");
    }

    // A state with one more host function as a global. The name must be ASCII.
    private static ProbeState Register(string name, delegate* unmanaged[Cdecl]<nint, int> function)
    {
        ProbeState state = new();
        Span<byte> key = stackalloc byte[name.Length + 1];
        Encoding.ASCII.GetBytes(name, key);
        key[name.Length] = 0;

        fixed (byte* pointer = key)
        {
            state.Require(Shim.sl_pushhostfunction(state.L, function, pointer));
            state.Require(Shim.sl_setfield(state.L, Lua.GlobalsIndex, pointer));
        }

        return state;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FailWithNumber(nint L)
    {
        Shim.sl_pushnumber(L, 42);
        return Shim.HostError;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FailWithNothing(nint L) => Shim.HostError;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ReturnTooMany(nint L) => 3;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int YieldTooMany(nint L) => Shim.HostYield - 3;
}
