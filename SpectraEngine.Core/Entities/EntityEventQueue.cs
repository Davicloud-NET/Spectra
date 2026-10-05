using System;

namespace SpectraEngine.Core.Entities;

internal enum EntityEventKind : byte
{
    Think = 0,

    // Delivered to every entity the target name resolves to.
    Input = 1,
}

// Thinks and inputs share one record and one queue. Two queues would order
// equal-time events by which queue drained first.
internal struct EntityEvent
{
    public float Time;

    // Tiebreak: equal times dispatch in the order they were scheduled.
    public long Sequence;

    public EntityEventKind Kind;

    // The thinking entity, or the entity whose output fired. Null for an
    // input nothing in the world sent.
    public Entity? Entity;

    // The one instance an input is for. Null delivers by TargetName.
    public Entity? Target;

    // A think whose serial no longer matches the entity's was superseded and
    // is dropped.
    public int ThinkSerial;

    public Entity? Activator;
    public string TargetName;
    public string Input;
    public string Parameter;

    // Only for the budget message.
    public string Output;

    // The wire's index in its sender's connection list, plus one. Zero is a
    // think or an input no wire sent, so an event built without it is not
    // taken for wire zero.
    public int WireOrdinal;
}

// Min-heap ordered by (Time, Sequence). Not PriorityQueue: without the
// sequence tiebreak, equal-time order depends on the insertion pattern.
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

    private static bool Precedes(in EntityEvent a, in EntityEvent b) =>
        a.Time != b.Time ? a.Time < b.Time : a.Sequence < b.Sequence;
}
