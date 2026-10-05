using System;
using System.IO;
using System.Text;

namespace LuauSpike;

// Loads the .luau files under scripts/. A build without the compiler loads
// the .luauc files that --emit-bytecode wrote instead.
internal static unsafe class Scripts
{
    internal static string Folder = Path.Combine(AppContext.BaseDirectory, "scripts");

    internal static byte[] ReadSource(string name) => File.ReadAllBytes(Path.Combine(Folder, name + ".luau"));

    internal static byte[] ReadBytecode(string name)
    {
#if LUAU_COMPILER
        return Compile(ReadSource(name));
#else
        return File.ReadAllBytes(Path.Combine(Folder, name + ".luauc"));
#endif
    }

#if LUAU_COMPILER
    // A failed compile still returns bytecode: it starts with a zero byte and
    // carries the message, which luau_load then reports.
    internal static byte[] Compile(ReadOnlySpan<byte> source, int optimizationLevel = 1, int debugLevel = 1, int typeInfoLevel = 0)
    {
        fixed (byte* p = source)
        {
            nuint size;
            byte* bytecode = Shim.sl_compile(p, (nuint)source.Length, optimizationLevel, debugLevel, typeInfoLevel, &size);
            try
            {
                return new ReadOnlySpan<byte>(bytecode, (int)size).ToArray();
            }
            finally
            {
                // The buffer came from the library's malloc. Only its free may release it.
                Shim.sl_free(bytecode);
            }
        }
    }
#endif

    // Pushes the chunk as a function. On failure pushes the message and returns false.
    internal static bool LoadBytecode(nint L, string chunkName, ReadOnlySpan<byte> bytecode)
    {
        Span<byte> name = stackalloc byte[160];
        int length = Encoding.UTF8.GetBytes("=" + chunkName, name[..^1]);
        name[length] = 0;

        fixed (byte* namePtr = name)
        fixed (byte* data = bytecode)
            return Lua.luau_load(L, namePtr, data, (nuint)bytecode.Length, 0) == 0;
    }

    internal static void Load(nint L, string name)
    {
        if (LoadBytecode(L, name, ReadBytecode(name)))
            return;

        string? message = LuaStack.ToText(L, -1);
        Lua.Pop(L, 1);
        throw new InvalidOperationException($"{name}: {message}");
    }

    // Runs a script that returns one value and keeps that value in the registry.
    internal static int LoadModule(nint L, string name)
    {
        Load(L, name);
        LuaStack.Call(L, 0, 1);
        int reference = Lua.lua_ref(L, -1);
        Lua.Pop(L, 1);
        return reference;
    }
}
