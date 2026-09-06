using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace SpectraEngine.Core.Bsp;

/// <summary>Immutable placement storage plus authored order. Slots never shift on edits.</summary>
internal sealed class PlacementSnapshot : IReadOnlyList<BrushPlacement>
{
    internal readonly record struct Entry(Guid Identity, int Slot, UInt128 Order, BrushPlacement Placement);
    internal readonly record struct Change(int Slot, Entry? Before, Entry? After);
    private static long _nextId;
    internal long Id { get; } = Interlocked.Increment(ref _nextId);
    internal long ParentId { get; }
    internal PagedArray<Entry?> Slots { get; }
    internal OrderNode? Root { get; }
    internal IReadOnlyList<Change> Changes { get; }
    internal PlacementSlotView Storage { get; }

    internal PlacementSnapshot(PagedArray<Entry?> slots, OrderNode? root, long parentId, Change[] changes)
    { Slots = slots; Root = root; ParentId = parentId; Changes = changes; Storage = new(this); }

    public int Count => OrderNode.Size(Root);
    public BrushPlacement this[int index] => EntryAt(index).Placement;
    internal Entry EntryAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
        var node = Root!;
        while (true)
        {
            int left = OrderNode.Size(node.Left);
            if (index == left) return node.Value;
            if (index < left) node = node.Left!;
            else { index -= left + 1; node = node.Right!; }
        }
    }

    internal int DenseIndex(int slot)
    {
        var entry = Slots[slot] ?? throw new InvalidOperationException("A removed placement has no authored index.");
        int rank = 0;
        for (var node = Root; node is not null;)
        {
            if (entry.Order == node.Value.Order) return rank + OrderNode.Size(node.Left);
            if (entry.Order < node.Value.Order) node = node.Left;
            else { rank += OrderNode.Size(node.Left) + 1; node = node.Right; }
        }
        throw new InvalidOperationException("Placement order and storage disagree.");
    }

    internal BrushPlacement[] ToDense(out int[] slots)
    {
        var placements = new BrushPlacement[Count];
        var orderedSlots = new int[Count];
        int index = 0;
        Fill(Root);
        slots = orderedSlots;
        return placements;
        void Fill(OrderNode? node)
        {
            if (node is null) return;
            Fill(node.Left);
            placements[index] = node.Value.Placement;
            orderedSlots[index++] = node.Value.Slot;
            Fill(node.Right);
        }
    }

    public IEnumerator<BrushPlacement> GetEnumerator()
    {
        var stack = new Stack<OrderNode>();
        var node = Root;
        while (node is not null || stack.Count > 0)
        {
            while (node is not null) { stack.Push(node); node = node.Left; }
            node = stack.Pop();
            yield return node.Value.Placement;
            node = node.Right;
        }
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // Persistent AVL tree. Old worlds retain untouched subtrees by reference.
    internal sealed class OrderNode(Entry value, OrderNode? left = null, OrderNode? right = null)
    {
        internal readonly Entry Value = value;
        internal readonly OrderNode? Left = left, Right = right;
        internal readonly int Count = 1 + Size(left) + Size(right);
        internal readonly int Height = 1 + Math.Max(H(left), H(right));
        internal static int Size(OrderNode? node) => node?.Count ?? 0;
        private static int H(OrderNode? node) => node?.Height ?? 0;

        internal static OrderNode Put(OrderNode? node, Entry entry)
        {
            if (node is null) return new(entry);
            if (entry.Order == node.Value.Order) return new(entry, node.Left, node.Right);
            return Balance(entry.Order < node.Value.Order
                ? new(node.Value, Put(node.Left, entry), node.Right)
                : new(node.Value, node.Left, Put(node.Right, entry)));
        }

        internal static OrderNode? Remove(OrderNode? node, UInt128 key)
        {
            if (node is null) return null;
            if (key < node.Value.Order) return Balance(new(node.Value, Remove(node.Left, key), node.Right));
            if (key > node.Value.Order) return Balance(new(node.Value, node.Left, Remove(node.Right, key)));
            if (node.Left is null) return node.Right;
            if (node.Right is null) return node.Left;
            var successor = node.Right;
            while (successor.Left is not null) successor = successor.Left;
            return Balance(new(successor.Value, node.Left, Remove(node.Right, successor.Value.Order)));
        }

        internal static UInt128 Successor(OrderNode? node, UInt128 key)
        {
            UInt128 result = UInt128.MaxValue;
            while (node is not null)
            {
                if (node.Value.Order > key) { result = node.Value.Order; node = node.Left; }
                else node = node.Right;
            }
            return result;
        }

        internal static Entry? Predecessor(OrderNode? node, UInt128 key)
        {
            Entry? result = null;
            while (node is not null)
            {
                if (node.Value.Order < key) { result = node.Value; node = node.Right; }
                else node = node.Left;
            }
            return result;
        }

        private static OrderNode Balance(OrderNode node)
        {
            if (H(node.Left) - H(node.Right) > 1)
            {
                if (H(node.Left!.Left) < H(node.Left.Right))
                    node = new(node.Value, RotateLeft(node.Left), node.Right);
                return RotateRight(node);
            }
            if (H(node.Right) - H(node.Left) > 1)
            {
                if (H(node.Right!.Right) < H(node.Right.Left))
                    node = new(node.Value, node.Left, RotateRight(node.Right));
                return RotateLeft(node);
            }
            return node;
        }
        private static OrderNode RotateLeft(OrderNode node) =>
            new(node.Right!.Value, new(node.Value, node.Left, node.Right.Left), node.Right.Right);
        private static OrderNode RotateRight(OrderNode node) =>
            new(node.Left!.Value, node.Left.Left, new(node.Value, node.Left.Right, node.Right));
    }
}

/// <summary>Compiler-only slot view; a vacant slot has a default placement.</summary>
internal sealed class PlacementSlotView(PlacementSnapshot snapshot) : IReadOnlyList<BrushPlacement>, IComparer<int>
{
    internal PlacementSnapshot Snapshot { get; } = snapshot;
    public int Count => Snapshot.Slots.Count;
    public BrushPlacement this[int index] => Snapshot.Slots[index]?.Placement ?? default;
    public int Compare(int left, int right)
    {
        UInt128 a = Snapshot.Slots[left]?.Order ?? UInt128.MaxValue;
        UInt128 b = Snapshot.Slots[right]?.Order ?? UInt128.MaxValue;
        int order = a.CompareTo(b);
        return order != 0 ? order : left.CompareTo(right);
    }
    public IEnumerator<BrushPlacement> GetEnumerator()
    { for (int i = 0; i < Count; i++) yield return this[i]; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Render-thread journal; snapshots contain no scene-node references.</summary>
internal sealed class PlacementJournal
{
    private readonly Dictionary<Guid, PlacementSnapshot.Entry> _live = [];
    private readonly Dictionary<int, PlacementSnapshot.Entry?> _pending = [];
    private readonly Stack<int> _free = [];
    private PlacementSnapshot.OrderNode? _root;
    private int _slotCount;
    private PlacementSnapshot _snapshot = new(PagedArray<PlacementSnapshot.Entry?>.From([]), null, 0, []);
    private static readonly UInt128 OrderStep = (UInt128)1 << 64;
    internal int Count => _live.Count;
    internal bool Contains(Guid identity) => _live.ContainsKey(identity);

    internal void Set(Guid identity, BrushPlacement placement, Guid? after = null, bool reorder = false)
    {
        bool existing = _live.TryGetValue(identity, out var old);
        if (existing && reorder && PlacementSnapshot.OrderNode.Predecessor(_root, old.Order)?.Identity == after)
            reorder = false;
        if (existing && !reorder && old.Placement == placement) return;
        UInt128 order;
        if (existing && !reorder) order = old.Order;
        else
        {
            if (existing) _root = PlacementSnapshot.OrderNode.Remove(_root, old.Order);
            UInt128 lower = after is { } predecessor && _live.TryGetValue(predecessor, out var entry) ? entry.Order : 0;
            UInt128 upper = PlacementSnapshot.OrderNode.Successor(_root, lower);
            if (upper - lower <= 1)
            {
                Relabel();
                lower = after is { } prior && _live.TryGetValue(prior, out var relabeled) ? relabeled.Order : 0;
                upper = PlacementSnapshot.OrderNode.Successor(_root, lower);
            }
            order = upper == UInt128.MaxValue && lower < UInt128.MaxValue - OrderStep
                ? lower + OrderStep : lower + (upper - lower) / 2;
        }
        int slot = existing ? old.Slot : _free.Count > 0 ? _free.Pop() : _slotCount++;
        var next = new PlacementSnapshot.Entry(identity, slot, order, placement);
        _live[identity] = next;
        _pending[slot] = next;
        _root = PlacementSnapshot.OrderNode.Put(_root, next);
    }

    internal void Remove(Guid identity)
    {
        if (!_live.Remove(identity, out var old)) return;
        _root = PlacementSnapshot.OrderNode.Remove(_root, old.Order);
        _pending[old.Slot] = null;
        _free.Push(old.Slot);
    }

    internal PlacementSnapshot Capture()
    {
        if (_pending.Count == 0) return _snapshot;
        var replacements = new (int, PlacementSnapshot.Entry?)[_pending.Count];
        var changes = new PlacementSnapshot.Change[_pending.Count];
        int cursor = 0;
        foreach (var (slot, after) in _pending)
        {
            replacements[cursor] = (slot, after);
            changes[cursor++] = new(slot, slot < _snapshot.Slots.Count ? _snapshot.Slots[slot] : null, after);
        }
        Array.Sort(changes, static (a, b) => a.Slot.CompareTo(b.Slot));
        _snapshot = new(_snapshot.Slots.WithReplacements(_slotCount, replacements), _root, _snapshot.Id, changes);
        _pending.Clear();
        return _snapshot;
    }

    private void Relabel()
    {
        // Exhausting a 64-bit gap requires repeated insertion into the same
        // interval. Rekey without changing authored order; journal every slot.
        var ordered = new List<PlacementSnapshot.Entry>(_live.Count);
        Collect(_root);
        _root = null;
        UInt128 key = OrderStep;
        foreach (var entry in ordered)
        {
            var updated = entry with { Order = key };
            key += OrderStep;
            _live[entry.Identity] = updated;
            _pending[entry.Slot] = updated;
            _root = PlacementSnapshot.OrderNode.Put(_root, updated);
        }
        void Collect(PlacementSnapshot.OrderNode? node)
        {
            if (node is null) return;
            Collect(node.Left); ordered.Add(node.Value); Collect(node.Right);
        }
    }
}
