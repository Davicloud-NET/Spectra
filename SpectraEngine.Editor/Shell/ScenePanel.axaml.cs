using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Editing.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The scene tree panel: filter, flat virtualized list, and the sync that
/// keeps the list in step with the engine's selection.
/// </summary>
// The window drains snapshots once and hands the newest to SyncSelection.
public partial class ScenePanel : UserControl
{
    /// <summary>
    /// The user changed the tree's selection; the whole id set goes to the
    /// engine as one replace batch.
    /// </summary>
    public event Action<IReadOnlyList<Guid>>? SelectionRequested;

    /// <summary>An in-place rename was committed: id and the new name.</summary>
    public event Action<Guid, string>? RenameRequested;

    /// <summary>A tree key or context-menu verb that maps to a host command.</summary>
    public event Action<EditorHostCommand>? CommandRequested;

    /// <summary>Frame the selection in the viewport (double-click, F, menu).</summary>
    public event Action? FrameRequested;

    /// <summary>
    /// A drag-and-drop asked for a reparent: the dragged ids, the new parent,
    /// and the child index to insert at (-1 appends).
    /// </summary>
    public event Action<IReadOnlyList<Guid>, Guid, int>? ReparentRequested;

    /// <summary>Where the panel's own diagnostics go. Set by the host window.</summary>
    public ILogger? Logger { get; set; }

    // Breaks the tree -> engine -> tree loop: the snapshot writing the
    // selection back would otherwise be reported as a fresh user selection.
    private bool _syncingSelection;

    // Only reveal a selection that is new and did not come from the tree.
    private Guid _revealedId;
    private Guid _treeRequestedId;

    // SelectionChanged carries no modifiers. Ctrl/Shift means extend, so
    // selected nodes hidden under a collapsed parent must survive.
    private KeyModifiers _gestureModifiers;

    private SceneTreeNode? _renaming;

    private static readonly DataFormat<Guid[]> DragFormat =
        DataFormat.CreateInProcessFormat<Guid[]>("spectra-scene-nodes");

    private const double DragThresholdPixels = 4.0;
    private SceneTreeNode? _pressedRow;
    // DoDragDropAsync wants the press that started the gesture, not the move.
    private PointerPressedEventArgs? _pressEvent;
    private Point _pressPoint;
    private bool _dragInProgress;
    private SceneTreeNode? _deferredCollapse;
    private SceneTreeNode? _dropRow;

    // Reused per sync; this runs at the snapshot rate.
    private readonly HashSet<SceneTreeNode> _listSelectionScratch = [];
    private readonly List<SceneTreeNode> _desiredListSelection = [];

    // The selection last requested, held until the engine echoes it. Snapshots
    // in between still describe the selection the gesture replaced.
    private readonly HashSet<Guid> _pendingSelection = [];
    private bool _hasPendingSelection;
    private int _pendingSelectionTicks;

    // Snapshots to wait for the echo before the engine wins anyway.
    private const int PendingSelectionTickLimit = 8;

    public ScenePanel()
    {
        InitializeComponent();

        // Tunnel: ListBox marks Left and Right handled itself, so a bubbling
        // handler for collapse/expand would never run.
        SceneTree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);

        // Tunnel: must see the press before the list's own selection logic
        // and before a context menu opens.
        SceneTree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
        SceneTree.AddHandler(PointerMovedEvent, OnTreePointerMoved, RoutingStrategies.Tunnel);
        SceneTree.AddHandler(PointerReleasedEvent, OnTreePointerReleased, RoutingStrategies.Tunnel);

        DragDrop.SetAllowDrop(SceneTree, true);
        SceneTree.AddHandler(DragDrop.DragOverEvent, OnTreeDragOver);
        SceneTree.AddHandler(DragDrop.DropEvent, OnTreeDrop);
        SceneTree.AddHandler(DragDrop.DragLeaveEvent, OnTreeDragLeave);
    }

    private ShellModel? Model => DataContext as ShellModel;

    /// <summary>Whether the filter box has keyboard focus.</summary>
    public bool IsFilterFocused => FilterBox.IsFocused;

    /// <summary>
    /// Clears the reveal and pending-selection state. Call when a session ends,
    /// so the next session's first pick is not mistaken for an echo.
    /// </summary>
    public void ResetSelectionMemory()
    {
        _revealedId = Guid.Empty;
        _treeRequestedId = Guid.Empty;
        _pendingSelection.Clear();
        _hasPendingSelection = false;
        _pendingSelectionTicks = 0;
        CancelRename();
    }

    /// <summary>
    /// Applies the engine's reported selection to the tree and reveals it.
    /// Pass the newest snapshot only.
    /// </summary>
    public void SyncSelection(FrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (Model?.Tree is not { } tree)
            return;

        // This snapshot predates the user's click. Applying it would revert
        // the click and break the next Ctrl-click.
        if (_hasPendingSelection)
        {
            // Bounded: the engine may answer with a different set (an id left
            // the scene), and then the echo never comes.
            if (!MatchesPending(snapshot.SelectedIds) && ++_pendingSelectionTicks < PendingSelectionTickLimit)
                return;

            _hasPendingSelection = false;
            _pendingSelectionTicks = 0;
        }

        _syncingSelection = true;
        tree.ApplySelection(snapshot.SelectedIds);
        SyncListSelection(tree, snapshot.SelectedIds);
        _syncingSelection = false;

        RevealSelection(tree, snapshot.SelectedIds);
    }

    private bool MatchesPending(IReadOnlyList<Guid> selected)
    {
        if (selected.Count != _pendingSelection.Count)
            return false;

        for (int i = 0; i < selected.Count; i++)
        {
            if (!_pendingSelection.Contains(selected[i]))
                return false;
        }

        return true;
    }

    // The highlight comes from model flags, but the ListBox's own selection
    // feeds the next Ctrl/Shift gesture, so it has to match the engine's too.
    private void SyncListSelection(SceneTreeModel tree, IReadOnlyList<Guid> selected)
    {
        if (SceneTree.SelectedItems is not { } items)
            return;

        _desiredListSelection.Clear();
        for (int i = 0; i < selected.Count; i++)
        {
            // An item outside ItemsSource cannot be selected.
            if (tree.TryGetNode(selected[i], out SceneTreeNode node) && tree.IsRowVisible(node))
                _desiredListSelection.Add(node);
        }

        // Usually unchanged. Clear and re-add fires SelectionChanged per row.
        if (items.Count == _desiredListSelection.Count)
        {
            _listSelectionScratch.Clear();
            foreach (object? item in items)
            {
                if (item is SceneTreeNode node)
                    _listSelectionScratch.Add(node);
            }

            bool same = true;
            for (int i = 0; i < _desiredListSelection.Count && same; i++)
                same = _listSelectionScratch.Contains(_desiredListSelection[i]);

            if (same)
                return;
        }

        items.Clear();
        for (int i = 0; i < _desiredListSelection.Count; i++)
            items.Add(_desiredListSelection[i]);
    }

    // Scrolls the tree to a viewport pick, expanding collapsed parents.
    // Only when the selection changed, did not come from the tree, and the
    // filter box is not focused.
    private void RevealSelection(SceneTreeModel tree, IReadOnlyList<Guid> selected)
    {
        if (selected.Count == 0)
        {
            _revealedId = Guid.Empty;
            return;
        }

        // Ids are in selection order; the last is the one just acted on.
        Guid target = selected[^1];
        if (target == _revealedId)
            return;

        _revealedId = target;

        // Echo of a row the user clicked in the tree.
        if (target == _treeRequestedId)
            return;

        if (FilterBox.IsFocused)
            return;

        if (!tree.TryReveal(target, out SceneTreeNode node))
        {
            // Not in the tree yet. Clear so the next tick retries.
            _revealedId = Guid.Empty;
            return;
        }

        // Posted: rows an expand just added have no extent until layout runs.
        // Don't assign SelectedItem here, it would collapse a multi-selection.
        Dispatcher.UIThread.Post(() =>
        {
            if (Model?.Tree is not { } current || !ReferenceEquals(current, tree))
                return;

            ScrollWithContext(tree, node);
        }, DispatcherPriority.Loaded);
    }

    // How far down the panel a revealed row rests.
    private const double RevealRestingFraction = 1.0 / 3.0;

    // Computed from the row's index: an off-screen row has no container under
    // virtualization. Sets the offset directly because an oversized
    // BringIntoView rect is clamped to the control and does nothing.
    private void ScrollWithContext(SceneTreeModel tree, SceneTreeNode node)
    {
        int index = tree.Rows.IndexOf(node);
        if (index < 0 || tree.Rows.Count == 0)
            return;

        if (SceneTree.Scroll is not { } scroller)
            return;

        double rowHeight = scroller.Extent.Height / tree.Rows.Count;
        if (rowHeight <= 0)
            return;

        double resting = (scroller.Viewport.Height - rowHeight) * RevealRestingFraction;
        double target = Math.Clamp(
            (index * rowHeight) - resting,
            0,
            Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));

        scroller.Offset = scroller.Offset.WithY(target);
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
            return;

        // The list drops removed rows from its selection and reports that
        // like a click. Without this, collapsing a group would deselect
        // everything inside it in the engine.
        if (Model?.Tree is { IsPatchingRows: true })
            return;

        var ids = new List<Guid>();
        if (SceneTree.SelectedItems is { } items)
        {
            foreach (object? item in items)
            {
                if (item is SceneTreeNode node)
                    ids.Add(node.Id);
            }
        }

        // The list only reports visible rows. An additive gesture keeps the
        // selected nodes hidden under collapsed parents.
        if ((_gestureModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0)
            Model?.Tree?.CollectHiddenSelected(ids);

        _treeRequestedId = ids.Count > 0 ? ids[^1] : Guid.Empty;

        _pendingSelection.Clear();
        for (int i = 0; i < ids.Count; i++)
            _pendingSelection.Add(ids[i]);

        _hasPendingSelection = true;
        SelectionRequested?.Invoke(ids);
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _gestureModifiers = e.KeyModifiers;
        PointerPointProperties props = e.GetCurrentPoint(SceneTree).Properties;

        if (props.IsRightButtonPressed)
        {
            if (RowNodeFrom(e.Source) is not { } node)
                return;

            // Right-press on an unselected row selects it before the menu
            // opens; on a selected row the whole set stays.
            if (!node.IsSelected && SceneTree.SelectedItems is { } items)
            {
                items.Clear();
                items.Add(node);
            }

            return;
        }

        if (!props.IsLeftButtonPressed || _renaming is not null)
            return;

        // A press on the chevron or the rename box must not arm a drag.
        if (RowNodeForDrag(e.Source) is not { } row)
            return;

        _pressedRow = row;
        _pressEvent = e;
        _pressPoint = e.GetPosition(SceneTree);

        // A plain press on one row of a multi-selection must not collapse it
        // yet, or dragging three rows would drag one. Collapse on release if
        // no drag began.
        if (row.IsSelected && e.KeyModifiers == KeyModifiers.None &&
            SceneTree.SelectedItems is { Count: > 1 })
        {
            _deferredCollapse = row;
            SceneTree.Focus();
            e.Handled = true;
        }
    }

    private void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedRow is not { } origin || _pressEvent is not { } press || _dragInProgress)
            return;

        if (!e.GetCurrentPoint(SceneTree).Properties.IsLeftButtonPressed)
        {
            _pressedRow = null;
            _pressEvent = null;
            return;
        }

        Point position = e.GetPosition(SceneTree);
        if (Math.Abs(position.X - _pressPoint.X) < DragThresholdPixels &&
            Math.Abs(position.Y - _pressPoint.Y) < DragThresholdPixels)
        {
            return;
        }

        StartDrag(press, origin);
    }

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressedRow = null;
        _pressEvent = null;

        if (_deferredCollapse is not { } node)
            return;

        _deferredCollapse = null;

        if (!_dragInProgress && ReferenceEquals(RowNodeFrom(e.Source), node) &&
            SceneTree.SelectedItems is { } items)
        {
            items.Clear();
            items.Add(node);
        }
    }

    private async void StartDrag(PointerPressedEventArgs trigger, SceneTreeNode origin)
    {
        _dragInProgress = true;
        _deferredCollapse = null;
        _pressedRow = null;
        _pressEvent = null;

        // A selected row drags the whole selection, hidden rows included.
        // An unselected one drags only itself.
        var ids = new List<Guid>();
        if (origin.IsSelected)
        {
            if (SceneTree.SelectedItems is { } items)
            {
                foreach (object? item in items)
                {
                    if (item is SceneTreeNode node)
                        ids.Add(node.Id);
                }
            }

            Model?.Tree?.CollectHiddenSelected(ids);
        }

        if (!ids.Contains(origin.Id))
            ids.Add(origin.Id);

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(DragFormat, ids.ToArray()));

        try
        {
            await DragDrop.DoDragDropAsync(trigger, transfer, DragDropEffects.Move);
        }
        finally
        {
            _dragInProgress = false;
            ClearDropIndicator();
        }
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        e.Handled = true;

        if (TryResolveDrop(e, out SceneTreeNode? row, out SceneTreeDropZone zone, out _, out _))
        {
            SetDropIndicator(row, zone);
            e.DragEffects = DragDropEffects.Move;
        }
        else
        {
            ClearDropIndicator();
        }
    }

    private void OnTreeDragLeave(object? sender, DragEventArgs e) => ClearDropIndicator();

    private void OnTreeDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        bool valid = TryResolveDrop(e, out _, out _, out Guid parentId, out int index);
        ClearDropIndicator();
        if (!valid)
            return;

        if (e.DataTransfer.TryGetValue(DragFormat) is { Length: > 0 } ids)
            ReparentRequested?.Invoke(ids, parentId, index);
    }

    // False for anything that must not drop: foreign data, a target inside the
    // dragged subtree, a sibling slot beside a top-level row.
    private bool TryResolveDrop(
        DragEventArgs e, out SceneTreeNode? row, out SceneTreeDropZone zone, out Guid parentId, out int index)
    {
        row = null;
        zone = SceneTreeDropZone.None;
        parentId = Guid.Empty;
        index = -1;

        if (Model?.Tree is not { } tree ||
            e.DataTransfer.TryGetValue(DragFormat) is not { Length: > 0 } ids)
        {
            return false;
        }

        if (RowBorderFrom(e.Source) is not { } border || border.DataContext is not SceneTreeNode target)
        {
            // Empty space below the rows: append to the scene root.
            if (tree.Roots.Count != 1 || ContainsId(ids, tree.Roots[0].Id))
                return false;

            parentId = tree.Roots[0].Id;
            return true;
        }

        double y = e.GetPosition(border).Y;
        double height = border.Bounds.Height;
        zone = y < height * 0.3 ? SceneTreeDropZone.Before
            : y > height * 0.7 ? SceneTreeDropZone.After
            : SceneTreeDropZone.Into;

        SceneTreeNode? parent = zone == SceneTreeDropZone.Into ? target : tree.ParentOf(target);
        if (parent is null)
        {
            // The scene root is not a row, so a top-level row has no sibling slot.
            return false;
        }

        // A drop inside the dragged subtree would be a cycle. The engine
        // refuses it too, but the cursor should say no first.
        for (SceneTreeNode? ancestor = parent; ancestor is not null; ancestor = tree.ParentOf(ancestor))
        {
            if (ContainsId(ids, ancestor.Id))
                return false;
        }

        row = target;
        parentId = parent.Id;
        index = zone switch
        {
            SceneTreeDropZone.Before => parent.Children.IndexOf(target),
            SceneTreeDropZone.After => parent.Children.IndexOf(target) + 1,
            _ => -1,
        };

        return true;
    }

    private static bool ContainsId(Guid[] ids, Guid id)
    {
        for (int i = 0; i < ids.Length; i++)
        {
            if (ids[i] == id)
                return true;
        }

        return false;
    }

    private void SetDropIndicator(SceneTreeNode? row, SceneTreeDropZone zone)
    {
        if (!ReferenceEquals(_dropRow, row))
            ClearDropIndicator();

        _dropRow = row;
        if (row is not null)
            row.DropZone = zone;
    }

    private void ClearDropIndicator()
    {
        if (_dropRow is { } row)
            row.DropZone = SceneTreeDropZone.None;

        _dropRow = null;
    }

    private static SceneTreeNode? RowNodeFrom(object? source)
    {
        for (Visual? current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Control { DataContext: SceneTreeNode node })
                return node;
        }

        return null;
    }

    // Like RowNodeFrom, but null for a press inside the chevron or the rename box.
    private static SceneTreeNode? RowNodeForDrag(object? source)
    {
        for (Visual? current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Button or TextBox)
                return null;

            if (current is Border { Classes: { } classes, DataContext: SceneTreeNode node } &&
                classes.Contains("row"))
            {
                return node;
            }
        }

        return null;
    }

    private static Border? RowBorderFrom(object? source)
    {
        for (Visual? current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Border border && border.Classes.Contains("row") &&
                border.DataContext is SceneTreeNode)
            {
                return border;
            }
        }

        return null;
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_renaming is not null)
            return;

        // Two quick chevron clicks must not also frame the camera.
        if (RowNodeForDrag(e.Source) is null)
            return;

        FrameRequested?.Invoke();
        e.Handled = true;
    }

    // Clicking the chevron must not select its row.
    private void OnChevronPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private void OnChevronClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SceneTreeNode node })
            return;

        Model?.Tree?.ToggleExpanded(node);
        Dispatcher.UIThread.Post(LogRealization, DispatcherPriority.Loaded);
    }

    // Nothing else would show it if virtualization stopped working.
    private void LogRealization()
    {
        if (Model?.Tree is not { } tree || SceneTree.ItemsPanelRoot is not { } panel)
            return;

        // For a virtualizing panel, Children is the realised set.
        Logger?.LogDebug(
            "Scene tree: {Realized} row(s) realised of {Visible} visible, {Total} in the scene ({Panel})",
            panel.Children.Count, tree.Rows.Count, tree.Count, panel.GetType().Name);
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        _gestureModifiers = e.KeyModifiers;

        // This tunnels ahead of the rename TextBox. Taking Left/Right here
        // would break its caret.
        if (_renaming is not null)
            return;

        if (Model?.Tree is not { } tree || SceneTree.SelectedItem is not SceneTreeNode node)
            return;

        // Handled here because the engine keymap only hears keys the viewport
        // receives.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            EditorHostCommand? chord = e.Key switch
            {
                Key.D => EditorHostCommand.Duplicate,
                Key.G when e.KeyModifiers.HasFlag(KeyModifiers.Shift) => EditorHostCommand.Ungroup,
                Key.G => EditorHostCommand.Group,
                Key.T => EditorHostCommand.ToggleBrushKind,
                _ => null,
            };

            if (chord is { } command)
            {
                CommandRequested?.Invoke(command);
                e.Handled = true;
                return;
            }
        }

        switch (e.Key)
        {
            case Key.Delete:
                CommandRequested?.Invoke(EditorHostCommand.Delete);
                e.Handled = true;
                return;

            case Key.F2:
                BeginRename(node);
                e.Handled = true;
                return;

            case Key.F when e.KeyModifiers == KeyModifiers.None:
                FrameRequested?.Invoke();
                e.Handled = true;
                return;
        }

        if (e.Key == Key.Right)
        {
            if (node.HasChildren && !node.IsExpanded)
                tree.ToggleExpanded(node);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Left)
            return;

        if (node.IsExpanded)
        {
            tree.ToggleExpanded(node);
            e.Handled = true;
            return;
        }

        // Parent is the first shallower row above.
        int index = tree.Rows.IndexOf(node);
        for (int i = index - 1; i >= 0; i--)
        {
            if (tree.Rows[i].Depth >= node.Depth)
                continue;

            SceneTree.SelectedItem = tree.Rows[i];
            break;
        }

        e.Handled = true;
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Model?.ClearFilter();
        e.Handled = true;
    }

    private void OnClearFilterClicked(object? sender, RoutedEventArgs e) => Model?.ClearFilter();

    private void BeginRename(SceneTreeNode node)
    {
        CancelRename();

        // A row scrolled out of the virtualization window has no container to
        // hold the editor.
        SceneTree.ScrollIntoView(node);

        _renaming = node;
        node.IsRenaming = true;

        // Posted: the editor cannot take focus until it has been laid out.
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_renaming, node))
                return;

            if (SceneTree.ContainerFromItem(node) is Control container &&
                container.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } box)
            {
                // Not bound: a snapshot republish would rewrite the text
                // mid-keystroke.
                box.Text = node.Name;
                box.Focus();
                box.SelectAll();
                return;
            }

            // No editor found. Cancel, or the tree keyboard stays dead.
            Logger?.LogDebug("Rename: no row editor for '{Name}'; the tree keyboard stays live", node.Name);
            CancelRename();
        }, DispatcherPriority.Loaded);
    }

    private void CancelRename()
    {
        if (_renaming is not { } node)
            return;

        _renaming = null;
        node.IsRenaming = false;
    }

    private void CommitRename(TextBox box)
    {
        if (_renaming is not { } node)
            return;

        _renaming = null;
        node.IsRenaming = false;

        string text = (box.Text ?? string.Empty).Trim();
        if (text.Length > 0 && !string.Equals(text, node.Name, StringComparison.Ordinal))
            RenameRequested?.Invoke(node.Id, text);
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        if (e.Key == Key.Enter)
        {
            CommitRename(box);
            SceneTree.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Cancel before moving focus: LostFocus commits.
            CancelRename();
            SceneTree.Focus();
            e.Handled = true;
        }
    }

    private void OnRenameBlurred(object? sender, RoutedEventArgs e)
    {
        // Blur commits, like the property panel's fields. Escape abandons.
        if (sender is TextBox box)
            CommitRename(box);
    }

    // A menu item inherits its DataContext from the row the menu opened on.
    private static SceneTreeNode? MenuNode(object? sender) =>
        (sender as Control)?.DataContext as SceneTreeNode;

    private void OnMenuRename(object? sender, RoutedEventArgs e)
    {
        if (MenuNode(sender) is { } node)
            BeginRename(node);
    }

    private void OnMenuFrame(object? sender, RoutedEventArgs e) => FrameRequested?.Invoke();

    private void OnMenuDuplicate(object? sender, RoutedEventArgs e) =>
        CommandRequested?.Invoke(EditorHostCommand.Duplicate);

    private void OnMenuDelete(object? sender, RoutedEventArgs e) =>
        CommandRequested?.Invoke(EditorHostCommand.Delete);

    private void OnMenuGroup(object? sender, RoutedEventArgs e) =>
        CommandRequested?.Invoke(EditorHostCommand.Group);

    private void OnMenuUngroup(object? sender, RoutedEventArgs e) =>
        CommandRequested?.Invoke(EditorHostCommand.Ungroup);

    private void OnMenuConvertKind(object? sender, RoutedEventArgs e) =>
        CommandRequested?.Invoke(EditorHostCommand.ToggleBrushKind);

    private void OnMenuExpandAll(object? sender, RoutedEventArgs e)
    {
        if (MenuNode(sender) is { } node)
            Model?.Tree?.SetSubtreeExpanded(node, expanded: true);
    }

    private void OnMenuCollapseAll(object? sender, RoutedEventArgs e)
    {
        if (MenuNode(sender) is { } node)
            Model?.Tree?.SetSubtreeExpanded(node, expanded: false);
    }
}
