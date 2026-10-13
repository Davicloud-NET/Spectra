using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// Which node has which tag, for one play session. A node keeps its tags
// when it leaves the level's tree, and counts for a tag's list only while
// it is in it. Nothing here fires a signal or knows of Luau, so a level's
// own tags can be put in before any script runs.
internal sealed class TagTable
{
    private static readonly IReadOnlyList<TagNodes> None = [];

    private readonly Dictionary<SceneNode, List<TagNodes>> _ofNode = [];
    private readonly Dictionary<string, TagNodes> _tags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TagNodes>.AlternateLookup<ReadOnlySpan<char>> _byChars;

    // A node that gets a tag and loses it every tick takes its list from here.
    private readonly Stack<List<TagNodes>> _spare = [];
    private readonly TreeOrder _order = new();
    private int _moves;

    public TagTable() => _byChars = _tags.GetAlternateLookup<ReadOnlySpan<char>>();

    // How many nodes have a tag, in the level's tree or out of it.
    public int NodeCount => _ofNode.Count;

    // How many tags are kept. One no node has stays until the tags are
    // looked over, so a node that gets a tag and loses it makes it once.
    public int TagCount => _tags.Count;

    // How many times a tag's list had to be put in order.
    public int Orderings { get; private set; }

    // How many times a tag's list was cleared of the nodes that had left it.
    public int Tidyings { get; private set; }

    // The node's tags, in the order it got them.
    public IReadOnlyList<TagNodes> Of(SceneNode node) =>
        _ofNode.TryGetValue(node, out List<TagNodes>? tags) ? tags : None;

    public bool Has(SceneNode node, ReadOnlySpan<char> tag) =>
        _ofNode.TryGetValue(node, out List<TagNodes>? tags) && IndexOf(tags, tag) >= 0;

    // The tag, or null when it is not kept.
    public TagNodes? Find(ReadOnlySpan<char> tag) => _byChars.TryGetValue(tag, out TagNodes? found) ? found : null;

    public TagNodes FindOrMake(ReadOnlySpan<char> tag)
    {
        if (_byChars.TryGetValue(tag, out TagNodes? found))
            return found;

        var made = new TagNodes(tag.ToString());
        _tags.Add(made.Tag, made);
        return made;
    }

    // Null when the node has the tag already.
    public TagNodes? Add(SceneNode node, ReadOnlySpan<char> tag, bool isInLevel)
    {
        if (!_ofNode.TryGetValue(node, out List<TagNodes>? tags))
        {
            tags = _spare.Count > 0 ? _spare.Pop() : [];
            _ofNode.Add(node, tags);
        }
        else if (IndexOf(tags, tag) >= 0)
        {
            return null;
        }

        TagNodes added = FindOrMake(tag);
        tags.Add(added);
        added.Holders++;
        if (isInLevel)
            Put(added, node);

        return added;
    }

    // Null when the node does not have the tag.
    public TagNodes? Remove(SceneNode node, ReadOnlySpan<char> tag, out bool wasInLevel)
    {
        wasInLevel = false;
        if (!_ofNode.TryGetValue(node, out List<TagNodes>? tags))
            return null;

        int index = IndexOf(tags, tag);
        if (index < 0)
            return null;

        TagNodes removed = tags[index];
        tags.RemoveAt(index);
        if (tags.Count == 0)
        {
            _ofNode.Remove(node);
            _spare.Push(tags);
        }

        wasInLevel = TakeOut(removed, node);
        removed.Holders--;
        return removed;
    }

    // The node came into the level's tree. Its tags are handed back, for
    // whoever fires their signals.
    public IReadOnlyList<TagNodes> Enter(SceneNode node)
    {
        IReadOnlyList<TagNodes> tags = Of(node);
        for (int i = 0; i < tags.Count; i++)
            Put(tags[i], node);

        return tags;
    }

    // The node left the level's tree. It keeps its tags.
    public IReadOnlyList<TagNodes> Leave(SceneNode node)
    {
        IReadOnlyList<TagNodes> tags = Of(node);
        for (int i = 0; i < tags.Count; i++)
            TakeOut(tags[i], node);

        return tags;
    }

    // A node moved inside the tree, so every list may be out of order.
    public void TreeMoved() => _moves++;

    // The tag's nodes in the order of the tree. The list is put in order
    // when it is asked for after a node joined it or the tree moved, and is
    // handed over as it is every other time.
    public IReadOnlyList<SceneNode> InOrder(TagNodes tag)
    {
        List<SceneNode> listed = tag.Listed;
        if (tag.HasJoined || tag.OrderedAtMove != _moves)
        {
            listed.Clear();
            foreach (SceneNode node in tag.InLevel)
                listed.Add(node);

            if (listed.Count > 1)
            {
                _order.Sort(listed);
                Orderings++;
            }
        }
        else if (tag.HasLeft)
        {
            Tidy(tag);
        }

        tag.HasJoined = false;
        tag.HasLeft = false;
        tag.OrderedAtMove = _moves;
        return listed;
    }

    // Lets go of the tags no node has and no script asked a signal of.
    // Hands back how many more no node has and no listener waits on: those
    // are kept for signals a script may hold and nothing else.
    public int DropUnused()
    {
        // A dictionary may lose entries while it is walked.
        int unheard = 0;
        foreach (TagNodes tag in _tags.Values)
        {
            if (tag.Holders > 0)
                continue;

            if (!tag.HasSignal)
                _tags.Remove(tag.Tag);
            else if (!tag.IsHeard)
                unheard++;
        }

        return unheard;
    }

    // Lets go of the tags no node has, no listener waits on and no script
    // holds a signal of. A script that asks again gets new signals, and
    // cannot tell.
    public void DropUnheard(Func<ScriptSignal, bool> isHeld)
    {
        foreach (TagNodes tag in _tags.Values)
        {
            if (tag.Holders > 0 || tag.IsHeard)
                continue;

            bool isAddedHeld = tag.Added is { } added && isHeld(added);
            bool isRemovedHeld = tag.Removed is { } removed && isHeld(removed);
            if (!isAddedHeld && !isRemovedHeld)
                _tags.Remove(tag.Tag);
        }
    }

    public void Clear()
    {
        foreach (TagNodes tag in _tags.Values)
        {
            tag.InLevel.Clear();
            tag.Listed.Clear();
        }

        _ofNode.Clear();
        _tags.Clear();
        _spare.Clear();
    }

    private static void Put(TagNodes tag, SceneNode node)
    {
        if (tag.InLevel.Add(node))
            tag.HasJoined = true;
    }

    // The list keeps the node until it is asked for. So taking a tag off
    // every node of a large tag is paid once, and not once for each node.
    private static bool TakeOut(TagNodes tag, SceneNode node)
    {
        if (!tag.InLevel.Remove(node))
            return false;

        tag.HasLeft = tag.InLevel.Count > 0;
        if (!tag.HasLeft)
        {
            tag.Listed.Clear();
            tag.HasJoined = false;
        }

        return true;
    }

    // Taking nodes out leaves the rest in the order they were in.
    private void Tidy(TagNodes tag)
    {
        List<SceneNode> listed = tag.Listed;
        int kept = 0;
        for (int i = 0; i < listed.Count; i++)
        {
            if (tag.InLevel.Contains(listed[i]))
                listed[kept++] = listed[i];
        }

        listed.RemoveRange(kept, listed.Count - kept);
        Tidyings++;
    }

    private static int IndexOf(List<TagNodes> tags, ReadOnlySpan<char> tag)
    {
        for (int i = 0; i < tags.Count; i++)
        {
            if (tag.SequenceEqual(tags[i].Tag))
                return i;
        }

        return -1;
    }
}
