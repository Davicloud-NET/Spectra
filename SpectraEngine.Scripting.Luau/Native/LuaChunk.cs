using System.Text;

namespace SpectraEngine.Scripting.Luau.Native;

// Source text to a function on the stack.
internal static unsafe class LuaChunk
{
    // Line numbers and function names, which a traceback needs, and nothing
    // that makes a script harder to debug.
    private const int OptimizationLevel = 1;
    private const int DebugLevel = 1;

    // Compiles the source and pushes it as a function. When the status is not
    // Ok, the message is on top instead, and a mistake in the script starts it
    // with the chunk's name and the line. Luau shows that name without its
    // first character: "=door" reads as "door", "@Scripts/door.luau" as the path.
    internal static LuaStatus Load(nint L, string chunkName, ReadOnlySpan<byte> source) =>
        Load(L, Encoding.UTF8.GetBytes(chunkName + '\0'), source);

    // The name ends with a zero.
    internal static LuaStatus Load(nint L, ReadOnlySpan<byte> chunkName, ReadOnlySpan<byte> source)
    {
        nuint size;
        byte* bytecode;
        fixed (byte* text = source)
            bytecode = Shim.sl_compile(text, (nuint)source.Length, OptimizationLevel, DebugLevel, &size);

        if (bytecode == null)
        {
            LuaStatus pushed = LuaText.Push(L, "the Luau compiler failed");
            return pushed == LuaStatus.Ok ? LuaStatus.ErrorMemory : pushed;
        }

        try
        {
            fixed (byte* name = chunkName)
                return Shim.sl_load(L, name, bytecode, size);
        }
        finally
        {
            // The block came from the library's C runtime. Only sl_free may release it.
            Shim.sl_free(bytecode);
        }
    }
}
