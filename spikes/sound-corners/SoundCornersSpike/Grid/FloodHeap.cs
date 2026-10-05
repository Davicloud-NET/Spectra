using System;

namespace SoundCornersSpike.Grid;

// A binary min-heap of (key, cell). Stale entries are left in and skipped by
// the caller when they come out.
internal sealed class FloodHeap
{
    private float[] _keys = new float[4096];
    private int[] _cells = new int[4096];

    public int Count { get; private set; }

    public long Pushes { get; private set; }

    public int Peak { get; private set; }

    public void Clear()
    {
        Count = 0;
        Pushes = 0;
        Peak = 0;
    }

    public void Push(float key, int cell)
    {
        if (Count == _keys.Length)
        {
            Array.Resize(ref _keys, Count * 2);
            Array.Resize(ref _cells, Count * 2);
        }

        int i = Count++;
        Pushes++;
        if (Count > Peak) Peak = Count;

        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            if (_keys[parent] <= key) break;

            _keys[i] = _keys[parent];
            _cells[i] = _cells[parent];
            i = parent;
        }

        _keys[i] = key;
        _cells[i] = cell;
    }

    public int Pop(out float key)
    {
        key = _keys[0];
        int cell = _cells[0];

        int last = --Count;
        if (last > 0)
        {
            float moved = _keys[last];
            int movedCell = _cells[last];
            int i = 0;

            while (true)
            {
                int child = (2 * i) + 1;
                if (child >= last) break;
                if (child + 1 < last && _keys[child + 1] < _keys[child]) child++;
                if (moved <= _keys[child]) break;

                _keys[i] = _keys[child];
                _cells[i] = _cells[child];
                i = child;
            }

            _keys[i] = moved;
            _cells[i] = movedCell;
        }

        return cell;
    }
}
