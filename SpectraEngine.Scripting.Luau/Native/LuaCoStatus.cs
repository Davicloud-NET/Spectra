namespace SpectraEngine.Scripting.Luau.Native;

// lua_CoStatus in lua.h: what lua_costatus says about a thread. A thread
// that has not started is Suspended, and one with nothing on its stack is
// Finished, whether it ran or not.
internal enum LuaCoStatus
{
    Running = 0,
    Suspended = 1,
    Normal = 2,
    Finished = 3,
    Failed = 4,
}
