using System.Runtime.InteropServices;

namespace SpectraEngine.Scripting.Luau.Native;

// The entry points of native/luau/spectra_luau.cpp, which says what each
// does. A call that returns a LuaStatus never raises. When the status is not
// Ok, the error value is on top of the stack in place of the result. A thread
// that has yielded or failed refuses every call that runs protected.
internal static unsafe partial class Shim
{
    // Moves with SL_SHIM_REVISION in spectra_luau.h. LuauLibrary compares the two.
    internal const int Revision = 12;

    // What a host function returns instead of a result count. The shim raises
    // the error or yields once the managed frame has returned.
    internal const int HostError = -1; // the error value is on top; text gets the script's position in front
    internal const int HostRaise = -2; // the error value is on top and is raised as it is
    internal const int HostYield = -3; // HostYield - n yields the top n values

    // What sl_takestop answers.
    internal const int StopTime = 1;
    internal const int StopMemory = 2;

    [LibraryImport(Lua.Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int sl_constant(string name, int* value);

    [LibraryImport(Lua.Library)] internal static partial nint sl_newstate();

    // Takes the main state. Any thread may close a state that no thread is running.
    [LibraryImport(Lua.Library)] internal static partial void sl_close(nint L);

    [LibraryImport(Lua.Library)] internal static partial long sl_memory(nint L);
    [LibraryImport(Lua.Library)] internal static partial void sl_setbudget(nint L, long bytes);

    [LibraryImport(Lua.Library)] internal static partial void sl_sethost(nint L, nint host);
    [LibraryImport(Lua.Library), SuppressGCTransition] internal static partial nint sl_host(nint L);
    [LibraryImport(Lua.Library)] internal static partial nint sl_results(nint L);

    // Each name ends with a zero, and its atom is its place. 0 when the list was not taken.
    [LibraryImport(Lua.Library)] internal static partial int sl_setatoms(nint L, byte* names, nuint size, int count);

    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_openlibs(nint L);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_sandbox(nint L);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_load(nint L, byte* chunkName, byte* bytecode, nuint size);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_call(nint L, int arguments, int results);

    // Takes the top `count` values of `source`. Yield, Ok, Refused or an error.
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_resume(nint thread, nint source, int count, int* results);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_cancel(nint L, nint thread);

    // The thread is pushed on L and lives while something refers to it.
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_newthread(nint L, nint* thread);

    // The thread is kept by the reference and nothing is pushed.
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_spawn(nint L, int like, int sandbox, nint* thread, int* reference);

    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_keep(nint L, int index, int* reference);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_getref(nint L, int reference);

    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_pushnil(nint L);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_pushboolean(nint L, int value);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_pushnumber(nint L, double value);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_pushvector(nint L, float x, float y, float z);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_pushstring(nint L, byte* data, nuint length);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_pushvalue(nint L, int index);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_pushthread(nint L);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_pushhostfunction(nint L, delegate* unmanaged[Cdecl]<nint, int> function, byte* debugName);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_pushhostcall(nint L, delegate* unmanaged[Cdecl]<nint, nint, int, int> function, byte* debugName, int id);

    [LibraryImport(Lua.Library)] internal static partial void sl_pop(nint L, int count);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_xmove(nint from, nint to, int count);

    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_createtable(nint L, int arraySize, int recordSize);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_getfield(nint L, int index, byte* key, nuint length);

    // Both take the value on top, whether they succeed or not.
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_setfield(nint L, int index, byte* key, nuint length);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_rawseti(nint L, int index, int n);

    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_rawgeti(nint L, int index, int n);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_rawiter(nint L, int index, int cursor, int* next);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_freeze(nint L, int index);

    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_newuserdata(nint L, nuint size, int tag, void** block);

    // Takes the table on top, whether it succeeds or not.
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_setuserdatametatable(nint L, int tag, int releases);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_setvectormetatable(nint L);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_pushobject(nint L, int tag, uint slot, uint generation, uint nextGeneration, int* created);
    [LibraryImport(Lua.Library)] internal static partial int sl_takereleased(nint L, ulong* buffer, int capacity);

    // Bytes the host keeps while the userdata of a slot lives. They count against
    // the budget. Returns by how much the count moved, 0 when the userdata is gone.
    [LibraryImport(Lua.Library)] internal static partial long sl_setobjectweight(nint L, uint slot, uint generation, uint weight);
    [LibraryImport(Lua.Library), SuppressGCTransition] internal static partial long sl_outside(nint L);

    // Bytes the host keeps for the scripts outside any userdata. They count
    // against the budget.
    [LibraryImport(Lua.Library), SuppressGCTransition] internal static partial void sl_setlive(nint L, long bytes);

    // What counts against the budget now, garbage included.
    [LibraryImport(Lua.Library), SuppressGCTransition] internal static partial long sl_held(nint L);

    // Has the next call, return or loop of the running script raise, as the
    // interrupt does when it stops one.
    [LibraryImport(Lua.Library), SuppressGCTransition] internal static partial void sl_stopcall(nint L, int reason);

    // Has the collector work as for so many kilobytes allocated. 1 when it
    // rests between two cycles afterwards. With 0 it only answers.
    [LibraryImport(Lua.Library)] internal static partial int sl_gcstep(nint L, int kilobytes);

    // Pushes the value as text, through __tostring when it has one.
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_tostring(nint L, int index);

    // Pushes the message and returns HostError, for a host function to return.
    [LibraryImport(Lua.Library)] internal static partial int sl_fail(nint L, byte* message, nuint length);

    // 0 past the last frame, 1 for a script's function, 2 for one of the host or of Luau.
    [LibraryImport(Lua.Library)] internal static partial int sl_frame(nint L, int level, byte** source, byte** name, int* line);

    // The next three may come from any thread, while the caller keeps the state open.
    [LibraryImport(Lua.Library)] internal static partial ulong sl_callstate(nint L);
    [LibraryImport(Lua.Library)] internal static partial void sl_interrupt_arm(nint L, uint serial);
    [LibraryImport(Lua.Library)] internal static partial void sl_interrupt_disarm(nint L);

    [LibraryImport(Lua.Library)] internal static partial int sl_takestop(nint L);

    // Null when the compiler itself failed. A script with a mistake in it still
    // compiles, and sl_load then reports the mistake. Free the result with sl_free.
    // The two names end with a zero, or are null to leave the vector names out.
    [LibraryImport(Lua.Library)]
    internal static partial byte* sl_compile(
        byte* source, nuint size, int optimizationLevel, int debugLevel, byte* vectorLibrary, byte* vectorConstructor, nuint* bytecodeSize);
    [LibraryImport(Lua.Library)] internal static partial void sl_free(void* block);

    // The serial of the outermost call that is running or ran last.
    internal static uint CallSerial(nint L) => (uint)(sl_callstate(L) >> 1);
}
