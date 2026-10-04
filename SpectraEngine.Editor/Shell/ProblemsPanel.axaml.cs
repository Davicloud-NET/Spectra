using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>The problem list's view. Unlike the output pane it never scrolls on its own.</summary>
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
