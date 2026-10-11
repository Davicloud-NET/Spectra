using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SpectraEngine.Scripting.Luau.Native;

// The one host function of this layer. The tests and the crash cases call it,
// and every later host function follows its shape:
//
// - It lets no exception out. One that leaves ends the process.
// - It calls nothing in Luau that can raise.
// - It fails by returning an error code. The shim raises the error after this
//   frame has returned.
internal static unsafe class HostProbe
{
    // Pushes the function. When the status is not Ok, the error value is on top instead.
    internal static LuaStatus Push(nint L)
    {
        fixed (byte* name = "probe"u8)
            return Shim.sl_pushhostfunction(L, &Repeat, name);
    }

    // probe(text, times) returns the text repeated and the length of the result.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Repeat(nint L)
    {
        try
        {
            string? text = LuaText.Read(L, 1);
            if (text is null)
                return LuaText.Fail(L, "probe: argument 1 must be a string");

            int isNumber;
            double times = Lua.lua_tonumberx(L, 2, &isNumber);
            if (isNumber == 0)
                return LuaText.Fail(L, "probe: argument 2 must be a number");

            // A negative count throws. The tests use that to reach the catch.
            string repeated = new StringBuilder().Insert(0, text, (int)times).ToString();

            if (LuaText.Push(L, repeated) != LuaStatus.Ok)
                return Shim.HostError;
            Lua.lua_pushnumber(L, repeated.Length);
            return 2;
        }
        catch (Exception e) // boundary: a managed exception must not reach a native frame
        {
            return LuaText.Fail(L, e.Message);
        }
    }
}
