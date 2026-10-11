using System.Globalization;
using System.Text;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau;

// Turns what Luau knows about a line or an error into a ScriptMessage.
internal static unsafe class ScriptReport
{
    // A script that recursed until the stack ran out has 20000 frames. The
    // ones nearest to the error are kept, and the error says how many are not.
    private const int MostFrames = 32;

    private const int ScriptFrameKind = 1;

    // A print or a warning. Its position is the script function that called.
    internal static ScriptMessage Line(nint L, LuauScript? script, ScriptSeverity severity, string text)
    {
        for (int level = 0; ; level++)
        {
            byte* source;
            byte* name;
            int line;
            int kind = Shim.sl_frame(L, level, &source, &name, &line);
            if (kind == 0)
                return new ScriptMessage(severity, text, script?.Name ?? string.Empty, 0);
            if (kind == ScriptFrameKind)
                return new ScriptMessage(severity, text, NameOf(source, script), line);
        }
    }

    // A thread that ended in an error. The value is on top of its stack and
    // its frames are still there.
    internal static ScriptMessage Failure(nint thread, nint main, LuauScript? script)
    {
        ScriptFrame[] stack = Frames(thread, script, out int leftOut);
        string text = ErrorText(thread, main);

        // The message names the position itself, and error(message, 2) names
        // the caller's. So the position is read from the message.
        foreach (ScriptFrame frame in stack)
        {
            if (TrySplit(text, frame.Script, out int line, out string rest))
                return new ScriptMessage(ScriptSeverity.Error, WithCut(rest, leftOut), frame.Script, line, stack);
        }

        return stack.Length > 0
            ? new ScriptMessage(ScriptSeverity.Error, WithCut(text, leftOut), stack[0].Script, stack[0].Line, stack)
            : new ScriptMessage(ScriptSeverity.Error, text, script?.Name ?? string.Empty, 0);
    }

    // Luau reports a mistake in a script as "name:line: what is wrong".
    internal static ScriptMessage CompileError(string script, string text) =>
        TrySplit(text, script, out int line, out string rest)
            ? new ScriptMessage(ScriptSeverity.Error, rest, script, line)
            : new ScriptMessage(ScriptSeverity.Error, text, script, 0);

    // The script functions nearest to the error, and how many more there
    // are further out.
    private static ScriptFrame[] Frames(nint thread, LuauScript? script, out int leftOut)
    {
        leftOut = 0;
        List<ScriptFrame> frames = [];
        for (int level = 0; ; level++)
        {
            byte* source;
            byte* name;
            int line;
            int kind = Shim.sl_frame(thread, level, &source, &name, &line);
            if (kind == 0)
                break;
            if (kind != ScriptFrameKind)
                continue;

            if (frames.Count == MostFrames)
            {
                leftOut++;
                continue;
            }

            string? function = name == null ? null : Encoding.UTF8.GetString(LuaText.FromNative(name));
            frames.Add(new ScriptFrame(NameOf(source, script), line, function));
        }

        return [.. frames];
    }

    // The calls that show under an error would read as all there were. So an
    // error with more of them says how many are missing.
    private static string WithCut(string text, int leftOut)
    {
        if (leftOut == 0)
            return text;

        string more = leftOut == 1
            ? "1 more call is not shown."
            : $"{leftOut.ToString(CultureInfo.InvariantCulture)} more calls are not shown.";

        if (text.Length == 0)
            return more;

        return text[^1] is '.' or '!' or '?' ? $"{text} {more}" : $"{text}. {more}";
    }

    // The chunk's name without the character Luau puts in front. Most frames
    // are the script's own, and those reuse its name.
    private static string NameOf(byte* source, LuauScript? script)
    {
        ReadOnlySpan<byte> name = LuaText.FromNative(source);
        if (name.Length > 0 && name[0] is (byte)'@' or (byte)'=')
            name = name[1..];

        return script is not null && name.SequenceEqual(script.NameUtf8) ? script.Name : Encoding.UTF8.GetString(name);
    }

    private static string ErrorText(nint thread, nint main)
    {
        if (LuaText.Read(thread, -1) is { } message)
            return message;
        if (Lua.lua_gettop(thread) == 0)
            return "the script failed and left no error value";

        // error() was given something else than text. Turning it into text can
        // run a script's __tostring, which only a thread that is whole can do.
        if (Shim.sl_xmove(thread, main, 1) != LuaStatus.Ok)
        {
            Shim.sl_pop(thread, 1);
            return "the script failed with a value that could not be read";
        }

        string text = Shim.sl_tostring(main, -1) == LuaStatus.Ok && LuaText.Read(main, -1) is { } described
            ? described
            : "the script failed with a value that has no text";
        Shim.sl_pop(main, 2);
        return text;
    }

    // "name:12: rest" into 12 and "rest".
    private static bool TrySplit(string text, string script, out int line, out string rest)
    {
        line = 0;
        rest = text;
        if (script.Length == 0 || !text.StartsWith(script, StringComparison.Ordinal))
            return false;

        ReadOnlySpan<char> tail = text.AsSpan(script.Length);
        if (tail.Length < 2 || tail[0] != ':')
            return false;

        int end = tail[1..].IndexOf(':') + 1;
        if (end < 2 || !int.TryParse(tail[1..end], NumberStyles.None, CultureInfo.InvariantCulture, out line) || line <= 0)
            return false;

        ReadOnlySpan<char> after = tail[(end + 1)..];
        rest = (after.StartsWith(" ") ? after[1..] : after).ToString();
        return true;
    }
}
