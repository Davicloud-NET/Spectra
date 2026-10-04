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
    public static EntityPanelInfo? Capture(
        SceneNode node,
        EntitySchemaCatalog? schemas,
        Scene.Scene? scene,
        List<EntityTargetInfo>? targetScratch = null)
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

        return new EntityPanelInfo
        {
            NodeId = node.Id,
            ClassName = entity.ClassName,
            IsKnown = known,
            Outputs = outputs,
            Connections = described,
            Targets = targets.ToArray(),
            TargetsTruncated = truncated,
        };
    }

    /// <summary>How many entities one capture will list.</summary>
    public const int MaxTargets = 2000;

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

    // Must accept the same forms as TargetNameIndex.Resolve and no more.
    // A "!" token names an entity chosen at runtime, so it counts as resolving.
    private static bool Resolves(string? target, List<EntityTargetInfo> names)
    {
        if (string.IsNullOrEmpty(target))
            return false;

        if (target[0] == '!')
        {
            return target is TargetNameIndex.SelfToken
                or TargetNameIndex.ActivatorToken
                or TargetNameIndex.CallerToken;
        }

        if (target[^1] == '*')
        {
            ReadOnlySpan<char> prefix = target.AsSpan(0, target.Length - 1);
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i].Name.AsSpan().StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i].Name, target, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

}
