using SpectraEngine.Core.Inspection;

namespace SpectraEngine.Core.Entities;

// The newest events of a running level, for a view's list of them. What the
// wires did and what scripts did are held apart: scripts can do a great deal
// every tick, and in one list that would push out everything a wire did.
internal sealed class LogicRecentEvents
{
    private readonly LogicEventRing _ofWires;
    private readonly LogicEventRing _ofScripts;
    private long _nextNumber;

    // Holds this many events of the wires, and as many of the scripts.
    public LogicRecentEvents(int capacity)
    {
        _ofWires = new LogicEventRing(capacity);
        _ofScripts = new LogicEventRing(capacity);
    }

    public void Clear()
    {
        _ofWires.Clear();
        _ofScripts.Clear();
        _nextNumber = 0;
    }

    // Adds what a wire did as the newest event.
    public void Add(in LogicEventInfo happened) => _ofWires.Add(happened with { Number = _nextNumber++ });

    // The same for what a script sent or heard. A script may do one thing
    // every tick, so a line that is held already is not held twice: it
    // becomes the newest, with its new tick and a new number.
    public void AddOfScript(in LogicEventInfo happened) =>
        _ofScripts.AddOrRenew(happened with { Number = _nextNumber++ });

    // Both kinds in the order they happened, oldest first.
    public LogicEventInfo[] Copy()
    {
        var copy = new LogicEventInfo[_ofWires.Count + _ofScripts.Count];
        int wire = 0;
        int script = 0;

        for (int i = 0; i < copy.Length; i++)
        {
            bool isWiresTurn = script == _ofScripts.Count
                || (wire < _ofWires.Count && _ofWires[wire].Number < _ofScripts[script].Number);

            copy[i] = isWiresTurn ? _ofWires[wire++] : _ofScripts[script++];
        }

        return copy;
    }
}
