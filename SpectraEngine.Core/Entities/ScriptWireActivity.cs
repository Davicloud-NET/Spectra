using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

// What a level's scripts hear and send while a view watches, as wires the
// view can draw: one for each script, entity, port and line of code. What a
// script hears is read from the level's listeners, so a connection that ended
// is gone. What it sent is remembered from the first send on.
internal sealed class ScriptWireActivity
{
    // A level that makes wires without end would otherwise grow the table
    // for as long as it runs. Past this many a new one is not counted.
    private const int MaxCounted = 2 * LogicScriptWiring.MaxWires;

    private static readonly Comparison<LogicScriptWire> ByWhatItJoins = Compare;

    private readonly Dictionary<LogicScriptWire, Counts> _counts = [];
    private readonly List<LogicScriptWire> _sent = [];

    // Scratch for a capture.
    private readonly List<ScriptListener> _listening = [];
    private readonly List<LogicScriptWire> _wires = [];
    private readonly HashSet<LogicScriptWire> _listed = [];

    private long _version;
    private long _capturedVersion = -1;
    private long _capturedListeners = -1;

    public void Clear()
    {
        _counts.Clear();
        _sent.Clear();
        _version++;
    }

    // A script sent an input. Every send from one line is the same wire.
    public void Sent(in EntityTraceEvent traced, Guid entity)
    {
        var wire = new LogicScriptWire
        {
            Script = traced.SenderNode,
            Entity = entity,
            Port = traced.Input,
            IsSend = true,
            File = traced.SenderScript,
            Line = traced.SenderLine,
        };

        if (!_counts.ContainsKey(wire) && _counts.Count < MaxCounted)
            _sent.Add(wire);

        Count(wire, traced.Tick);
    }

    // An output fired for a listener of a script.
    public void Heard(Guid entity, string output, in ScriptPlace by, long tick) =>
        Count(Hears(entity, output, by), tick);

    // Hands back the previous lists while they still hold.
    public void Capture(
        EntityWorld world,
        LogicPlayInfo? previous,
        out LogicScriptWiring wiring,
        out IReadOnlyList<LogicScriptActivity> activity)
    {
        long listeners = world.Listeners.Version;
        if (previous is not null && _capturedVersion == _version && _capturedListeners == listeners)
        {
            wiring = previous.Scripts;
            activity = previous.ScriptActivity;
            return;
        }

        _capturedVersion = _version;
        _capturedListeners = listeners;

        bool isTruncated = List(world);
        wiring = previous is not null && Holds(previous.Scripts, isTruncated)
            ? previous.Scripts
            : new LogicScriptWiring { Wires = _wires.ToArray(), IsTruncated = isTruncated };

        activity = previous is not null && Holds(previous.ScriptActivity) ? previous.ScriptActivity : CopyActivity();
    }

    // Fills _wires, in an order that depends on nothing but the wires.
    // Returns whether some were left out.
    private bool List(EntityWorld world)
    {
        _wires.Clear();
        _listed.Clear();
        _listening.Clear();
        world.Listeners.CopyTo(_listening);

        for (int i = 0; i < _listening.Count; i++)
        {
            ScriptListener listener = _listening[i];
            if (!listener.By.HasNode)
                continue;

            // Two functions connected on one line are one wire.
            LogicScriptWire wire = Hears(listener.Node, listener.Name, listener.By);
            if (_listed.Add(wire))
                _wires.Add(wire);
        }

        _wires.AddRange(_sent);
        _wires.Sort(ByWhatItJoins);

        if (_wires.Count <= LogicScriptWiring.MaxWires)
            return false;

        _wires.RemoveRange(LogicScriptWiring.MaxWires, _wires.Count - LogicScriptWiring.MaxWires);
        return true;
    }

    private bool Holds(LogicScriptWiring previous, bool isTruncated)
    {
        if (previous.IsTruncated != isTruncated || previous.Wires.Count != _wires.Count)
            return false;

        for (int i = 0; i < _wires.Count; i++)
        {
            if (previous.Wires[i] != _wires[i])
                return false;
        }

        return true;
    }

    private bool Holds(IReadOnlyList<LogicScriptActivity> previous)
    {
        if (previous.Count != _wires.Count)
            return false;

        for (int i = 0; i < _wires.Count; i++)
        {
            if (previous[i] != ActivityOf(_wires[i]))
                return false;
        }

        return true;
    }

    private LogicScriptActivity[] CopyActivity()
    {
        var copy = new LogicScriptActivity[_wires.Count];
        for (int i = 0; i < copy.Length; i++)
            copy[i] = ActivityOf(_wires[i]);

        return copy;
    }

    private LogicScriptActivity ActivityOf(in LogicScriptWire wire) =>
        _counts.TryGetValue(wire, out Counts? counts)
            ? new LogicScriptActivity(counts.Fired, counts.LastFiredTick)
            : default;

    private void Count(in LogicScriptWire wire, long tick)
    {
        if (!_counts.TryGetValue(wire, out Counts? counts))
        {
            if (_counts.Count >= MaxCounted)
                return;

            counts = new Counts();
            _counts.Add(wire, counts);
        }

        counts.Fired++;
        counts.LastFiredTick = tick;
        _version++;
    }

    private static LogicScriptWire Hears(Guid entity, string output, in ScriptPlace by) => new()
    {
        Script = by.Node,
        Entity = entity,
        Port = output,
        IsSend = false,
        File = by.File,
        Line = by.Line,
    };

    private static int Compare(LogicScriptWire a, LogicScriptWire b)
    {
        int order = a.Script.CompareTo(b.Script);
        if (order == 0)
            order = a.Entity.CompareTo(b.Entity);
        if (order == 0)
            order = a.IsSend.CompareTo(b.IsSend);
        if (order == 0)
            order = string.CompareOrdinal(a.Port, b.Port);
        if (order == 0)
            order = string.CompareOrdinal(a.File, b.File);

        return order != 0 ? order : a.Line.CompareTo(b.Line);
    }

    private sealed class Counts
    {
        public int Fired;
        public long LastFiredTick;
    }
}
