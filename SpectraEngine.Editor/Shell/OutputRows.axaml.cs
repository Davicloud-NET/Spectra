using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The lines of the output log, for the panels that show it. A line that
/// names a place is a row that opens it, by a click or by Enter.
/// </summary>
public partial class OutputRows : UserControl
{
    private TopLevel? _window;

    public OutputRows()
    {
        InitializeComponent();
    }

    // Tab is pressed on whatever stands before the list, so the window is
    // asked for the key: before it moves focus, the list says to which row.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _window = TopLevel.GetTopLevel(this);
        _window?.AddHandler(KeyDownEvent, OnTabPressed, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _window?.RemoveHandler(KeyDownEvent, OnTabPressed);
        _window = null;

        base.OnDetachedFromVisualTree(e);
    }

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

    // Tab stops in the list once: on the row that had the keyboard last, or
    // on the newest line. The first row would scroll a full log to its top.
    private void OnTabPressed(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || !IsEffectivelyVisible)
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
