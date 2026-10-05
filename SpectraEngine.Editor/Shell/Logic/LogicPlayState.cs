using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpectraEngine.Editor.Shell.Logic;

// What the running level last said, indexed for the view. The tables are
// reused, so taking a snapshot allocates nothing once they have grown.
internal sealed class LogicPlayState
{
    public const int EventLines = 3;

    private readonly Dictionary<LogicWireKey, LogicWireActivity> _wires = [];
    private readonly Dictionary<Guid, LogicEntityState> _states = [];
    private LogicPlayInfo? _info;
    private IReadOnlyList<LogicEntityState>? _statesTaken;

    public bool IsPlaying => _info is not null;

    public long Tick => _info?.Tick ?? 0;

    public float Time => _info?.Time ?? 0f;

    // The tick as text, for a readout. Empty while editing.
    public string TickText { get; private set; } = "";

    // The newest events, oldest first. A new list whenever they change.
    public IReadOnlyList<LogicEventLine> Events { get; private set; } = [];

    // Whether the last snapshot taken brought state lines that read differently.
    public bool StatesChanged { get; private set; }

    // Returns whether the level said anything new.
    public bool Take(LogicPlayInfo? info)
    {
        StatesChanged = false;
        if (ReferenceEquals(info, _info))
            return false;

        LogicPlayInfo? before = _info;
        _info = info;

        if (info is null)
        {
            StatesChanged = _states.Count > 0;
            _wires.Clear();
            _states.Clear();
            _statesTaken = null;
            Events = [];
            TickText = "";
            return true;
        }

        if (before is null || before.Tick != info.Tick)
            TickText = info.Tick.ToString(CultureInfo.InvariantCulture);

        // The engine hands the same lists on until what they hold changes.
        if (!ReferenceEquals(info.Wires, before?.Wires))
            TakeWires(info.Wires);

        if (!ReferenceEquals(info.States, _statesTaken))
            TakeStates(info.States);

        if (!ReferenceEquals(info.Recent, before?.Recent))
            TakeEvents(info.Recent);

        return true;
    }

    // Everything the wires an edge draws have done.
    public LogicWireActivity Activity(LogicEdge edge)
    {
        LogicWireActivity sum = default;
        for (int i = 0; i < edge.WireIndices.Count; i++)
        {
            if (_wires.TryGetValue(new LogicWireKey(edge.From.NodeId, edge.WireIndices[i]), out LogicWireActivity wire))
                sum = LogicWireState.Sum(sum, wire);
        }

        return sum;
    }

    public bool TryGetState(Guid nodeId, out LogicEntityState state) => _states.TryGetValue(nodeId, out state);

    private void TakeWires(IReadOnlyList<LogicWireActivity> wires)
    {
        _wires.Clear();
        for (int i = 0; i < wires.Count; i++)
            _wires[new LogicWireKey(wires[i].NodeId, wires[i].Wire)] = wires[i];
    }

    // The engine reads the lines again every few ticks into a new list, so
    // they are compared by what they say.
    private void TakeStates(IReadOnlyList<LogicEntityState> states)
    {
        _statesTaken = states;

        bool same = states.Count == _states.Count;
        for (int i = 0; same && i < states.Count; i++)
            same = _states.TryGetValue(states[i].NodeId, out LogicEntityState kept) && kept == states[i];

        if (same)
            return;

        StatesChanged = true;
        _states.Clear();
        for (int i = 0; i < states.Count; i++)
            _states[states[i].NodeId] = states[i];
    }

    private void TakeEvents(IReadOnlyList<LogicEventInfo> recent)
    {
        int count = Math.Min(recent.Count, EventLines);
        bool same = count == Events.Count;
        for (int i = 0; same && i < count; i++)
            same = Events[i].Number == recent[recent.Count - count + i].Number;

        if (same)
            return;

        var lines = new LogicEventLine[count];
        for (int i = 0; i < count; i++)
        {
            LogicEventInfo info = recent[recent.Count - count + i];
            lines[i] = Written(info.Number) ?? LogicEventLine.From(info);
        }

        Events = lines;
    }

    // A line that is still among the newest is kept, not written again.
    private LogicEventLine? Written(long number)
    {
        for (int i = 0; i < Events.Count; i++)
        {
            if (Events[i].Number == number)
                return Events[i];
        }

        return null;
    }
}
