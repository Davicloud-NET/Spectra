using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

// What an entity world is waiting to send, and the sending of it once it is
// due. Thinks and inputs share one order: the tick, then the order made in.
internal sealed class EntityDispatcher
{
    private readonly EntityWorld _world;
    private readonly ILogger _logger;
    private readonly EntityEventQueue _queue = new();

    // Reused across dispatches. Safe: FireOutput queues, so no input handler
    // can resolve a target mid-dispatch.
    private readonly List<Entity> _resolved = [];

    private long _sequence;
    private int _maxDispatchesPerTick = 4096;

    public EntityDispatcher(EntityWorld world, ILogger logger)
    {
        _world = world;
        _logger = logger;
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

    public int BudgetTripCount { get; private set; }

    public int DiscardedCount { get; private set; }

    public int PendingCount => _queue.Count;

    public void Clear()
    {
        _queue.Clear();
        _resolved.Clear();
        _sequence = 0;
    }

    public void QueueInput(Entity target, string input, string parameter, Entity? activator)
    {
        var queued = new EntityEvent
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
        };

        _queue.Push(queued);
        _world.Trace?.Record(EntityTraceEvent.OfInput(EntityTraceKind.InputQueued, _world, queued, target));
    }

    public void QueueInput(string targetName, string input, string parameter, float delay)
    {
        var queued = new EntityEvent
        {
            Tick = _world.TickAfter(delay),
            Sequence = _sequence++,
            Kind = EntityEventKind.Input,
            TargetName = targetName,
            Input = input,
            Parameter = parameter,
            Output = "",
        };

        _queue.Push(queued);
        _world.Trace?.Record(EntityTraceEvent.OfInput(EntityTraceKind.InputQueued, _world, queued, null));
    }

    public void ScheduleThink(Entity entity, long tick, int serial) =>
        _queue.Push(new EntityEvent
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

    public void ScheduleOutput(
        Entity caller,
        Entity? activator,
        string output,
        in EntityConnection wire,
        int wireIndex,
        string? parameterOverride)
    {
        var queued = new EntityEvent
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
        };

        _queue.Push(queued);
        _world.Trace?.Record(EntityTraceEvent.OfInput(EntityTraceKind.InputQueued, _world, queued, null));
    }

    // Runs everything due this tick and returns how many events that was.
    // What was waiting when the drain began always runs: it sorts ahead of
    // anything made here, and only what is made here counts against the
    // budget. The sequence number tells the two apart, whatever kind of event
    // it is.
    public int DrainDue()
    {
        long tick = _world.TickNumber;
        long firstMadeHere = _sequence;
        int dispatched = 0;
        int madeHere = 0;

        while (_queue.TryPeek(out EntityEvent next) && next.Tick <= tick)
        {
            if (next.Sequence >= firstMadeHere)
            {
                if (madeHere == _maxDispatchesPerTick)
                {
                    TripBudget(next, firstMadeHere);
                    break;
                }

                madeHere++;
            }

            _queue.TryPop(out EntityEvent due);
            dispatched++;
            Dispatch(due);
        }

        return dispatched;
    }

    private void Dispatch(in EntityEvent due)
    {
        if (due.Kind == EntityEventKind.Think)
        {
            Entity thinker = due.Entity!;
            // Superseded by a later SetNextThink or CancelThink.
            if (thinker.ThinkSerial != due.ThinkSerial)
                return;

            thinker.Think();
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
        _world.Index!.Resolve(due.TargetName, due.Entity, due.Activator, due.Entity, _resolved);

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

    private void TripBudget(in EntityEvent offender, long firstMadeHere)
    {
        BudgetTripCount++;

        // Drop what this drain made and has not sent. It stops at anything
        // else: an event that was waiting, or one that is due on a later tick.
        long tick = _world.TickNumber;
        int discarded = 0;
        while (_queue.TryPeek(out EntityEvent next)
            && next.Tick <= tick
            && next.Sequence >= firstMadeHere)
        {
            _queue.TryPop(out _);
            discarded++;
        }

        DiscardedCount += discarded;

        string offenderName = offender.Kind == EntityEventKind.Think
            ? offender.Entity?.TargetName ?? ""
            : offender.Target?.TargetName ?? offender.TargetName;
        string what = offender.Kind == EntityEventKind.Think
            ? "a think"
            : offender.Output.Length > 0
                ? $"output '{offender.Output}' sending '{offender.Input}'"
                : $"input '{offender.Input}'";

        _logger.LogError(
            "Entity dispatch budget of {Budget} was exhausted in one tick; the cascade was still firing " +
            "{What} at '{TargetName}'. The {Discarded} event(s) it had made and not sent were dropped. " +
            "What was already waiting has run. This is what a zero-delay loop between two entities " +
            "looks like.",
            _maxDispatchesPerTick, what, offenderName, discarded);
    }
}
