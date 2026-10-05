namespace SpectraEngine.Core.Entities;

/// <summary>
/// One thing an <see cref="EntityWorld"/> did, as an <see cref="IEntityTrace"/>
/// is told it. The strings are empty where they do not apply, never null.
/// </summary>
public readonly struct EntityTraceEvent
{
    /// <summary>What happened.</summary>
    public EntityTraceKind Kind { get; init; }

    /// <summary>The world's tick when it happened. Zero while the level spawns.</summary>
    public long Tick { get; init; }

    /// <summary>The world's time when it happened, in seconds.</summary>
    public float Time { get; init; }

    /// <summary>
    /// The world time the input is or was due at. Later than
    /// <see cref="Time"/> only for a queued input that waits. For a fired
    /// output it is <see cref="Time"/>.
    /// </summary>
    public float DueTime { get; init; }

    /// <summary>The entity whose output it is, or null for an input no output sent.</summary>
    public Entity? Source { get; init; }

    /// <summary>The output's name. Empty for an input no output sent.</summary>
    public string Output { get; init; }

    /// <summary>
    /// The target as the wire spells it, or as a host named it. Empty for an
    /// input queued for one entity and for a fired output.
    /// </summary>
    public string TargetName { get; init; }

    /// <summary>
    /// The entity the input reached, or the one it was queued for. Null where
    /// a name is all there is.
    /// </summary>
    public Entity? Target { get; init; }

    /// <summary>The input's name. Empty for a fired output.</summary>
    public string Input { get; init; }

    /// <summary>The argument sent with the input. Empty for none.</summary>
    public string Parameter { get; init; }

    /// <summary>Whoever started the chain, or null when no entity did.</summary>
    public Entity? Activator { get; init; }

    /// <summary>How many wires a fired output queued. Zero for every other kind.</summary>
    public int WiresQueued { get; init; }

    /// <summary>
    /// How many wires a fired output skipped because they had no fires left.
    /// Zero for every other kind.
    /// </summary>
    public int WiresSpent { get; init; }

    /// <summary>
    /// Which of the sender's wires carried the input: its index in the
    /// entity's connection list. -1 for a fired output and for an input no
    /// wire sent.
    /// </summary>
    public int Wire { get; init; }

    /// <summary>
    /// The queued input's number. Its queueing, each delivery, a refusal and a
    /// miss all carry the same one. -1 for a fired output.
    /// </summary>
    public long Sequence { get; init; }

    // Both builders are called only behind a null check on the world's trace,
    // so an unwatched world builds no event.
    internal static EntityTraceEvent OfInput(
        EntityTraceKind kind,
        EntityWorld world,
        in EntityEvent queued,
        Entity? target) => new()
    {
        Kind = kind,
        Tick = world.TickNumber,
        Time = world.Time,
        DueTime = queued.Time,
        Source = queued.Entity,
        Output = queued.Output,
        TargetName = queued.TargetName,
        Target = target,
        Input = queued.Input,
        Parameter = queued.Parameter,
        Activator = queued.Activator,
        Wire = queued.WireOrdinal - 1,
        Sequence = queued.Sequence,
    };

    // Counts the wires as they stand, so build it before they fire.
    internal static EntityTraceEvent OfOutput(Entity source, Entity activator, string output, EntityOutput? wired)
    {
        int live = wired?.LiveWireCount ?? 0;

        return new EntityTraceEvent
        {
            Kind = EntityTraceKind.OutputFired,
            Tick = source.World.TickNumber,
            Time = source.World.Time,
            DueTime = source.World.Time,
            Source = source,
            Output = output,
            TargetName = "",
            Input = "",
            Parameter = "",
            Activator = activator,
            WiresQueued = live,
            WiresSpent = (wired?.WireCount ?? 0) - live,
            Wire = -1,
            Sequence = -1,
        };
    }
}
