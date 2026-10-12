using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

// What an entity world is waiting to send, and the sending of it once it is
// due. Thinks, inputs and the wakes of scripts share one order: the tick,
// then the order made in.
internal sealed class EntityDispatcher
{
    private readonly EntityWorld _world;
    private readonly ILogger _logger;
    private readonly EntityDispatchLog _log;

    // Each has the tick's budget and the queue's room to itself. A level
    // with too many threads then runs its scripts late and its wires on time.
    private readonly EntityEventLane _entities = new();
    private readonly EntityEventLane _scripts = new();

    // Reused across dispatches. Safe: FireOutput queues, so no input handler
    // can resolve a target mid-dispatch.
    private readonly List<Entity> _resolved = [];

    private long _sequence;
    private int _maxDispatchesPerTick = 4096;
    private int _maxPendingEvents = 65536;

    public EntityDispatcher(EntityWorld world, ILogger logger)
    {
        _world = world;
        _logger = logger;
        _log = new EntityDispatchLog(logger);
    }

    public int MaxDispatchesPerTick
    {
        get => _maxDispatchesPerTick;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxDispatchesPerTick = value;
        }
    }

    public int MaxPendingEvents
    {
        get => _maxPendingEvents;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxPendingEvents = value;
        }
    }

    public int BudgetTripCount { get; private set; }

    public int DiscardedCount { get; private set; }

    public int PendingCount => _entities.Count + _scripts.Count;

    public void Clear()
    {
        _entities.Clear();
        _scripts.Clear();
        _resolved.Clear();
        _sequence = 0;
    }

    public void QueueInput(Entity target, string input, string parameter, Entity? activator) =>
        Queue(
            new EntityEvent
            {
                Tick = _world.TickNumber,
                Sequence = _sequence++,
                Kind = EntityEventKind.Input,
                Target = target,
                Activator = activator,
                TargetName = "",
                Input = input,
                Parameter = parameter,
                Output = "",
            },
            target);

    public void QueueInput(string targetName, string input, string parameter, float delay) =>
        Queue(
            new EntityEvent
            {
                Tick = _world.TickAfter(delay),
                Sequence = _sequence++,
                Kind = EntityEventKind.Input,
                TargetName = targetName,
                Input = input,
                Parameter = parameter,
                Output = "",
            },
            null);

    public void ScheduleOutput(
        Entity caller,
        Entity? activator,
        string output,
        in EntityConnection wire,
        int wireIndex,
        string? parameterOverride) =>
        Queue(
            new EntityEvent
            {
                Tick = _world.TickAfter(wire.Delay),
                Sequence = _sequence++,
                Kind = EntityEventKind.Input,
                Entity = caller,
                Activator = activator,
                TargetName = wire.TargetName,
                Input = wire.Input,
                Parameter = parameterOverride ?? wire.Parameter,
                Output = output,
                WireOrdinal = wireIndex + 1,
            },
            null);

    // False when the think was not queued and will not come.
    public bool ScheduleThink(Entity entity, long tick, int serial) =>
        TryPush(_entities, new EntityEvent
        {
            Tick = tick,
            Sequence = _sequence++,
            Kind = EntityEventKind.Think,
            Entity = entity,
            ThinkSerial = serial,
            TargetName = "",
            Input = "",
            Parameter = "",
            Output = "",
        });

    // False when the wake was not queued and will not come.
    public bool QueueScriptWake(long token, long ticks) =>
        TryPush(_scripts, new EntityEvent
        {
            Tick = ticks > WholeTicks.Never - _world.TickNumber ? WholeTicks.Never : _world.TickNumber + ticks,
            Sequence = _sequence++,
            Kind = EntityEventKind.ScriptWake,
            Token = token,
            TargetName = "",
            Input = "",
            Parameter = "",
            Output = "",
        });

    // Sends what is due, the earlier of the two lanes' next events first. A
    // lane that has used up its share stops for this drain, and the other
    // goes on.
    public int DrainDue()
    {
        _entities.BeginDrain(_maxPendingEvents);
        _scripts.BeginDrain(_maxPendingEvents);

        long tick = _world.TickNumber;
        long firstMadeHere = _sequence;
        int sent = 0;

        while (NextDue(tick, out EntityEvent next) is { } lane)
        {
            if (!HasBudgetFor(lane, next, firstMadeHere))
            {
                lane.IsOpen = false;
                continue;
            }

            lane.TryPop(out EntityEvent due);
            Dispatch(due);
            sent++;
        }

        return sent;
    }

    private EntityEventLane? NextDue(long tick, out EntityEvent next)
    {
        bool isWakeDue = _scripts.TryPeekDue(tick, out EntityEvent wake);
        if (!_entities.TryPeekDue(tick, out next))
        {
            next = wake;
            return isWakeDue ? _scripts : null;
        }

        if (!isWakeDue || EntityEventQueue.Precedes(next, wake))
            return _entities;

        next = wake;
        return _scripts;
    }

    // An event numbered below firstMadeHere was waiting when the drain began,
    // and those all sort ahead of what the drain makes. Each of the two has
    // the budget to itself, so a loop cannot use up what the waiting need.
    private bool HasBudgetFor(EntityEventLane lane, in EntityEvent next, long firstMadeHere)
    {
        if (next.Sequence < firstMadeHere)
        {
            if (lane.Waited == _maxDispatchesPerTick)
            {
                FallBehind(lane, next);
                return false;
            }

            lane.Waited++;
            return true;
        }

        if (lane.MadeHere == _maxDispatchesPerTick)
        {
            DropWhatThisDrainMade(lane, next, firstMadeHere);
            return false;
        }

        lane.MadeHere++;
        return true;
    }

    private void Queue(in EntityEvent input, Entity? target)
    {
        if (TryPush(_entities, input))
            _world.Trace?.Record(EntityTraceEvent.OfInput(EntityTraceKind.InputQueued, _world, input, target));
    }

    private bool TryPush(EntityEventLane lane, in EntityEvent made)
    {
        // It would sit in the queue until the level stops.
        if (made.Tick == WholeTicks.Never)
        {
            _log.NeverDue(made);
            return false;
        }

        if (lane.Count >= _maxPendingEvents)
        {
            RefuseForRoom(lane, made);
            return false;
        }

        lane.Push(made);
        return true;
    }

    private void Dispatch(in EntityEvent due)
    {
        if (due.Kind == EntityEventKind.Think)
        {
            // A think whose serial has moved on was superseded or cancelled.
            if (due.Entity is { } thinker && thinker.ThinkSerial == due.ThinkSerial)
                thinker.Think();

            return;
        }

        if (due.Kind == EntityEventKind.ScriptWake)
        {
            _world.Scripts?.Wake(due.Token);
            return;
        }

        var context = new EntityInputContext(due.Activator, due.Entity, due.Parameter);

        if (due.Target is { } instance)
        {
            // Unlisted: despawned, or its node left the scene. A name would
            // not find it either.
            if (instance.IndexedName is null)
            {
                _logger.LogDebug(
                    "'{Input}' was queued for an entity that is gone by now; it was not sent.", due.Input);
                _world.Trace?.Record(EntityTraceEvent.OfInput(EntityTraceKind.TargetMissing, _world, due, instance));
                return;
            }

            Deliver(instance, due, ref context);
            return;
        }

        _resolved.Clear();
        // Self and caller are the same entity for a connection.
        _world.Index?.Resolve(due.TargetName, due.Entity, due.Activator, due.Entity, _resolved);

        if (_resolved.Count == 0)
        {
            _logger.LogDebug(
                "Output '{Output}' names '{TargetName}', which matches nothing right now; '{Input}' was not sent.",
                due.Output, due.TargetName, due.Input);
            _world.Trace?.Record(EntityTraceEvent.OfInput(EntityTraceKind.TargetMissing, _world, due, null));
            return;
        }

        for (int i = 0; i < _resolved.Count; i++)
            Deliver(_resolved[i], due, ref context);
    }

    private void Deliver(Entity target, in EntityEvent due, ref EntityInputContext context)
    {
        // Before the entity handles it, so what it fires in answer comes after.
        _world.Trace?.Record(EntityTraceEvent.OfInput(EntityTraceKind.InputDelivered, _world, due, target));

        if (target.AcceptInput(due.Input, ref context))
            return;

        _world.Trace?.Record(EntityTraceEvent.OfInput(EntityTraceKind.InputRefused, _world, due, target));

        _logger.LogDebug(
            "Entity '{TargetName}' ({ClassName}) has no input '{Input}'.",
            target.TargetName, target.ClassName, due.Input);
    }

    // More was waiting than one tick may send. The rest stays queued and
    // sorts first on the next tick, so nothing is lost and the order holds.
    private void FallBehind(EntityEventLane lane, in EntityEvent next)
    {
        BudgetTripCount++;
        SayBehind(lane, next);
    }

    private void SayBehind(EntityEventLane lane, in EntityEvent next)
    {
        if (lane.ReportedBehind)
            return;

        lane.ReportedBehind = true;
        _log.FellBehind(ReferenceEquals(lane, _scripts), _maxDispatchesPerTick, next);
    }

    private void DropWhatThisDrainMade(EntityEventLane lane, in EntityEvent offender, long firstMadeHere)
    {
        BudgetTripCount++;

        // It stops at anything else: an event that was waiting, or one that
        // is due on a later tick.
        long tick = _world.TickNumber;
        int discarded = 0;
        int late = 0;
        while (lane.TryPeek(out EntityEvent next)
            && next.Tick <= tick
            && next.Sequence >= firstMadeHere)
        {
            lane.TryPop(out EntityEvent dropped);

            // A thread waits on a wake and would wait for good. A runtime
            // that has asked for another wake has lost nothing.
            if (dropped.Kind == EntityEventKind.ScriptWake && _world.Scripts?.Drop(dropped.Token) == false)
                late++;
            else
                discarded++;
        }

        DiscardedCount += discarded;
        if (discarded > 0)
            _log.DroppedWhatADrainMade(ReferenceEquals(lane, _scripts), _maxDispatchesPerTick, offender, discarded);
        if (late > 0)
            SayBehind(lane, offender);
    }

    private void RefuseForRoom(EntityEventLane lane, in EntityEvent made)
    {
        DiscardedCount++;
        if (lane.ReportedFull)
            return;

        lane.ReportedFull = true;
        _log.RefusedForRoom(ReferenceEquals(lane, _scripts), _maxPendingEvents, made);
    }
}
