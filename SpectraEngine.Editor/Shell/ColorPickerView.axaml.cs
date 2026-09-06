using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.ComponentModel;
using System.Numerics;

namespace SpectraEngine.Editor.Shell;

/// <summary>The colour picker's surface.</summary>
/// <remarks>
/// <para>
/// <b>Hand-built rather than Avalonia's ColorPicker.</b> That package is not
/// referenced by this project, adding it means a version row and an AOT posture
/// nobody has verified, and its ColorView would then need its tabbed Fluent
/// template restyled against the token rule. What is actually wanted is a square
/// and a strip.
/// </para>
/// <para>
/// <b>It reports and does not decide.</b> Every movement raises a linear colour;
/// whether that becomes an edit, and whether the edit is one undo entry or
/// sixty, is the panel's business.
/// </para>
/// </remarks>
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

    /// <summary>Raised on every movement, in linear light.</summary>
    public event Action<Vector3>? ColorChanged;

    /// <summary>Enter: keep it.</summary>
    public event Action? CommitRequested;

    /// <summary>Escape: put it back.</summary>
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

    /// <summary>Puts the markers and the hex box where the model says.</summary>
    private void SyncFromModel()
    {
        if (SquareMarker is null || StripMarker is null || HexBox is null) return;

        // Guarded, because writing the box's text is indistinguishable from
        // somebody typing into it and would post an edit per movement.
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

    // --- The square ---------------------------------------------------------
    //
    // Press captures, move tracks while captured, release ends. The pointer is
    // read against the panel rather than the event source, so a drag that
    // wanders over a marker or off the edge keeps working: the value simply
    // clamps, which is what a picker should do at its own boundary.

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

    // --- The hex box --------------------------------------------------------

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

        // A refused hex puts the model's own value back rather than sticking:
        // "#80" is unreadable and the box must not keep claiming it is a colour.
        if (Model.TrySetHex(HexBox.Text)) ColorChanged?.Invoke(Model.Linear);
        else HexBox.Text = Model.Hex;
    }
}
