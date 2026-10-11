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

    // The shim's own, SL_REFUSED: the call was not made and nothing was pushed.
    Refused = 100,
}
