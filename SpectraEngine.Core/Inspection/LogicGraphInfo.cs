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

    /// <summary>How many scripts one capture will list.</summary>
    public const int MaxScripts = 4000;

    /// <summary>The entities, in the order they joined the scene.</summary>
    public IReadOnlyList<LogicEntityInfo> Entities { get; init; } = [];

    /// <summary>How many entities the scene has, listed or not.</summary>
    public int TotalEntities { get; init; }

    /// <summary>
    /// The scripts that run when the level plays, in the order they joined
    /// the scene: every script that is switched on and is no module.
    /// </summary>
    public IReadOnlyList<LogicScriptInfo> Scripts { get; init; } = [];

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

        if (previous is not null && previous.Describes(nodes, listed) && previous.DescribesScripts(scene.ScriptNodes))
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
                entity is { Connections.Count: > 0 } ? entity.Connections.ToArray() : [])
            {
                CopyGroup = node.CopyGroup,
            };
        }

        return new LogicGraphInfo
        {
            Entities = entities,
            TotalEntities = nodes.Count,
            Scripts = CaptureScripts(scene.ScriptNodes),
        };
    }

    private static LogicScriptInfo[] CaptureScripts(IReadOnlyList<SceneNode> nodes)
    {
        var scripts = new List<LogicScriptInfo>();
        for (int i = 0; i < nodes.Count && scripts.Count < MaxScripts; i++)
        {
            if (Runs(nodes[i]))
                scripts.Add(new LogicScriptInfo(nodes[i].Id, nodes[i].Name, EntityOf(nodes[i])));
        }

        return [.. scripts];
    }

    private static bool Runs(SceneNode node) => node.Script is { IsEnabled: true, IsModule: false };

    // A script may sit on the node of the entity itself.
    private static Guid EntityOf(SceneNode script)
    {
        for (SceneNode? node = script; node is not null; node = node.Parent)
        {
            if (node.Entity is not null)
                return node.Id;
        }

        return Guid.Empty;
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

    // The same, for the scripts.
    private bool DescribesScripts(IReadOnlyList<SceneNode> nodes)
    {
        int seen = 0;
        for (int i = 0; i < nodes.Count && seen < MaxScripts; i++)
        {
            if (!Runs(nodes[i]))
                continue;

            if (seen == Scripts.Count)
                return false;

            LogicScriptInfo script = Scripts[seen++];
            if (nodes[i].Id != script.NodeId
                || !string.Equals(nodes[i].Name, script.Name, StringComparison.Ordinal)
                || EntityOf(nodes[i]) != script.EntityId)
            {
                return false;
            }
        }

        return seen == Scripts.Count;
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
