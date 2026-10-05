using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// Resolves a wire's target name to the entities it means: an exact name, a
/// trailing-<c>*</c> prefix, or one of the runtime forms
/// <c>!self</c> / <c>!activator</c> / <c>!caller</c>. Duplicate names are
/// legal and a name resolves to every match, in scene traversal order.
/// </summary>
// The scene event handlers must touch only this index: membership events fire
// mid ownership walk, where a structural edit corrupts the traversal.
public sealed class TargetNameIndex : IDisposable
{
    /// <summary>The entity whose output is firing.</summary>
    public const string SelfToken = "!self";

    /// <summary>Whoever started the chain.</summary>
    public const string ActivatorToken = "!activator";

    /// <summary>The entity that fired the output being delivered.</summary>
    public const string CallerToken = "!caller";

    // Keyed by id, not reference: undo of a delete rebuilds the node as a new
    // object with the old id.
    private readonly Dictionary<Guid, Entity> _byNodeId = [];

    // Only entities whose node is in the graph. A removed node keeps its
    // _byNodeId entry so an undo can relist it.
    private readonly Dictionary<string, List<Entity>> _byName = new(StringComparer.Ordinal);

    private readonly Scene.Scene _scene;
    private bool _disposed;

    /// <summary>Subscribes to <paramref name="scene"/>'s membership and rename events.</summary>
    public TargetNameIndex(Scene.Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _scene = scene;
        scene.NodeAdded += OnNodeAdded;
        scene.NodeRemoved += OnNodeRemoved;
        scene.NodeRenamed += OnNodeRenamed;
    }

    /// <summary>How many distinct names currently have at least one entity.</summary>
    public int NameCount
    {
        get
        {
            int names = 0;
            foreach (List<Entity> bucket in _byName.Values)
            {
                if (bucket.Count > 0)
                    names++;
            }

            return names;
        }
    }

    /// <summary>How many entities this index knows about, listed or not.</summary>
    public int EntityCount => _byNodeId.Count;

    /// <summary>Unsubscribes from the scene. The index is unusable afterwards.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _scene.NodeAdded -= OnNodeAdded;
        _scene.NodeRemoved -= OnNodeRemoved;
        _scene.NodeRenamed -= OnNodeRenamed;

        // Otherwise another index would think the entity is already listed.
        foreach (Entity entity in _byNodeId.Values)
            entity.IndexedName = null;

        _byNodeId.Clear();
        _byName.Clear();
    }

    /// <summary>The entity built for the node with this id, if there is one.</summary>
    public bool TryGetByNodeId(Guid nodeId, [MaybeNullWhen(false)] out Entity entity) =>
        _byNodeId.TryGetValue(nodeId, out entity);

    /// <summary>
    /// Appends every entity <paramref name="target"/> names to
    /// <paramref name="results"/>, in scene traversal order. Names and tokens
    /// match ordinally. <paramref name="results"/> is not cleared first.
    /// </summary>
    public void Resolve(
        string? target,
        Entity? self,
        Entity? activator,
        Entity? caller,
        List<Entity> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (string.IsNullOrEmpty(target))
            return;

        if (target[0] == '!')
        {
            Entity? one = target switch
            {
                SelfToken => self,
                ActivatorToken => activator,
                CallerToken => caller,
                _ => null,
            };

            if (one is not null)
                results.Add(one);

            return;
        }

        int start = results.Count;

        if (TargetNamePattern.IsPrefix(target))
        {
            foreach (KeyValuePair<string, List<Entity>> bucket in _byName)
            {
                if (TargetNamePattern.Matches(target, bucket.Key))
                    results.AddRange(bucket.Value);
            }
        }
        else if (_byName.TryGetValue(target, out List<Entity>? exact))
        {
            results.AddRange(exact);
        }

        SortByTraversalOrder(results, start);
    }

    internal void Register(Entity entity)
    {
        _byNodeId[entity.Node.Id] = entity;
        Relist(entity);
    }

    internal void Unregister(Entity entity)
    {
        Unlist(entity);
        if (_byNodeId.TryGetValue(entity.Node.Id, out Entity? indexed) && ReferenceEquals(indexed, entity))
            _byNodeId.Remove(entity.Node.Id);
    }

    // The node may already be listed, or be the same id restored as a new
    // object by an undo.
    private void OnNodeAdded(SceneNode node)
    {
        if (!_byNodeId.TryGetValue(node.Id, out Entity? entity))
            return;

        if (!ReferenceEquals(entity.Node, node))
            entity.RebindNode(node);

        Relist(entity);
    }

    private void OnNodeRemoved(SceneNode node)
    {
        if (!_byNodeId.TryGetValue(node.Id, out Entity? entity))
            return;

        // If two live nodes share an id, only the entity's own node unlists it.
        if (!ReferenceEquals(entity.Node, node))
            return;

        // Keep the id mapping so an undo of the delete can relist.
        Unlist(entity);
    }

    private void OnNodeRenamed(SceneNode node)
    {
        if (!_byNodeId.TryGetValue(node.Id, out Entity? entity))
            return;

        if (!ReferenceEquals(entity.Node, node))
            return;

        // Renaming a detached node must not list it again.
        if (entity.IndexedName is null)
            return;

        Relist(entity);
    }

    private void Relist(Entity entity)
    {
        string name = entity.Node.Name;

        if (entity.IndexedName is { } current)
        {
            if (string.Equals(current, name, StringComparison.Ordinal))
                return;

            RemoveFromBucket(current, entity);
        }

        if (!_byName.TryGetValue(name, out List<Entity>? bucket))
        {
            bucket = [];
            _byName[name] = bucket;
        }

        if (!bucket.Contains(entity))
            bucket.Add(entity);

        entity.IndexedName = name;
    }

    private void Unlist(Entity entity)
    {
        if (entity.IndexedName is not { } current)
            return;

        RemoveFromBucket(current, entity);
        entity.IndexedName = null;
    }

    private void RemoveFromBucket(string name, Entity entity)
    {
        if (_byName.TryGetValue(name, out List<Entity>? bucket))
            bucket.Remove(entity);
    }

    // Insertion sort: buckets are tiny and the sort must be stable, because
    // nodes with no common ancestor compare equal.
    private static void SortByTraversalOrder(List<Entity> results, int start)
    {
        for (int i = start + 1; i < results.Count; i++)
        {
            Entity current = results[i];
            int j = i - 1;
            while (j >= start && CompareTraversalOrder(results[j].Node, current.Node) > 0)
            {
                results[j + 1] = results[j];
                j--;
            }

            results[j + 1] = current;
        }
    }

    // Pre-order position, computed per call. A reparent raises no event this
    // index sees, so a cached order would go stale.
    private static int CompareTraversalOrder(SceneNode a, SceneNode b)
    {
        if (ReferenceEquals(a, b))
            return 0;

        int depthA = Depth(a);
        int depthB = Depth(b);

        SceneNode x = a;
        SceneNode y = b;
        for (int i = depthA; i > depthB; i--)
            x = x.Parent!;
        for (int i = depthB; i > depthA; i--)
            y = y.Parent!;

        // One is an ancestor of the other; the ancestor comes first.
        if (ReferenceEquals(x, y))
            return depthA > depthB ? 1 : -1;

        while (!ReferenceEquals(x.Parent, y.Parent))
        {
            if (x.Parent is null || y.Parent is null)
                return 0;

            x = x.Parent;
            y = y.Parent;
        }

        SceneNode? parent = x.Parent;
        if (parent is null)
            return 0;

        return SiblingIndex(parent, x).CompareTo(SiblingIndex(parent, y));
    }

    private static int Depth(SceneNode node)
    {
        int depth = 0;
        for (SceneNode? walk = node.Parent; walk is not null; walk = walk.Parent)
            depth++;

        return depth;
    }

    private static int SiblingIndex(SceneNode parent, SceneNode child)
    {
        IReadOnlyList<SceneNode> children = parent.Children;
        for (int i = 0; i < children.Count; i++)
        {
            if (ReferenceEquals(children[i], child))
                return i;
        }

        return int.MaxValue;
    }
}
