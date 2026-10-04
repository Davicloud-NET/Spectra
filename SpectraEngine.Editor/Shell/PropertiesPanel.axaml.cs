using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The property panel: the selection's editable values, patched from every
/// published snapshot.
/// </summary>
public partial class PropertiesPanel : UserControl
{
    public PropertiesPanel()
    {
        InitializeComponent();

        ColorPicker.ColorChanged += OnPickerColorChanged;
        ColorPicker.CommitRequested += OnPickerCommit;
        ColorPicker.CancelRequested += OnPickerCancel;

        AssetPicker.Picked += OnAssetPicked;
        AssetPicker.Cancelled += OnAssetPickerCancelled;

        TargetPicker.Picked += OnTargetPicked;
        TargetPicker.Cancelled += OnTargetPickerCancelled;
    }

    /// <summary>Raised when Escape ends an edit, so the host can take focus back.</summary>
    public event Action? EscapePressed;

    private PropertyRowModel? _scrubRow;
    private PropertyFieldModel? _scrubField;
    private PropertyPanelModel? _scrubPanel;
    private Point _scrubOrigin;
    private double _scrubStart;
    private double _scrubValue;
    private double _scrubLastX;
    private bool _scrubMoved;

    private void OnPropertyFieldFocused(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: PropertyFieldModel field })
        {
            field.BeginEdit();
            return;
        }

        // The header's name box binds to the panel, not to a row.
        if (sender is TextBox { DataContext: ShellModel { Properties: { } panel } })
            panel.NameField.BeginEdit();
    }

    private void OnPropertyFieldBlurred(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: PropertyFieldModel field })
        {
            field.Commit();
            return;
        }

        if (sender is TextBox { DataContext: ShellModel { Properties: { } panel } })
            panel.NameField.Commit();
    }

    private void OnPropertyFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        PropertyFieldModel? field = box.DataContext as PropertyFieldModel
            ?? (box.DataContext as ShellModel)?.Properties?.NameField;

        if (field is null)
            return;

        // After Enter the box keeps focus but is not editing, so the next key
        // re-arms the guard. Not Escape: it hands focus away.
        if (e.Key is not (Key.Enter or Key.Escape) && !field.IsEditing)
            field.BeginEdit();

        switch (e.Key)
        {
            case Key.Enter:
                // Don't BeginEdit again here: a focused box would then stop
                // refreshing and show a stale value while the object moves.
                field.Commit();
                e.Handled = true;
                break;

            case Key.Escape:
                field.Revert();
                EscapePressed?.Invoke();
                e.Handled = true;
                break;

            case Key.Up:
                e.Handled = Nudge(box, field, +1, e.KeyModifiers);
                break;

            case Key.Down:
                e.Handled = Nudge(box, field, -1, e.KeyModifiers);
                break;
        }
    }

    // Arrow-key step. Commits the absolute value read from the box, not a delta.
    private static bool Nudge(TextBox box, PropertyFieldModel field, int direction, KeyModifiers modifiers)
    {
        if (FindRow(box) is not { IsScrubbable: true } row)
            return false;

        if (!PropertyFieldModel.TryParseNumber(field.Text, out float current))
            return false;

        float step = row.KeyStep * Scale(modifiers);
        field.BeginEdit();
        field.Text = PropertyFieldModel.Format(current + (step * direction));
        field.Commit();
        field.BeginEdit();
        return true;
    }

    private void OnAddConnection(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellModel { Properties: { } panel })
            panel.Wiring.Add();
    }

    private void OnRemoveConnection(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ConnectionRowModel row }
            && DataContext is ShellModel { Properties: { } panel })
        {
            panel.Wiring.Remove(row);
        }
    }

    private void OnAxisPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: PropertyFieldModel field } handle)
            return;

        BeginScrub(handle, FindRow(handle), field, e);
    }

    private void OnLabelPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: PropertyRowModel row } handle || !row.IsScrubbable)
            return;

        // A vector's label drags all three cells. The first field is only the readout.
        BeginScrub(handle, row, row.Fields.Count > 0 ? row.Fields[0] : null, e);
    }

    private void BeginScrub(
        Control handle, PropertyRowModel? row, PropertyFieldModel? field, PointerPressedEventArgs e)
    {
        // Left button only. PointerPressed fires for every button and capture
        // is per pointer, so a right-press would otherwise start a drag.
        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            return;

        if (row is null || field is null || !row.IsScrubbable)
            return;

        // A mixed cell has no start value, and an absolute write would
        // collapse the selection onto one number.
        if (!PropertyFieldModel.TryParseNumber(field.Text, out float start))
            return;

        if (DataContext is not ShellModel { Properties: { } panel })
            return;

        _scrubRow = row;
        _scrubField = field;
        _scrubPanel = panel;
        _scrubOrigin = e.GetPosition(this);
        _scrubLastX = _scrubOrigin.X;
        _scrubStart = start;
        _scrubValue = start;
        _scrubMoved = false;

        // Per-cell start: a label drag offsets each cell by the same amount.
        for (int i = 0; i < row.Fields.Count && i < row.ScrubStarts.Length; i++)
        {
            row.ScrubStarts[i] = PropertyFieldModel.TryParseNumber(row.Fields[i].Text, out float v)
                ? v
                : float.NaN;
        }

        foreach (PropertyFieldModel cell in row.Fields)
            cell.BeginScrub();

        panel.BeginGesture(row.Name);
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void OnScrubMoved(object? sender, PointerEventArgs e)
    {
        if (_scrubRow is not { } row || _scrubField is null || sender is not Control handle)
            return;

        if (!ReferenceEquals(e.Pointer.Captured, handle))
            return;

        double x = e.GetPosition(this).X;
        double dx = x - _scrubLastX;
        _scrubLastX = x;

        // Accumulated, so a modifier changed mid-drag only changes the rate from here on.
        _scrubValue += dx * row.ScrubStep * Scale(e.KeyModifiers);
        _scrubMoved = true;

        var value = (float)_scrubValue;

        // Label drag: same delta on each cell's own start. Writing `value`
        // to all three would flatten them onto x.
        if (ReferenceEquals(handle.DataContext, row))
        {
            var delta = (float)(_scrubValue - _scrubStart);
            for (int i = 0; i < row.Fields.Count && i < row.ScrubStarts.Length; i++)
            {
                float from = row.ScrubStarts[i];
                if (float.IsNaN(from))
                    continue;

                row.ScrubTo(row.Fields[i], from + delta);
            }
        }
        else
        {
            row.ScrubTo(_scrubField, value);
        }

        e.Handled = true;
    }

    private void OnScrubReleased(object? sender, PointerReleasedEventArgs e)
    {
        // No travel means a click: cancel so the history stays clean.
        EndScrub(commit: _scrubMoved);
        e.Handled = true;
    }

    private void OnScrubLost(object? sender, PointerCaptureLostEventArgs e) => EndScrub(commit: _scrubMoved);

    private void EndScrub(bool commit)
    {
        if (_scrubRow is { } row)
        {
            foreach (PropertyFieldModel cell in row.Fields)
                cell.EndScrub();
        }

        _scrubPanel?.EndGesture(commit);

        _scrubRow = null;
        _scrubField = null;
        _scrubPanel = null;
        _scrubMoved = false;
    }

    private static float Scale(KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Shift) ? 10f
        : modifiers.HasFlag(KeyModifiers.Control) ? 0.1f
        : 1f;

    // The colour picker rides the same property gesture as the numeric
    // scrubs, so a whole drag is one undo entry.

    private PropertyRowModel? _colorRow;
    private bool _colorChanged;
    private bool _colorCancelled;

    private void OnSwatchPressed(object? sender, PointerPressedEventArgs e)
    {
        // Left button only, as in BeginScrub.
        if (sender is not Control control ||
            !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed ||
            FindRow(control) is not { } row ||
            (DataContext as ShellModel)?.Properties is not { } panel)
        {
            return;
        }

        _colorRow = row;
        _colorChanged = false;
        _colorCancelled = false;

        ColorPicker.Open(row.ColorLinear);
        if (row.Fields.Count > 0) row.Fields[0].BeginScrub();
        panel.BeginGesture("Color");

        ColorPopup.PlacementTarget = control;
        ColorPopup.IsOpen = true;
        e.Handled = true;
    }

    private void OnPickerColorChanged(System.Numerics.Vector3 linear)
    {
        if (_colorRow is not { } row) return;

        _colorChanged = true;
        row.ScrubColor(linear);
    }

    private void OnPickerCommit() => ColorPopup.IsOpen = false;

    private void OnPickerCancel()
    {
        _colorCancelled = true;
        ColorPopup.IsOpen = false;
    }

    private void OnColorPopupClosed(object? sender, EventArgs e)
    {
        if (_colorRow is not { } row) return;

        if (row.Fields.Count > 0) row.Fields[0].EndScrub();

        // Light dismiss commits. Only Escape cancels. No change records nothing.
        (DataContext as ShellModel)?.Properties?.EndGesture(_colorChanged && !_colorCancelled);

        _colorRow = null;
        _colorChanged = false;

        if (_colorCancelled) EscapePressed?.Invoke();
        _colorCancelled = false;
    }

    private ConnectionRowModel? _targetRow;
    private bool _targetCancelled;

    private void OnPickTargetPressed(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ConnectionRowModel row) return;
        if ((DataContext as ShellModel)?.Properties?.Wiring is not { } wiring) return;

        _targetRow = row;
        _targetCancelled = false;

        TargetPicker.Open(wiring.Targets, wiring.TargetsTruncated, row.TargetField.Text);

        TargetPopup.PlacementTarget = (Control)sender!;
        TargetPopup.IsOpen = true;
    }

    private PropertyRowModel? _targetPropertyRow;

    // Same picker as the wiring one, over a keyvalue row declared as a target name.
    private void OnPickRowTargetPressed(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control || FindRow(control) is not { } row || !row.IsTarget) return;
        if ((DataContext as ShellModel)?.Properties?.Wiring is not { } wiring) return;

        _targetPropertyRow = row;
        _targetRow = null;

        TargetPicker.Open(wiring.Targets, wiring.TargetsTruncated, row.Fields[0].Text);

        TargetPopup.PlacementTarget = control;
        TargetPopup.IsOpen = true;
    }

    private void OnTargetPicked(string name)
    {
        _targetRow?.PickTarget(name);
        _targetPropertyRow?.PickTarget(name);

        TargetPopup.IsOpen = false;
        _targetRow = null;
        _targetPropertyRow = null;
    }

    private void OnTargetPickerCancelled()
    {
        _targetCancelled = true;
        TargetPopup.IsOpen = false;
        _targetRow = null;
        _targetPropertyRow = null;

        EscapePressed?.Invoke();
        _targetCancelled = false;
    }

    private PropertyRowModel? _assetRow;
    private bool _assetCancelled;

    // The catalogue is rebuilt at each open, so a file written a moment ago shows up.
    private void OnAssetCellPressed(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control ||
            FindRow(control) is not { } row ||
            !row.IsAsset)
        {
            return;
        }

        if ((DataContext as ShellModel)?.Assets is not { } catalog)
        {
            // No project, nothing to offer.
            return;
        }

        _assetRow = row;
        _assetCancelled = false;

        catalog.Rebuild(RootFor(catalog));
        AssetPicker.Open(catalog, AssetCatalog.KindFor(row.AssetKind), row.AssetPath);

        AssetPopup.PlacementTarget = control;
        AssetPopup.IsOpen = true;
    }

    private static string? RootFor(AssetCatalog catalog) => catalog.Root;

    private void OnAssetPicked(string contentPath)
    {
        if (_assetRow is { } row)
            row.PickAsset(contentPath);

        AssetPopup.IsOpen = false;
    }

    private void OnAssetPickerCancelled()
    {
        _assetCancelled = true;
        AssetPopup.IsOpen = false;
    }

    private void OnAssetPopupClosed(object? sender, EventArgs e)
    {
        _assetRow = null;

        if (_assetCancelled) EscapePressed?.Invoke();
        _assetCancelled = false;
    }

    // A vector cell's DataContext is the field. Its row is only reachable through the tree.
    private static PropertyRowModel? FindRow(Control? from)
    {
        for (Visual? v = from; v is not null; v = v.GetVisualParent())
        {
            if (v is Control { DataContext: PropertyRowModel row })
                return row;
        }

        return null;
    }

}
