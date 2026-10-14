using System.Runtime.CompilerServices;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The tags of destroyed nodes. A destroyed node keeps its tags and takes
// more, as in Roblox. It is held weakly: its tags last as long as anything
// can hand the node to a script, and go with the node. It is in no tag's
// list and counts as no holder of one.
internal sealed class DestroyedTags
{
    private readonly ConditionalWeakTable<SceneNode, List<TagNodes>> _ofNode = new();

    // How many destroyed nodes a tag is still kept for. It walks them all,
    // so it is for a test to ask.
    public int Count
    {
        get
        {
            int count = 0;
            foreach (KeyValuePair<SceneNode, List<TagNodes>> kept in _ofNode)
            {
                if (kept.Value.Count > 0)
                    count++;
            }

            return count;
        }
    }

    public List<TagNodes>? Of(SceneNode node) =>
        TreeWrites.IsDestroyed(node) && _ofNode.TryGetValue(node, out List<TagNodes>? tags) ? tags : null;

    // The node was destroyed with these tags.
    public void Keep(SceneNode node, List<TagNodes> tags) => _ofNode.AddOrUpdate(node, tags);

    // The tag a destroyed node gets is the kept one when there is one, and
    // one of its own otherwise, which no list and no signal knows. Null when
    // the node has it already.
    public TagNodes? Add(SceneNode node, ReadOnlySpan<char> tag, TagNodes? kept)
    {
        List<TagNodes> tags = _ofNode.GetValue(node, static _ => []);
        if (TagNodes.IndexOf(tags, tag) >= 0)
            return null;

        TagNodes added = kept ?? new TagNodes(tag.ToString());
        tags.Add(added);
        return added;
    }

    // Null when the node does not have the tag.
    public TagNodes? Remove(SceneNode node, ReadOnlySpan<char> tag)
    {
        if (Of(node) is not { } tags)
            return null;

        int index = TagNodes.IndexOf(tags, tag);
        if (index < 0)
            return null;

        TagNodes removed = tags[index];
        tags.RemoveAt(index);
        return removed;
    }

    public void Clear() => _ofNode.Clear();
}
