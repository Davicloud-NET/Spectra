using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// The one way a script adds to a level's tree, takes from it and moves a
/// node within it while the level plays. Each change records how the tree
/// was in the level's journal first, so stopping the level puts it back.
/// </summary>
public sealed class TreeWrites
{
    private readonly EntityWorld _world;
    private readonly TreeJournal _journal;

    // Reused by every walk of a subtree.
    private readonly List<SceneNode> _walk = [];

    /// <summary>The changes to the tree of one level.</summary>
    public TreeWrites(EntityWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _journal = world.Journal.Tree;
    }

    /// <summary>How many nodes the level's scripts have made that are still there.</summary>
    public int MadeCount => _journal.MadeCount;

    /// <summary>
    /// Whether the last <see cref="TryMake"/> or <see cref="TryClone"/> was
    /// refused for <see cref="TreeLimits.MaxMadeNodes"/>. Whoever can find
    /// made nodes that no script reaches any more hands them to
    /// <see cref="Forget"/> and asks again.
    /// </summary>
    public bool IsOutOfRoom { get; private set; }

    /// <summary>Whether the level destroyed the node. Stop makes it whole again.</summary>
    public static bool IsDestroyed(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.IsDestroyed;
    }

    /// <summary>Whether a script made the node while its level played.</summary>
    public static bool IsMade(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.IsMadeByLevel;
    }

    /// <summary>Whether the node is in the level's tree: the root or anywhere below it.</summary>
    public bool IsInLevel(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return ReferenceEquals(node.Owner, _world.Scene);
    }

    /// <summary>Makes a node of a class, in no tree. <see cref="MadeNodes"/> says which classes can be made.</summary>
    /// <param name="refusal">Why nothing was made, as the script's error says it.</param>
    public bool TryMake(
        ReadOnlySpan<char> className, [NotNullWhen(true)] out SceneNode? made, [NotNullWhen(false)] out string? refusal)
    {
        made = null;
        IsOutOfRoom = false;
        if (!_world.IsRunningEntities)
        {
            refusal = "Instance.new made nothing: the level has stopped";
            return false;
        }

        if (_journal.MadeCount >= TreeLimits.MaxMadeNodes)
        {
            IsOutOfRoom = true;
            refusal = $"Instance.new made nothing: {TooMany}";
            return false;
        }

        made = MadeNodes.Make(className);
        if (made is null)
        {
            refusal = $"Unable to create an Instance of type \"{className}\". Instance.new makes {MadeNodes.Classes}. " +
                "Clone a node of the level for anything else";
            return false;
        }

        made.IsMadeByLevel = true;
        _journal.MadeCount++;
        refusal = null;
        return true;
    }

    /// <summary>
    /// Copies a node and everything below it, into no tree. The copy stands
    /// where the node stands in the world, and a reference to a node of the
    /// copied tree points at its copy.
    /// </summary>
    /// <param name="copies">Gets every copied node beside its copy. It is cleared first.</param>
    /// <param name="refusal">Why nothing was copied, as the script's error says it.</param>
    public bool TryClone(
        SceneNode source, List<NodeCopy> copies, [NotNullWhen(true)] out SceneNode? copy,
        [NotNullWhen(false)] out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(copies);

        copy = null;
        copies.Clear();
        refusal = WhyNotCloned(source);
        IsOutOfRoom = refusal is null
            && !NodeCopies.Fill(source, copies, TreeLimits.MaxMadeNodes - _journal.MadeCount);
        if (IsOutOfRoom)
            refusal = $"{source.Name} was not cloned: with its copy, {TooMany}";

        if (refusal is not null)
        {
            copies.Clear();
            return false;
        }

        for (int i = 0; i < copies.Count; i++)
            copies[i].Copy.IsMadeByLevel = true;

        // A node in no tree is in the world where its own transform says.
        copy = copies[0].Copy;
        copy.LocalTransform = WorldFrame.LocalUnder(null, source.LocalTransform, source.CFrame);
        NodeCopies.PointIntoTheCopy(copies);
        _journal.MadeCount += copies.Count;
        return true;
    }

    /// <summary>
    /// Gives a node another parent, or none, which takes it out of its tree.
    /// The node stays where it is in the world. An entity at or below a node
    /// that leaves the level stops, and one that comes into it starts.
    /// </summary>
    /// <param name="refusal">Why the node kept its parent, as the script's error says it.</param>
    public bool TrySetParent(SceneNode node, SceneNode? parent, [NotNullWhen(false)] out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(node);

        refusal = WhyNotParented(node, parent);
        if (refusal is not null)
            return false;

        if (!ReferenceEquals(node.Parent, parent))
            Move(node, parent);

        return true;
    }

    /// <summary>
    /// Takes a node and everything below it out of the level for good: each
    /// loses its parent and can get no other. Destroying a node again does
    /// nothing.
    /// </summary>
    /// <param name="destroyed">Gets every node this call destroyed, a node before what was below it. It is cleared first.</param>
    /// <param name="refusal">Why nothing was destroyed, as the script's error says it.</param>
    public bool TryDestroy(SceneNode node, List<SceneNode> destroyed, [NotNullWhen(false)] out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(destroyed);

        destroyed.Clear();
        refusal = WhyNotDestroyed(node);
        if (refusal is not null)
            return false;
        if (node.IsDestroyed)
            return true;

        Collect(node, destroyed);
        if (node.Parent is not null)
            Move(node, null);

        for (int i = 0; i < destroyed.Count; i++)
        {
            if (destroyed[i].Children.Count > 0)
                _journal.RecordChildren(destroyed[i]);
        }

        // From the leaves up, so each list is taken apart from its end.
        for (int i = destroyed.Count - 1; i >= 0; i--)
        {
            SceneNode gone = destroyed[i];
            if (i > 0)
                gone.Parent?.RemoveChild(gone);

            gone.IsDestroyed = true;
            _journal.NoteDestroyed(gone);
            if (gone.IsMadeByLevel)
                _journal.MadeCount--;
        }

        return true;
    }

    /// <summary>
    /// Stops counting the nodes of a tree no script can reach any more: one a
    /// script made, never put into the level and let go of. Hand it the top.
    /// </summary>
    public void Forget(SceneNode top)
    {
        ArgumentNullException.ThrowIfNull(top);
        if (top.Parent is not null || ReferenceEquals(top.Owner, _world.Scene))
            return;

        Collect(top, _walk);
        for (int i = 0; i < _walk.Count; i++)
        {
            if (_walk[i] is { IsMadeByLevel: true, IsDestroyed: false })
                _journal.MadeCount--;
        }

        _walk.Clear();
    }

    private void Move(SceneNode node, SceneNode? parent)
    {
        Scene.Scene level = _world.Scene;
        bool wasInLevel = ReferenceEquals(node.Owner, level);
        bool willBeInLevel = parent is not null && ReferenceEquals(parent.Owner, level);
        if (wasInLevel && !willBeInLevel)
            StopEntities(node);

        // Before the move, while the node still says where it is in the world.
        Transform local = WorldFrame.LocalUnder(parent, node.LocalTransform, node.CFrame);
        if (node.Parent is { } old)
            _journal.RecordChildren(old);
        if (parent is not null)
            _journal.RecordChildren(parent);

        if (new CFrame(local.Position, local.Rotation) != node.LocalCFrame)
        {
            _world.Journal.Record(node, JournalKind.LocalTransform);
            node.LocalTransform = local;
        }

        if (parent is null)
            node.Parent?.RemoveChild(node);
        else
            parent.AddChild(node);

        if (willBeInLevel && !wasInLevel)
            StartEntities(node);
    }

    // The entity of each node is built at the end of the tick, with the
    // others that came with it.
    private void StartEntities(SceneNode top)
    {
        Collect(top, _walk);
        for (int i = 0; i < _walk.Count; i++)
        {
            if (_walk[i].Entity is not null)
                _world.QueueSpawn(_walk[i]);
        }

        _walk.Clear();
    }

    private void StopEntities(SceneNode top)
    {
        if (_world.Index is not { } index)
            return;

        Collect(top, _walk);
        for (int i = 0; i < _walk.Count; i++)
        {
            SceneNode node = _walk[i];
            if (node.Entity is not null && index.TryGetByNodeId(node.Id, out Entity? entity)
                && ReferenceEquals(entity.Node, node))
            {
                _world.QueueDespawn(entity);
            }
        }

        _walk.Clear();
    }

    private string? WhyNotParented(SceneNode node, SceneNode? parent)
    {
        if (ReferenceEquals(node, _world.Scene.Root))
            return $"The Parent property of {node.Name} is locked: it is the root of the level";
        if (node.IsDestroyed)
            return $"The Parent property of {node.Name} is locked: {node.Name} was destroyed";
        if (ReferenceEquals(node.Parent, parent))
            return null;
        if (!_world.IsRunningEntities)
            return $"{node.Name} cannot be changed: the level has stopped.";
        if (node.HoldsWorldBrush)
            return $"{node.Name} cannot get another parent: {OnlyPartsCan("move")}";

        return parent is null ? null : WhyNotUnder(node, parent);
    }

    private static string? WhyNotUnder(SceneNode node, SceneNode parent)
    {
        if (parent.IsDestroyed)
            return $"{node.Name} cannot get {parent.Name} as its parent: {parent.Name} was destroyed";

        for (SceneNode? above = parent; above is not null; above = above.Parent)
        {
            if (ReferenceEquals(above, node))
                return $"Attempt to set parent of {node.Name} to {parent.Name} would result in circular reference";
        }

        if (node.SubtreeBrushCount > 0 && IsScaled(parent))
        {
            return $"{node.Name} cannot get {parent.Name} as its parent: that node or one above it is scaled, " +
                "and a node cannot scale a brush at or below it. Put it under a node that is not scaled.";
        }

        int room = TreeLimits.MaxDepth - TreeLimits.LevelOf(parent);
        return TreeLimits.HeightUpTo(node, room) > room
            ? $"{node.Name} cannot get {parent.Name} as its parent: that would make the tree more than " +
              $"{TreeLimits.MaxDepth} nodes deep, which is as deep as a tree may be"
            : null;
    }

    private string? WhyNotCloned(SceneNode source)
    {
        if (ReferenceEquals(source, _world.Scene.Root))
            return $"{source.Name} is the root of the level and cannot be cloned";
        if (!_world.IsRunningEntities)
            return $"{source.Name} was not cloned: the level has stopped";

        return source.HoldsWorldBrush ? $"{source.Name} cannot be cloned: {OnlyPartsCan("be made")}" : null;
    }

    private string? WhyNotDestroyed(SceneNode node)
    {
        if (ReferenceEquals(node, _world.Scene.Root))
            return $"{node.Name} is the root of the level and cannot be destroyed";
        if (node.IsDestroyed)
            return null;
        if (!_world.IsRunningEntities)
            return $"{node.Name} cannot be changed: the level has stopped.";

        return node.HoldsWorldBrush ? $"{node.Name} cannot be destroyed: {OnlyPartsCan("be destroyed")}" : null;
    }

    // What the count of nodes scripts made says when no more fit.
    private static string TooMany =>
        $"scripts would have made more than {TreeLimits.MaxMadeNodes} nodes that are still there, " +
        "and a level holds no more. Destroy the ones that are done with";

    // The words the other refusals for a world brush use.
    private static string OnlyPartsCan(string what) =>
        $"a world brush sits at or below it, and only parts can {what} while a level plays. Convert the brush to a part.";

    private static bool IsScaled(SceneNode node)
    {
        for (SceneNode? at = node; at is not null; at = at.Parent)
        {
            if (at.LocalScale != Vector3.One)
                return true;
        }

        return false;
    }

    // A node before what is below it. The list is its own stack, so no walk
    // goes deeper than one call.
    private static void Collect(SceneNode top, List<SceneNode> into)
    {
        into.Clear();
        into.Add(top);
        for (int i = 0; i < into.Count; i++)
        {
            IReadOnlyList<SceneNode> children = into[i].Children;
            for (int c = 0; c < children.Count; c++)
                into.Add(children[c]);
        }
    }
}
