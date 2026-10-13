namespace SpectraEngine.Core.Entities;

// One of the dispatcher's two queues, with its own share of a drain and its
// own room. What entities send waits in one. What scripts queue waits in
// the other: a thread's wake, an input one sent and a listener's call.
internal sealed class EntityEventLane
{
    private readonly EntityEventQueue _queue = new();

    public int Count => _queue.Count;

    // What the running drain has sent of the events that were waiting, and
    // of those it made itself.
    public int Waited { get; set; }

    public int MadeHere { get; set; }

    // False once the lane has used up its share of the running drain.
    public bool IsOpen { get; set; }

    // Falling behind loses nothing, so it is logged once a level. A full
    // queue is logged each time it fills.
    public bool ReportedBehind { get; set; }

    public bool ReportedFull { get; set; }

    public void Clear()
    {
        _queue.Clear();
        ReportedBehind = false;
        ReportedFull = false;
    }

    public void BeginDrain(int room)
    {
        Waited = 0;
        MadeHere = 0;
        IsOpen = true;

        if (_queue.Count <= room / 2)
            ReportedFull = false;
    }

    public void Push(in EntityEvent made) => _queue.Push(made);

    public bool TryPeek(out EntityEvent next) => _queue.TryPeek(out next);

    public bool TryPop(out EntityEvent due) => _queue.TryPop(out due);

    // The next event, when the lane may still send in this drain and the
    // event is due.
    public bool TryPeekDue(long tick, out EntityEvent next)
    {
        next = default;
        return IsOpen && _queue.TryPeek(out next) && next.Tick <= tick;
    }
}
