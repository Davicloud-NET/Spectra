using System.Buffers;
using System.Text;

namespace SpectraEngine.Scripting.Luau.Native;

// Text across the boundary. Luau strings are bytes, and here they are UTF-8.
internal static unsafe class LuaText
{
    private const int StackBytes = 512;

    // Pushes the text. When the status is not Ok, the error value is on top instead.
    internal static LuaStatus Push(nint L, ReadOnlySpan<char> text) => (LuaStatus)Send(L, text, isError: false);

    internal static LuaStatus PushUtf8(nint L, ReadOnlySpan<byte> text)
    {
        fixed (byte* data = text)
            return Shim.sl_pushstring(L, data, (nuint)text.Length);
    }

    // For a host function: `return LuaText.Fail(L, "...")`. The shim raises the
    // message as an error once the function has returned.
    internal static int Fail(nint L, ReadOnlySpan<char> message) => Send(L, message, isError: true);

    // Null when the value is not a string.
    internal static string? Read(nint L, int index) =>
        TryReadUtf8(L, index, out ReadOnlySpan<byte> text) ? Encoding.UTF8.GetString(text) : null;

    // The bytes belong to Luau and live while the string stays on the stack.
    internal static bool TryReadUtf8(nint L, int index, out ReadOnlySpan<byte> text)
    {
        text = default;
        if (Lua.lua_type(L, index) != LuaType.String)
            return false;

        nuint length;
        byte* data = Lua.lua_tolstring(L, index, &length);
        text = new ReadOnlySpan<byte>(data, checked((int)length));
        return true;
    }

    // A zero-terminated text of Luau's own, such as a chunk's name.
    internal static ReadOnlySpan<byte> FromNative(byte* text) =>
        text == null ? default : System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text);

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
