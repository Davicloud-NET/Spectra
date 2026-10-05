using SpectraEngine.Core.Scene;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

// The transform each node had before a running level first moved it, so
// stopping can put the level back.
internal sealed class MovedNodeJournal
{
    private readonly List<Entry> _entries = [];

    // Membership only. The list is the order.
    private readonly HashSet<SceneNode> _recorded = new(ReferenceEqualityComparer.Instance);

    public int Count => _entries.Count;

    // Call before every write. Only the first one for a node records.
    public void Record(SceneNode node)
    {
        if (_recorded.Add(node))
            _entries.Add(new Entry(node, node.LocalTransform));
    }

    // Newest first. A node that never left its authored transform takes the
    // setter's early-out, and a part subtree dirties no compile.
    public void Restore()
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
            _entries[i].Node.LocalTransform = _entries[i].Authored;

        _entries.Clear();
        _recorded.Clear();
    }

    private readonly record struct Entry(SceneNode Node, Transform Authored);
}
