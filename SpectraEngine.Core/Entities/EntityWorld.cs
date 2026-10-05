using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// The live entity graph over one <see cref="Scene"/>: builds an
/// <see cref="Entity"/> per node carrying <see cref="EntityData"/>, resolves
/// their wiring, and drains their think wakeups and output events in a
/// deterministic total order. Instances exist only while the world is active
/// and never write back to the authored data. A node they move goes back to
/// its authored transform when the world deactivates. Render thread only.
/// </summary>
public sealed class EntityWorld
{
    private readonly Scene.Scene _scene;
    private readonly ILogger _logger;
    private readonly EntityCatalog _catalog;

    private readonly List<Entity> _entities = [];
    private readonly EntityEventQueue _queue = new();

    // Reused across dispatches. Safe: FireOutput queues, so no input handler
    // can resolve a target mid-dispatch.
    private readonly List<Entity> _resolved = [];

    private readonly List<SceneNode> _pendingSpawn = [];
    private readonly List<SceneNode> _spawnScratch = [];
    private readonly List<Entity> _pendingDespawn = [];
    private readonly List<Entity> _despawnScratch = [];

    // Warn once per class name, not per entity.
    private readonly HashSet<string> _warnedMissingClasses = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warnedPlaceholderInputs = new(StringComparer.Ordinal);

    private readonly MovedNodeJournal _movedNodes = new();

    // Log a refused move once per node, not once per tick.
    private readonly HashSet<Guid> _refusedMoveNodes = [];

    private TargetNameIndex? _index;
    private long _sequence;
    private float _time;
    private int _tickingCount;
    private int _maxDispatchesPerTick = 4096;

    // True while Deactivate runs OnRemove. IsActive is already false by then.
    private bool _removing;

    /// <param name="scene">The scene whose nodes carry the authored entities.</param>
    /// <param name="logger">Where refusals, missing classes and budget trips go.</param>
    /// <param name="catalog">
    /// The classes this world can build, or null for
    /// <see cref="EntityCatalog.Shared"/>.
    /// </param>
    public EntityWorld(Scene.Scene scene, ILogger logger, EntityCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(logger);

        _scene = scene;
        _logger = logger;
        _catalog = catalog ?? EntityCatalog.Shared;
        Touches = new TouchTracker(this);
        Sounds = new SoundEmitters(this);
    }

    /// <summary>The scene this world runs over.</summary>
    public Scene.Scene Scene => _scene;

    /// <summary>The classes this world can build.</summary>
    public EntityCatalog Catalog => _catalog;

    /// <summary>Whether <see cref="Activate"/> has run and <see cref="Deactivate"/> has not.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Seconds of ticked time since <see cref="Activate"/>. A sum of floats:
    /// count <see cref="TickNumber"/> for anything that must land on a tick.
    /// </summary>
    public float Time => _time;

    /// <summary>
    /// The tick being run, or the last one run. Zero while entities spawn, one
    /// during the first <see cref="Tick"/>.
    /// </summary>
    public long TickNumber { get; private set; }

    /// <summary>
    /// The fixed step in seconds: what <see cref="Tick"/> was last given, and
    /// the engine's own step before the first tick.
    /// </summary>
    public float FixedDeltaTime { get; private set; } = PhysicsDefaults.FixedDeltaTime;

    /// <summary>
    /// The player, for entities that sense or move it, or null when the host
    /// has given none. A host that assigns it after <see cref="Activate"/>
    /// leaves it null while entities spawn. <see cref="Deactivate"/> clears it.
    /// </summary>
    public IPlayerPresence? Player { get; set; }

    /// <summary>
    /// Which sensing entities the player is inside. An entity registers its
    /// brushes here to hear touches start and end.
    /// </summary>
    public TouchTracker Touches { get; }

    /// <summary>
    /// Every live entity, in the traversal order they were built in. Empty while
    /// the world is inactive.
    /// </summary>
    public IReadOnlyList<Entity> Entities => _entities;

    /// <summary>The name index, or null while the world is inactive.</summary>
    public TargetNameIndex? Index => _index;

    /// <summary>
    /// Is told of every output fired and every input queued, sent or lost, or
    /// null when nothing watches. Set it before <see cref="Activate"/> to hear
    /// what fires while entities spawn.
    /// </summary>
    public IEntityTrace? Trace { get; set; }

    /// <summary>
    /// The sounds playing in the level. Entities start and stop them here.
    /// <see cref="Deactivate"/> empties it.
    /// </summary>
    public SoundEmitters Sounds { get; }

    /// <summary>
    /// What entities ask about a sound: whether it is there, how long it is
    /// and where its markers are. Null when the host has given none, and then
    /// no sound can play. Set it before <see cref="Activate"/>, so a sound
    /// that plays as the level spawns finds it.
    /// </summary>
    public ISoundCatalog? SoundCatalog { get; set; }

    /// <summary>
    /// How many events one <see cref="Tick"/> may dispatch before the cascade
    /// is treated as a runaway loop, logged and dropped.
    /// </summary>
    public int MaxDispatchesPerTick
    {
        get => _maxDispatchesPerTick;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxDispatchesPerTick = value;
        }
    }

    /// <summary>How many ticks have hit <see cref="MaxDispatchesPerTick"/>.</summary>
    public int DispatchBudgetTripCount { get; private set; }

    /// <summary>How many events a budget trip has discarded over this world's life.</summary>
    public int DiscardedEventCount { get; private set; }

    /// <summary>How many events the most recent <see cref="Tick"/> dispatched.</summary>
    public int LastTickDispatchCount { get; private set; }

    /// <summary>Events waiting for their time to come.</summary>
    public int PendingEventCount => _queue.Count;

    /// <summary>Nodes queued for a deferred spawn.</summary>
    public int PendingSpawnCount => _pendingSpawn.Count;

    /// <summary>Entities queued for a deferred despawn.</summary>
    public int PendingDespawnCount => _pendingDespawn.Count;

    /// <summary>Entities that have asked for <see cref="Entity.OnTick"/>.</summary>
    public int TickingEntityCount => _tickingCount;

    /// <summary>Nodes that will go back to their authored transform on <see cref="Deactivate"/>.</summary>
    public int MovedNodeCount => _movedNodes.Count;

    /// <summary>How many moves <see cref="SetLocalTransform"/> has refused since <see cref="Activate"/>.</summary>
    public int RefusedMoveCount { get; private set; }

    /// <summary>
    /// Builds every entity in the scene and brings it to life, in four phases.
    /// </summary>
    /// <exception cref="InvalidOperationException">The world is already active.</exception>
    public void Activate()
    {
        if (IsActive)
            throw new InvalidOperationException("This entity world is already active.");

        _time = 0f;
        TickNumber = 0;
        FixedDeltaTime = PhysicsDefaults.FixedDeltaTime;
        _sequence = 0;
        _tickingCount = 0;
        RefusedMoveCount = 0;
        _refusedMoveNodes.Clear();
        Touches.Clear();
        Sounds.Clear();
        _queue.Clear();
        _entities.Clear();
        _pendingSpawn.Clear();
        _pendingDespawn.Clear();

        // The four phases must stay separate: in one pass, the first entity's
        // spawn fires at targets that don't exist yet and delivers nothing.

        // 1: construct and parse. No index yet, so no lookups.
        foreach (SceneNode node in _scene.Root.Traverse())
        {
            if (node.Entity is not { } data)
                continue;

            Entity entity = Build(node, data);
            _entities.Add(entity);
            ParseKeyvalues(entity, data);
        }

        // 2: index every entity, then copy the wires.
        _index = new TargetNameIndex(_scene);
        _scene.NodeAdded += Sounds.OnNodeAdded;
        for (int i = 0; i < _entities.Count; i++)
            _index.Register(_entities[i]);
        for (int i = 0; i < _entities.Count; i++)
            _entities[i].BuildOutputs(_entities[i].Data.Connections);

        // Active before OnSpawn, so a spawn can schedule a think or fire an output.
        IsActive = true;

        // 3: spawn, in traversal order.
        for (int i = 0; i < _entities.Count; i++)
            _entities[i].OnSpawn();

        // 4: activate, once every spawn has finished.
        for (int i = 0; i < _entities.Count; i++)
            _entities[i].OnActivate();

        Trace?.EndTick(TickNumber, _time);
    }

    /// <summary>
    /// Runs <see cref="Entity.OnRemove"/> on everything, puts every moved node
    /// back where it was authored, unsubscribes from the scene and drops every
    /// instance. Harmless on an inactive world.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive)
            return;

        IsActive = false;

        // Before OnRemove: a stop tells no listener that its touch ended.
        Touches.Clear();

        _removing = true;
        for (int i = 0; i < _entities.Count; i++)
            _entities[i].OnRemove();
        _removing = false;

        Sounds.Clear();

        // After every OnRemove: an entity being removed must still read the
        // pose it moved to, not the authored one.
        _movedNodes.Restore();

        _scene.NodeAdded -= Sounds.OnNodeAdded;
        _index?.Dispose();
        _index = null;
        Player = null;

        _entities.Clear();
        _tickingCount = 0;
        _queue.Clear();
        _pendingSpawn.Clear();
        _pendingDespawn.Clear();
        _resolved.Clear();
    }

    /// <summary>
    /// Runs one tick of <paramref name="fixedDt"/> seconds: finds the touches
    /// that started and ended since the last tick, delivers everything now
    /// due, calls <see cref="Entity.OnTick"/> on the entities that asked
    /// for it, in <see cref="Entities"/> order, then drains the deferred spawn
    /// and despawn queues.
    /// </summary>
    /// <exception cref="InvalidOperationException">The world is not active.</exception>
    public void Tick(float fixedDt)
    {
        if (!IsActive)
            throw new InvalidOperationException("Tick on an entity world that is not active.");

        // A playing sound counts its frames a tick from the step. One that
        // started while the level spawned had only the engine's own to go by.
        if (fixedDt != FixedDeltaTime)
            Sounds.OnStepChanged(TickNumber, fixedDt);

        TickNumber++;
        FixedDeltaTime = fixedDt;
        _time += fixedDt;

        // Before the drain: an output a touch fires is delivered this tick.
        Touches.Update();

        int dispatched = 0;
        while (_queue.TryPeek(out EntityEvent next) && next.Time <= _time)
        {
            if (dispatched >= _maxDispatchesPerTick)
            {
                TripDispatchBudget(next);
                break;
            }

            _queue.TryPop(out EntityEvent due);
            dispatched++;
            Dispatch(due);
        }

        LastTickDispatchCount = dispatched;

        // After the drain: an input delivered this tick moves its entity this
        // tick. Spawns and despawns are deferred, so the list holds still.
        if (_tickingCount > 0)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                if (_entities[i].IsTicking)
                    _entities[i].OnTick();
            }
        }

        DrainDeferred();

        Trace?.EndTick(TickNumber, _time);
    }

    /// <summary>
    /// Moves a node while the level plays. The only way a running entity may
    /// move one: the first move of a node records its authored transform, and
    /// <see cref="Deactivate"/> puts it back.
    /// </summary>
    /// <returns>
    /// False when the move was refused: a world brush sits at or below the
    /// node, or the scale changes above a brush. Nothing is written then.
    /// </returns>
    /// <exception cref="InvalidOperationException">The world is not active.</exception>
    public bool SetLocalTransform(SceneNode node, in Transform value)
    {
        ArgumentNullException.ThrowIfNull(node);
        ThrowIfStopped(nameof(SetLocalTransform));

        // A moving world brush recompiles the level every tick, and the
        // picture stays right while it does.
        if (node.SubtreeStaticWorldBrushCount > 0)
        {
            return RefuseMove(node,
                "a world brush sits at or below it, and only parts can move while a level plays. " +
                "Convert the brush to a part");
        }

        if (node.SubtreeBrushCount > 0 && value.Scale != node.LocalScale)
        {
            return RefuseMove(node,
                "the move changes its scale, and a node cannot scale a brush at or below it. " +
                "Resize the brush instead");
        }

        _movedNodes.Record(node);
        node.LocalTransform = value;
        return true;
    }

    /// <summary>
    /// Queues an input for one entity, with no caller. It is delivered by the
    /// next drain, in the order it was queued among everything else due then.
    /// </summary>
    /// <param name="target">Who receives it. An entity that is gone by then gets nothing.</param>
    /// <param name="input">The input's name.</param>
    /// <param name="parameter">The argument, or null for none.</param>
    /// <param name="activator">Whoever started the chain, or null when no entity did.</param>
    /// <exception cref="InvalidOperationException">The world is not active.</exception>
    public void QueueInput(Entity target, string input, string? parameter = null, Entity? activator = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(input);
        ThrowIfStopped(nameof(QueueInput));

        if (!ReferenceEquals(target.World, this))
        {
            throw new ArgumentException(
                "The entity belongs to another entity world, or to none.", nameof(target));
        }

        var queued = new EntityEvent
        {
            Time = _time,
            Sequence = _sequence++,
            Kind = EntityEventKind.Input,
            Target = target,
            Activator = activator,
            TargetName = "",
            Input = input,
            Parameter = parameter ?? "",
            Output = "",
        };

        _queue.Push(queued);
        Trace?.Record(EntityTraceEvent.OfInput(EntityTraceKind.InputQueued, this, queued, target));
    }

    /// <summary>
    /// Queues an input for every entity a name resolves to, with no caller and
    /// no activator. The name resolves when the input comes due, the way a
    /// wire's target does.
    /// </summary>
    /// <param name="targetName">A name, or a prefix ending in <c>*</c>.</param>
    /// <param name="input">The input's name.</param>
    /// <param name="parameter">The argument, or null for none.</param>
    /// <param name="delay">Seconds to wait. Zero delivers on the next drain.</param>
    /// <exception cref="InvalidOperationException">The world is not active.</exception>
    public void QueueInput(string targetName, string input, string? parameter = null, float delay = 0f)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetName);
        ArgumentNullException.ThrowIfNull(input);
        ThrowIfStopped(nameof(QueueInput));

        var queued = new EntityEvent
        {
            // Negative and NaN delays become zero.
            Time = _time + (delay > 0f ? delay : 0f),
            Sequence = _sequence++,
            Kind = EntityEventKind.Input,
            TargetName = targetName,
            Input = input,
            Parameter = parameter ?? "",
            Output = "",
        };

        _queue.Push(queued);
        Trace?.Record(EntityTraceEvent.OfInput(EntityTraceKind.InputQueued, this, queued, null));
    }

    /// <summary>
    /// Finds the entity that owns <paramref name="node"/>: the one on the node
    /// itself, or on the nearest node above it that carries an entity. False
    /// when there is none, or the world is not active.
    /// </summary>
    public bool TryFindOwner(SceneNode node, [MaybeNullWhen(false)] out Entity owner)
    {
        ArgumentNullException.ThrowIfNull(node);

        owner = null;
        if (_index is not { } index)
            return false;

        for (SceneNode? walk = node; walk is not null; walk = walk.Parent)
        {
            if (index.TryGetByNodeId(walk.Id, out Entity? found) && ReferenceEquals(found.Node, walk))
            {
                owner = found;
                return true;
            }

            // Authored as an entity but not running: it still owns what is
            // below it, so the search does not pass it.
            if (walk.Entity is not null)
                return false;
        }

        return false;
    }

    /// <summary>
    /// Asks for an entity to be built for <paramref name="node"/> at the end of
    /// the current tick. Use this from a scene event handler, which must not
    /// change the graph itself.
    /// </summary>
    public void QueueSpawn(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _pendingSpawn.Add(node);
    }

    /// <summary>Asks for <paramref name="entity"/> to be removed at the end of the current tick.</summary>
    public void QueueDespawn(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _pendingDespawn.Add(entity);
    }

    internal void ScheduleThink(Entity entity, float time, int serial) =>
        _queue.Push(new EntityEvent
        {
            // A NaN time would corrupt the heap's ordering. Use now instead.
            Time = float.IsFinite(time) ? time : _time,
            Sequence = _sequence++,
            Kind = EntityEventKind.Think,
            Entity = entity,
            ThinkSerial = serial,
            TargetName = "",
            Input = "",
            Parameter = "",
            Output = "",
        });

    internal void ScheduleOutput(
        Entity caller,
        Entity? activator,
        string output,
        in EntityConnection wire,
        int wireIndex,
        string? parameterOverride)
    {
        // Negative and NaN delays become zero.
        float delay = wire.Delay > 0f ? wire.Delay : 0f;

        var queued = new EntityEvent
        {
            Time = _time + delay,
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
        Trace?.Record(EntityTraceEvent.OfInput(EntityTraceKind.InputQueued, this, queued, null));
    }

    internal void SetTicking(Entity entity, bool on)
    {
        if (entity.IsTicking == on)
            return;

        entity.IsTicking = on;
        _tickingCount += on ? 1 : -1;
    }

    // OnRemove still runs inside the world: what it moves is put back and
    // what it queues or plays is dropped.
    internal bool IsRunningEntities => IsActive || _removing;

    private void ThrowIfStopped(string call)
    {
        if (!IsRunningEntities)
            throw new InvalidOperationException($"{call} on an entity world that is not active.");
    }

    private bool RefuseMove(SceneNode node, string why)
    {
        RefusedMoveCount++;

        if (_refusedMoveNodes.Add(node.Id))
            _logger.LogError("An entity tried to move '{NodeName}' and was refused: {Why}.", node.Name, why);

        return false;
    }

    internal void ReportRefusedKeyvalue(Entity entity, string key, string value) =>
        _logger.LogWarning(
            "Entity '{TargetName}' ({ClassName}) cannot read keyvalue '{Key}' = '{Value}'; keeping the default.",
            entity.TargetName, entity.ClassName, key, value);

    internal void ReportWarning(Entity entity, string problem) =>
        _logger.LogWarning(
            "Entity '{TargetName}' ({ClassName}) {Problem}.", entity.TargetName, entity.ClassName, problem);

    internal void ReportPlaceholderInput(Entity entity, string input)
    {
        if (!_warnedPlaceholderInputs.Add(entity.ClassName))
            return;

        _logger.LogWarning(
            "Entity class '{ClassName}' is not registered in this build, so '{TargetName}' cannot accept " +
            "'{Input}' or any other input. Its data is kept and re-saves unchanged.",
            entity.ClassName, entity.TargetName, input);
    }

    private Entity Build(SceneNode node, EntityData data)
    {
        Entity entity;
        if (data.ClassName.Length > 0 && _catalog.TryCreate(data.ClassName, out Entity? created))
        {
            entity = created;
        }
        else
        {
            entity = new PlaceholderEntity();
            if (data.ClassName.Length > 0 && _warnedMissingClasses.Add(data.ClassName))
            {
                _logger.LogWarning(
                    "No entity class named '{ClassName}' is registered; '{NodeName}' keeps its data as a " +
                    "placeholder and behaves as nothing.",
                    data.ClassName, node.Name);
            }
        }

        entity.Bind(node, this, data);
        return entity;
    }

    private void ParseKeyvalues(Entity entity, EntityData data)
    {
        IReadOnlyList<KeyValuePair<string, string>> keyvalues = data.Keyvalues;
        for (int i = 0; i < keyvalues.Count; i++)
        {
            KeyValuePair<string, string> pair = keyvalues[i];
            if (entity.ParseKeyValue(pair.Key, pair.Value))
                continue;

            // Debug, not a warning: unknown keys are normal and are kept.
            _logger.LogDebug(
                "Entity '{TargetName}' ({ClassName}) has no property '{Key}'; the value is kept but unused.",
                entity.TargetName, entity.ClassName, pair.Key);
        }
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
                Trace?.Record(EntityTraceEvent.OfInput(EntityTraceKind.TargetMissing, this, due, instance));
                return;
            }

            Deliver(instance, due, ref context);
            return;
        }

        _resolved.Clear();
        // Self and caller are the same entity for a connection.
        _index!.Resolve(due.TargetName, due.Entity, due.Activator, due.Entity, _resolved);

        if (_resolved.Count == 0)
        {
            _logger.LogDebug(
                "Output '{Output}' names '{TargetName}', which matches nothing right now; '{Input}' was not sent.",
                due.Output, due.TargetName, due.Input);
            Trace?.Record(EntityTraceEvent.OfInput(EntityTraceKind.TargetMissing, this, due, null));
            return;
        }

        for (int i = 0; i < _resolved.Count; i++)
            Deliver(_resolved[i], due, ref context);
    }

    private void Deliver(Entity target, in EntityEvent due, ref EntityInputContext context)
    {
        // Before the entity handles it, so what it fires in answer comes after.
        Trace?.Record(EntityTraceEvent.OfInput(EntityTraceKind.InputDelivered, this, due, target));

        if (target.AcceptInput(due.Input, ref context))
            return;

        Trace?.Record(EntityTraceEvent.OfInput(EntityTraceKind.InputRefused, this, due, target));

        _logger.LogDebug(
            "Entity '{TargetName}' ({ClassName}) has no input '{Input}'.",
            target.TargetName, target.ClassName, due.Input);
    }

    private void TripDispatchBudget(in EntityEvent offender)
    {
        DispatchBudgetTripCount++;

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
            "{What} at '{TargetName}'. Everything still due this tick has been dropped. This is what a " +
            "zero-delay loop between two entities looks like.",
            _maxDispatchesPerTick, what, offenderName);

        // Drop only what is due now. Later events are ordinary work, so a level
        // with one bad relay keeps running.
        int discarded = 0;
        while (_queue.TryPeek(out EntityEvent next) && next.Time <= _time)
        {
            _queue.TryPop(out _);
            discarded++;
        }

        DiscardedEventCount += discarded;
    }

    private void DrainDeferred()
    {
        // Drain from scratch copies, so work queued by the drain waits a tick.
        if (_pendingDespawn.Count > 0)
        {
            _despawnScratch.AddRange(_pendingDespawn);
            _pendingDespawn.Clear();

            for (int i = 0; i < _despawnScratch.Count; i++)
                Despawn(_despawnScratch[i]);

            _despawnScratch.Clear();
        }

        // Despawns first, so a spawn reusing a name doesn't overlap the old holder.
        if (_pendingSpawn.Count > 0)
        {
            _spawnScratch.AddRange(_pendingSpawn);
            _pendingSpawn.Clear();

            for (int i = 0; i < _spawnScratch.Count; i++)
                Spawn(_spawnScratch[i]);

            _spawnScratch.Clear();
        }
    }

    private void Spawn(SceneNode node)
    {
        if (node.Entity is not { } data)
            return;

        // May have been queued twice, or already built by Activate.
        if (_index!.TryGetByNodeId(node.Id, out _))
            return;

        Entity entity = Build(node, data);
        _entities.Add(entity);
        ParseKeyvalues(entity, data);
        _index.Register(entity);
        entity.BuildOutputs(data.Connections);
        entity.OnSpawn();
        entity.OnActivate();
    }

    private void Despawn(Entity entity)
    {
        int at = _entities.IndexOf(entity);
        if (at < 0)
            return;

        // Before OnRemove: a touch that started must be told it ended.
        Touches.Unregister(entity);
        entity.OnRemove();
        SetTicking(entity, false);
        _index!.Unregister(entity);
        _entities.RemoveAt(at);
    }
}
