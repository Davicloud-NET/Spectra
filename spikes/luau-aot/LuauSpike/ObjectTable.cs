using System;

namespace LuauSpike;

// Managed objects addressed by a small integer. Luau userdata stores the
// integer, this table stores the object.
internal sealed class ObjectTable
{
    private object?[] _slots = new object?[256];
    private int[] _free = new int[256];
    private int _freeCount;
    private int _next;

    internal int Count { get; private set; }

    internal int Add(object value)
    {
        int id = _freeCount > 0 ? _free[--_freeCount] : _next++;
        if (id == _slots.Length)
            Array.Resize(ref _slots, _slots.Length * 2);

        _slots[id] = value;
        Count++;
        return id;
    }

    internal object? Get(int id) => (uint)id < (uint)_slots.Length ? _slots[id] : null;

    internal void Release(int id)
    {
        if (_slots[id] is null)
            return;

        _slots[id] = null;
        if (_freeCount == _free.Length)
            Array.Resize(ref _free, _free.Length * 2);
        _free[_freeCount++] = id;
        Count--;
    }
}
