using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Inspection;

/// <summary>
/// One authored wire, plus whether anything in the scene answers to its target
/// name.
/// </summary>
/// <param name="Wire">The authored connection, as the node stores it.</param>
/// <param name="TargetResolves">
/// Whether at least one entity in the scene answers to
/// <see cref="EntityConnection.TargetName"/>: by exact name or by a trailing-*
/// prefix. The runtime <c>!</c> forms cannot be checked and report as resolving.
/// </param>
public readonly record struct EntityConnectionInfo(EntityConnection Wire, bool TargetResolves);

/// <summary>One entity a wire could aim at.</summary>
/// <param name="Name">The node's name, which is its targetname.</param>
/// <param name="ClassName">The entity's class, as the map records it.</param>
public readonly record struct EntityTargetInfo(string Name, string ClassName);

/// <summary>
/// What a wiring panel needs about the selected entity: its class, the outputs
/// that class declares, and the node's wires. Holds copies, never live lists.
/// Published for a single-node entity selection only.
/// </summary>
public sealed class EntityPanelInfo
{
    /// <summary>
    /// The node the wiring belongs to. A connection edit should address this id,
    /// not the current selection: it replaces the whole list.
    /// </summary>
    public required Guid NodeId { get; init; }

    /// <summary>The class the entity names, as the map spells it.</summary>
    public string ClassName { get; init; } = "";

    /// <summary>
    /// Whether a schema for <see cref="ClassName"/> was found. False is not an
    /// error, but <see cref="Outputs"/> is then empty.
    /// </summary>
    public bool IsKnown { get; init; }

    /// <summary>The output names the class declares, in declaration order.</summary>
    public IReadOnlyList<string> Outputs { get; init; } = [];

    /// <summary>
    /// The node's wires in authored order, each with whether its target resolves.
    /// The order round-trips through <c>map.json</c>; do not sort it.
    /// </summary>
    public IReadOnlyList<EntityConnectionInfo> Connections { get; init; } = [];

    /// <summary>
    /// Every entity in the scene a wire could aim at, in walk order.
    /// </summary>
    public IReadOnlyList<EntityTargetInfo> Targets { get; init; } = [];

    /// <summary>Whether the scene has more entities than <see cref="Targets"/> carries.</summary>
    public bool TargetsTruncated { get; init; }

    /// <summary>
    /// The wires that arrive here, from other entities and from this one, in
    /// the order their senders joined the scene.
    /// </summary>
    public IReadOnlyList<EntityIncomingInfo> Incoming { get; init; } = [];

    /// <summary>Whether more wires arrive than <see cref="Incoming"/> carries.</summary>
    public bool IncomingTruncated { get; init; }

    /// <summary>
    /// The entity's live state while a level runs, one name and value per row.
    /// Empty when nothing is running.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> State { get; init; } = [];

    /// <summary>
    /// Describes <paramref name="node"/>'s entity payload, or null when it
    /// carries none. Render thread only.
    /// </summary>
    /// <param name="node">The selected node.</param>
    /// <param name="schemas">What the scene's classes declare, or null.</param>
    /// <param name="scene">
    /// The scene to resolve target names against. With null, every target
    /// reports as unresolved.
    /// </param>
    /// <param name="targetScratch">A list the caller owns, reused across calls to avoid allocating.</param>
    /// <param name="world">The running level, or null. Gives <see cref="State"/>.</param>
    public static EntityPanelInfo? Capture(
        SceneNode node,
        EntitySchemaCatalog? schemas,
        Scene.Scene? scene,
        List<EntityTargetInfo>? targetScratch = null,
        EntityWorld? world = null)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Entity is not { } entity)
            return null;

        EntitySchema? schema = null;
        bool known = schemas is not null && schemas.TryGetSchema(entity.ClassName, out schema);
        IReadOnlyList<string> outputs = known && schema is not null ? schema.Outputs : [];

        // One walk feeds both the picker and the resolve check, so they cannot
        // disagree. Runs even with no wires: the picker is offered then too.
        List<EntityTargetInfo> targets = targetScratch ?? [];
        targets.Clear();
        bool truncated = false;
        if (scene is not null)
            CollectTargets(scene, targets, out truncated);

        List<EntityConnection> wires = entity.Connections;

        var described = new EntityConnectionInfo[wires.Count];
        for (int i = 0; i < wires.Count; i++)
            described[i] = new EntityConnectionInfo(wires[i], Resolves(wires[i].TargetName, targets));

        bool incomingTruncated = false;
        EntityIncomingInfo[] incoming = scene is null ? [] : CollectIncoming(scene, node, out incomingTruncated);

        return new EntityPanelInfo
        {
            NodeId = node.Id,
            ClassName = entity.ClassName,
            IsKnown = known,
            Outputs = outputs,
            Connections = described,
            Targets = targets.ToArray(),
            TargetsTruncated = truncated,
            Incoming = incoming,
            IncomingTruncated = incomingTruncated,
            State = ReadState(node, world),
        };
    }

    /// <summary>How many entities one capture will list.</summary>
    public const int MaxTargets = 2000;

    /// <summary>How many arriving wires one capture will list.</summary>
    public const int MaxIncoming = 200;

    private static EntityIncomingInfo[] CollectIncoming(Scene.Scene scene, SceneNode node, out bool truncated)
    {
        truncated = false;
        List<EntityIncomingInfo>? found = null;

        IReadOnlyList<SceneNode> senders = scene.EntityNodes;
        for (int i = 0; i < senders.Count; i++)
        {
            SceneNode sender = senders[i];
            if (sender.Entity is not { } data)
                continue;

            bool isSelf = ReferenceEquals(sender, node);
            for (int w = 0; w < data.Connections.Count; w++)
            {
                EntityConnection wire = data.Connections[w];
                if (!Arrives(wire.TargetName, node.Name, isSelf))
                    continue;

                found ??= [];
                if (found.Count >= MaxIncoming)
                {
                    truncated = true;
                    return [.. found];
                }

                found.Add(new EntityIncomingInfo(sender.Id, sender.Name, wire.Output, wire.Input));
            }
        }

        return found is null ? [] : [.. found];
    }

    // For a wire, !self and !caller are both the entity that sends it.
    private static bool Arrives(string target, string name, bool fromSelf) =>
        TargetNamePattern.Matches(target, name)
        || (fromSelf && target is TargetNameIndex.SelfToken or TargetNameIndex.CallerToken);

    private static KeyValuePair<string, string>[] ReadState(SceneNode node, EntityWorld? world)
    {
        if (world is not { IsActive: true, Index: { } index }
            || !index.TryGetByNodeId(node.Id, out Entity? live)
            || live is null)
        {
            return [];
        }

        var rows = new List<KeyValuePair<string, string>>();
        live.DescribeState(new EntityStateWriter(rows));
        return [.. rows];
    }

    /// <summary>
    /// Collects every entity in the scene as a name and a class. Only nodes
    /// carrying an entity are listed, since the runtime resolves nothing else.
    /// </summary>
    public static void CollectTargets(Scene.Scene scene, List<EntityTargetInfo> into, out bool truncated)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(into);

        truncated = false;
        Walk(scene.Root, into, ref truncated);

        static void Walk(SceneNode node, List<EntityTargetInfo> into, ref bool truncated)
        {
            if (into.Count >= MaxTargets)
            {
                truncated = true;
                return;
            }

            if (node.Entity is { } entity)
                into.Add(new EntityTargetInfo(node.Name, entity.ClassName));

            IReadOnlyList<SceneNode> children = node.Children;
            for (int i = 0; i < children.Count; i++)
                Walk(children[i], into, ref truncated);
        }
    }

    // A "!" token names an entity chosen at runtime, so it counts as resolving.
    private static bool Resolves(string? target, List<EntityTargetInfo> names)
    {
        if (TargetNamePattern.IsRuntimeToken(target))
            return true;

        for (int i = 0; i < names.Count; i++)
        {
            if (TargetNamePattern.Matches(target, names[i].Name))
                return true;
        }

        return false;
    }

}
