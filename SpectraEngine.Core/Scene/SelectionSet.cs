using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// The ordered set of selected nodes for one <see cref="Scene"/>
/// (<see cref="Scene.Selection"/>). Only nodes attached to that scene can be
/// selected; nodes that leave it are deselected. Render thread only.
/// </summary>
public sealed class SelectionSet
{
    private readonly Scene _scene;

    // Same nodes in both: the list keeps selection order, the set makes
    // membership O(1).
    private readonly List<SceneNode> _items = [];
    private readonly HashSet<SceneNode> _membership = [];

    // Scratch for the batched operations. Reuse is safe because handlers may
    // not mutate the selection from inside SelectionChanged.
    private readonly List<SceneNode> _scratchItems = [];
    private readonly HashSet<SceneNode> _scratchMembership = [];
    private readonly HashSet<SceneNode> _seen = [];

    internal SelectionSet(Scene scene)
    {
        _scene = scene;
        scene.NodeRemoved += OnNodeRemoved;
    }

    /// <summary>The selected nodes, in the order they were selected.</summary>
    public IReadOnlyList<SceneNode> Items => _items;

    /// <summary>Number of selected nodes.</summary>
    public int Count => _items.Count;

    /// <summary>
    /// Raised once per operation that changed the selection, never for a
    /// no-op. Handlers must not mutate the selection or the scene graph.
    /// </summary>
    public event Action? SelectionChanged;

    /// <summary>Raised when the picked face changes.</summary>
    public event Action? FaceChanged;

    /// <summary>The node whose face is picked, or null.</summary>
    public SceneNode? FaceNode { get; private set; }

    /// <summary>
    /// Which of <see cref="FaceNode"/>'s brush planes is picked, or -1.
    /// </summary>
    public int FacePlane { get; private set; } = -1;

    /// <summary>
    /// Picks one face of the sole selected brush. Any selection change clears it.
    /// </summary>
    /// <returns>False when there is not exactly one brush selected, or the index is out of range.</returns>
    public bool SelectFace(SceneNode node, int planeIndex)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (Count != 1 || !ReferenceEquals(Items[0], node)) return false;
        if (node.Brush is not { } brush) return false;
        if (planeIndex < 0 || planeIndex >= brush.LocalPlanes.Count) return false;

        if (ReferenceEquals(FaceNode, node) && FacePlane == planeIndex) return true;

        FaceNode = node;
        FacePlane = planeIndex;
        FaceChanged?.Invoke();
        return true;
    }

    /// <summary>Drops the picked face.</summary>
    public void ClearFace()
    {
        if (FaceNode is null && FacePlane < 0) return;

        FaceNode = null;
        FacePlane = -1;
        FaceChanged?.Invoke();
    }

    // Clear the face first: a stale one names a plane on a brush that is no
    // longer selected.
    private void RaiseChanged()
    {
        ClearFace();
        SelectionChanged?.Invoke();
    }

    /// <summary>True when the node is currently selected.</summary>
    public bool Contains(SceneNode node) => _membership.Contains(node);

    /// <summary>
    /// Replaces the whole selection with <paramref name="node"/>. Throws when
    /// the node does not belong to this scene.
    /// </summary>
    public void Select(SceneNode node)
    {
        RequireOwnNode(node);
        if (_items.Count == 1 && ReferenceEquals(_items[0], node))
            return;

        _items.Clear();
        _membership.Clear();
        _items.Add(node);
        _membership.Add(node);
        RaiseChanged();
    }

    /// <summary>
    /// Adds the node to the selection. Throws when the node does not belong
    /// to this scene.
    /// </summary>
    public void Add(SceneNode node)
    {
        RequireOwnNode(node);
        if (!_membership.Add(node))
            return;

        _items.Add(node);
        RaiseChanged();
    }

    /// <summary>
    /// Adds the node when unselected, removes it when selected. Throws when
    /// the node does not belong to this scene.
    /// </summary>
    public void Toggle(SceneNode node)
    {
        RequireOwnNode(node);
        if (_membership.Add(node))
        {
            _items.Add(node);
        }
        else
        {
            _membership.Remove(node);
            _items.Remove(node);
        }
        RaiseChanged();
    }

    /// <summary>
    /// Removes the node from the selection; a no-op when it is not selected.
    /// </summary>
    public void Deselect(SceneNode node)
    {
        // No ownership check: the auto-deselect path gets here after the
        // removal has already cleared or repointed the node's Owner.
        if (!_membership.Remove(node))
            return;

        _items.Remove(node);
        RaiseChanged();
    }

    /// <summary>
    /// Replaces the whole selection with <paramref name="nodes"/>, raising
    /// <see cref="SelectionChanged"/> at most once.
    /// </summary>
    public void SetRange(IReadOnlyList<SceneNode> nodes) => Apply(nodes, SelectionUpdate.Replace);

    /// <summary>
    /// Adds every node in <paramref name="nodes"/> not already selected, in
    /// list order, raising <see cref="SelectionChanged"/> at most once.
    /// </summary>
    public void AddRange(IReadOnlyList<SceneNode> nodes) => Apply(nodes, SelectionUpdate.Add);

    /// <summary>
    /// Flips membership for every distinct node in <paramref name="nodes"/>,
    /// raising <see cref="SelectionChanged"/> at most once.
    /// </summary>
    public void ToggleRange(IReadOnlyList<SceneNode> nodes) => Apply(nodes, SelectionUpdate.Toggle);

    /// <summary>
    /// Applies a batch in the given mode. Raises <see cref="SelectionChanged"/>
    /// at most once, and not when membership and order are unchanged. Throws
    /// before changing anything when a node does not belong to this scene.
    /// </summary>
    public void Apply(IReadOnlyList<SceneNode> nodes, SelectionUpdate mode)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        for (int i = 0; i < nodes.Count; i++)
            RequireOwnNode(nodes[i]);

        _scratchItems.Clear();
        _scratchMembership.Clear();
        _seen.Clear();

        if (mode != SelectionUpdate.Replace)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                _scratchItems.Add(_items[i]);
                _scratchMembership.Add(_items[i]);
            }
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            SceneNode node = nodes[i];
            // A node listed twice would toggle twice.
            if (!_seen.Add(node))
                continue;

            if (mode == SelectionUpdate.Toggle && _scratchMembership.Remove(node))
            {
                _scratchItems.Remove(node);
                continue;
            }

            if (_scratchMembership.Add(node))
                _scratchItems.Add(node);
        }

        if (MatchesCurrentSelection())
            return;

        _items.Clear();
        _membership.Clear();
        for (int i = 0; i < _scratchItems.Count; i++)
        {
            _items.Add(_scratchItems[i]);
            _membership.Add(_scratchItems[i]);
        }
        RaiseChanged();
    }

    // Order counts: the UI renders Items in order.
    private bool MatchesCurrentSelection()
    {
        if (_scratchItems.Count != _items.Count)
            return false;

        for (int i = 0; i < _items.Count; i++)
        {
            if (!ReferenceEquals(_items[i], _scratchItems[i]))
                return false;
        }
        return true;
    }

    /// <summary>Empties the selection; a no-op when already empty.</summary>
    public void Clear()
    {
        if (_items.Count == 0)
            return;

        _items.Clear();
        _membership.Clear();
        RaiseChanged();
    }

    private void OnNodeRemoved(SceneNode node) => Deselect(node);

    private void RequireOwnNode(SceneNode node)
    {
        if (!ReferenceEquals(node.Owner, _scene))
        {
            throw new ArgumentException(
                node.Owner is null
                    ? $"Node '{node.Name}' is not attached to any scene and cannot be selected."
                    : $"Node '{node.Name}' belongs to scene '{node.Owner.Name}', not to '{_scene.Name}'.",
                nameof(node));
        }
    }
}
