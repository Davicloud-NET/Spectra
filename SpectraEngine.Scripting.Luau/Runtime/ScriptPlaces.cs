using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// Says which script called a host function and from which line, for a view
// that draws what scripts hear and send. A script is the node its thread
// was started for, also when the line is in a module. A file's name is made
// once and handed out again, so asking makes nothing in a level that has run
// for a while.
internal sealed class ScriptPlaces(LuauHost host)
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    public ScriptPlace Of(LuauStack stack)
    {
        LuauScript? script = host.ScriptOf(stack);
        ReadOnlySpan<byte> file = ScriptReport.CallerFile(stack.State, out int line);
        return new ScriptPlace(NodeOf(script), FileName(file, script), line);
    }

    // Empty for a script no node was named for, such as a module nothing in
    // the level asked for.
    public static Guid NodeOf(LuauScript? script) => script?.Owner is SceneNode node ? node.Id : Guid.Empty;

    // No script function called: the thread began with the host function,
    // and all there is to say is whose thread it is.
    private string FileName(ReadOnlySpan<byte> utf8, LuauScript? script)
    {
        if (utf8.IsEmpty)
            return script?.Name ?? string.Empty;
        if (script is not null && utf8.SequenceEqual(script.NameUtf8))
            return script.Name;

        Span<char> room = stackalloc char[NameChars.Room];
        if (!NameChars.TryRead(utf8, room, out ReadOnlySpan<char> name))
            return string.Empty;

        Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> known =
            _files.GetAlternateLookup<ReadOnlySpan<char>>();
        if (known.TryGetValue(name, out string? made))
            return made;

        made = name.ToString();
        _files.Add(made, made);
        return made;
    }
}
