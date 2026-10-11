using System.Text;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau;

/// <summary>The startup check of the native Luau library.</summary>
public static unsafe class LuauLibrary
{
    private const string HowToBuild =
        "Build it with native/build-luau.ps1 on Windows or native/build-luau.sh on Linux.";

    // Every number this binding copied from Luau's headers or from the shim.
    // A release that moves one would make the binding read the wrong stack
    // slot or misname a type, with no error.
    private static readonly (string Name, int Value)[] Copied =
    [
        ("SL_LUAU_RELEASE", Lua.Release),
        ("SL_HOST_ERROR", Shim.HostError),
        ("SL_HOST_YIELD", Shim.HostYield),

        ("LUA_USE_LONGJMP", 1),
        ("LUA_VECTOR_SIZE", Lua.VectorSize),
        ("LUA_VECTOR_DOUBLE", 0),
        ("LUA_MINSTACK", Lua.MinStack),
        ("LUAI_MAXCSTACK", Lua.MaxCStack),
        ("LUA_UTAG_LIMIT", Lua.UserdataTagLimit),

        ("LUA_MULTRET", Lua.MultRet),
        ("LUA_REGISTRYINDEX", Lua.RegistryIndex),
        ("LUA_GLOBALSINDEX", Lua.GlobalsIndex),

        ("LUA_OK", (int)LuaStatus.Ok),
        ("LUA_YIELD", (int)LuaStatus.Yield),
        ("LUA_ERRRUN", (int)LuaStatus.ErrorRun),
        ("LUA_ERRSYNTAX", (int)LuaStatus.ErrorSyntax),
        ("LUA_ERRMEM", (int)LuaStatus.ErrorMemory),
        ("LUA_ERRERR", (int)LuaStatus.ErrorInHandler),
        ("LUA_BREAK", (int)LuaStatus.Break),

        ("LUA_TNONE", (int)LuaType.None),
        ("LUA_TNIL", (int)LuaType.Nil),
        ("LUA_TBOOLEAN", (int)LuaType.Boolean),
        ("LUA_TLIGHTUSERDATA", (int)LuaType.LightUserdata),
        ("LUA_TNUMBER", (int)LuaType.Number),
        ("LUA_TINTEGER", (int)LuaType.Integer),
        ("LUA_TVECTOR", (int)LuaType.Vector),
        ("LUA_TSTRING", (int)LuaType.String),
        ("LUA_TTABLE", (int)LuaType.Table),
        ("LUA_TFUNCTION", (int)LuaType.Function),
        ("LUA_TUSERDATA", (int)LuaType.Userdata),
        ("LUA_TTHREAD", (int)LuaType.Thread),
        ("LUA_TBUFFER", (int)LuaType.Buffer),

        ("LUA_GCSTOP", (int)LuaGc.Stop),
        ("LUA_GCRESTART", (int)LuaGc.Restart),
        ("LUA_GCCOLLECT", (int)LuaGc.Collect),
        ("LUA_GCCOUNT", (int)LuaGc.Count),
        ("LUA_GCCOUNTB", (int)LuaGc.CountBytes),
        ("LUA_GCISRUNNING", (int)LuaGc.IsRunning),
        ("LUA_GCSTEP", (int)LuaGc.Step),
        ("LUA_GCSETGOAL", (int)LuaGc.SetGoal),
        ("LUA_GCSETSTEPMUL", (int)LuaGc.SetStepMultiplier),
        ("LUA_GCSETSTEPSIZE", (int)LuaGc.SetStepSize),
    ];

    /// <summary>
    /// Says what is wrong with the native library, or returns null when it is
    /// there and was built from the Luau release this binding was written for.
    /// Call it once before the first script runs.
    /// </summary>
    public static string? FindProblem()
    {
        try
        {
            string mismatches = FindMismatches();
            return mismatches.Length == 0
                ? null
                : $"The Luau library does not match the binding:{mismatches} {HowToBuild}";
        }
        catch (DllNotFoundException)
        {
            return $"The Luau library ({Lua.Library}) was not found. {HowToBuild}";
        }
        catch (EntryPointNotFoundException e)
        {
            return $"The Luau library is from another build: {e.Message} {HowToBuild}";
        }
    }

    private static string FindMismatches()
    {
        StringBuilder mismatches = new();
        foreach ((string name, int expected) in Copied)
        {
            int actual;
            if (Shim.sl_constant(name, &actual) == 0)
                mismatches.Append($" {name} is unknown to the library.");
            else if (actual != expected)
                mismatches.Append($" {name} is {actual} in the library and {expected} here.");
        }

        return mismatches.ToString();
    }
}
