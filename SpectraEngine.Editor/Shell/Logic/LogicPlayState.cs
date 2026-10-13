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
    private readonly LogicEventInfo[] _newest = new LogicEventInfo[EventLines];
    private LogicPlayInfo? _info;
    private IReadOnlyList<LogicEntityState>? _statesTaken;
    private IReadOnlyList<LogicScriptActivity> _scriptActivity = [];
    private bool _showsScripts = true;

    public bool IsPlaying => _info is not null;

    public long Tick => _info?.Tick ?? 0;

    public float Time => _info?.Time ?? 0f;

    // The tick as text, for a readout. Empty while editing.
    public string TickText { get; private set; } = "";

    // The newest events, oldest first. A new list whenever they change.
    public IReadOnlyList<LogicEventLine> Events { get; private set; } = [];

    // Takes what the level says now, and returns what of it is new to the view.
    public LogicPlayChange Take(LogicPlayInfo? info)
    {
        if (ReferenceEquals(info, _info))
            return LogicPlayChange.None;

        LogicPlayInfo? before = _info;
        IReadOnlyList<LogicEventLine> events = Events;
        _info = info;

        LogicPlayChange change = LogicPlayChange.Said;
        if ((before is null) != (info is null))
            change |= LogicPlayChange.Playing | LogicPlayChange.Tick;

        if (info is null)
        {
            change |= _states.Count > 0 ? LogicPlayChange.States : LogicPlayChange.None;
            _wires.Clear();
            _states.Clear();
            _statesTaken = null;
            _scriptActivity = [];
            Events = [];
            TickText = "";
            return WithEvents(change, events);
        }

        if (before is null || before.Tick != info.Tick)
        {
            TickText = info.Tick.ToString(CultureInfo.InvariantCulture);
            change |= LogicPlayChange.Tick;
        }

        // The engine hands the same lists on until what they hold changes.
        if (!ReferenceEquals(info.Wires, before?.Wires))
            TakeWires(info.Wires);

        if (!ReferenceEquals(info.States, _statesTaken) && TakeStates(info.States))
            change |= LogicPlayChange.States;

        if (!ReferenceEquals(info.Recent, before?.Recent))
            TakeEvents(info.Recent);

        _scriptActivity = info.ScriptActivity;
        return WithEvents(change, events);
    }

    // Whether what scripts sent and heard is among the events. Returns
    // whether the events differ for it.
    public bool ShowScripts(bool shows)
    {
        if (shows == _showsScripts)
            return false;

        _showsScripts = shows;
        IReadOnlyList<LogicEventLine> before = Events;
        if (_info is not null)
            TakeEvents(_info.Recent);

        return !ReferenceEquals(before, Events);
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

    // Everything the lines of code behind a script's edge have carried.
    public LogicScriptActivity ScriptActivity(LogicEdge edge)
    {
        int fired = 0;
        long last = 0;

        for (int i = 0; i < edge.ScriptWires.Count; i++)
        {
            // The edge may be of a graph one snapshot older than the counts.
            int wire = edge.ScriptWires[i];
            if ((uint)wire >= (uint)_scriptActivity.Count)
                continue;

            fired += _scriptActivity[wire].Fired;
            last = Math.Max(last, _scriptActivity[wire].LastFiredTick);
        }

        return new LogicScriptActivity(fired, last);
    }

    public bool TryGetState(Guid nodeId, out LogicEntityState state) => _states.TryGetValue(nodeId, out state);

    private void TakeWires(IReadOnlyList<LogicWireActivity> wires)
    {
        _wires.Clear();
        for (int i = 0; i < wires.Count; i++)
            _wires[new LogicWireKey(wires[i].NodeId, wires[i].Wire)] = wires[i];
    }

    private LogicPlayChange WithEvents(LogicPlayChange change, IReadOnlyList<LogicEventLine> before) =>
        ReferenceEquals(before, Events) ? change : change | LogicPlayChange.Events;

    // The engine reads the lines again every few ticks into a new list, so
    // they are compared by what they say. Returns whether any reads differently.
    private bool TakeStates(IReadOnlyList<LogicEntityState> states)
    {
        _statesTaken = states;

        bool same = states.Count == _states.Count;
        for (int i = 0; same && i < states.Count; i++)
            same = _states.TryGetValue(states[i].NodeId, out LogicEntityState kept) && kept == states[i];

        if (same)
            return false;

        _states.Clear();
        for (int i = 0; i < states.Count; i++)
            _states[states[i].NodeId] = states[i];

        return true;
    }

    private void TakeEvents(IReadOnlyList<LogicEventInfo> recent)
    {
        int count = Newest(recent);
        bool same = count == Events.Count;
        for (int i = 0; same && i < count; i++)
            same = Events[i].Number == _newest[i].Number;

        if (same)
            return;

        var lines = new LogicEventLine[count];
        for (int i = 0; i < count; i++)
            lines[i] = Written(_newest[i].Number) ?? LogicEventLine.From(_newest[i]);

        Events = lines;
    }

    // Fills _newest with the newest events on show, oldest first, and
    // returns how many there are. What scripts did is left out while the
    // view shows no scripts.
    private int Newest(IReadOnlyList<LogicEventInfo> recent)
    {
        int count = 0;
        for (int i = recent.Count - 1; i >= 0 && count < EventLines; i--)
        {
            if (_showsScripts || !recent[i].IsOfScript)
                _newest[count++] = recent[i];
        }

        Array.Reverse(_newest, 0, count);
        return count;
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
