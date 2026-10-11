using System;

namespace SpectraEngine.Core.Entities;

// Min-heap ordered by (Tick, Sequence). Not PriorityQueue: without the
// sequence tiebreak, the order of events due on one tick depends on the
// insertion pattern.
internal sealed class EntityEventQueue
{
    private EntityEvent[] _heap = new EntityEvent[16];
    private int _count;

    public int Count => _count;

    public void Clear()
    {
        // Entries hold entity references; don't keep them alive.
        Array.Clear(_heap, 0, _count);
        _count = 0;
    }

    public void Push(in EntityEvent item)
    {
        if (_count == _heap.Length)
            Array.Resize(ref _heap, _heap.Length * 2);

        int index = _count++;
        _heap[index] = item;

        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (Precedes(_heap[parent], _heap[index]))
                break;

            (_heap[parent], _heap[index]) = (_heap[index], _heap[parent]);
            index = parent;
        }
    }

    public bool TryPeek(out EntityEvent item)
    {
        if (_count == 0)
        {
            item = default;
            return false;
        }

        item = _heap[0];
        return true;
    }

    public bool TryPop(out EntityEvent item)
    {
        if (!TryPeek(out item))
            return false;

        _count--;
        _heap[0] = _heap[_count];
        _heap[_count] = default;

        int index = 0;
        while (true)
        {
            int left = (index * 2) + 1;
            if (left >= _count)
                break;

            int smallest = left;
            int right = left + 1;
            if (right < _count && Precedes(_heap[right], _heap[left]))
                smallest = right;

            if (Precedes(_heap[index], _heap[smallest]))
                break;

            (_heap[index], _heap[smallest]) = (_heap[smallest], _heap[index]);
            index = smallest;
        }

        return true;
    }

    public static bool Precedes(in EntityEvent a, in EntityEvent b) =>
        a.Tick != b.Tick ? a.Tick < b.Tick : a.Sequence < b.Sequence;
}
