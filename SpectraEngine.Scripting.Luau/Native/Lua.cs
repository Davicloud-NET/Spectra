using System.Runtime.InteropServices;

namespace SpectraEngine.Scripting.Luau.Native;

// Luau's own entry points, named as in lua.h. A lua_State* is an nint.
//
// Only calls that cannot raise an error belong here. Luau raises with longjmp,
// and a jump over a managed frame kills the process at a later garbage
// collection. Whatever can run script code or fail goes through Shim, which
// answers with a status. BindingSourceTests holds the list of names allowed here.
//
// A push takes a stack slot and Luau finds it one. Past MaxCStack values in
// one frame it raises instead, so code that pushes in a loop pops as it goes,
// or asks lua_checkstack first, which answers 0.
internal static unsafe partial class Lua
{
    internal const string Library = "spectra_luau";

    // Copied from lua.h, luaconf.h and the pin. LuauLibrary compares them with
    // the library at startup.
    internal const int Release = 742;
    internal const int MultRet = -1;
    internal const int RegistryIndex = -10000;
    internal const int GlobalsIndex = -10002;
    internal const int MinStack = 20;
    internal const int MaxCStack = 8000;
    internal const int VectorSize = 3;
    internal const int UserdataTagLimit = 128;

    [LibraryImport(Library)] internal static partial int lua_gettop(nint L);

    // Only to shrink. Growing takes slots, as a push does.
    [LibraryImport(Library)] internal static partial void lua_settop(nint L, int index);

    [LibraryImport(Library)] internal static partial int lua_checkstack(nint L, int size);

    [LibraryImport(Library)] internal static partial LuaType lua_type(nint L, int index);
    [LibraryImport(Library)] internal static partial double lua_tonumberx(nint L, int index, int* isNumber);
    [LibraryImport(Library)] internal static partial int lua_toboolean(nint L, int index);

    // Turns a number into a string where it sits. Ask lua_type first.
    [LibraryImport(Library)] internal static partial byte* lua_tolstring(nint L, int index, nuint* length);

    [LibraryImport(Library)] internal static partial void lua_pushnumber(nint L, double value);

    // The thread is pushed on L and lives while something refers to it.
    [LibraryImport(Library)] internal static partial nint lua_newthread(nint L);

    [LibraryImport(Library)] internal static partial int lua_gc(nint L, LuaGc what, int data);

    internal static void Pop(nint L, int count) => lua_settop(L, -count - 1);
}
