using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// One module of a play session: a file that ran once, and what came of it.
internal sealed class ScriptModule(string path, SceneNode? node)
{
    // Its file, as a content path.
    public string Path { get; } = path;

    // The node it was asked for by, which is its global script. Null for one
    // that was asked for by path.
    public SceneNode? Node { get; } = node;

    // What it returned. Null while it runs and when it failed.
    public LuauReference? Value { get; set; }

    // Why it returned nothing, with its file and line in front.
    public string Failure { get; set; } = $"{path}: the module did not run to its end";
}
