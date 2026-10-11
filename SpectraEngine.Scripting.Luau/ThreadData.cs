using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau;

// What the host writes into a Luau thread's data pointer: its script in the
// low half and its slot in the high half, each plus one so that zero means
// none. The shim gives a thread that a script makes the low half of its
// maker's, so a coroutine belongs to its script and has no slot.
internal static unsafe class ThreadData
{
    internal static void* Pack(int slot, LuauScript? script) =>
        (void*)(((ulong)(uint)(slot + 1) << 32) | (uint)((script?.Id ?? -1) + 1));

    // -1 when the thread has no slot.
    internal static int SlotOf(nint state) => (int)((ulong)Lua.lua_getthreaddata(state) >> 32) - 1;

    // -1 when the thread belongs to no script.
    internal static int ScriptOf(nint state) => (int)(uint)(ulong)Lua.lua_getthreaddata(state) - 1;
}
