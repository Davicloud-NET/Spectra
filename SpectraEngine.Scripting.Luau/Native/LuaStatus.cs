namespace SpectraEngine.Scripting.Luau.Native;

// lua_Status in lua.h. What a call into Luau answers, and the state a thread is in.
internal enum LuaStatus
{
    Ok = 0,
    Yield = 1,
    ErrorRun = 2,
    ErrorSyntax = 3,
    ErrorMemory = 4,
    ErrorInHandler = 5,
    Break = 6,
}
