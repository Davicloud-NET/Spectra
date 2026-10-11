using System.Buffers.Text;
using System.Globalization;
using System.Text;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau;

/// <summary>
/// Compiles a script the way a level loads it and says where its first
/// mistake is. It opens no Luau state and keeps nothing between calls, so a
/// cook may ask from many threads at once.
/// </summary>
public sealed unsafe class LuauSyntaxCheck : IScriptSyntaxCheck
{
    // What LuaChunk compiles a level's script with. A script can pass at one
    // level and run out of registers at another, so the two must not differ.
    // SyntaxCheckSourceTests holds them together.
    private const int OptimizationLevel = 1;
    private const int DebugLevel = 1;

    private static ReadOnlySpan<byte> VectorLibrary => "Vector3\0"u8;

    private static ReadOnlySpan<byte> VectorConstructor => "new\0"u8;

    /// <inheritdoc/>
    public string CompilerVersion { get; } =
        "luau 0." + Lua.Release.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public string? Problem => LuauLibrary.FindProblem();

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The Luau library is missing. Ask <see cref="Problem"/> first.</exception>
    public ScriptSyntaxError? Check(ReadOnlySpan<byte> source)
    {
        if (Problem is { } problem)
            throw new InvalidOperationException(problem);

        // As LuauHost.Load does: a byte order mark is no part of the text.
        if (source.StartsWith(Encoding.UTF8.Preamble))
            source = source[Encoding.UTF8.Preamble.Length..];

        nuint size;
        byte* bytecode;
        fixed (byte* text = source)
        fixed (byte* library = VectorLibrary)
        fixed (byte* constructor = VectorConstructor)
        {
            bytecode = Shim.sl_compile(
                text, (nuint)source.Length, OptimizationLevel, DebugLevel, library, constructor, &size);
        }

        if (bytecode == null)
            return new ScriptSyntaxError(0, 0, "The Luau compiler failed on this script and said nothing more.");

        try
        {
            return MistakeIn(new ReadOnlySpan<byte>(bytecode, checked((int)size)));
        }
        finally
        {
            // The block came from the library's C runtime. Only sl_free may release it.
            Shim.sl_free(bytecode);
        }
    }

    // Luau compiles a script with a mistake too. What comes back then starts
    // with a zero where bytecode has its version, and the rest is the
    // message, ":12: what is wrong". It names a line and no column.
    private static ScriptSyntaxError? MistakeIn(ReadOnlySpan<byte> compiled)
    {
        if (compiled.Length > 0 && compiled[0] != 0)
            return null;

        ReadOnlySpan<byte> message = compiled.Length > 0 ? compiled[1..] : compiled;
        int line = 0;

        if (message.Length > 1 && message[0] == (byte)':'
            && Utf8Parser.TryParse(message[1..], out int parsed, out int digits)
            && parsed > 0
            && message.Length > 1 + digits && message[1 + digits] == (byte)':')
        {
            line = parsed;
            message = message[(2 + digits)..];
            if (message.StartsWith(" "u8))
                message = message[1..];
        }

        return new ScriptSyntaxError(line, 0, Encoding.UTF8.GetString(message));
    }
}
