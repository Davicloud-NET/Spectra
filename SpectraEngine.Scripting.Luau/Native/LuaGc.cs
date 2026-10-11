namespace SpectraEngine.Scripting.Luau.Native;

// lua_GCOp in lua.h: what lua_gc is asked to do.
internal enum LuaGc
{
    Stop = 0,
    Restart = 1,
    Collect = 2,
    Count = 3,
    CountBytes = 4,
    IsRunning = 5,
    Step = 6,
    SetGoal = 7,
    SetStepMultiplier = 8,
    SetStepSize = 9,
}
