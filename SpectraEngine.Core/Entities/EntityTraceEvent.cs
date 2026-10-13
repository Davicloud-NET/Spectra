using System;

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

    /// <summary>
    /// The tick the input is or was due on. Later than <see cref="Tick"/>
    /// only for a queued input that waits. For a fired output it is
    /// <see cref="Tick"/>.
    /// </summary>
    public long DueTick { get; init; }

    /// <summary>The world time of <see cref="Tick"/>, in seconds.</summary>
    public float Time { get; init; }

    /// <summary>
    /// The world time of <see cref="DueTick"/>, in seconds. For a person to
    /// read: compare the ticks to tell whether an input waits.
    /// </summary>
    public float DueTime { get; init; }

    /// <summary>
    /// How long a queued input waits, in seconds: the ticks from
    /// <see cref="Tick"/> to <see cref="DueTick"/>. Zero when it is due.
    /// </summary>
    public float Wait { get; init; }

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

    /// <summary>
    /// The file of the script that sent the input, as a content path. Empty
    /// for an input no script sent, and for a fired output.
    /// </summary>
    public string SenderScript { get; init; }

    /// <summary>The line in <see cref="SenderScript"/> that sent the input, or 0 when it is not known.</summary>
    public int SenderLine { get; init; }

    /// <summary>
    /// The node of the script that sent the input. Empty for an input no
    /// script sent, and when the sender did not say which node it is.
    /// </summary>
    public Guid SenderNode { get; init; }

    /// <summary>
    /// How many listeners of scripts a fired output queued a call for: the
    /// connected functions and waiting threads the queue had room for. Zero
    /// for every other kind.
    /// </summary>
    public int ListenersQueued { get; init; }

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
        DueTick = queued.Tick,
        Time = world.Time,
        DueTime = world.TimeAt(queued.Tick),
        Wait = (float)(Math.Max(queued.Tick - world.TickNumber, 0L) * (double)world.FixedDeltaTime),
        Source = queued.Entity,
        Output = queued.Output,
        TargetName = queued.TargetName,
        Target = target,
        Input = queued.Input,
        Parameter = queued.Parameter,
        Activator = queued.Activator,
        Wire = queued.WireOrdinal - 1,
        Sequence = queued.Sequence,
        SenderScript = queued.SenderScript ?? "",
        SenderLine = queued.SenderLine,
        SenderNode = queued.SenderNode,
    };

    // Counts the wires as they stand, so build it before they fire.
    internal static EntityTraceEvent OfOutput(
        Entity source, Entity activator, string output, EntityOutput? wired, int listeners)
    {
        int live = wired?.LiveWireCount ?? 0;

        return new EntityTraceEvent
        {
            Kind = EntityTraceKind.OutputFired,
            Tick = source.World.TickNumber,
            DueTick = source.World.TickNumber,
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
            SenderScript = "",
            ListenersQueued = listeners,
        };
    }
}
