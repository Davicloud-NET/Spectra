using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell;

// A displayed value that shows the click at once and defers to the engine
// after HoldTicks disagreeing snapshots. Safe only because the shell posts set
// verbs, never toggles: a stale echo can be old, not opposite.
// Ticks are snapshots, not time: the publish rate changes mid-gesture.
// UI thread only.
internal sealed class OptimisticValue<T>
{
    private readonly IEqualityComparer<T> _comparer;
    private T _value;
    private T _pending;
    private bool _hasPending;
    private int _ticks;

    public OptimisticValue(T initial, IEqualityComparer<T>? comparer = null)
    {
        _comparer = comparer ?? EqualityComparer<T>.Default;
        _value = initial;
        _pending = initial;
    }

    // Disagreeing snapshots ignored before the engine wins. About 100ms at
    // rest. Short, so a refusal shows before the user clicks again.
    public int HoldTicks { get; init; } = 6;

    public T Value => _value;

    public bool HasPending => _hasPending;

    // Returns true when the displayed value changed.
    public bool Request(T wanted)
    {
        // Last write wins, no queue.
        _pending = wanted;
        _hasPending = true;
        _ticks = 0;

        if (_comparer.Equals(_value, wanted))
            return false;

        _value = wanted;
        return true;
    }

    // Takes a value the engine reported. Returns true when the displayed value changed.
    public bool Apply(T reported)
    {
        if (_hasPending)
        {
            if (_comparer.Equals(reported, _pending))
            {
                _hasPending = false;
                _ticks = 0;
            }
            else if (++_ticks < HoldTicks)
            {
                // Snapshot predates the click. Writing it back would flicker.
                return false;
            }
            else
            {
                // Not lag any more. The engine wins.
                _hasPending = false;
                _ticks = 0;
            }
        }

        if (_comparer.Equals(_value, reported))
            return false;

        _value = reported;
        return true;
    }

    // For a session boundary: a pending request was aimed at an engine that is gone.
    public void Reset(T value)
    {
        _value = value;
        _pending = value;
        _hasPending = false;
        _ticks = 0;
    }
}
