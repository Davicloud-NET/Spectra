using System.Runtime.InteropServices;

namespace SpectraEngine.Scripting.Luau.Native;

// Luau's own entry points, named as in lua.h. A lua_State* is an nint. Only
// calls that cannot raise belong here, and no push is one of them. The rest
// goes through Shim. BindingSourceTests holds the list of names allowed here,
// and native/README.md says why.
internal static unsafe partial class Lua
{
    internal const string Library = "spectra_luau";

    // Copied from lua.h, luaconf.h and the pin. LuauLibrary compares them with
    // the library at startup.
    internal const int Release = 742;
    internal const int MultRet = -1;
    internal const int RegistryIndex = -10000;
    internal const int GlobalsIndex = -10002;
    internal const int MaxCStack = 8000;
    internal const int VectorSize = 3;
    internal const int UserdataTagLimit = 128;
    internal const int NoReference = -1;
    internal const int NilReference = 0;

    // The reads below take no lock, call nothing back and end in a few
    // instructions, so they skip the switch out of managed mode. Luau checks
    // an index in a debug build only: LuauStack checks before it calls.
    [LibraryImport(Library), SuppressGCTransition] internal static partial int lua_gettop(nint L);
    [LibraryImport(Library), SuppressGCTransition] internal static partial LuaType lua_type(nint L, int index);
    [LibraryImport(Library), SuppressGCTransition] internal static partial double lua_tonumberx(nint L, int index, int* isNumber);
    [LibraryImport(Library), SuppressGCTransition] internal static partial int lua_toboolean(nint L, int index);

    // Null when the value is not a vector. Otherwise three floats.
    [LibraryImport(Library), SuppressGCTransition] internal static partial float* lua_tovector(nint L, int index);

    // Null when the value is not userdata of this tag.
    [LibraryImport(Library), SuppressGCTransition] internal static partial void* lua_touserdatatagged(nint L, int index, int tag);

    // Zero when the value is not a thread.
    [LibraryImport(Library), SuppressGCTransition] internal static partial nint lua_tothread(nint L, int index);

    [LibraryImport(Library), SuppressGCTransition] internal static partial LuaStatus lua_status(nint L);

    // Zero inside a metamethod or a call from Luau's library, where lua_yield raises.
    [LibraryImport(Library), SuppressGCTransition] internal static partial int lua_isyieldable(nint L);
    [LibraryImport(Library), SuppressGCTransition] internal static partial void* lua_getthreaddata(nint L);
    [LibraryImport(Library), SuppressGCTransition] internal static partial void lua_setthreaddata(nint L, void* data);

    // Turns a number into a string where it sits. Ask lua_type first.
    [LibraryImport(Library)] internal static partial byte* lua_tolstring(nint L, int index, nuint* length);

    // The length of a string, a table's array or a userdata block.
    [LibraryImport(Library)] internal static partial int lua_objlen(nint L, int index);

    // L is any other thread of the same state.
    [LibraryImport(Library)] internal static partial LuaCoStatus lua_costatus(nint L, nint thread);

    // Frees a reference sl_keep or sl_spawn made. Any thread of the state will do.
    [LibraryImport(Library)] internal static partial int lua_unref(nint L, int reference);

    [LibraryImport(Library)] internal static partial int lua_gc(nint L, LuaGc what, int data);

    // Private: lua_settop also grows the stack, and growing can raise.
    [LibraryImport(Library)] private static partial void lua_settop(nint L, int index);

    // The caller knows there are so many values. Shim.sl_pop asks.
    internal static void Pop(nint L, int count) => lua_settop(L, -count - 1);
}
