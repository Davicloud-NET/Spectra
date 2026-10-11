using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The content browser's view. Every gesture resolves to a call on
/// <see cref="ContentBrowserModel"/>, or to an intent the window answers.
/// </summary>
public partial class ContentPanel : UserControl
{
    // DoDragDropAsync wants the press args, but the threshold is only crossed
    // during a move, so the press is kept.
    private const double DragThresholdPixels = 4.0;

    // A tile with its name and size lines, plus the row's own margins.
    private const double TileRowHeight = 112.0;

    private ContentEntry? _pressedEntry;
    private PointerPressedEventArgs? _pressEvent;
    private Point _pressPoint;
    private bool _dragInProgress;

    public ContentPanel()
    {
        InitializeComponent();

        // Tunnel, and never handled: the drag has to see the press before the
        // tile does, and the tile still needs it for its own DoubleTapped.
        AddHandler(PointerPressedEvent, OnTilePointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnTilePointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnTilePointerReleased, RoutingStrategies.Tunnel);

        // Tunnel: the list takes Enter for itself and marks it handled.
        ListRows.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);

        SizeChanged += (_, _) => UpdateCramped();
        DataContextChanged += (_, _) => UpdateCramped();

        // The search box gets what the rest of the row leaves, so its width
        // moves with the panel's and with the breadcrumb's.
        SearchBox.SizeChanged += (_, _) => UpdateNarrow();
        DataContextChanged += (_, _) => UpdateNarrow();
    }

    // The least width the search box can be typed in.
    private const double SearchRoom = 100.0;

    // The row of chips and the breadcrumb at their full widths, each kept
    // from when it last showed.
    private double _chipsWidth;
    private double _breadcrumbWidth;

    // Asks what the search box would get with a chip per kind and the
    // breadcrumb in the row, whichever are showing. The answer is the same in
    // every state, so the row cannot flip, and the first letter of a search,
    // which hides the breadcrumb, does not bring the chips back.
    private void UpdateNarrow()
    {
        // Before the first layout there is no width to judge by.
        if (Model is not { } model || Toolbar.Bounds.Width <= 0)
            return;

        if (FilterChips.IsVisible && FilterChips.Bounds.Width > 0)
            _chipsWidth = FilterChips.Bounds.Width;

        if (Breadcrumb.IsVisible)
            _breadcrumbWidth = Breadcrumb.Bounds.Width;

        double room = SearchBox.Bounds.Width;

        if (!FilterChips.IsVisible)
            room += FilterMenuChip.Bounds.Width - _chipsWidth;

        if (!Breadcrumb.IsVisible)
            room -= _breadcrumbWidth;

        model.IsNarrow = room < SearchRoom;
    }

    // Half a tile is worse than a row, so a short panel shows rows.
    private void UpdateCramped()
    {
        if (Model is { } model && Bounds.Height > 0)
            model.IsCramped = Bounds.Height - Toolbar.Bounds.Height < TileRowHeight;
    }

    /// <summary>
    /// Raised when the user activates a file, by a double click or Enter.
    /// Folders are opened by the panel itself.
    /// </summary>
    public event Action<ContentEntry>? EntryActivated;

    /// <summary>Raised when a row's menu asks to put the file into the level.</summary>
    public event Action<ContentEntry>? InsertRequested;

    private ContentBrowserModel? Model =>
        (DataContext as ShellModel)?.Content;

    /// <summary>Raised when a row's menu asks for the file on disk.</summary>
    public event Action<ContentEntry>? RevealRequested;

    private void OnMenuInsert(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ContentEntry entry)
            InsertRequested?.Invoke(entry);
    }

    private void OnMenuOpen(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ContentEntry entry)
            EntryActivated?.Invoke(entry);
    }

    private void OnMenuReveal(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ContentEntry entry)
            RevealRequested?.Invoke(entry);
    }

    private void OnUpClicked(object? sender, RoutedEventArgs e) => Model?.GoUp();

    private void OnRefreshClicked(object? sender, RoutedEventArgs e) => Model?.Refresh();

    private void OnCrumbClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is string path)
            Model?.NavigateTo(path);
    }

    // A chip's Tag is its filter's word.
    private void OnFilterClicked(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } model || (sender as Control)?.Tag is not string tag) return;

        model.Filter = ContentFilterNames.Parse(tag);
    }

    // The narrow panel's one chip: the same kinds, as a list.
    private void OnFilterMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } model || sender is not Control chip) return;

        var list = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };

        foreach ((ContentFilter filter, string word) in ContentFilterNames.All)
        {
            var item = new MenuItem
            {
                Header = word,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = model.Filter == filter,
            };
            item.Click += (_, _) => model.Filter = filter;
            list.Items.Add(item);
        }

        list.ShowAt(chip);
    }

    private void OnViewModeClicked(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } model) return;

        model.ViewMode = model.ViewMode == ContentViewMode.Grid
            ? ContentViewMode.List
            : ContentViewMode.Grid;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Model is not { } model) return;

        if (model.Query.Length > 0)
        {
            model.Query = string.Empty;
            e.Handled = true;
        }
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: ContentEntry entry })
            Model?.Select(entry);
    }

    // From what was tapped, not from the sender: the list raises this for
    // all its rows, and its own DataContext is the shell's model.
    private void OnEntryActivated(object? sender, TappedEventArgs e)
    {
        if (EntryFrom(e.Source) is { } entry)
            Activate(entry);
    }

    // Enter on a row is a double click on it. Only the list takes the keyboard.
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not ListBox { SelectedItem: ContentEntry entry })
            return;

        Activate(entry);
        e.Handled = true;
    }

    private void Activate(ContentEntry entry)
    {
        if (entry.IsFolder)
            Model?.Open(entry);
        else
            EntryActivated?.Invoke(entry);
    }

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedEntry = null;
        _pressEvent = null;

        // PointerPressed fires for every button; a right-press is for the context menu.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (EntryFrom(e.Source) is not { IsFolder: false } entry)
            return;

        // A press on a row's play button plays the file. It never drags it.
        if (IsOnPlayButton(e.Source))
            return;

        _pressedEntry = entry;
        _pressEvent = e;
        _pressPoint = e.GetPosition(this);
    }

    private void OnTilePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragInProgress || _pressedEntry is not { } entry || _pressEvent is not { } press)
            return;

        // Re-check the button: a release this panel never saw would leave the
        // press armed and the next idle pointer sweep would start a drag.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pressedEntry = null;
            _pressEvent = null;
            return;
        }

        Point now = e.GetPosition(this);
        if (Math.Abs(now.X - _pressPoint.X) < DragThresholdPixels &&
            Math.Abs(now.Y - _pressPoint.Y) < DragThresholdPixels)
        {
            return;
        }

        // A file the engine cannot name never starts a drag.
        if (Model is not { } model || !model.TryDescribe(entry, out ContentDragPayload? payload))
        {
            _pressedEntry = null;
            _pressEvent = null;
            return;
        }

        _ = StartDragAsync(press, payload);
    }

    private void OnTilePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // A press that never became a drag is a click.
        if (_pressedEntry is { } entry && !_dragInProgress)
            Model?.Select(entry);

        _pressedEntry = null;
        _pressEvent = null;
    }

    private async System.Threading.Tasks.Task StartDragAsync(
        PointerPressedEventArgs trigger, ContentDragPayload payload)
    {
        _dragInProgress = true;
        _pressedEntry = null;
        _pressEvent = null;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(ContentDrag.Format, payload));

        try
        {
            // Copy, not Move: the file stays on disk and the scene references it.
            await DragDrop.DoDragDropAsync(trigger, transfer, DragDropEffects.Copy);
        }
        finally
        {
            _dragInProgress = false;
        }
    }

    private static bool IsOnPlayButton(object? source)
    {
        for (Visual? visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is SoundPreviewButton)
                return true;
        }

        return false;
    }

    // Walks up from the leaf under the pointer to the row's DataContext.
    private static ContentEntry? EntryFrom(object? source)
    {
        for (Visual? visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is StyledElement { DataContext: ContentEntry entry })
                return entry;
        }

        return null;
    }
}
