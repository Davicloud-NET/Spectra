using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// One tag: the nodes of the level's tree that have it, and the two signals
// CollectionService hands out for it, each made when a script first asks.
// A tag stays once it is made, since a script may hold its signals.
internal sealed class TagNodes(string tag)
{
    public string Tag { get; } = tag;

    // In the tree's order while IsInOrder says so. TagTable keeps both.
    public List<SceneNode> InLevel { get; } = [];

    public bool IsInOrder { get; set; } = true;

    // The count of moves in the tree the order was made at.
    public int OrderedAtMove { get; set; }

    public ScriptSignal? Added { get; set; }

    public ScriptSignal? Removed { get; set; }
}
