using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace SpectraEngine.Core.Entities;

// The first value of everything a running level changed on what outlives
// it, so stopping can put the level back. An entity's move and a script's
// write record in the same one. The owner is a node, or what is no node and
// still outlives the level, such as how the player moves.
internal sealed class PlayJournal
{
    private readonly List<Entry> _entries = [];

    // Membership only. The list is the order.
    private readonly HashSet<Key> _recorded = new(new KeyComparer());

    public int Count => _entries.Count;

    // What the level added to its tree, took from it and moved within it.
    public TreeJournal Tree { get; } = new();

    // How many owners hold a first value of this kind.
    public int CountOf<TOwner, T>(JournalKind<TOwner, T> kind)
        where TOwner : class
    {
        int count = 0;
        for (int i = 0; i < _entries.Count; i++)
        {
            if (ReferenceEquals(_entries[i].Kind, kind))
                count++;
        }

        return count;
    }

    // Call before every write. Only the first one for an owner and a kind
    // records. A later one is a lookup and allocates nothing.
    public void Record<TOwner, T>(TOwner owner, JournalKind<TOwner, T> kind)
        where TOwner : class
    {
        // A node the level made is gone at Stop, with all it holds. Kept
        // here, every part a level spawns would stay until then.
        if (owner is SceneNode { IsMadeByLevel: true })
            return;

        if (_recorded.Add(new Key(owner, kind)))
            _entries.Add(new FirstValue<TOwner, T>(owner, kind, kind.Read(owner)));
    }

    // Newest first, and then the tree. A value that is already back raises
    // no event and dirties nothing.
    public void Restore()
    {
        ExceptionDispatchInfo? first = PutValuesBack();

        try
        {
            // After the values: a node that comes back into the scene comes
            // back where it was built, and is indexed once.
            Tree.Restore();
        }
        catch (Exception failure)
        {
            // Thrown again below, unless a value failed before it.
            first ??= ExceptionDispatchInfo.Capture(failure);
        }

        first?.Throw();
    }

    private ExceptionDispatchInfo? PutValuesBack()
    {
        ExceptionDispatchInfo? first = null;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            try
            {
                _entries[i].PutBack();
            }
            catch (Exception failure)
            {
                // Thrown again below. The older values go back first, or
                // they stay in the level and the next save writes them.
                first ??= ExceptionDispatchInfo.Capture(failure);
            }
        }

        _entries.Clear();
        _recorded.Clear();
        return first;
    }

    private readonly record struct Key(object Owner, object Kind);

    // By reference: two owners are never one entry, whatever they hold.
    private sealed class KeyComparer : IEqualityComparer<Key>
    {
        public bool Equals(Key x, Key y) => ReferenceEquals(x.Owner, y.Owner) && ReferenceEquals(x.Kind, y.Kind);

        public int GetHashCode(Key key) =>
            (RuntimeHelpers.GetHashCode(key.Owner) * 397) ^ RuntimeHelpers.GetHashCode(key.Kind);
    }

    private abstract class Entry(object kind)
    {
        public object Kind { get; } = kind;

        public abstract void PutBack();
    }

    private sealed class FirstValue<TOwner, T>(TOwner owner, JournalKind<TOwner, T> kind, T first) : Entry(kind)
        where TOwner : class
    {
        public override void PutBack() => kind.PutBack(owner, first);
    }
}
