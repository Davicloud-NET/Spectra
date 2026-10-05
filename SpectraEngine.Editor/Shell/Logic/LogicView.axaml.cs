using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.ComponentModel;
using System.Globalization;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// The body of the Logic pane: a toolbar, the graph of a level's wiring, what
/// the wires just did while it runs, and a status row.
/// </summary>
// Don't hand-write InitializeComponent: a parameterless one shadows the
// generated overload and every x:Name field stays null.
public partial class LogicView : UserControl
{
    /// <summary>Defines <see cref="Model"/>.</summary>
    public static readonly StyledProperty<LogicViewModel?> ModelProperty =
        AvaloniaProperty.Register<LogicView, LogicViewModel?>(nameof(Model));

    private LogicViewModel? _heard;
    private bool _isShown;

    /// <summary>Creates the view.</summary>
    public LogicView()
    {
        InitializeComponent();

        Graph.SelectRequested += (entity, adds) => SelectRequested?.Invoke(entity, adds);
        Graph.FrameRequested += entity => FrameRequested?.Invoke(entity);
        ApplyFit();
    }

    /// <summary>
    /// Raised when the view asks for an entity to be selected. The flag is
    /// whether to add it to the selection.
    /// </summary>
    public event Action<Guid, bool>? SelectRequested;

    /// <summary>Raised when the view asks for an entity to be selected and framed in the 3D view.</summary>
    public event Action<Guid>? FrameRequested;

    /// <summary>What the view shows. It remembers everything, the view nothing.</summary>
    public LogicViewModel? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != ModelProperty)
            return;

        Root.DataContext = Model;
        Graph.Model = Model;
        Listen(_isShown ? Model : null);
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isShown = true;
        Listen(Model);
    }

    /// <inheritdoc/>
    // The model outlives the view. It must not keep one that is gone.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isShown = false;
        Listen(null);
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc/>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyFit();
    }

    private void Listen(LogicViewModel? model)
    {
        if (ReferenceEquals(model, _heard))
            return;

        if (_heard is not null)
            _heard.PropertyChanged -= OnModelChanged;

        _heard = model;

        if (model is not null)
            model.PropertyChanged += OnModelChanged;

        ShowSteps();
        ApplyFit();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs change)
    {
        switch (change.PropertyName)
        {
            case nameof(LogicViewModel.Steps):
                ShowSteps();
                break;

            case nameof(LogicViewModel.IsPlaying) or nameof(LogicViewModel.Status):
                ApplyFit();
                break;
        }
    }

    // Hides what the rows have no room for, so nothing in them is ever cut
    // through or pushed onto its neighbour.
    private void ApplyFit()
    {
        double width = Bounds.Width;
        LogicViewFit fit = LogicViewFit.For(
            Bounds.Size, Model?.IsPlaying ?? false, Model?.Status ?? LogicStatus.None);

        ShowLabel.IsVisible = fit.ShowsLabels;
        StepsLabel.IsVisible = fit.ShowsLabels;
        AroundName.Text = fit.UsesShortNames ? LogicViewText.AroundSelectionShort : LogicViewText.AroundSelection;
        WholeName.Text = fit.UsesShortNames ? LogicViewText.WholeLevelShort : LogicViewText.WholeLevel;
        StepsBox.IsVisible = fit.ShowsSteps;
        PlayingPill.IsVisible = fit.ShowsPlaying;
        TickReadout.IsVisible = fit.ShowsTick;
        ZoomKeys.IsVisible = fit.ShowsZoomKeys;

        FilterBox.IsVisible = fit.ShowsFilter;
        FilterBox.PlaceholderText = fit.FilterWidth(width) >= LogicViewFit.LongPlaceholderWidth
            ? LogicViewText.FilterPlaceholder
            : LogicViewText.FilterPlaceholderShort;

        Counts.IsVisible = fit.ShowsCounts;
        Notes.IsVisible = fit.ShowsNotes;
        HintText.IsVisible = fit.ShowsHint;
        EventStrip.IsVisible = fit.ShowsEvents;
    }

    private void ShowSteps() =>
        StepsBox.Text = Model?.Steps.ToString(CultureInfo.InvariantCulture) ?? "";

    private void OnStepsKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                TakeSteps();
                break;

            case Key.Up:
                model.Steps++;
                break;

            case Key.Down:
                model.Steps--;
                break;

            case Key.Escape:
                ShowSteps();
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private void OnStepsLostFocus(object? sender, RoutedEventArgs e) => TakeSteps();

    // What was typed is taken if it is a number. The field then shows what
    // the model made of it.
    private void TakeSteps()
    {
        if (Model is { } model
            && int.TryParse(StepsBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int steps))
        {
            model.Steps = steps;
        }

        ShowSteps();
    }

    private void OnGoingNowhereClicked(object? sender, RoutedEventArgs e)
    {
        if (Model is not { Status.GoingNowhereSender: Guid entity } model)
            return;

        SelectRequested?.Invoke(entity, false);
        model.CenterOn(entity);
    }
}
