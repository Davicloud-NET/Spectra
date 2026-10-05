using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LuauSpike;

// Managed functions that scripts call. The contract: let no managed exception
// out, never call a Luau function that can raise, and report a failure by
// pushing a message and returning Shim.HostError.
internal static unsafe class HostFunctions
{
    internal static void Register(nint L)
    {
        LuaStack.RegisterHost(L, "host_add"u8, &Add);
        LuaStack.RegisterHost(L, "host_add_fast"u8, &AddFast);
        LuaStack.RegisterHost(L, "host_greet"u8, &Greet);
        LuaStack.RegisterHost(L, "host_throw"u8, &Throw);
        LuaStack.RegisterHost(L, "host_reject"u8, &Reject);
        LuaStack.RegisterHost(L, "host_in_native"u8, &InNative);
        LuaStack.RegisterHost(L, "wait"u8, &Wait);

        fixed (byte* name = "host_add_typed"u8)
        {
            Shim.sl_pushhostnumberfunction(L, &AddTyped, name);
            Lua.lua_setfield(L, Lua.GlobalsIndex, name);
        }

        // Registered as a lua_CFunction with no trampoline in between. Fine
        // for a function that cannot fail, and it shows what the trampoline costs.
        fixed (byte* name = "host_add_direct"u8)
        {
            Lua.lua_pushcclosurek(L, &Add, name, 0, 0);
            Lua.lua_setfield(L, Lua.GlobalsIndex, name);
        }

        Shim.sl_pushnativeadd(L);
        LuaStack.SetGlobal(L, "native_add"u8);
    }

    internal static int Fail(nint L, string message)
    {
        LuaStack.PushString(L, message);
        return Shim.HostError;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Add(nint L)
    {
        try
        {
            int isNumber;
            double a = Lua.lua_tonumberx(L, 1, &isNumber);
            if (isNumber == 0)
                return Fail(L, "host_add: argument 1 must be a number");
            double b = Lua.lua_tonumberx(L, 2, &isNumber);
            if (isNumber == 0)
                return Fail(L, "host_add: argument 2 must be a number");

            Lua.lua_pushnumber(L, a + b);
            return 1;
        }
        catch (Exception e) // boundary: a managed exception must not reach a native frame
        {
            return Fail(L, e.Message);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int AddFast(nint L)
    {
        try
        {
            int isNumber;
            double a = Lua.lua_tonumberx_fast(L, 1, &isNumber);
            if (isNumber == 0)
                return Fail(L, "host_add_fast: argument 1 must be a number");
            double b = Lua.lua_tonumberx_fast(L, 2, &isNumber);
            if (isNumber == 0)
                return Fail(L, "host_add_fast: argument 2 must be a number");

            Lua.lua_pushnumber_fast(L, a + b);
            return 1;
        }
        catch (Exception e) // boundary: a managed exception must not reach a native frame
        {
            return Fail(L, e.Message);
        }
    }

    // The shim checks and unpacks the arguments, so this makes no call back into Luau.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static double AddTyped(double a, double b) => a + b;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Greet(nint L)
    {
        try
        {
            string? name = LuaStack.ToText(L, 1);
            if (name is null)
                return Fail(L, "host_greet: argument 1 must be a string");

            LuaStack.PushString(L, "hello " + name);
            Lua.lua_pushnumber(L, Lua.ToNumber(L, 2) * 2);
            return 2;
        }
        catch (Exception e) // boundary: a managed exception must not reach a native frame
        {
            return Fail(L, e.Message);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Throw(nint L)
    {
        try
        {
            FailDeep(4);
            return 0;
        }
        catch (Exception e) // boundary: a managed exception must not reach a native frame
        {
            return Fail(L, e.Message);
        }
    }

    // A throw a few frames down, as real host code would have it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int FailDeep(int depth)
    {
        if (depth == 0)
            throw new InvalidOperationException("managed failure");
        return FailDeep(depth - 1) + 1;
    }

    // Fails without a managed exception, to price the error path alone.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Reject(nint L) => Fail(L, "rejected");

    // True when the calling Luau function runs as native code from Luau.CodeGen.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int InNative(nint L)
    {
        Lua.lua_pushboolean(L, Lua.lua_incustomexecution(L, 1));
        return 1;
    }

    // Yields the calling coroutine. The yielded value is the requested delay.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Wait(nint L)
    {
        Lua.lua_settop(L, 1);
        return Shim.HostYieldBase - 1;
    }
}
