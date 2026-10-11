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
    private int _maxPendingEvents = 65536;

    // Each is logged when it starts, not on every tick it lasts.
    private bool _isBehind;
    private bool _saidFull;

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

    public int PendingCount => _queue.Count;

    public void Clear()
    {
        _queue.Clear();
        _resolved.Clear();
        _sequence = 0;
        _isBehind = false;
        _saidFull = false;
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
        TryPush(new EntityEvent
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

    // An event numbered below firstMadeHere was waiting when the drain began,
    // and those all sort ahead of what the drain makes. Each of the two has
    // the budget to itself, so a loop cannot use up what the waiting need.
    public int DrainDue()
    {
        if (_queue.Count <= _maxPendingEvents / 2)
            _saidFull = false;

        long tick = _world.TickNumber;
        long firstMadeHere = _sequence;
        int waited = 0;
        int madeHere = 0;
        bool behind = false;

        while (_queue.TryPeek(out EntityEvent next) && next.Tick <= tick)
        {
            if (next.Sequence < firstMadeHere)
            {
                if (waited == _maxDispatchesPerTick)
                {
                    behind = true;
                    FallBehind(next);
                    break;
                }

                waited++;
            }
            else if (madeHere == _maxDispatchesPerTick)
            {
                DropWhatThisDrainMade(next, firstMadeHere);
                break;
            }
            else
            {
                madeHere++;
            }

            _queue.TryPop(out EntityEvent due);
            Dispatch(due);
        }

        _isBehind = behind;
        return waited + madeHere;
    }

    private void Queue(in EntityEvent input, Entity? target)
    {
        if (TryPush(input))
            _world.Trace?.Record(EntityTraceEvent.OfInput(EntityTraceKind.InputQueued, _world, input, target));
    }

    private bool TryPush(in EntityEvent made)
    {
        // It would sit in the queue until the level stops.
        if (made.Tick == WholeTicks.Never)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Not queued: {What} at '{TargetName}' waits longer than a level can count, so it " +
                    "would never come.",
                    Describe(made), NameOf(made));
            }

            return false;
        }

        if (_queue.Count >= _maxPendingEvents)
        {
            RefuseForRoom(made);
            return false;
        }

        _queue.Push(made);
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
    private void FallBehind(in EntityEvent next)
    {
        BudgetTripCount++;
        if (_isBehind)
            return;

        _logger.LogError(
            "More than {Budget} entity events were waiting for one tick. The rest are sent on the next " +
            "ticks, in the same order, so this level's logic runs late until it has caught up. First in " +
            "line is {What} at '{TargetName}'. This is what a loop that waits and multiplies looks like.",
            _maxDispatchesPerTick, Describe(next), NameOf(next));
    }

    private void DropWhatThisDrainMade(in EntityEvent offender, long firstMadeHere)
    {
        BudgetTripCount++;

        // It stops at anything else: an event that was waiting, or one that
        // is due on a later tick.
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

        _logger.LogError(
            "Entity dispatch budget of {Budget} was exhausted in one tick; the cascade was still firing " +
            "{What} at '{TargetName}'. The {Discarded} event(s) it had made and not sent were dropped. " +
            "What was already waiting has run. This is what a zero-delay loop between two entities " +
            "looks like.",
            _maxDispatchesPerTick, Describe(offender), NameOf(offender), discarded);
    }

    private void RefuseForRoom(in EntityEvent made)
    {
        DiscardedCount++;
        if (_saidFull)
            return;

        _saidFull = true;
        _logger.LogError(
            "The entity event queue is full at {Limit} events, so {What} at '{TargetName}' was dropped, " +
            "and so is every event made while there is no room. What is already waiting will still run. " +
            "This is what a loop that waits and multiplies looks like.",
            _maxPendingEvents, Describe(made), NameOf(made));
    }

    private static string Describe(in EntityEvent made) =>
        made.Kind == EntityEventKind.Think
            ? "a think"
            : made.Output.Length > 0
                ? $"output '{made.Output}' sending '{made.Input}'"
                : $"input '{made.Input}'";

    private static string NameOf(in EntityEvent made) =>
        made.Kind == EntityEventKind.Think
            ? made.Entity?.TargetName ?? ""
            : made.Target?.TargetName ?? made.TargetName;
}
