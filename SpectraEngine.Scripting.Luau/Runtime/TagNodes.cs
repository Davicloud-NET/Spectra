using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// One tag: the nodes of the level's tree that have it, and the two signals
// CollectionService hands out for it, each made when a script first asks.
// TagTable keeps all of it.
internal sealed class TagNodes(string tag)
{
    public string Tag { get; } = tag;

    // How many nodes have the tag, in the level's tree or out of it.
    public int Holders { get; set; }

    // The nodes of the level's tree that have the tag.
    public HashSet<SceneNode> InLevel { get; } = [];

    // The same nodes as GetTagged hands them out. A node that got the tag
    // is missing and one that lost it is still there until the list is
    // asked for, which is what the two flags say.
    public List<SceneNode> Listed { get; } = [];

    public bool HasJoined { get; set; }

    public bool HasLeft { get; set; }

    // The count of moves in the tree the order was made at.
    public int OrderedAtMove { get; set; }

    public ScriptSignal? Added { get; set; }

    public ScriptSignal? Removed { get; set; }

    public bool HasSignal => Added is not null || Removed is not null;

    // Whether a listener waits on one of the signals.
    public bool IsHeard => Added is { ListenerCount: > 0 } || Removed is { ListenerCount: > 0 };
}
