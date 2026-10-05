using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// Counts what each wire does while a level runs, for a view that lights the
/// wiring. Prints nothing. Render thread only.
/// </summary>
public sealed class WireActivityTrace : IEntityTrace
{
    /// <summary>How many events <see cref="LogicPlayInfo.Recent"/> holds.</summary>
    public const int RecentCapacity = 32;

    // A queued input this long past due was dropped by the world unseen.
    private const float OverdueSeconds = 1f;

    private readonly Dictionary<(Guid Node, int Wire), Counts> _byWire = [];

    // Capture order. A dictionary's own order is not one to publish.
    private readonly List<Counts> _wires = [];
    private readonly Dictionary<long, Wait> _waiting = [];
    private readonly List<long> _overdue = [];
    private readonly LogicEventInfo[] _recent = new LogicEventInfo[RecentCapacity];

    private long _eventCount;
    private long _tick;
    private float _time;
    private long _version;
    private long _capturedVersion = -1;

    /// <summary>
    /// Forgets everything. Call when a level starts, and when this trace is
    /// attached to one already running: an input queued before then would
    /// otherwise wait forever.
    /// </summary>
    public void Clear()
    {
        _byWire.Clear();
        _wires.Clear();
        _waiting.Clear();
        _eventCount = 0;
        _tick = 0;
        _time = 0f;
        _version++;
    }

    /// <inheritdoc/>
    public void Record(in EntityTraceEvent traced)
    {
        switch (traced.Kind)
        {
            case EntityTraceKind.InputQueued:
                if (CountsFor(traced) is { } fired)
                {
                    fired.Fired++;
                    fired.LastFiredTick = traced.Tick;

                    if (traced.DueTime > traced.Time)
                        _waiting[traced.Sequence] = new Wait(fired, traced.Time, traced.DueTime);
                }

                break;

            case EntityTraceKind.InputDelivered:
                _waiting.Remove(traced.Sequence);
                Remember(traced);
                break;

            case EntityTraceKind.TargetMissing:
                _waiting.Remove(traced.Sequence);
                if (CountsFor(traced) is { } missed)
                    missed.Missed++;

                Remember(traced);
                break;

            case EntityTraceKind.InputRefused:
                if (CountsFor(traced) is { } refused)
                    refused.Refused++;

                Remember(traced);
                break;

            default:
                return;
        }

        _version++;
    }

    /// <inheritdoc/>
    public void EndTick(long tick, float time)
    {
        _tick = tick;
        _time = time;

        if (_waiting.Count == 0)
            return;

        _overdue.Clear();
        foreach (KeyValuePair<long, Wait> waiting in _waiting)
        {
            if (waiting.Value.Due + OverdueSeconds < time)
                _overdue.Add(waiting.Key);
        }

        for (int i = 0; i < _overdue.Count; i++)
            _waiting.Remove(_overdue[i]);

        if (_overdue.Count > 0)
            _version++;
    }

    /// <summary>
    /// Describes the level's wiring as it stands. Hands back
    /// <paramref name="previous"/>'s lists while no wire has done anything since.
    /// </summary>
    /// <param name="previous">The last capture, or null.</param>
    /// <param name="states">The state lines to send along.</param>
    public LogicPlayInfo Capture(LogicPlayInfo? previous, IReadOnlyList<LogicEntityState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        if (previous is not null && _capturedVersion == _version)
        {
            return new LogicPlayInfo
            {
                Tick = _tick,
                Time = _time,
                Wires = previous.Wires,
                Recent = previous.Recent,
                States = states,
            };
        }

        _capturedVersion = _version;

        return new LogicPlayInfo
        {
            Tick = _tick,
            Time = _time,
            Wires = CopyWires(),
            Recent = CopyRecent(),
            States = states,
        };
    }

    private LogicWireActivity[] CopyWires()
    {
        for (int i = 0; i < _wires.Count; i++)
            _wires[i].Waiting = 0;

        foreach (Wait waiting in _waiting.Values)
        {
            Counts counts = waiting.Wire;
            if (counts.Waiting == 0 || waiting.Due < counts.WaitingDue)
            {
                counts.WaitingSince = waiting.Since;
                counts.WaitingDue = waiting.Due;
            }

            counts.Waiting++;
        }

        var copy = new LogicWireActivity[_wires.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            Counts counts = _wires[i];
            copy[i] = new LogicWireActivity
            {
                NodeId = counts.Node,
                Wire = counts.Wire,
                Fired = counts.Fired,
                LastFiredTick = counts.LastFiredTick,
                Missed = counts.Missed,
                Refused = counts.Refused,
                Waiting = counts.Waiting,
                WaitingSince = counts.Waiting > 0 ? counts.WaitingSince : 0f,
                WaitingDue = counts.Waiting > 0 ? counts.WaitingDue : 0f,
            };
        }

        return copy;
    }

    private LogicEventInfo[] CopyRecent()
    {
        int held = (int)Math.Min(_eventCount, RecentCapacity);
        var copy = new LogicEventInfo[held];
        long oldest = _eventCount - held;

        for (int i = 0; i < held; i++)
            copy[i] = _recent[(oldest + i) % RecentCapacity];

        return copy;
    }

    // Null for an input no wire sent: there is no wire to count it on.
    private Counts? CountsFor(in EntityTraceEvent traced)
    {
        if (traced.Wire < 0 || traced.Source is not { } source)
            return null;

        (Guid, int) key = (source.Node.Id, traced.Wire);
        if (!_byWire.TryGetValue(key, out Counts? counts))
        {
            counts = new Counts(source.Node.Id, traced.Wire);
            _byWire.Add(key, counts);
            _wires.Add(counts);
        }

        return counts;
    }

    private void Remember(in EntityTraceEvent traced)
    {
        _recent[_eventCount % RecentCapacity] = new LogicEventInfo
        {
            Number = _eventCount,
            Tick = traced.Tick,
            Kind = traced.Kind,
            SourceId = traced.Source?.Node.Id ?? Guid.Empty,
            SourceName = traced.Source?.TargetName ?? "",
            Output = traced.Output,
            Wire = traced.Wire,
            TargetId = traced.Target?.Node.Id ?? Guid.Empty,
            TargetName = traced.Target?.TargetName ?? traced.TargetName,
            Input = traced.Input,
            Parameter = traced.Parameter,
        };

        _eventCount++;
    }

    private sealed class Counts(Guid node, int wire)
    {
        public Guid Node { get; } = node;
        public int Wire { get; } = wire;
        public int Fired;
        public long LastFiredTick;
        public int Missed;
        public int Refused;

        // Scratch for a capture.
        public int Waiting;
        public float WaitingSince;
        public float WaitingDue;
    }

    private readonly record struct Wait(Counts Wire, float Since, float Due);
}
