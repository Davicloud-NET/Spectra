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

    // Stands in the list where a node was that left it, so the list keeps
    // no node alive that is in no level any more.
    public static readonly SceneNode Vacant = new("Vacant");

    // The nodes of the level's tree that have the tag, each with its place
    // in the list below, or -1 while it is not in it.
    public Dictionary<SceneNode, int> InLevel { get; } = [];

    // The same nodes as GetTagged hands them out. A node that got the tag
    // is missing, and where one was that lost it stands the vacant node,
    // until the list is asked for. That is what the two flags say.
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

    // Where the tag is in a node's list, or -1.
    public static int IndexOf(List<TagNodes> tags, ReadOnlySpan<char> tag)
    {
        for (int i = 0; i < tags.Count; i++)
        {
            if (tag.SequenceEqual(tags[i].Tag))
                return i;
        }

        return -1;
    }
}
