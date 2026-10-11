using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The output log's view. Follows the tail only while already scrolled to it.
/// A line that names a place is a row that opens it, by a click or by Enter.
/// </summary>
public partial class OutputPanel : UserControl
{
    private const double TailSlack = 4.0;

    public OutputPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe();

        // Before the window moves focus: Tab stops in the list once, and
        // this says on which row.
        AddHandler(KeyDownEvent, OnTabPressed, RoutingStrategies.Tunnel);
    }

    private OutputLog? _log;

    private void Subscribe()
    {
        if (_log is not null)
            _log.Appended -= OnAppended;

        _log = (DataContext as ShellModel)?.Output;

        if (_log is not null)
            _log.Appended += OnAppended;
    }

    private void OnAppended(OutputEntry entry)
    {
        if (Scroller is not { } scroller)
            return;

        // Measure before the new row is laid out. After it the extent has
        // grown and this is never at the tail.
        bool atTail = scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - TailSlack;
        if (!atTail)
            return;

        // Post: the new row is not measured yet, ScrollToEnd now lands short.
        Dispatcher.UIThread.Post(scroller.ScrollToEnd, DispatcherPriority.Background);
    }

    private void OnClearClicked(object? sender, RoutedEventArgs e) =>
        (DataContext as ShellModel)?.Output.Clear();

    private void OnLineClicked(object? sender, RoutedEventArgs e) =>
        ((sender as Control)?.DataContext as OutputEntry)?.Open();

    private void OnCallClicked(object? sender, RoutedEventArgs e) =>
        ((sender as Control)?.DataContext as OutputFrame)?.Open();

    private void OnFoldClicked(object? sender, RoutedEventArgs e) =>
        ((sender as Control)?.DataContext as OutputStack)?.Toggle();

    // The rows that open something, top to bottom.
    private List<Button> OpeningRows() =>
    [
        .. Rows.GetVisualDescendants().OfType<Button>()
            .Where(row => row.Classes.Contains("outrow") && row.Focusable && row.IsEffectivelyVisible),
    ];

    // The row Tab lands on: the one that had the keyboard last, or the newest
    // line. The first one would scroll a full log back to its top.
    private void OnTabPressed(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab)
            return;

        List<Button> rows = OpeningRows();
        if (KeyboardNavigation.GetTabOnceActiveElement(Rows) is Button had && rows.Contains(had))
            return;

        if (rows.LastOrDefault(row => row.DataContext is OutputEntry) is { } newest)
            KeyboardNavigation.SetTabOnceActiveElement(Rows, newest);
    }

    private void OnRowGotFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button row && row.Classes.Contains("outrow"))
            KeyboardNavigation.SetTabOnceActiveElement(Rows, row);
    }

    private void OnRowsKeyDown(object? sender, KeyEventArgs e)
    {
        int step = e.Key switch
        {
            Key.Down => 1,
            Key.Up => -1,
            _ => 0,
        };

        if (step == 0 || e.Source is not Button from)
            return;

        List<Button> rows = OpeningRows();
        int at = rows.IndexOf(from);
        if (at < 0 || at + step < 0 || at + step >= rows.Count)
            return;

        rows[at + step].Focus(NavigationMethod.Directional);
        e.Handled = true;
    }
}
