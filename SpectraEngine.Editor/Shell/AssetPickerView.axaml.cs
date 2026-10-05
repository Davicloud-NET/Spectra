using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell;

/// <summary>One row in the asset picker.</summary>
/// <param name="ContentPath">What picking it writes. Empty means the engine default.</param>
/// <param name="IsSound">Whether the picker lists sounds, whose rows have a play button.</param>
public sealed record AssetPickerRow(string ContentPath, string Stem, string Folder, bool IsSound = false)
{
    /// <summary>Whether the row names a sound file to play.</summary>
    public bool CanPreview => IsSound && ContentPath.Length > 0;
}

/// <summary>
/// The list an asset row opens: the project's files of one kind, searchable.
/// </summary>
// No live preview on hover: it would recompile the world per row.
public partial class AssetPickerView : UserControl
{
    /// <summary>The row for wearing no material at all.</summary>
    public static readonly AssetPickerRow None = new(string.Empty, "None", "engine default");

    /// <summary>The row for playing no sound. Nothing stands in for an empty sound.</summary>
    public static readonly AssetPickerRow NoSound = new(string.Empty, "None", "no sound", IsSound: true);

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

    /// <summary>Raised on Escape.</summary>
    public event Action? Cancelled;

    /// <summary>How many rows the last search produced, for tests.</summary>
    public int RowCount => _rows.Count;

    // For tests.
    internal IReadOnlyList<AssetPickerRow> ShownRows => _rows;

    /// <summary>Fills the list and takes the keyboard.</summary>
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

        Query.Focus();
    }

    private void Refresh()
    {
        string query = Query.Text ?? string.Empty;

        bool sounds = _kind == ContentKind.Sound;

        _rows.Clear();
        _rows.Add(sounds ? NoSound : None);

        int matches = 0;
        if (_catalog is { } catalog)
        {
            foreach (AssetCatalogEntry entry in catalog.Search(query, _kind, MaxRows))
            {
                _rows.Add(new AssetPickerRow(
                    entry.ContentPath, entry.Stem, entry.Folder, sounds));
                matches++;
            }
        }

        Rows.ItemsSource = null;
        Rows.ItemsSource = _rows;

        // Preselect the current value so Enter on an untouched picker changes nothing.
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

    // For tests.
    internal void TakeSelected() => Take();
}
