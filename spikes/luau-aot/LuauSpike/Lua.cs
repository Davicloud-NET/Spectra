using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DisableRuntimeMarshalling]

namespace LuauSpike;

// Raw Luau entry points, named as in lua.h. Only what the spike calls.
// A lua_State* is an nint here. Strings are UTF-8 byte pointers.
internal static unsafe partial class Lua
{
    private const string Lib = "luau";

    // From lua.h and luaconf.h. Program.CheckAbi compares them with the library.
    internal const int RegistryIndex = -10000;
    internal const int GlobalsIndex = -10002;

    internal const int Ok = 0;
    internal const int Yield = 1;
    internal const int ErrRun = 2;
    internal const int ErrMem = 4;

    internal const int TNil = 0;
    internal const int TNumber = 3;
    internal const int TString = 6;
    internal const int TTable = 7;
    internal const int TFunction = 8;
    internal const int TUserdata = 9;
    internal const int UserdataTagLimit = 128;

    internal const int GcCollect = 2;
    internal const int GcCount = 3;

    [LibraryImport(Lib)] internal static partial nint lua_newthread(nint L);
    [LibraryImport(Lib)] internal static partial int lua_gettop(nint L);
    [LibraryImport(Lib)] internal static partial void lua_settop(nint L, int idx);
    [LibraryImport(Lib)] internal static partial void lua_remove(nint L, int idx);
    [LibraryImport(Lib)] internal static partial void lua_xmove(nint from, nint to, int n);

    [LibraryImport(Lib)] internal static partial int lua_type(nint L, int idx);
    [LibraryImport(Lib)] internal static partial double lua_tonumberx(nint L, int idx, int* isnum);
    [LibraryImport(Lib)] internal static partial int lua_toboolean(nint L, int idx);
    [LibraryImport(Lib)] internal static partial byte* lua_tolstring(nint L, int idx, nuint* len);
    [LibraryImport(Lib)] internal static partial void* lua_touserdatatagged(nint L, int idx, int tag);

    [LibraryImport(Lib)] internal static partial void lua_pushnumber(nint L, double n);
    [LibraryImport(Lib)] internal static partial void lua_pushboolean(nint L, int b);
    [LibraryImport(Lib)] internal static partial void lua_pushlstring(nint L, byte* s, nuint len);
    [LibraryImport(Lib)] internal static partial void lua_pushcclosurek(nint L, delegate* unmanaged[Cdecl]<nint, int> fn, byte* debugname, int nup, nint cont);
    [LibraryImport(Lib)] internal static partial void* lua_newuserdatataggedwithmetatable(nint L, nuint sz, int tag);

    [LibraryImport(Lib)] internal static partial void lua_createtable(nint L, int narr, int nrec);
    [LibraryImport(Lib)] internal static partial int lua_getfield(nint L, int idx, byte* k);
    [LibraryImport(Lib)] internal static partial void lua_setfield(nint L, int idx, byte* k);
    [LibraryImport(Lib)] internal static partial int lua_rawgeti(nint L, int idx, int n);
    [LibraryImport(Lib)] internal static partial void lua_rawseti(nint L, int idx, int n);
    [LibraryImport(Lib)] internal static partial void lua_setuserdatametatable(nint L, int tag);
    [LibraryImport(Lib)] internal static partial void lua_setuserdatadtor(nint L, int tag, delegate* unmanaged[Cdecl]<nint, void*, void> dtor);

    [LibraryImport(Lib)] internal static partial int luau_load(nint L, byte* chunkname, byte* data, nuint size, int env);
    [LibraryImport(Lib)] internal static partial int lua_pcall(nint L, int nargs, int nresults, int errfunc);
    [LibraryImport(Lib)] internal static partial int lua_resume(nint L, nint from, int narg);
    [LibraryImport(Lib)] internal static partial int lua_incustomexecution(nint L, int level);

    // Never returns: it unwinds to the nearest protected call. Only the
    // crash cases call it from managed code.
    [LibraryImport(Lib)] internal static partial void lua_error(nint L);

    [LibraryImport(Lib)] internal static partial int lua_gc(nint L, int what, int data);
    [LibraryImport(Lib)] internal static partial int lua_ref(nint L, int idx);

    [LibraryImport(Lib)] internal static partial void luaL_openlibs(nint L);
    [LibraryImport(Lib)] internal static partial void luaL_sandbox(nint L);
    [LibraryImport(Lib)] internal static partial void luaL_sandboxthread(nint L);

    // The same entry points without the GC transition. Only safe for calls
    // that are short, never block, never call back and never raise.
    [LibraryImport(Lib, EntryPoint = "lua_gettop"), SuppressGCTransition]
    internal static partial int lua_gettop_fast(nint L);
    [LibraryImport(Lib, EntryPoint = "lua_settop"), SuppressGCTransition]
    internal static partial void lua_settop_fast(nint L, int idx);
    [LibraryImport(Lib, EntryPoint = "lua_rawgeti"), SuppressGCTransition]
    internal static partial int lua_rawgeti_fast(nint L, int idx, int n);
    [LibraryImport(Lib, EntryPoint = "lua_pushnumber"), SuppressGCTransition]
    internal static partial void lua_pushnumber_fast(nint L, double n);
    [LibraryImport(Lib, EntryPoint = "lua_tonumberx"), SuppressGCTransition]
    internal static partial double lua_tonumberx_fast(nint L, int idx, int* isnum);

#if LUAU_CODEGEN
    [LibraryImport(Lib)] internal static partial int luau_codegen_supported();
    [LibraryImport(Lib)] internal static partial void luau_codegen_create(nint L);
    [LibraryImport(Lib)] internal static partial void luau_codegen_compile(nint L, int idx);
#endif

    internal static void Pop(nint L, int n) => lua_settop(L, -n - 1);
    internal static double ToNumber(nint L, int idx) => lua_tonumberx(L, idx, null);
}
