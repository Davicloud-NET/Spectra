using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau;

// print and warn as every script sees them. Luau's own print writes to the
// process's standard output, which a game does not have.
internal static unsafe class ScriptOutput
{
    // Sets the two globals. False: the error value is on top.
    internal static bool Install(nint L) =>
        Set(L, &Print, "print"u8) && Set(L, &Warn, "warn"u8);

    private static bool Set(nint L, delegate* unmanaged[Cdecl]<nint, int> function, ReadOnlySpan<byte> name)
    {
        LuauStack stack = new(L);
        return stack.PushFunction(function, name) && stack.SetGlobal(name);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Print(nint L) => Write(L, ScriptSeverity.Print);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Warn(nint L) => Write(L, ScriptSeverity.Warning);

    private static int Write(nint L, ScriptSeverity severity)
    {
        try
        {
            if (LuauHost.FromState(L) is not { } host)
                return LuaText.Fail(L, "the script host is gone");

            // A __tostring that fails has left its own error, with its own position.
            if (Join(L) is not { } text)
                return Shim.HostRaise;

            host.Report(ScriptReport.Line(L, host.ScriptOf(L), severity, text));
            return 0;
        }
        catch (Exception e) // boundary: a managed exception must not reach a native frame
        {
            return LuaText.Fail(L, e.Message);
        }
    }

    // The arguments as text with a space between, the way Roblox prints them.
    // Null when one could not be turned into text: the error value is on top.
    private static string? Join(nint L)
    {
        int count = Lua.lua_gettop(L);
        StringBuilder? line = count > 1 ? new StringBuilder() : null;
        string single = string.Empty;

        for (int index = 1; index <= count; index++)
        {
            if (Shim.sl_tostring(L, index) != LuaStatus.Ok)
                return null;

            single = LuaText.Read(L, -1) ?? string.Empty;
            Lua.Pop(L, 1);
            if (index > 1)
                line?.Append(' ');
            line?.Append(single);
        }

        return line?.ToString() ?? single;
    }
}
