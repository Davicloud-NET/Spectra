using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell;

/// <summary>One row in the entity picker.</summary>
/// <param name="Name">What picking it writes into the target field.</param>
public sealed record EntityPickerRow(string Name, string ClassName);

/// <summary>
/// The entities a wire can aim at, searchable. The runtime tokens
/// (!self, !activator, !caller) are listed first.
/// </summary>
public partial class EntityPickerView : UserControl
{
    private readonly List<EntityPickerRow> _rows = [];
    private IReadOnlyList<EntityTargetInfo> _targets = [];
    private bool _truncated;
    private string _current = string.Empty;

    public EntityPickerView()
    {
        InitializeComponent();

        Query.TextChanged += OnQueryChanged;
        Query.KeyDown += OnQueryKeyDown;
        Rows.DoubleTapped += OnRowsDoubleTapped;
    }

    /// <summary>The name somebody chose.</summary>
    public event Action<string>? Picked;

    /// <summary>Raised on Escape.</summary>
    public event Action? Cancelled;

    /// <summary>How many rows the last search produced, for tests.</summary>
    public int RowCount => _rows.Count;

    /// <summary>How many rows one search offers.</summary>
    public const int MaxRows = 40;

    /// <summary>Fills the list and takes the keyboard.</summary>
    public void Open(IReadOnlyList<EntityTargetInfo> targets, bool truncated, string current)
    {
        _targets = targets ?? [];
        _truncated = truncated;
        _current = current ?? string.Empty;

        Query.Text = string.Empty;
        Refresh();
        Query.Focus();
    }

    private void Refresh()
    {
        string query = Query.Text ?? string.Empty;

        _rows.Clear();

        AddIfMatching(TargetNameIndex.SelfToken, "this entity", query);
        AddIfMatching(TargetNameIndex.ActivatorToken, "whoever triggered it", query);
        AddIfMatching(TargetNameIndex.CallerToken, "whoever sent the output", query);

        int matches = 0;
        List<(EntityPickerRow Row, int Score)> ranked = [];

        foreach (EntityTargetInfo target in _targets)
        {
            int score = query.Length == 0 ? 0 : CommandScore.Of(target.Name, query);
            if (score == CommandScore.NoMatch)
            {
                int byClass = CommandScore.Of(target.ClassName, query);
                if (byClass == CommandScore.NoMatch) continue;

                // A class match ranks below a name match.
                score = byClass - 8;
            }

            ranked.Add((new EntityPickerRow(target.Name, target.ClassName), score));
            matches++;
        }

        ranked.Sort(static (a, b) =>
        {
            int byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : string.CompareOrdinal(a.Row.Name, b.Row.Name);
        });

        foreach ((EntityPickerRow row, _) in ranked)
        {
            if (_rows.Count >= MaxRows + 3) break;
            _rows.Add(row);
        }

        Rows.ItemsSource = null;
        Rows.ItemsSource = _rows;

        int selected = 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            if (string.Equals(_rows[i].Name, _current, StringComparison.Ordinal))
            {
                selected = i;
                break;
            }
        }

        Rows.SelectedIndex = _rows.Count > 0 ? selected : -1;

        string footer =
            _rows.Count == 0 ? "Nothing in this scene matches."
            : _truncated ? $"This scene has more than {EntityPanelInfo.MaxTargets} entities. Type a name to reach the rest."
            : matches == 0 && query.Length == 0 ? "This scene has no other entities yet."
            : string.Empty;

        Footer.Text = footer;
        Footer.IsVisible = footer.Length > 0;
    }

    private void AddIfMatching(string token, string description, string query)
    {
        if (query.Length > 0 && CommandScore.Of(token, query) == CommandScore.NoMatch)
            return;

        _rows.Add(new EntityPickerRow(token, description));
    }

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
        if (Rows.SelectedItem is EntityPickerRow row)
            Picked?.Invoke(row.Name);
    }
}
