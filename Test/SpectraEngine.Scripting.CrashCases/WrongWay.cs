using System.Runtime.InteropServices;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.CrashCases;

// What the binding never declares: a Luau call that raises, reachable from
// managed code. It is here so that one crash case can show what follows.
internal static partial class WrongWay
{
    // Does not return. It jumps to the nearest protected call, over the
    // managed frame that called it.
    [LibraryImport(Lua.Library)] internal static partial void lua_error(nint L);
}
