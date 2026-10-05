using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// One live entity: the behaviour built from a node's <see cref="EntityData"/>
/// while an <see cref="EntityWorld"/> is active. Built from the authored data
/// and never writes back into it. Render thread only.
/// </summary>
public abstract class Entity
{
    // First-appearance order over the authored connection list.
    private readonly List<EntityOutput> _outputs = [];

    private float _nextThinkTime = float.PositiveInfinity;
    private int _thinkSerial;

    /// <summary>
    /// The node this entity is. Repointed when the node is deleted and restored
    /// under the same id.
    /// </summary>
    public SceneNode Node { get; private set; } = null!;

    /// <summary>The world running this entity.</summary>
    public EntityWorld World { get; private set; } = null!;

    /// <summary>The authored data this instance was built from. Read only at runtime.</summary>
    public EntityData Data { get; private set; } = null!;

    /// <summary>The class this entity is, as the map spells it.</summary>
    public string ClassName => Data.ClassName;

    /// <summary>
    /// The name other entities' outputs address this one by: the node's name.
    /// Duplicates are legal, and firing at a name fires every match.
    /// </summary>
    public string TargetName => Node.Name;

    /// <summary>
    /// This entity's outputs and the runtime copy of the wires leaving them, in
    /// first-appearance order over the authored connection list.
    /// </summary>
    public IReadOnlyList<EntityOutput> Outputs => _outputs;

    /// <summary>
    /// The world time this entity next thinks at, or
    /// <see cref="float.PositiveInfinity"/> when it has no think pending.
    /// </summary>
    public float NextThinkTime => _nextThinkTime;

    // A heap can't remove an entry, so a superseded think stays queued. The
    // serial tells the dispatcher to drop it.
    internal int ThinkSerial => _thinkSerial;

    // Name this entity is listed under in the target-name index, null while
    // unlisted. A rename event carries only the new name, so the old one is
    // kept here to find the stale entry.
    internal string? IndexedName { get; set; }

    // Written only by EntityWorld.SetTicking, which keeps the count.
    internal bool IsTicking { get; set; }

    /// <summary>
    /// Called once, after every entity in the world exists and has parsed its
    /// keyvalues. Every target name resolves by now. An output fired here is
    /// queued and delivered on the first tick.
    /// </summary>
    protected internal virtual void OnSpawn()
    {
    }

    /// <summary>Called once, after every entity in the world has spawned.</summary>
    protected internal virtual void OnActivate()
    {
    }

    /// <summary>Called once, when the world deactivates or this entity is despawned.</summary>
    protected internal virtual void OnRemove()
    {
    }

    /// <summary>
    /// Called at the time <see cref="SetNextThink"/> asked for. A think does
    /// not reschedule itself.
    /// </summary>
    protected internal virtual void Think()
    {
    }

    /// <summary>
    /// Called every tick while <see cref="SetTicking"/> is on, after the
    /// inputs and thinks due that tick. Entities tick in the order the world
    /// lists them. Anything that moves does it here, counting ticks.
    /// </summary>
    protected internal virtual void OnTick()
    {
    }

    /// <summary>
    /// Reads one authored keyvalue. Returns whether this class recognises
    /// <paramref name="key"/>. A recognised key with an unreadable value should
    /// call <see cref="RefuseKeyvalue"/>, keep the default and return true.
    /// </summary>
    public virtual bool ParseKeyValue(string key, string value) => false;

    /// <summary>
    /// Receives one input. Returns whether this class recognised
    /// <paramref name="input"/>.
    /// </summary>
    public virtual bool AcceptInput(string input, ref EntityInputContext context) => false;

    /// <summary>
    /// Writes this entity's live state for a person to read, one named value
    /// at a time. Must change nothing. The base class has no state.
    /// </summary>
    public virtual void DescribeState(EntityStateWriter state)
    {
    }

    /// <summary>
    /// Asks to be woken at world time <paramref name="time"/>, replacing any
    /// think already pending. A time already past fires on the next tick.
    /// </summary>
    public void SetNextThink(float time)
    {
        _nextThinkTime = time;
        _thinkSerial++;
        World.ScheduleThink(this, time, _thinkSerial);
    }

    /// <summary>Asks to be woken <paramref name="delay"/> seconds from now.</summary>
    public void SetNextThinkIn(float delay) => SetNextThink(World.Time + delay);

    /// <summary>Drops any pending think.</summary>
    public void CancelThink()
    {
        // Bumping the serial is the cancel; the queued entry is dropped when it surfaces.
        _thinkSerial++;
        _nextThinkTime = float.PositiveInfinity;
    }

    /// <summary>
    /// Fires <paramref name="output"/> along this entity's wires. Every wire
    /// queues an event, even at zero delay; nothing is delivered inside this call.
    /// </summary>
    /// <param name="output">The output's name.</param>
    /// <param name="activator">Whoever started the chain. Null means this entity.</param>
    /// <param name="parameterOverride">
    /// Replaces the parameter each wire authored, or null to send what the map
    /// says. Empty is a real, blank argument.
    /// </param>
    public void FireOutput(string output, Entity? activator = null, string? parameterOverride = null)
    {
        ArgumentNullException.ThrowIfNull(output);

        EntityOutput? wired = FindOutput(output);

        // Before the wires fire, so the output is on record ahead of the
        // inputs it queues.
        World.Trace?.Record(EntityTraceEvent.OfOutput(this, activator ?? this, output, wired));

        wired?.Fire(this, activator ?? this, parameterOverride);
    }

    /// <summary>This entity's <paramref name="output"/>, or null if nothing wires it.</summary>
    public EntityOutput? FindOutput(string output)
    {
        for (int i = 0; i < _outputs.Count; i++)
        {
            if (string.Equals(_outputs[i].Name, output, StringComparison.Ordinal))
                return _outputs[i];
        }

        return null;
    }

    /// <summary>
    /// Reports that <paramref name="key"/> was recognised and its value could
    /// not be read, so the default stands.
    /// </summary>
    protected void RefuseKeyvalue(string key, string value) =>
        World.ReportRefusedKeyvalue(this, key, value);

    /// <summary>
    /// Logs a warning that names this entity. <paramref name="problem"/>
    /// finishes the sentence "Entity 'name' (class) ...", with no full stop.
    /// </summary>
    protected void Warn(string problem) => World.ReportWarning(this, problem);

    /// <summary>
    /// Asks for <see cref="OnTick"/> every tick, or stops asking. Turned on
    /// while handling an input, the first call comes in the same tick.
    /// </summary>
    protected void SetTicking(bool on) => World.SetTicking(this, on);

    /// <summary>
    /// Moves this entity's node through
    /// <see cref="EntityWorld.SetLocalTransform"/>, so stopping the level puts
    /// it back. Never write a node's transform directly.
    /// </summary>
    /// <returns>False when the world refused the move.</returns>
    protected bool MoveNode(in Transform local) => World.SetLocalTransform(Node, in local);

    /// <summary>
    /// The brush nodes this entity owns: its own node and those below it,
    /// stopping at any node that carries an entity of its own. Builds a new
    /// list, so collect once at spawn.
    /// </summary>
    protected IReadOnlyList<SceneNode> CollectOwnedBrushes()
    {
        var owned = new List<SceneNode>();
        if (Node.Brush is not null)
            owned.Add(Node);

        CollectBrushesBelow(Node, owned);
        return owned;
    }

    private static void CollectBrushesBelow(SceneNode parent, List<SceneNode> owned)
    {
        IReadOnlyList<SceneNode> children = parent.Children;
        for (int i = 0; i < children.Count; i++)
        {
            SceneNode child = children[i];
            if (child.Entity is not null || child.SubtreeBrushCount == 0)
                continue;

            if (child.Brush is not null)
                owned.Add(child);

            CollectBrushesBelow(child, owned);
        }
    }

    internal void Bind(SceneNode node, EntityWorld world, EntityData data)
    {
        Node = node;
        World = world;
        Data = data;
    }

    // For a node deleted and restored under the same id.
    internal void RebindNode(SceneNode node) => Node = node;

    // The runtime copy of the wires. Fire counts are decremented here, never
    // on the authored list, or the decrement ends up in the saved map.
    internal void BuildOutputs(IReadOnlyList<EntityConnection> connections)
    {
        _outputs.Clear();
        for (int i = 0; i < connections.Count; i++)
        {
            EntityConnection wire = connections[i];
            EntityOutput? output = FindOutput(wire.Output);
            if (output is null)
            {
                output = new EntityOutput(wire.Output);
                _outputs.Add(output);
            }

            output.Add(wire);
        }
    }
}
