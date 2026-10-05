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

        SizeChanged += (_, _) => UpdateCramped();
        DataContextChanged += (_, _) => UpdateCramped();
    }

    // Half a tile is worse than a row, so a short panel shows rows.
    private void UpdateCramped()
    {
        if (Model is { } model && Bounds.Height > 0)
            model.IsCramped = Bounds.Height - Toolbar.Bounds.Height < TileRowHeight;
    }

    /// <summary>
    /// Raised when the user activates a file. Folders are opened by the panel itself.
    /// </summary>
    public event Action<ContentEntry>? EntryActivated;

    private ContentBrowserModel? Model =>
        (DataContext as ShellModel)?.Content;

    /// <summary>Raised when a row's menu asks for the file on disk.</summary>
    public event Action<ContentEntry>? RevealRequested;

    private void OnMenuInsert(object? sender, RoutedEventArgs e)
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

    // Hand-written table, not Enum.Parse: trimming removes enum name reflection.
    private void OnFilterClicked(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } model || (sender as Control)?.Tag is not string tag) return;

        model.Filter = tag switch
        {
            "Textures" => ContentFilter.Textures,
            "Materials" => ContentFilter.Materials,
            "Models" => ContentFilter.Models,
            "Sounds" => ContentFilter.Sounds,
            _ => ContentFilter.All,
        };
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
        if (EntryFrom(e.Source) is not { } entry)
            return;

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
