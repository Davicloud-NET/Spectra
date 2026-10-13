using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The Tags and Attributes sections of the property panel. Its
/// <c>DataContext</c> is the panel's <see cref="PropertyPanelModel"/>.
/// </summary>
// Don't hand-write InitializeComponent: a parameterless one shadows the
// generated overload and every x:Name field stays null.
public partial class TagsAndAttributesView : UserControl
{
    private AttributeRowModel? _colorRow;
    private bool _colorChanged;
    private bool _colorCancelled;

    /// <summary>Creates the two sections.</summary>
    public TagsAndAttributesView()
    {
        InitializeComponent();

        ColorPicker.ColorChanged += OnPickerColorChanged;
        ColorPicker.CommitRequested += () => ColorPopup.IsOpen = false;
        ColorPicker.CancelRequested += OnPickerCancel;
    }

    /// <summary>Raised when Escape ends an edit, so the host can take focus back.</summary>
    public event Action? EscapePressed;

    private PropertyPanelModel? Panel => DataContext as PropertyPanelModel;

    private void OnFieldFocused(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: PropertyFieldModel field })
            field.BeginEdit();
    }

    // A value is kept when the box is left. A new tag or attribute is not
    // added that way: leaving its box for the kind dropdown would add it
    // before the kind is picked. What was typed stays until Enter or Add.
    private void OnFieldBlurred(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: PropertyFieldModel field } && !IsAddField(field))
            field.Commit();
    }

    private bool IsAddField(PropertyFieldModel field) =>
        Panel is { } panel
        && (ReferenceEquals(field, panel.Tags.AddField) || ReferenceEquals(field, panel.Attributes.NewNameField));

    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: PropertyFieldModel field })
            return;

        // After Enter the box keeps focus but is not editing, so the next key
        // re-arms the guard. Not Escape: it hands focus away.
        if (e.Key is not (Key.Enter or Key.Escape) && !field.IsEditing)
            field.BeginEdit();

        switch (e.Key)
        {
            case Key.Enter:
                field.Commit();
                e.Handled = true;
                break;

            case Key.Escape:
                field.Revert();
                EscapePressed?.Invoke();
                e.Handled = true;
                break;
        }
    }

    private void OnRemoveTag(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TagChipModel chip })
            Panel?.Tags.Remove(chip);
    }

    private void OnRemoveAttribute(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender as Control) is { } row)
            Panel?.Attributes.Remove(row);
    }

    private void OnAddAttribute(object? sender, RoutedEventArgs e) => Panel?.Attributes.AddTyped();

    // The colour picker rides the property gesture, as a light's colour
    // does, so a whole drag is one undo entry.
    private void OnSwatchPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control swatch ||
            !e.GetCurrentPoint(swatch).Properties.IsLeftButtonPressed ||
            RowOf(swatch) is not { IsColor: true } row ||
            Panel is not { } panel)
        {
            return;
        }

        _colorRow = row;
        _colorChanged = false;
        _colorCancelled = false;

        ColorPicker.Open(row.ColorLinear);
        row.Cells[0].BeginScrub();
        panel.BeginGesture("Color");

        ColorPopup.PlacementTarget = swatch;
        ColorPopup.IsOpen = true;
        e.Handled = true;
    }

    private void OnPickerColorChanged(System.Numerics.Vector3 linear)
    {
        if (_colorRow is not { } row) return;

        _colorChanged = true;
        row.ScrubColor(linear);
    }

    private void OnPickerCancel()
    {
        _colorCancelled = true;
        ColorPopup.IsOpen = false;
    }

    private void OnColorPopupClosed(object? sender, EventArgs e)
    {
        if (_colorRow is not { } row) return;

        row.Cells[0].EndScrub();

        // Light dismiss commits. Only Escape cancels. No change records nothing.
        Panel?.EndGesture(_colorChanged && !_colorCancelled);

        _colorRow = null;
        _colorChanged = false;

        if (_colorCancelled)
        {
            // The swatch and its cell showed the drag. Back to what the level holds.
            Panel?.Attributes.ShowAgain();
            EscapePressed?.Invoke();
        }

        _colorCancelled = false;
    }

    // A cell's DataContext is its field. Its row is only reachable through the tree.
    private static AttributeRowModel? RowOf(Control? from)
    {
        for (Visual? v = from; v is not null; v = v.GetVisualParent())
        {
            if (v is Control { DataContext: AttributeRowModel row })
                return row;
        }

        return null;
    }
}
