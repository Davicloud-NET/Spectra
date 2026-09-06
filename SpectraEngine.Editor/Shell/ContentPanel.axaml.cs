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
    // The movement that separates a click from a drag, and the press that
    // started it. Avalonia 12's DoDragDropAsync wants the PRESS args, while the
    // threshold is only crossed during a move - the same pair, for the same
    // reason, as the scene tree's row drag.
    private const double DragThresholdPixels = 4.0;

    private ContentEntry? _pressedEntry;
    private PointerPressedEventArgs? _pressEvent;
    private Point _pressPoint;
    private bool _dragInProgress;

    public ContentPanel()
    {
        InitializeComponent();

        // TUNNEL, and deliberately never claiming the event. The tiles carry
        // their own DoubleTapped for descending into a folder, and a bubbling
        // handler here would see the press only after the item had, which is
        // fine for a click and useless for a drag: the gesture has to be
        // recognised from the same press the tile is about to treat as a tap.
        AddHandler(PointerPressedEvent, OnTilePointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnTilePointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnTilePointerReleased, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Raised when the user activates a FILE (a folder is handled here, by
    /// descending into it).
    /// </summary>
    /// <remarks>
    /// An intent rather than an action, like every other panel in this shell:
    /// the panel knows what was double-clicked, and the window is the only
    /// thing that knows whether there is a session to insert it into.
    /// </remarks>
    public event Action<ContentEntry>? EntryActivated;

    private ContentBrowserModel? Model =>
        (DataContext as ShellModel)?.Content;

    /// <summary>Raised when a row's menu asks for the file on disk.</summary>
    /// <remarks>
    /// An intent, like <see cref="EntryActivated"/>: opening a shell window is
    /// the window's business, and this panel is used headlessly in tests.
    /// </remarks>
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

    /// <summary>
    /// Switches the kind filter from the chip's own tag.
    /// </summary>
    /// <remarks>
    /// <b>A hand-written table, never <c>Enum.Parse</c>.</b> Reflection over
    /// enum names is exactly what trimming removes, so a published build would
    /// fail here having worked in every debug run - the same discipline the
    /// console's verb table and the gizmo shortcuts follow.
    /// </remarks>
    private void OnFilterClicked(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } model || (sender as Control)?.Tag is not string tag) return;

        model.Filter = tag switch
        {
            "Textures" => ContentFilter.Textures,
            "Materials" => ContentFilter.Materials,
            "Models" => ContentFilter.Models,
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

    /// <summary>
    /// Escape clears the query and gives the keyboard back.
    /// </summary>
    /// <remarks>
    /// A search box with no way out but selecting its text and deleting it is
    /// the one control in a panel that can trap somebody: every other field in
    /// this shell abandons on Escape and this one has to agree with them.
    /// </remarks>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Model is not { } model) return;

        if (model.Query.Length > 0)
        {
            model.Query = string.Empty;
            e.Handled = true;
        }
    }

    // The list control reports its own selection, which is the same selection
    // the tiles set by hand: one model property either way, so the details strip
    // cannot disagree with whichever view is on screen.
    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: ContentEntry entry })
            Model?.Select(entry);
    }

    private void OnEntryActivated(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: ContentEntry entry })
            return;

        if (entry.IsFolder)
            Model?.Open(entry);
        else
            EntryActivated?.Invoke(entry);
    }

    // --- Drag source ---------------------------------------------------------

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedEntry = null;
        _pressEvent = null;

        // Left button only. Avalonia raises PointerPressed for every button, and
        // a right-press on its way to a context menu that opened a drag instead
        // would be a gesture nobody asked for.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (EntryFrom(e.Source) is not { IsFolder: false } entry)
            return;

        _pressedEntry = entry;
        _pressEvent = e;
        _pressPoint = e.GetPosition(this);
    }

    private void OnTilePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragInProgress || _pressedEntry is not { } entry || _pressEvent is not { } press)
            return;

        // The button is re-checked on every move rather than trusted from the
        // press. A gesture that ended somewhere this panel never saw - a capture
        // taken away, a release delivered elsewhere - would otherwise leave the
        // press state armed, and the next idle sweep of the pointer across a
        // tile would start a drag nobody asked for.
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

        // Refused HERE rather than at the drop, because a drag that cannot
        // possibly resolve should never start: the "no drop" cursor over every
        // surface in the window is a clearer answer than a payload the viewport
        // has to decline.
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
        // A press that never became a drag is a click, and a click selects.
        // Measured the same way the drag threshold is, because these are the
        // two readings of one gesture and a second definition of "moved" would
        // let both fire.
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
            // Copy rather than Move, and the distinction is not cosmetic: the
            // file stays exactly where it is and the scene gains a reference to
            // it, so a Move cursor would promise that dropping moved something
            // on disk.
            await DragDrop.DoDragDropAsync(trigger, transfer, DragDropEffects.Copy);
        }
        finally
        {
            _dragInProgress = false;
        }
    }

    // The entry a press landed on, found by walking up from whatever leaf the
    // template put under the pointer: the tile's picture, its name and its size
    // label are all separate controls sharing the row's DataContext.
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
