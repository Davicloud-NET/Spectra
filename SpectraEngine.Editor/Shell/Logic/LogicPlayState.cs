using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// What the running level last said, indexed for the view. The tables are
// reused, so taking a snapshot allocates nothing once they have grown.
internal sealed class LogicPlayState
{
    public const int EventLines = 3;

    private readonly Dictionary<LogicWireKey, LogicWireActivity> _wires = [];
    private readonly Dictionary<Guid, LogicEntityState> _states = [];
    private LogicPlayInfo? _info;
    private long _newestEvent = long.MinValue;

    public bool IsPlaying => _info is not null;

    public long Tick => _info?.Tick ?? 0;

    public float Time => _info?.Time ?? 0f;

    // How many digits a wire's label has to have room for.
    public int Digits { get; private set; } = LogicWireState.LeastDigits;

    // The newest events, oldest first. A new list whenever they change.
    public IReadOnlyList<LogicEventLine> Events { get; private set; } = [];

    // Whether the last snapshot taken brought other state lines.
    public bool StatesChanged { get; private set; }

    // Returns whether the level said anything new.
    public bool Take(LogicPlayInfo? info)
    {
        if (ReferenceEquals(info, _info))
            return false;

        LogicPlayInfo? before = _info;
        _info = info;

        if (info is null)
        {
            _wires.Clear();
            _states.Clear();
            Digits = LogicWireState.LeastDigits;
            Events = [];
            _newestEvent = long.MinValue;
            StatesChanged = true;
            return true;
        }

        // The engine hands the same lists on until what they hold changes.
        if (!ReferenceEquals(info.Wires, before?.Wires))
            TakeWires(info);

        StatesChanged = !ReferenceEquals(info.States, before?.States);
        if (StatesChanged)
            TakeStates(info.States);

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

    private void TakeWires(LogicPlayInfo info)
    {
        _wires.Clear();
        for (int i = 0; i < info.Wires.Count; i++)
        {
            LogicWireActivity wire = info.Wires[i];
            _wires[new LogicWireKey(wire.NodeId, wire.Wire)] = wire;
            Digits = Math.Max(Digits, LogicWireState.Digits(wire, info.Time));
        }
    }

    private void TakeStates(IReadOnlyList<LogicEntityState> states)
    {
        _states.Clear();
        for (int i = 0; i < states.Count; i++)
            _states[states[i].NodeId] = states[i];
    }

    private void TakeEvents(IReadOnlyList<LogicEventInfo> recent)
    {
        long newest = recent.Count == 0 ? long.MinValue : recent[^1].Number;
        if (newest == _newestEvent)
            return;

        _newestEvent = newest;
        int count = Math.Min(recent.Count, EventLines);
        var lines = new LogicEventLine[count];

        for (int i = 0; i < count; i++)
            lines[i] = LogicEventLine.From(recent[recent.Count - count + i]);

        Events = lines;
    }
}
