using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>The problem list's view.</summary>
/// <remarks>
/// <b>It does not follow the tail, and that is the difference from the output
/// pane.</b> A log is read at the bottom because the newest line is the one that
/// just happened; a problem list is read from the top, because the first thing
/// that broke is usually the cause of everything under it. Nothing here scrolls
/// on its own.
/// </remarks>
public partial class ProblemsPanel : UserControl
{
    public ProblemsPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Raised when a row is double-clicked, so the window can go to whatever it
    /// is about.
    /// </summary>
    /// <remarks>
    /// The panel raises rather than acts, because what a subject means is the
    /// window's business: a node id is a selection, an asset path is a file on
    /// disk, and this control knows about neither.
    /// </remarks>
    public event Action<ProblemEntry>? EntryActivated;

    private void OnClearClicked(object? sender, RoutedEventArgs e) =>
        (DataContext as ShellModel)?.Problems.Clear();

    private void OnDismissClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ProblemEntry entry)
            (DataContext as ShellModel)?.Problems.Remove(entry);
    }

    private void OnEntryActivated(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ProblemEntry entry)
            EntryActivated?.Invoke(entry);
    }
}
