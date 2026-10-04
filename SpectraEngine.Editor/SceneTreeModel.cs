using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor;

/// <summary>Where a drag hovering over a row would drop, for the indicator.</summary>
public enum SceneTreeDropZone
{
    /// <summary>Not a drop target right now.</summary>
    None,

    /// <summary>Insert as the row's earlier sibling.</summary>
    Before,

    /// <summary>Reparent into the row's node.</summary>
    Into,

    /// <summary>Insert as the row's later sibling.</summary>
    After,
}

/// <summary>How a node stands against the tree's current filter.</summary>
public enum SceneTreeMatch
{
    /// <summary>The node itself matches, or there is no filter.</summary>
    Match,

    /// <summary>The node does not match but something under it does.</summary>
    Ancestor,

    /// <summary>Neither the node nor anything under it matches.</summary>
    None,
}

/// <summary>
/// One node in the shell's tree view. A copy, never a live <see cref="SceneNode"/>,
/// which belongs to the render thread.
/// </summary>
public sealed class SceneTreeNode(Guid id, string name) : ObservableObject
{
    private string _name = name;
    private bool _isSelected;
    private bool _isExpanded;
    private bool _hasChildren;
    private int _depth;
    private SceneNodeKind _kind;
    private SceneTreeMatch _match = SceneTreeMatch.Match;

    /// <summary>The node's id, which is how the engine is addressed about it.</summary>
    public Guid Id { get; } = id;

    /// <summary>The node's name at the last change that mentioned it.</summary>
    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    /// <summary>What the node is, for the row's icon.</summary>
    public SceneNodeKind Kind
    {
        get => _kind;
        set => Set(ref _kind, value);
    }

    /// <summary>The node's children, in the graph's own sibling order.</summary>
    public ObservableCollection<SceneTreeNode> Children { get; } = [];

    /// <summary>Whether the engine reports this node as selected.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    /// <summary>How this node stands against the filter.</summary>
    public SceneTreeMatch Match
    {
        get => _match;
        set
        {
            if (!Set(ref _match, value))
                return;

            Raise(nameof(IsDimmed));
            Raise(nameof(IsContext));
        }
    }

    /// <summary>
    /// Whether this node's children are shown. Change it through
    /// <see cref="SceneTreeModel.ToggleExpanded"/> so the rows follow.
    /// </summary>
    // On the model, not the container: rows under a collapsed parent have no
    // container, and a reveal has to expand them anyway.
    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    /// <summary>
    /// How deep this node sits, with a top-level node at zero. The row's indent.
    /// </summary>
    public int Depth
    {
        get => _depth;
        internal set => Set(ref _depth, value);
    }

    /// <summary>Whether this node has children, and therefore an expander.</summary>
    public bool HasChildren
    {
        get => _hasChildren;
        internal set => Set(ref _hasChildren, value);
    }

    /// <summary>Whether the row is showing its in-place rename editor.</summary>
    // View state, kept here because virtualization recycles containers.
    public bool IsRenaming
    {
        get => _isRenaming;
        set => Set(ref _isRenaming, value);
    }

    private bool _isRenaming;

    /// <summary>Where a drag hovering this row would drop.</summary>
    public SceneTreeDropZone DropZone
    {
        get => _dropZone;
        set
        {
            if (Set(ref _dropZone, value))
            {
                Raise(nameof(IsDropBefore));
                Raise(nameof(IsDropInto));
                Raise(nameof(IsDropAfter));
            }
        }
    }

    private SceneTreeDropZone _dropZone;

    /// <summary>Style-class views of <see cref="DropZone"/>.</summary>
    public bool IsDropBefore => _dropZone == SceneTreeDropZone.Before;

    /// <inheritdoc cref="IsDropBefore"/>
    public bool IsDropInto => _dropZone == SceneTreeDropZone.Into;

    /// <inheritdoc cref="IsDropBefore"/>
    public bool IsDropAfter => _dropZone == SceneTreeDropZone.After;

    /// <summary>Whether the filter excludes this node. Bound as a style class.</summary>
    public bool IsDimmed => _match == SceneTreeMatch.None;

    /// <summary>Whether this node is only present to hold a match below it.</summary>
    public bool IsContext => _match == SceneTreeMatch.Ancestor;

    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>
/// The shell's mirror of the scene graph, maintained from
/// <see cref="FrameSnapshot"/>s.
/// </summary>
// UI thread only. An overflowed change log is answered by asking the render
// thread for the whole graph, which comes back as Added changes so there is
// one apply path.
public sealed class SceneTreeModel
{
    private readonly EngineHost _host;
    private readonly ILogger _logger;
    private readonly Dictionary<Guid, SceneTreeNode> _index = [];

    // So a run of overflowed snapshots queues one rebuild.
    private bool _rebuildPending;

    private long _appliedFrame = -1;

    // Current selection and a scratch set, swapped per apply.
    private HashSet<Guid> _selectedIds = [];
    private HashSet<Guid> _incoming = [];

    private string _filter = string.Empty;

    // Scratch for the visible-row list, reused across rebuilds.
    private readonly List<SceneTreeNode> _desired = [];
    private readonly HashSet<SceneTreeNode> _desiredSet = [];

    // Child to parent. Written only by Insert and RemoveFromParent.
    private readonly Dictionary<SceneTreeNode, SceneTreeNode> _parents = [];

    /// <summary>Creates a tree fed by <paramref name="host"/>.</summary>
    public SceneTreeModel(EngineHost host, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(logger);
        _host = host;
        _logger = logger;
    }

    /// <summary>The top-level nodes, which for a live scene is its root.</summary>
    public ObservableCollection<SceneTreeNode> Roots { get; } = [];

    /// <summary>
    /// The currently visible rows, flattened in display order. This is what a
    /// virtualizing list binds to.
    /// </summary>
    // Patched in place: replacing the collection resets scroll and selection.
    public ObservableCollection<SceneTreeNode> Rows { get; } = [];

    /// <summary>How many nodes the tree is showing.</summary>
    public int Count => _index.Count;

    /// <summary>
    /// Applies one snapshot's structural changes. Every published snapshot must
    /// be passed here once: a skipped one loses its changes for good.
    /// </summary>
    public void ApplyChanges(FrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Replaying a batch would insert at indices that are no longer right.
        if (snapshot.FrameNumber == _appliedFrame)
            return;

        _appliedFrame = snapshot.FrameNumber;

        if (snapshot.ChangesOverflowed)
        {
            MarkStale();
            return;
        }

        for (int i = 0; i < snapshot.Changes.Count; i++)
            ApplyChange(snapshot.Changes[i]);

        if (snapshot.Changes.Count == 0)
            return;

        // New nodes start out matching; re-run the filter to dim them.
        if (_filter.Length > 0)
            ApplyFilter(_filter);

        RebuildRows();
    }

    /// <summary>Asks the engine for the whole graph and rebuilds from it.</summary>
    public void MarkStale() => RequestRebuild();

    /// <summary>Marks which nodes the engine reports as selected.</summary>
    public void ApplySelection(IReadOnlyList<Guid> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ApplySelectionCore(selected);
    }

    /// <summary>
    /// Expands the ancestors of the node with this id so its row is visible.
    /// Never collapses anything and does not expand the node itself. False when
    /// the tree does not have the id, which is normal for a node added this frame.
    /// </summary>
    public bool TryReveal(Guid nodeId, out SceneTreeNode node)
    {
        if (!_index.TryGetValue(nodeId, out SceneTreeNode? found))
        {
            node = null!;
            return false;
        }

        node = found;

        bool opened = false;
        SceneTreeNode current = found;
        while (_parents.TryGetValue(current, out SceneTreeNode? parent))
        {
            opened |= !parent.IsExpanded;
            parent.IsExpanded = true;
            current = parent;
        }

        if (opened)
            RebuildRows();

        return true;
    }

    /// <summary>Opens or closes a node's children and updates <see cref="Rows"/>.</summary>
    public void ToggleExpanded(SceneTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!node.HasChildren)
            return;

        node.IsExpanded = !node.IsExpanded;
        RebuildRows();
    }

    /// <summary>Resolves an id to its row object.</summary>
    public bool TryGetNode(Guid nodeId, out SceneTreeNode node)
    {
        if (_index.TryGetValue(nodeId, out SceneTreeNode? found))
        {
            node = found;
            return true;
        }

        node = null!;
        return false;
    }

    /// <summary>
    /// Whether a node currently has a visible row (no collapsed ancestor).
    /// </summary>
    public bool IsRowVisible(SceneTreeNode node) => _desiredSet.Contains(node);

    /// <summary>The node's parent, or null for a top-level row.</summary>
    public SceneTreeNode? ParentOf(SceneTreeNode node) =>
        _parents.TryGetValue(node, out SceneTreeNode? parent) ? parent : null;

    /// <summary>
    /// Collects the selected ids whose rows are hidden under a collapsed parent.
    /// The list cannot report those, so an additive selection must add them back.
    /// </summary>
    public void CollectHiddenSelected(List<Guid> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        foreach (Guid id in _selectedIds)
        {
            if (_index.TryGetValue(id, out SceneTreeNode? node) && !_desiredSet.Contains(node))
                into.Add(id);
        }
    }

    /// <summary>Expands or collapses a node and everything under it.</summary>
    public void SetSubtreeExpanded(SceneTreeNode node, bool expanded)
    {
        ArgumentNullException.ThrowIfNull(node);

        SetExpandedRecursive(node, expanded);
        RebuildRows();
    }

    private static void SetExpandedRecursive(SceneTreeNode node, bool expanded)
    {
        if (node.Children.Count > 0)
            node.IsExpanded = expanded;

        for (int i = 0; i < node.Children.Count; i++)
            SetExpandedRecursive(node.Children[i], expanded);
    }

    /// <summary>
    /// True while <see cref="Rows"/> is being patched. A list control reports a
    /// removed row as a deselection; the view must ignore those while this is
    /// set or collapsing a group clears the engine's selection.
    /// </summary>
    public bool IsPatchingRows { get; private set; }

    private void RebuildRows()
    {
        _desired.Clear();
        for (int i = 0; i < Roots.Count; i++)
            Flatten(Roots[i], 0);

        SyncRows();
    }

    private void Flatten(SceneTreeNode node, int depth)
    {
        node.Depth = depth;
        node.HasChildren = node.Children.Count > 0;
        _desired.Add(node);

        if (!node.IsExpanded)
            return;

        for (int i = 0; i < node.Children.Count; i++)
            Flatten(node.Children[i], depth + 1);
    }

    // Patches Rows to match _desired, touching only rows that differ.
    private void SyncRows()
    {
        _desiredSet.Clear();
        for (int i = 0; i < _desired.Count; i++)
            _desiredSet.Add(_desired[i]);

        IsPatchingRows = true;
        try
        {
            int index = 0;
            while (index < _desired.Count)
            {
                if (index >= Rows.Count)
                {
                    Rows.Add(_desired[index]);
                    index++;
                    continue;
                }

                if (ReferenceEquals(Rows[index], _desired[index]))
                {
                    index++;
                    continue;
                }

                if (!_desiredSet.Contains(Rows[index]))
                {
                    Rows.RemoveAt(index);
                    continue;
                }

                Rows.Insert(index, _desired[index]);
                index++;
            }

            while (Rows.Count > _desired.Count)
                Rows.RemoveAt(Rows.Count - 1);
        }
        finally
        {
            IsPatchingRows = false;
        }
    }

    /// <summary>How many nodes pass the current filter. Equals <see cref="Count"/> when there is none.</summary>
    public int MatchCount { get; private set; }

    /// <summary>
    /// Filters the tree to nodes whose name contains <paramref name="text"/>,
    /// or to a kind with a <c>t:</c> prefix (<c>t:light</c>, <c>t:part</c>).
    /// Non-matching rows are dimmed, not removed. Empty text clears the filter.
    /// </summary>
    public void ApplyFilter(string? text)
    {
        string query = (text ?? string.Empty).Trim();
        _filter = query;

        if (query.Length == 0)
        {
            foreach (SceneTreeNode node in _index.Values)
                node.Match = SceneTreeMatch.Match;

            FilterIsUnknown = false;
            MatchCount = _index.Count;
            return;
        }

        SceneNodeKind? kindQuery = null;
        bool kindFilter = query.StartsWith("t:", StringComparison.OrdinalIgnoreCase);
        if (kindFilter)
        {
            kindQuery = ParseKind(query[2..]);
            query = string.Empty;
        }

        // An unknown "t:" kind must match nothing. Without this it would fall
        // through to Name.Contains(""), which matches every node.
        FilterIsUnknown = kindFilter && kindQuery is null;

        int matches = 0;
        foreach (SceneTreeNode node in _index.Values)
        {
            bool hit = !FilterIsUnknown
                && (kindQuery is { } wanted
                    ? node.Kind == wanted
                    : node.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

            node.Match = hit ? SceneTreeMatch.Match : SceneTreeMatch.None;
            if (hit)
                matches++;
        }

        MatchCount = matches;

        // Promote the ancestors of every hit so a deep match stays reachable.
        foreach (SceneTreeNode node in _index.Values)
        {
            if (node.Match != SceneTreeMatch.Match)
                continue;

            SceneTreeNode current = node;
            while (_parents.TryGetValue(current, out SceneTreeNode? parent))
            {
                // Already promoted, so the rest of the chain is too.
                if (parent.Match != SceneTreeMatch.None)
                    break;

                parent.Match = SceneTreeMatch.Ancestor;
                current = parent;
            }
        }
    }

    /// <summary>Whether the filter is a <c>t:</c> query naming no known kind.</summary>
    public bool FilterIsUnknown { get; private set; }

    // The words the toolbar and menus use, plus the enum names. Case and a
    // trailing plural s are ignored.
    private static SceneNodeKind? ParseKind(string text) => text.Trim().TrimEnd('s').ToLowerInvariant() switch
    {
        "block" or "world" or "brush" or "brushworld" => SceneNodeKind.BrushWorld,
        "part" or "brushpart" => SceneNodeKind.BrushPart,
        "cut" or "hole" or "subtractive" or "brushsubtractive" => SceneNodeKind.BrushSubtractive,
        "light" or "lamp" => SceneNodeKind.Light,
        // "entitie" is what TrimEnd('s') leaves of "entities".
        "entity" or "entitie" or "logic" => SceneNodeKind.Entity,
        "mesh" or "model" or "prop" => SceneNodeKind.Mesh,
        "group" or "folder" => SceneNodeKind.Group,
        "empty" or "node" => SceneNodeKind.Empty,
        _ => null,
    };


    private void ApplyChange(in SceneChange change)
    {
        switch (change.Kind)
        {
            case SceneChangeKind.Added:
                Attach(change);
                break;

            case SceneChangeKind.Removed:
                Detach(change.NodeId);
                break;

            case SceneChangeKind.Reparented:
                // Reuse the node object so its subtree stays expanded.
                if (_index.TryGetValue(change.NodeId, out SceneTreeNode? moved))
                {
                    RemoveFromParent(moved);
                    Insert(moved, change.ParentId, change.SiblingIndex);
                    moved.Name = change.Name;
                    moved.Kind = change.NodeKind;
                }
                else
                {
                    Attach(change);
                }
                break;

            case SceneChangeKind.Renamed:
                if (_index.TryGetValue(change.NodeId, out SceneTreeNode? renamed))
                {
                    renamed.Name = change.Name;
                    renamed.Kind = change.NodeKind;
                }
                break;
        }
    }

    private void Attach(in SceneChange change)
    {
        if (_index.TryGetValue(change.NodeId, out SceneTreeNode? existing))
        {
            // An undone delete re-adds the node under the same id.
            existing.Name = change.Name;
            existing.Kind = change.NodeKind;
            RemoveFromParent(existing);
            Insert(existing, change.ParentId, change.SiblingIndex);
            return;
        }

        var node = new SceneTreeNode(change.NodeId, change.Name) { Kind = change.NodeKind };
        _index[change.NodeId] = node;
        Insert(node, change.ParentId, change.SiblingIndex);
    }

    private void Insert(SceneTreeNode node, Guid parentId, int siblingIndex)
    {
        SceneTreeNode? parent = null;
        bool hasParent = parentId != Guid.Empty && _index.TryGetValue(parentId, out parent);
        ObservableCollection<SceneTreeNode> siblings = hasParent ? parent!.Children : Roots;

        // The scene root is the only top-level row; open it or the panel shows
        // one collapsed line. One level only.
        if (!hasParent)
            node.IsExpanded = true;

        // Clamp: mid-batch, a reported index can be past the end of the list.
        int index = siblingIndex < 0 || siblingIndex > siblings.Count ? siblings.Count : siblingIndex;
        siblings.Insert(index, node);

        if (hasParent) _parents[node] = parent!;
        else _parents.Remove(node);
    }

    private void Detach(Guid nodeId)
    {
        if (!_index.TryGetValue(nodeId, out SceneTreeNode? node))
            return;

        RemoveFromParent(node);
        Forget(node);
    }

    private void RemoveFromParent(SceneTreeNode node)
    {
        if (_parents.TryGetValue(node, out SceneTreeNode? parent))
        {
            parent.Children.Remove(node);
            _parents.Remove(node);
            return;
        }

        Roots.Remove(node);
    }

    private void Forget(SceneTreeNode node)
    {
        _index.Remove(node.Id);
        _parents.Remove(node);
        for (int i = 0; i < node.Children.Count; i++)
            Forget(node.Children[i]);
        node.Children.Clear();
    }

    private void ApplySelectionCore(IReadOnlyList<Guid> selected)
    {
        // Diff against the last selection. Runs per snapshot, so clearing and
        // re-setting every flag would walk the whole index each time.
        _incoming.Clear();
        for (int i = 0; i < selected.Count; i++)
            _incoming.Add(selected[i]);

        foreach (Guid id in _selectedIds)
        {
            if (!_incoming.Contains(id) && _index.TryGetValue(id, out SceneTreeNode? gone))
                gone.IsSelected = false;
        }

        foreach (Guid id in _incoming)
        {
            if (!_selectedIds.Contains(id) && _index.TryGetValue(id, out SceneTreeNode? added))
                added.IsSelected = true;
        }

        (_selectedIds, _incoming) = (_incoming, _selectedIds);
    }

    private void RequestRebuild()
    {
        if (_rebuildPending)
            return;

        _rebuildPending = true;
        _logger.LogDebug("Scene tree fell behind or the scene was swapped; asking for the whole graph");

        // Only the render thread may read live nodes. The list posted back
        // holds values, not nodes.
        _host.EnqueueCommand(scene =>
        {
            var flattened = new List<SceneChange>();
            Flatten(scene.Root, flattened);

            Dispatcher.UIThread.Post(() =>
            {
                Roots.Clear();
                _index.Clear();
                _parents.Clear();
                _selectedIds.Clear();

                for (int i = 0; i < flattened.Count; i++)
                    ApplyChange(flattened[i]);

                if (_filter.Length > 0)
                    ApplyFilter(_filter);

                RebuildRows();

                _rebuildPending = false;
                _logger.LogDebug("Scene tree rebuilt: {Count} node(s)", _index.Count);
            });
        });
    }

    // Render thread only. Must be pre-order: a child applied before its parent
    // lands at the top level. Internal for tests.
    internal static void Flatten(SceneNode node, List<SceneChange> into)
    {
        into.Add(new SceneChange(
            SceneChangeKind.Added,
            node.Id,
            node.Parent?.Id ?? Guid.Empty,
            node.Name,
            node.IndexInParent,
            SceneNodeClassifier.Classify(node)));

        IReadOnlyList<SceneNode> children = node.Children;
        for (int i = 0; i < children.Count; i++)
            Flatten(children[i], into);
    }
}
