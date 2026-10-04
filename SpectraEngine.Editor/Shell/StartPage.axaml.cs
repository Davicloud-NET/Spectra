using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.IO;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// One recent project as the start page shows it: the record, plus the labels
/// the row binds.
/// </summary>
/// <param name="Location">
/// The folder, shortened from the left so the part naming the project survives.
/// </param>
/// <param name="IsMissing">The project's folder is no longer on disk.</param>
public sealed record RecentProjectRow(
    RecentProject Source, string Name, string Path, string Location, string OpenedLabel, bool IsMissing);

/// <summary>
/// The launch experience: recent projects, and the three ways to get something
/// open. Shown instead of the editor until a session exists.
/// </summary>
// Only raises events and renders a list. Pickers, dialogs and session
// lifetimes belong to the window.
public partial class StartPage : UserControl
{
    /// <summary>The user asked to create a project.</summary>
    public event Action? NewProjectRequested;

    /// <summary>The user asked to browse for a project.</summary>
    public event Action? OpenProjectRequested;

    /// <summary>The user asked to open a loose map bundle, outside any project.</summary>
    public event Action? OpenMapRequested;

    /// <summary>The user activated a recent-project row.</summary>
    public event Action<RecentProject>? RecentProjectPicked;

    /// <summary>The user asked to drop a recent entry from the list.</summary>
    public event Action<RecentProject>? RecentProjectForgotten;

    /// <summary>The user asked to see a recent project in the OS file browser.</summary>
    public event Action<RecentProject>? RecentProjectRevealRequested;

    private readonly List<RecentProjectRow> _all = [];
    private readonly List<RecentProjectRow> _shown = [];

    public StartPage()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FocusRecents();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
            FocusRecents();
    }

    // So Enter opens the most recent project straight after launch.
    private void FocusRecents() =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (IsEffectivelyVisible && RecentList.IsVisible)
                    RecentList.Focus();
            },
            DispatcherPriority.Loaded);

    /// <summary>Rebuilds the recent list.</summary>
    public void ShowRecents(IReadOnlyList<RecentProject> recents)
    {
        ArgumentNullException.ThrowIfNull(recents);

        _all.Clear();

        // Whole list at once: same-named projects need longer paths to differ.
        IReadOnlyList<string> locations = RecentLocation.Locations(recents);
        for (int i = 0; i < recents.Count; i++)
        {
            RecentProject recent = recents[i];
            bool missing = !Directory.Exists(recent.Path) && !File.Exists(recent.Path);
            _all.Add(new RecentProjectRow(
                recent, recent.Name, recent.Path, locations[i],
                missing ? "not found" : OpenedLabel(recent.OpenedUtc), missing));
        }

        ApplyFilter();
    }

    private static string OpenedLabel(DateTime openedUtc)
    {
        if (openedUtc == DateTime.MinValue)
            return string.Empty;

        DateTime local = openedUtc.ToLocalTime();
        int days = (DateTime.Now.Date - local.Date).Days;
        return days switch
        {
            <= 0 => $"today {local:HH:mm}",
            1 => "yesterday",
            < 7 => $"{days} days ago",
            _ => local.ToString("yyyy-MM-dd"),
        };
    }

    private void ApplyFilter()
    {
        string query = (FilterBox.Text ?? string.Empty).Trim();

        _shown.Clear();
        foreach (RecentProjectRow row in _all)
        {
            if (query.Length == 0 ||
                row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _shown.Add(row);
            }
        }

        // _shown is a plain List, so null first to make the ListBox re-read it.
        RecentList.ItemsSource = null;
        RecentList.ItemsSource = _shown;

        RecentList.IsVisible = _shown.Count > 0;

        // The first row is what Enter opens, so it is the one shown selected.
        if (_shown.Count > 0)
            RecentList.SelectedIndex = 0;

        CountLabel.Text = _all.Count > 0 ? _all.Count.ToString() : string.Empty;
        ColumnHeadings.IsVisible = _shown.Count > 1;
        FilterRow.IsVisible = _all.Count > 4;
        EmptyState.IsVisible = _shown.Count == 0;
        EmptyActions.IsVisible = _all.Count == 0;
        FirstRunHelp.IsVisible = _all.Count == 0;

        // Two empty states: a first launch, and a filter that matched nothing.
        bool firstRun = _all.Count == 0;
        EmptyTitle.Text = firstRun ? "Nothing open yet." : "No match.";
        EmptyLabel.Text = firstRun
            ? "Make a project to start building, or open one you already have. Projects you open appear in this list."
            : $"None of your recent projects match “{query}”.";
    }

    private void OnFilterChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                FilterBox.Text = string.Empty;
                e.Handled = true;
                break;

            case Key.Down when _shown.Count > 0:
                RecentList.SelectedIndex = 0;
                RecentList.Focus();
                e.Handled = true;
                break;

            case Key.Enter when _shown.Count > 0:
                RecentProjectPicked?.Invoke(_shown[0].Source);
                e.Handled = true;
                break;
        }
    }

    private void OnRecentTapped(object? sender, TappedEventArgs e)
    {
        // A tap on one of the row's own buttons bubbles here too.
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;

        if (MenuRow(sender) is { } row)
            RecentProjectPicked?.Invoke(row.Source);
    }

    private void OnRowRevealClicked(object? sender, RoutedEventArgs e)
    {
        if (MenuRow(sender) is { } row)
            RecentProjectRevealRequested?.Invoke(row.Source);
    }

    private void OnRowForgetClicked(object? sender, RoutedEventArgs e)
    {
        if (MenuRow(sender) is { } row)
            RecentProjectForgotten?.Invoke(row.Source);
    }

    private void OnRecentKeyDown(object? sender, KeyEventArgs e)
    {
        if (RecentList.SelectedItem is not RecentProjectRow row)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                RecentProjectPicked?.Invoke(row.Source);
                e.Handled = true;
                break;

            // Only forgets the list entry, so no confirmation.
            case Key.Delete:
                RecentProjectForgotten?.Invoke(row.Source);
                e.Handled = true;
                break;
        }
    }

    private void OnNewProjectClicked(object? sender, RoutedEventArgs e) => NewProjectRequested?.Invoke();
    private void OnOpenProjectClicked(object? sender, RoutedEventArgs e) => OpenProjectRequested?.Invoke();
    private void OnOpenMapClicked(object? sender, RoutedEventArgs e) => OpenMapRequested?.Invoke();

    // The row the sender belongs to: a menu item, a row button or the row itself.
    private static RecentProjectRow? MenuRow(object? sender) =>
        (sender as Control)?.DataContext as RecentProjectRow;

    private void OnRecentMenuOpen(object? sender, RoutedEventArgs e)
    {
        if (MenuRow(sender) is { } row)
            RecentProjectPicked?.Invoke(row.Source);
    }

    private void OnRecentMenuReveal(object? sender, RoutedEventArgs e)
    {
        if (MenuRow(sender) is { } row)
            RecentProjectRevealRequested?.Invoke(row.Source);
    }

    private void OnRecentMenuForget(object? sender, RoutedEventArgs e)
    {
        if (MenuRow(sender) is { } row)
            RecentProjectForgotten?.Invoke(row.Source);
    }
}
