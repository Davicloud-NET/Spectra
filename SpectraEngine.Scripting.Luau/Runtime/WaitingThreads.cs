namespace SpectraEngine.Scripting.Luau.Runtime;

// The threads that wait, each under a token. For a tick, the entity queue
// holds the token and hands it back when the wait is over. For a signal, the
// signal's listener holds it. A thread waits for one thing at a time: a new
// wait replaces the one before, whose token then names nothing.
internal sealed class WaitingThreads
{
    // Luau's own words for it. A wait says them before it is kept.
    public const string CannotYield = "attempt to yield across metamethod/C-call boundary";

    private readonly Dictionary<LuauThread, int> _slotOf = [];
    private Slot[] _slots = new Slot[32];
    private int _used;
    private int _firstFree = -1;

    public int Count => _slotOf.Count;

    public long Add(in WaitingThread waiting)
    {
        Remove(waiting.Thread);

        int index = _firstFree;
        if (index >= 0)
            _firstFree = _slots[index].NextFree;
        else
        {
            if (_used == _slots.Length)
                Array.Resize(ref _slots, _used * 2);
            index = _used++;
        }

        _slots[index].Waiting = waiting;
        _slots[index].IsWaiting = true;
        _slotOf.Add(waiting.Thread, index);
        return ((long)_slots[index].Generation << 32) | (uint)index;
    }

    // Takes the wait a token names out of the table. False for a wait that
    // was replaced or removed since the token was made.
    public bool TryTake(long token, out WaitingThread waiting)
    {
        int index = (int)(uint)token;
        if ((uint)index >= (uint)_used || !_slots[index].IsWaiting || _slots[index].Generation != (uint)(token >> 32))
        {
            waiting = default;
            return false;
        }

        waiting = _slots[index].Waiting;
        Free(index);
        return true;
    }

    // Ends a thread's wait, for one that is cancelled or resumed by a script.
    public void Remove(LuauThread thread)
    {
        if (!_slotOf.TryGetValue(thread, out int index))
            return;

        _slots[index].Waiting.Arguments?.Dispose();
        Free(index);
    }

    // Whether a token still names a wait, for a wake that may come more than once.
    public bool Holds(long token)
    {
        int index = (int)(uint)token;
        return (uint)index < (uint)_used && _slots[index].IsWaiting && _slots[index].Generation == (uint)(token >> 32);
    }

    // The generation moves on, so the old token matches nothing again.
    private void Free(int index)
    {
        ref Slot slot = ref _slots[index];
        _slotOf.Remove(slot.Waiting.Thread);

        slot.Waiting = default;
        slot.IsWaiting = false;
        slot.Generation++;
        slot.NextFree = _firstFree;
        _firstFree = index;
    }

    private struct Slot
    {
        public WaitingThread Waiting;
        public uint Generation;
        public int NextFree;
        public bool IsWaiting;
    }
}
