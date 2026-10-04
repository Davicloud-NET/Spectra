using System.Collections.Generic;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// What a spatial query is allowed to see: the per-node flag rules, an
/// exclusion list, and a collision-group filter. The default value sees
/// everything with <see cref="PhysicsFlags.CanQuery"/> set.
/// </summary>
public readonly record struct SceneQueryFilter
{
    /// <summary>
    /// Nodes this query must not report, or null for none. Compared by reference.
    /// </summary>
    public IReadOnlyList<SceneNode>? Ignore { get; init; }

    /// <summary>
    /// When set with <see cref="CollisionGroup"/>, only nodes whose group
    /// interacts with that group are reported.
    /// </summary>
    public CollisionGroups? Groups { get; init; }

    /// <summary>The group this query acts on behalf of. Ignored unless <see cref="Groups"/> is set.</summary>
    public int CollisionGroup { get; init; }

    /// <summary>
    /// Also require <see cref="PhysicsFlags.CanCollide"/> before a node is
    /// reported. Off by default; on gives Roblox's coupling of the two flags.
    /// </summary>
    public bool RespectCanCollide { get; init; }

    /// <summary>
    /// Ignore <see cref="PhysicsFlags.CanQuery"/> and report every spatial
    /// node. For editor picking: anything visible must be selectable.
    /// </summary>
    public bool IgnoreQueryFlags { get; init; }

    /// <summary>
    /// Report subtractive brushes, which queries otherwise skip because a hole
    /// has no solid. For editor tooling.
    /// </summary>
    public bool IncludeSubtractiveBrushes { get; init; }

    /// <summary>
    /// Skip <see cref="BrushKind.World"/> brushes. Gameplay raycasts set this:
    /// the compiled static world answers for them and knows what was carved.
    /// </summary>
    // An exclusion so the zero value keeps reporting them. default(struct)
    // runs no initializer, so an Include flag could not default to true.
    public bool ExcludeStaticWorldBrushes { get; init; }

    /// <summary>The filter editor tooling uses: everything selectable, flags disregarded.</summary>
    public static SceneQueryFilter EditorPicking =>
        new() { IgnoreQueryFlags = true, IncludeSubtractiveBrushes = true };

    /// <summary>Whether <paramref name="node"/> may be reported by this query.</summary>
    public bool Accepts(SceneNode node)
    {
        if (node is null)
            return false;

        if (Ignore is { } ignore)
        {
            for (int i = 0; i < ignore.Count; i++)
            {
                if (ReferenceEquals(ignore[i], node))
                    return false;
            }
        }

        if (!IgnoreQueryFlags)
        {
            PhysicsFlags flags = node.PhysicsFlags;
            if ((flags & PhysicsFlags.CanQuery) == 0)
                return false;
            if (RespectCanCollide && (flags & PhysicsFlags.CanCollide) == 0)
                return false;
        }

        // Interacts, not AreCollidable: a node may name an unregistered group,
        // and a broad-phase walk is the wrong place to throw for it. The query
        // group is validated at the Scene entry point.
        if (Groups is { } groups && !groups.Interacts(CollisionGroup, node.CollisionGroup))
            return false;

        if (!IncludeSubtractiveBrushes &&
            node.Brush is { Operation: Bsp.BrushOperation.Subtractive })
        {
            return false;
        }

        if (ExcludeStaticWorldBrushes && node.IsStaticWorldBrush)
            return false;

        return true;
    }
}
