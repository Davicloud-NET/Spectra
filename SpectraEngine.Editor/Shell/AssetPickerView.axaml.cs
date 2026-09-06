using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell;

/// <summary>One row in the asset picker.</summary>
/// <param name="ContentPath">What picking it writes. Empty means the engine default.</param>
/// <param name="Stem">The file name, for reading.</param>
/// <param name="Folder">Its folder, for telling two alike names apart.</param>
public sealed record AssetPickerRow(string ContentPath, string Stem, string Folder);

/// <summary>
/// The list an asset row opens: the project's files of one kind, searchable.
/// </summary>
/// <remarks>
/// <para>
/// <b>"None" is a real row, not an empty search result.</b> Putting a face back
/// to the engine default is a thing people mean to do, and the only other way to
/// say it would be clearing a text box that no longer exists.
/// </para>
/// <para>
/// <b>No live preview as the selection moves.</b> Each hover would have to swap
/// the brush, invalidate its carve and its neighbours', recompile the world and
/// then roll all of it back when the pointer moved on: a preview costs exactly
/// what an assignment costs, so the assignment is the preview and Ctrl+Z is the
/// way back.
/// </para>
/// </remarks>
public partial class AssetPickerView : UserControl
{
    /// <summary>The row for wearing no material at all.</summary>
    public static readonly AssetPickerRow None = new(string.Empty, "None", "engine default");

    private readonly List<AssetPickerRow> _rows = [];
    private AssetCatalog? _catalog;
    private ContentKind _kind = ContentKind.Material;
    private string _current = string.Empty;
    private string _word = "material";

    /// <summary>Creates the view.</summary>
    public AssetPickerView()
    {
        InitializeComponent();

        Query.TextChanged += OnQueryChanged;
        Query.KeyDown += OnQueryKeyDown;
        Rows.DoubleTapped += OnRowsDoubleTapped;
    }

    /// <summary>The file somebody chose, as a content-relative path.</summary>
    public event Action<string>? Picked;

    /// <summary>Escape, so the caller can close and put the keyboard back.</summary>
    public event Action? Cancelled;

    /// <summary>How many rows the last search produced, for tests.</summary>
    public int RowCount => _rows.Count;

    /// <summary>Fills the list and takes the keyboard.</summary>
    /// <param name="catalog">The project's files, already walked.</param>
    /// <param name="kind">Which of them to offer.</param>
    /// <param name="currentPath">What the row holds now, preselected.</param>
    public void Open(AssetCatalog catalog, ContentKind kind, string currentPath)
    {
        _catalog = catalog;
        _kind = kind;
        _current = currentPath ?? string.Empty;
        _word = ContentClassifier.Label(kind).ToLowerInvariant();

        Query.PlaceholderText = "Search " + _word + "s";
        Query.Text = string.Empty;
        Refresh();

        // The caret goes in the box and the arrows still move the list, which is
        // what makes this one gesture rather than a click and then a search.
        Query.Focus();
    }

    /// <summary>Rebuilds the list for the current query.</summary>
    private void Refresh()
    {
        string query = Query.Text ?? string.Empty;

        _rows.Clear();
        _rows.Add(None);

        int matches = 0;
        if (_catalog is { } catalog)
        {
            foreach (AssetCatalogEntry entry in catalog.Search(query, _kind, MaxRows))
            {
                _rows.Add(new AssetPickerRow(
                    entry.ContentPath, entry.Stem, entry.Folder));
                matches++;
            }
        }

        Rows.ItemsSource = null;
        Rows.ItemsSource = _rows;

        // Preselect what the row already wears, so Enter on an unchanged picker
        // is a no-op rather than a silent reassignment to whatever sorted first.
        int selected = 0;
        for (int i = 1; i < _rows.Count; i++)
        {
            if (string.Equals(_rows[i].ContentPath, _current, StringComparison.Ordinal))
            {
                selected = i;
                break;
            }
        }

        Rows.SelectedIndex = _rows.Count > 0 ? selected : -1;

        string? warning = _catalog?.Warning;
        string footer =
            warning is { Length: > 0 } ? warning
            : matches == 0 && query.Length > 0 ? "Nothing matches. Only \"None\" is left."
            : matches == 0 ? $"This project has no {_word} files yet."
            : string.Empty;

        Footer.Text = footer;
        Footer.IsVisible = footer.Length > 0;
    }

    /// <summary>How many files one search offers.</summary>
    /// <remarks>
    /// A cap rather than the whole folder, because a project's texture count is
    /// unbounded and a list nobody can reach the end of is a search box with
    /// extra scrolling. Typing narrows it, which is the affordance.
    /// </remarks>
    public const int MaxRows = 40;

    private void OnQueryChanged(object? sender, TextChangedEventArgs e) => Refresh();

    private void OnRowsDoubleTapped(object? sender, TappedEventArgs e) => Take();

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;

            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                Take();
                e.Handled = true;
                break;

            case Key.Escape:
                Cancelled?.Invoke();
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        if (_rows.Count == 0) return;

        int next = Rows.SelectedIndex + delta;
        if (next < 0) next = 0;
        if (next >= _rows.Count) next = _rows.Count - 1;

        Rows.SelectedIndex = next;
        Rows.ScrollIntoView(next);
    }

    private void Take()
    {
        if (Rows.SelectedItem is not AssetPickerRow row) return;

        Picked?.Invoke(row.ContentPath);
    }

    /// <summary>Picks the highlighted row, for the render suite and for tests.</summary>
    internal void TakeSelected() => Take();
}
