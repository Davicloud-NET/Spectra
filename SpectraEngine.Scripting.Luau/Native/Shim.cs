using System.Runtime.InteropServices;

namespace SpectraEngine.Scripting.Luau.Native;

// The entry points of native/luau/spectra_luau.cpp. A call that returns a
// LuaStatus never raises. When the status is not Ok, the error value is on top
// of the stack in place of the result. A thread that has yielded or failed
// refuses all of them but sl_pushnumber, sl_newthread and sl_resume.
internal static unsafe partial class Shim
{
    // Moves with SL_SHIM_REVISION in spectra_luau.h. LuauLibrary compares the two.
    internal const int Revision = 1;

    // What a host function returns instead of a result count. The shim raises
    // the error or yields once the managed frame has returned.
    internal const int HostError = -1; // the error value is on top; text gets the script's position in front
    internal const int HostRaise = -2; // the error value is on top and is raised as it is
    internal const int HostYield = -3; // HostYield - n yields the top n values

    [LibraryImport(Lua.Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int sl_constant(string name, int* value);

    [LibraryImport(Lua.Library)] internal static partial nint sl_newstate();

    // Takes the main state. Any thread may close a state that no thread is running.
    [LibraryImport(Lua.Library)] internal static partial void sl_close(nint L);

    [LibraryImport(Lua.Library)] internal static partial long sl_memory(nint L);

    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_openlibs(nint L);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_load(nint L, byte* chunkName, byte* bytecode, nuint size);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_call(nint L, int arguments, int results);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_resume(nint thread, nint from, int arguments);

    // The thread is pushed on L and lives while something refers to it.
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_newthread(nint L, nint* thread);

    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_pushnumber(nint L, double value);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_pushstring(nint L, byte* data, nuint length);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_pushhostfunction(nint L, delegate* unmanaged[Cdecl]<nint, int> function, byte* debugName);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_createtable(nint L, int arraySize, int recordSize);
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_newuserdata(nint L, nuint size, int tag, void** block);

    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_getfield(nint L, int index, byte* key);

    // Takes the value on top, whether it succeeds or not.
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_setfield(nint L, int index, byte* key);

    // Pushes the value as text, through __tostring when it has one.
    [LibraryImport(Lua.Library)] internal static partial LuaStatus sl_tostring(nint L, int index);

    // Pushes the message and returns HostError, for a host function to return.
    [LibraryImport(Lua.Library)] internal static partial int sl_fail(nint L, byte* message, nuint length);

    // From any thread, while the caller keeps the state open. The running
    // script stops with an error at its next loop or call, and again after
    // each pcall that catches it, until disarmed.
    [LibraryImport(Lua.Library)] internal static partial void sl_interrupt_arm(nint L);
    [LibraryImport(Lua.Library)] internal static partial void sl_interrupt_disarm(nint L);

    // Null when the compiler itself failed. A script with a mistake in it still
    // compiles, and sl_load then reports the mistake. Free the result with sl_free.
    [LibraryImport(Lua.Library)] internal static partial byte* sl_compile(byte* source, nuint size, int optimizationLevel, int debugLevel, nuint* bytecodeSize);
    [LibraryImport(Lua.Library)] internal static partial void sl_free(void* block);
}
