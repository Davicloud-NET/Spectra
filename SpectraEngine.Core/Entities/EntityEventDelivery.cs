using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

// Sends an event that has come due: a think to its entity, a wake or a
// listener's call to the script runtime, an input to every entity its
// target resolves to. EntityDispatcher says when.
internal sealed class EntityEventDelivery
{
    private readonly EntityWorld _world;
    private readonly ILogger _logger;

    // Reused across dispatches. Safe: FireOutput queues, so no input handler
    // can resolve a target mid-dispatch.
    private readonly List<Entity> _resolved = [];

    public EntityEventDelivery(EntityWorld world, ILogger logger)
    {
        _world = world;
        _logger = logger;
    }

    public void Clear() => _resolved.Clear();

    public void Send(in EntityEvent due)
    {
        if (due.Kind == EntityEventKind.Think)
        {
            // A think whose serial has moved on was superseded or cancelled.
            if (due.Entity is { } thinker && thinker.ThinkSerial == due.ThinkSerial)
                thinker.Think();

            return;
        }

        if (due.Kind is EntityEventKind.ScriptWake or EntityEventKind.ScriptCall)
        {
            _world.Scripts?.Wake(due.Token);
            return;
        }

        if (due.Kind == EntityEventKind.ScriptSignal)
        {
            _world.Scripts?.Hear(due.Token, due.Parameter);
            return;
        }

        if (due.Kind == EntityEventKind.ScriptTouch)
        {
            // The player as it is now. It may have left since the touch.
            IPlayerPresence? player = due.Visitor == TouchVisitor.PlayerId ? _world.Player : null;
            _world.Scripts?.HearTouch(due.Token, new TouchVisitor(due.Visitor, player));
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
}
