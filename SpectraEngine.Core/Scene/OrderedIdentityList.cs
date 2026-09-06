using System.Collections;
using System.Collections.Generic;

namespace SpectraEngine.Core.Scene;

/// <summary>Identity membership with stable attachment order and constant-time removal.
/// Dense indexing compacts tombstones once after a batch of changes. Render-thread owned.</summary>
internal sealed class OrderedIdentityList<T> : IReadOnlyList<T>, IComparer<T> where T : class
{
    private readonly Dictionary<T, (int Index, long Order)> _members = new(ReferenceEqualityComparer.Instance);
    private readonly List<T?> _slots = [];
    private long _nextOrder;
    private bool _hasHoles;

    public int Count => _members.Count;
    public T this[int index] { get { Compact(); return _slots[index]!; } }

    public bool Add(T item)
    {
        if (_members.ContainsKey(item)) return false;
        if (_hasHoles && _slots.Count > 1024 && _members.Count < _slots.Count / 2) Compact();
        _members.Add(item, (_slots.Count, _nextOrder++));
        _slots.Add(item);
        return true;
    }

    public bool Remove(T item)
    {
        if (!_members.Remove(item, out var entry)) return false;
        _slots[entry.Index] = null;
        _hasHoles = true;
        if (_members.Count == 0) { _slots.Clear(); _hasHoles = false; }
        return true;
    }

    public int Compare(T? x, T? y) => _members[x!].Order.CompareTo(_members[y!].Order);

    private void Compact()
    {
        if (!_hasHoles) return;
        int next = 0;
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i] is not { } item) continue;
            _slots[next] = item;
            _members[item] = (next++, _members[item].Order);
        }
        _slots.RemoveRange(next, _slots.Count - next);
        _hasHoles = false;
    }

    public IEnumerator<T> GetEnumerator()
    {
        foreach (T? item in _slots) if (item is not null) yield return item;
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
