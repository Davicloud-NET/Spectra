namespace LuauSpike;

internal static class LuaHost
{
    // A state the way a game would set one up: libraries, host functions, then the sandbox.
    internal static nint NewState(bool sandbox = true, bool nativeCode = false)
    {
        nint L = Shim.sl_newstate();

#if LUAU_CODEGEN
        if (nativeCode)
            Lua.luau_codegen_create(L);
#endif

        Lua.luaL_openlibs(L);
        HostFunctions.Register(L);
        if (sandbox)
            Lua.luaL_sandbox(L);
        return L;
    }
}
