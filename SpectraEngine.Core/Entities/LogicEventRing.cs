using SpectraEngine.Core.Inspection;
using System;

namespace SpectraEngine.Core.Entities;

// The newest events of one kind, oldest first. Whoever adds one numbers it,
// and the numbers only go up.
internal sealed class LogicEventRing
{
    private readonly LogicEventInfo[] _held;
    private long _count;

    public LogicEventRing(int capacity) => _held = new LogicEventInfo[capacity];

    public int Count => (int)Math.Min(_count, _held.Length);

    // Counted from the oldest that is still held.
    public ref readonly LogicEventInfo this[int index] => ref _held[(_count - Count + index) % _held.Length];

    public void Clear() => _count = 0;

    public void Add(in LogicEventInfo happened)
    {
        _held[_count % _held.Length] = happened;
        _count++;
    }

    // Adds an event, or puts it in the place of the one held that says the
    // same and moves that to the end.
    public void AddOrRenew(in LogicEventInfo happened)
    {
        for (long at = _count - 1; at >= _count - Count; at--)
        {
            if (!SaysTheSame(_held[at % _held.Length], happened))
                continue;

            for (long i = at; i < _count - 1; i++)
                _held[i % _held.Length] = _held[(i + 1) % _held.Length];

            _held[(_count - 1) % _held.Length] = happened;
            return;
        }

        Add(happened);
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
