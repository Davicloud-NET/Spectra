using SpectraEngine.Core.Inspection;
using System;

namespace SpectraEngine.Core.Entities;

// The newest events of a running level, for a view's list of them.
internal sealed class LogicEventRing
{
    // How far back a line of a script is looked for before it is added again.
    private const int SameLineReach = 8;

    private readonly LogicEventInfo[] _held;
    private long _count;
    private long _nextNumber;

    public LogicEventRing(int capacity) => _held = new LogicEventInfo[capacity];

    public void Clear()
    {
        _count = 0;
        _nextNumber = 0;
    }

    // Adds an event as the newest and numbers it.
    public void Add(in LogicEventInfo happened)
    {
        _held[_count % _held.Length] = happened with { Number = _nextNumber++ };
        _count++;
    }

    // The same for what a script sent or heard. A script may do one thing
    // every tick, and listed each time it would push out everything else. So
    // a line that is among the newest already moves to the end, with its new
    // tick and a new number.
    public void AddOrRenew(in LogicEventInfo happened)
    {
        int reach = (int)Math.Min(Math.Min(_count, _held.Length), SameLineReach);
        for (int back = 1; back <= reach; back++)
        {
            long at = _count - back;
            if (!SaysTheSame(_held[at % _held.Length], happened))
                continue;

            for (long i = at; i < _count - 1; i++)
                _held[i % _held.Length] = _held[(i + 1) % _held.Length];

            _held[(_count - 1) % _held.Length] = happened with { Number = _nextNumber++ };
            return;
        }

        Add(happened);
    }

    // Oldest first.
    public LogicEventInfo[] Copy()
    {
        int held = (int)Math.Min(_count, _held.Length);
        var copy = new LogicEventInfo[held];
        long oldest = _count - held;

        for (int i = 0; i < held; i++)
            copy[i] = _held[(oldest + i) % _held.Length];

        return copy;
    }

    private static bool SaysTheSame(in LogicEventInfo a, in LogicEventInfo b) =>
        a.Kind == b.Kind
        && a.ScriptId == b.ScriptId
        && a.SourceId == b.SourceId
        && a.TargetId == b.TargetId
        && a.ScriptLine == b.ScriptLine
        && string.Equals(a.Output, b.Output, StringComparison.Ordinal)
        && string.Equals(a.Input, b.Input, StringComparison.Ordinal)
        && string.Equals(a.ScriptFile, b.ScriptFile, StringComparison.Ordinal);
}
