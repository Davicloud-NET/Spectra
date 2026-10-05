using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Inspection;

/// <summary>
/// Every entity in a scene with its wires, for a view that draws the wiring
/// as a graph. Holds copies, never live lists.
/// </summary>
public sealed class LogicGraphInfo
{
    /// <summary>How many entities one capture will list.</summary>
    public const int MaxEntities = 4000;

    /// <summary>The entities, in the order they joined the scene.</summary>
    public IReadOnlyList<LogicEntityInfo> Entities { get; init; } = [];

    /// <summary>How many entities the scene has, listed or not.</summary>
    public int TotalEntities { get; init; }

    /// <summary>Whether the scene has more entities than <see cref="Entities"/> carries.</summary>
    public bool IsTruncated => TotalEntities > Entities.Count;

    /// <summary>
    /// Describes the scene's wiring. Returns <paramref name="previous"/> itself
    /// when nothing differs, so a reader can tell by reference that nothing
    /// changed. Render thread only.
    /// </summary>
    public static LogicGraphInfo Capture(Scene.Scene scene, LogicGraphInfo? previous = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        IReadOnlyList<SceneNode> nodes = scene.EntityNodes;
        int listed = Math.Min(nodes.Count, MaxEntities);

        if (previous is not null && previous.Describes(nodes, listed))
            return previous;

        var entities = new LogicEntityInfo[listed];
        for (int i = 0; i < listed; i++)
        {
            SceneNode node = nodes[i];
            EntityData? entity = node.Entity;

            entities[i] = new LogicEntityInfo(
                node.Id,
                node.Name,
                entity?.ClassName ?? "",
                entity is { Connections.Count: > 0 } ? entity.Connections.ToArray() : []);
        }

        return new LogicGraphInfo { Entities = entities, TotalEntities = nodes.Count };
    }

    // Runs on every publish while a view is open, so it allocates nothing.
    private bool Describes(IReadOnlyList<SceneNode> nodes, int listed)
    {
        if (TotalEntities != nodes.Count || Entities.Count != listed)
            return false;

        for (int i = 0; i < listed; i++)
        {
            SceneNode node = nodes[i];
            LogicEntityInfo seen = Entities[i];

            if (node.Id != seen.NodeId
                || !string.Equals(node.Name, seen.Name, StringComparison.Ordinal)
                || !string.Equals(node.Entity?.ClassName ?? "", seen.ClassName, StringComparison.Ordinal))
            {
                return false;
            }

            if (!SameWires(node.Entity?.Connections, seen.Wires))
                return false;
        }

        return true;
    }

    private static bool SameWires(List<EntityConnection>? live, IReadOnlyList<EntityConnection> seen)
    {
        if (live is null)
            return seen.Count == 0;

        if (live.Count != seen.Count)
            return false;

        for (int i = 0; i < seen.Count; i++)
        {
            if (live[i] != seen[i])
                return false;
        }

        return true;
    }
}
