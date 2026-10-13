using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// Counts what each wire does while a level runs, and what its scripts hear
/// and send, for a view that lights the wiring. Prints nothing. Render
/// thread only.
/// </summary>
public sealed class WireActivityTrace : IEntityTrace
{
    /// <summary>
    /// How many events of the wires <see cref="LogicPlayInfo.Recent"/> holds.
    /// It holds as many of what scripts heard and sent beside them.
    /// </summary>
    public const int RecentCapacity = 32;

    // A queued input this many ticks past due arrived while this trace was
    // not the world's, and nothing said so.
    private const long OverdueTicks = 60;

    private readonly Dictionary<(Guid Node, int Wire), Counts> _byWire = [];

    // Capture order. A dictionary's own order is not one to publish.
    private readonly List<Counts> _wires = [];
    private readonly Dictionary<long, Wait> _waiting = [];
    private readonly List<long> _overdue = [];
    private readonly LogicRecentEvents _recent = new(RecentCapacity);
    private readonly ScriptWireActivity _scripts = new();

    private long _tick;
    private float _time;
    private long _version;
    private long _capturedVersion = -1;
    private long _recentVersion;
    private long _capturedRecent = -1;

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
        _recent.Clear();
        _scripts.Clear();
        _tick = 0;
        _time = 0f;
        _version++;
        _recentVersion++;
    }

    /// <inheritdoc/>
    public void Record(in EntityTraceEvent traced)
    {
        // What a script sent is on no wire of the level.
        if (traced.SenderScript.Length > 0)
        {
            RecordSent(traced);
            return;
        }

        switch (traced.Kind)
        {
            case EntityTraceKind.OutputFired:
                if (traced.ListenersQueued > 0)
                    RecordHeard(traced);

                return;

            case EntityTraceKind.InputQueued:
                if (CountsFor(traced) is { } fired)
                {
                    fired.Fired++;
                    fired.LastFiredTick = traced.Tick;

                    if (traced.DueTick > traced.Tick)
                        _waiting[traced.Sequence] = new Wait(fired, traced.Time, traced.DueTime, traced.DueTick);
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
            if (waiting.Value.DueTick + OverdueTicks < tick)
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
    /// <param name="world">
    /// The level that runs, whose listeners say what its scripts hear. With
    /// none the capture says nothing of scripts.
    /// </param>
    public LogicPlayInfo Capture(
        LogicPlayInfo? previous, IReadOnlyList<LogicEntityState> states, EntityWorld? world = null)
    {
        ArgumentNullException.ThrowIfNull(states);

        bool wiresHold = previous is not null && _capturedVersion == _version;
        bool recentHolds = previous is not null && _capturedRecent == _recentVersion;
        _capturedVersion = _version;
        _capturedRecent = _recentVersion;

        LogicScriptWiring scripts = LogicScriptWiring.None;
        IReadOnlyList<LogicScriptActivity> scriptActivity = [];
        if (world is { IsActive: true })
            _scripts.Capture(world, previous, out scripts, out scriptActivity);

        return new LogicPlayInfo
        {
            Tick = _tick,
            Time = _time,
            Wires = wiresHold && previous is not null ? previous.Wires : CopyWires(),
            Recent = recentHolds && previous is not null ? previous.Recent : _recent.Copy(),
            States = states,
            Scripts = scripts,
            ScriptActivity = scriptActivity,
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

    // A view draws what a script sent from the script's node, so a sender
    // that did not say which node it is leaves no mark.
    private void RecordSent(in EntityTraceEvent traced)
    {
        if (traced.SenderNode == Guid.Empty || traced.Target is not { } target)
            return;

        if (traced.Kind == EntityTraceKind.InputQueued)
        {
            // A script that was switched off may send once more, from the
            // thread that was running then. That makes no wire.
            if (!target.World.StoppedScripts.Contains(traced.SenderNode))
                _scripts.Sent(traced, target.Node.Id);

            return;
        }

        _recent.AddOfScript(new LogicEventInfo
        {
            Tick = traced.Tick,
            Kind = traced.Kind,
            SourceName = "",
            Output = "",
            Wire = -1,
            TargetId = target.Node.Id,
            TargetName = target.TargetName,
            Input = traced.Input,
            Parameter = traced.Parameter,
            ScriptId = traced.SenderNode,
            ScriptName = NameOf(target, traced.SenderNode),
            ScriptFile = traced.SenderScript,
            ScriptLine = traced.SenderLine,
        });

        _recentVersion++;
    }

    // An output fired and scripts listen to it. Each is called later in the
    // drain, and one that disconnects before then is still listed here.
    private void RecordHeard(in EntityTraceEvent traced)
    {
        if (traced.Source is not { } source
            || source.World.Listeners.WhoHears(source.Node, traced.Output) is not { } hearing)
        {
            return;
        }

        for (int i = 0; i < hearing.Count; i++)
        {
            ScriptPlace by = hearing[i];
            if (!by.HasNode)
                continue;

            _scripts.Heard(source.Node.Id, traced.Output, by, traced.Tick);
            _recent.AddOfScript(new LogicEventInfo
            {
                Tick = traced.Tick,
                Kind = EntityTraceKind.OutputFired,
                SourceId = source.Node.Id,
                SourceName = source.TargetName,
                Output = traced.Output,
                Wire = -1,
                TargetName = "",
                Input = "",
                Parameter = "",
                ScriptId = by.Node,
                ScriptName = NameOf(source, by.Node),
                ScriptFile = by.File,
                ScriptLine = by.Line,
            });

            _recentVersion++;
        }
    }

    // The name of a script's node, read from the scene the entity is in.
    private static string NameOf(Entity inLevel, Guid script) =>
        inLevel.World.Scene.TryFindById(script, out Scene.SceneNode? node) ? node.Name : "";

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
        _recent.Add(new LogicEventInfo
        {
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
            ScriptName = "",
            ScriptFile = "",
        });

        _recentVersion++;
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

    // The times are for a view to draw. The tick is what says it is overdue.
    private readonly record struct Wait(Counts Wire, float Since, float Due, long DueTick);
}
