using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau;

// The two ways a managed function gets into Luau: as a static pointer the
// shim calls directly, or as a delegate reached through Dispatch.
internal sealed unsafe class HostFunctions
{
    private const int StackNameBytes = 128;

    // Delegates are kept for the life of the host: a closure in Luau holds
    // only the place in this list.
    private readonly List<LuauHostFunction> _delegates = [];

    internal static bool Push(nint L, delegate* unmanaged[Cdecl]<nint, int> function, ReadOnlySpan<byte> name)
    {
        Span<byte> terminated = name.Length < StackNameBytes ? stackalloc byte[StackNameBytes] : new byte[name.Length + 1];
        fixed (byte* debugName = Terminate(name, terminated))
            return Shim.sl_pushhostfunction(L, function, debugName) == LuaStatus.Ok;
    }

    internal bool Push(nint L, ReadOnlySpan<byte> name, LuauHostFunction function)
    {
        int id = _delegates.Count;
        _delegates.Add(function);

        Span<byte> terminated = name.Length < StackNameBytes ? stackalloc byte[StackNameBytes] : new byte[name.Length + 1];
        fixed (byte* debugName = Terminate(name, terminated))
            return Shim.sl_pushhostcall(L, &Dispatch, debugName, id) == LuaStatus.Ok;
    }

    // Luau reads a function's name up to a zero.
    private static Span<byte> Terminate(ReadOnlySpan<byte> name, Span<byte> buffer)
    {
        name.CopyTo(buffer);
        buffer[name.Length] = 0;
        return buffer;
    }

    // What Luau calls for every delegate. The shim hands over the host's
    // handle and the place in the list, and raises what this returns.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Dispatch(nint L, nint handle, int id)
    {
        try
        {
            if (LuauHost.FromHandle(handle) is not { } host)
                return LuaText.Fail(L, "the script host is gone");

            return host.Functions._delegates[id](new LuauStack(L));
        }
        catch (Exception e) // boundary: a managed exception must not reach a native frame
        {
            return LuaText.Fail(L, e.Message);
        }
    }
}
