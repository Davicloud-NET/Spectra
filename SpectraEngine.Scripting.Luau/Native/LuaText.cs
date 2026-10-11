using System.Buffers;
using System.Text;

namespace SpectraEngine.Scripting.Luau.Native;

// Text across the boundary. Luau strings are bytes, and here they are UTF-8.
internal static unsafe class LuaText
{
    private const int StackBytes = 512;

    // Pushes the text. When the status is not Ok, the error value is on top instead.
    internal static LuaStatus Push(nint L, ReadOnlySpan<char> text) => (LuaStatus)Send(L, text, isError: false);

    // For a host function: `return LuaText.Fail(L, "...")`. The shim raises the
    // message as an error once the function has returned.
    internal static int Fail(nint L, ReadOnlySpan<char> message) => Send(L, message, isError: true);

    // Null when the value is not a string.
    internal static string? Read(nint L, int index)
    {
        if (Lua.lua_type(L, index) != LuaType.String)
            return null;

        nuint length;
        byte* data = Lua.lua_tolstring(L, index, &length);
        return Encoding.UTF8.GetString(data, checked((int)length));
    }

    private static int Send(nint L, ReadOnlySpan<char> text, bool isError)
    {
        int capacity = Encoding.UTF8.GetMaxByteCount(text.Length);
        byte[]? rented = capacity > StackBytes ? ArrayPool<byte>.Shared.Rent(capacity) : null;
        Span<byte> buffer = rented is null ? stackalloc byte[StackBytes] : rented;
        try
        {
            int length = Encoding.UTF8.GetBytes(text, buffer);
            fixed (byte* data = buffer)
            {
                return isError
                    ? Shim.sl_fail(L, data, (nuint)length)
                    : (int)Shim.sl_pushstring(L, data, (nuint)length);
            }
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
