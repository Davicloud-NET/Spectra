using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SpectraEngine.Core.Entities;

// The first value of everything a running level changed on a node, so
// stopping can put the level back. An entity's move and a script's write
// record in the same one.
internal sealed class PlayJournal
{
    private readonly List<Entry> _entries = [];

    // Membership only. The list is the order.
    private readonly HashSet<Key> _recorded = new(new KeyComparer());

    public int Count => _entries.Count;

    // How many nodes hold a first value of this kind.
    public int CountOf<T>(JournalKind<T> kind)
    {
        int count = 0;
        for (int i = 0; i < _entries.Count; i++)
        {
            if (ReferenceEquals(_entries[i].Kind, kind))
                count++;
        }

        return count;
    }

    // Call before every write. Only the first one for a node and a kind
    // records. A later one is a lookup and allocates nothing.
    public void Record<T>(SceneNode node, JournalKind<T> kind)
    {
        if (_recorded.Add(new Key(node, kind)))
            _entries.Add(new FirstValue<T>(node, kind, kind.Read(node)));
    }

    // Newest first. A value that is already back raises no event and
    // dirties nothing.
    public void Restore()
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
            _entries[i].PutBack();

        _entries.Clear();
        _recorded.Clear();
    }

    private readonly record struct Key(SceneNode Node, object Kind);

    // By reference: two nodes are never one entry, whatever they hold.
    private sealed class KeyComparer : IEqualityComparer<Key>
    {
        public bool Equals(Key x, Key y) => ReferenceEquals(x.Node, y.Node) && ReferenceEquals(x.Kind, y.Kind);

        public int GetHashCode(Key key) =>
            (RuntimeHelpers.GetHashCode(key.Node) * 397) ^ RuntimeHelpers.GetHashCode(key.Kind);
    }

    private abstract class Entry(object kind)
    {
        public object Kind { get; } = kind;

        public abstract void PutBack();
    }

    private sealed class FirstValue<T>(SceneNode node, JournalKind<T> kind, T first) : Entry(kind)
    {
        public override void PutBack() => kind.PutBack(node, first);
    }
}
