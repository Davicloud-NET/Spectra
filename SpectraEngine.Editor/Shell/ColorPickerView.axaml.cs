using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.ComponentModel;
using System.Numerics;

namespace SpectraEngine.Editor.Shell;

/// <summary>The colour picker's surface. Reports movements; the caller decides what is an edit.</summary>
// Hand-built: Avalonia's ColorPicker package is not referenced and its AOT
// posture is unverified.
public partial class ColorPickerView : UserControl
{
    public ColorPickerView()
    {
        InitializeComponent();
        DataContext = Model;

        Model.Changed += _ => SyncFromModel();
        Model.PropertyChanged += OnModelChanged;
        SyncFromModel();
    }

    /// <summary>The colour being picked.</summary>
    public ColorPickerModel Model { get; } = new();

    /// <summary>Raised on every movement, with the linear colour.</summary>
    public event Action<Vector3>? ColorChanged;

    /// <summary>Raised on Enter.</summary>
    public event Action? CommitRequested;

    /// <summary>Raised on Escape.</summary>
    public event Action? CancelRequested;

    /// <summary>Opens the picker on a colour, NaN.X for a mixed selection.</summary>
    public void Open(Vector3 linear)
    {
        Model.Load(linear);
        SyncFromModel();
    }

    private bool _syncing;

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ColorPickerModel.Hue)
            or nameof(ColorPickerModel.Saturation)
            or nameof(ColorPickerModel.Value))
        {
            SyncFromModel();
        }
    }

    private void SyncFromModel()
    {
        if (SquareMarker is null || StripMarker is null || HexBox is null) return;

        // Guard: writing the box's text looks like typing and would post an edit.
        _syncing = true;
        try
        {
            Canvas.SetLeft(SquareMarker, (Model.Saturation * Square.Width) - 5);
            Canvas.SetTop(SquareMarker, ((1f - Model.Value) * Square.Height) - 5);
            Canvas.SetLeft(StripMarker, (Model.Hue / 360f * Strip.Width) - 1.5);
            HexBox.Text = Model.Hex;
        }
        finally
        {
            _syncing = false;
        }
    }

    // Positions are read against the panel, not the event source, so a drag
    // that leaves the edge keeps working and clamps.

    private void OnSquarePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Square).Properties.IsLeftButtonPressed) return;

        e.Pointer.Capture(Square);
        ApplySquare(e.GetPosition(Square));
    }

    private void OnSquareMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer.Captured, Square)) return;
        ApplySquare(e.GetPosition(Square));
    }

    private void OnStripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Strip).Properties.IsLeftButtonPressed) return;

        e.Pointer.Capture(Strip);
        ApplyStrip(e.GetPosition(Strip));
    }

    private void OnStripMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer.Captured, Strip)) return;
        ApplyStrip(e.GetPosition(Strip));
    }

    private void OnPointerDone(object? sender, PointerReleasedEventArgs e) => e.Pointer.Capture(null);

    private void ApplySquare(Point point)
    {
        Model.SetSaturationValue(
            (float)(point.X / Square.Width),
            1f - (float)(point.Y / Square.Height));

        ColorChanged?.Invoke(Model.Linear);
    }

    private void ApplyStrip(Point point)
    {
        Model.SetHue((float)(point.X / Strip.Width) * 360f);
        ColorChanged?.Invoke(Model.Linear);
    }

    private void OnHexKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitHex();
                CommitRequested?.Invoke();
                e.Handled = true;
                break;

            case Key.Escape:
                CancelRequested?.Invoke();
                e.Handled = true;
                break;
        }
    }

    private void OnHexBlurred(object? sender, RoutedEventArgs e) => CommitHex();

    private void CommitHex()
    {
        if (_syncing || HexBox is null) return;

        // Unreadable hex: put the model's value back.
        if (Model.TrySetHex(HexBox.Text)) ColorChanged?.Invoke(Model.Linear);
        else HexBox.Text = Model.Hex;
    }
}
