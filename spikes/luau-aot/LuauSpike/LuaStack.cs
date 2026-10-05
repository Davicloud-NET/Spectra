using System;
using System.Text;

namespace LuauSpike;

// Small conveniences over the raw stack API. Names passed as ReadOnlySpan<byte>
// must be u8 literals: the compiler puts a zero byte after those.
internal static unsafe class LuaStack
{
    internal static void PushString(nint L, string value)
    {
        int max = Encoding.UTF8.GetMaxByteCount(value.Length);
        Span<byte> buffer = max <= 512 ? stackalloc byte[max] : new byte[max];
        int length = Encoding.UTF8.GetBytes(value, buffer);
        fixed (byte* p = buffer)
            Lua.lua_pushlstring(L, p, (nuint)length);
    }

    internal static string? ToText(nint L, int idx)
    {
        nuint length;
        byte* p = Lua.lua_tolstring(L, idx, &length);
        return p == null ? null : Encoding.UTF8.GetString(p, (int)length);
    }

    internal static void SetGlobal(nint L, ReadOnlySpan<byte> name)
    {
        fixed (byte* p = name)
            Lua.lua_setfield(L, Lua.GlobalsIndex, p);
    }

    internal static void SetField(nint L, int idx, ReadOnlySpan<byte> name)
    {
        fixed (byte* p = name)
            Lua.lua_setfield(L, idx, p);
    }

    // Protected: an __index metamethod may raise. Leaves the value or the error message on top.
    internal static int GetField(nint L, int idx, ReadOnlySpan<byte> name)
    {
        fixed (byte* p = name)
            return Shim.sl_getfield(L, idx, p);
    }

    internal static void RegisterHost(nint L, ReadOnlySpan<byte> name, delegate* unmanaged[Cdecl]<nint, int> fn)
    {
        fixed (byte* p = name)
        {
            Shim.sl_pushhostfunction(L, fn, p);
            Lua.lua_setfield(L, Lua.GlobalsIndex, p);
        }
    }

    // Pushes module[name], where the module table is held by a registry reference.
    internal static void PushMember(nint L, int moduleRef, ReadOnlySpan<byte> name)
    {
        Lua.lua_rawgeti(L, Lua.RegistryIndex, moduleRef);
        if (GetField(L, -1, name) != Lua.Ok)
            throw new InvalidOperationException(ToText(L, -1));
        Lua.lua_remove(L, -2);
    }

    // Pops the function and its arguments. Throws with the Luau message on failure.
    internal static void Call(nint L, int argumentCount, int resultCount)
    {
        if (Lua.lua_pcall(L, argumentCount, resultCount, 0) == Lua.Ok)
            return;

        string? message = ToText(L, -1);
        Lua.Pop(L, 1);
        throw new InvalidOperationException(message);
    }
}
