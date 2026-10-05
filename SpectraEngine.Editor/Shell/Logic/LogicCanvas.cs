using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// Draws the Logic view's graph and answers the pointer on it. It keeps
/// nothing: what is shown and where it sits is the model's.
/// </summary>
public sealed class LogicCanvas : Control
{
    /// <summary>Defines <see cref="Model"/>.</summary>
    public static readonly StyledProperty<LogicViewModel?> ModelProperty =
        AvaloniaProperty.Register<LogicCanvas, LogicViewModel?>(nameof(Model));

    private LogicGraphPainter? _painter;
    private LogicViewModel? _heard;
    private bool _isShown;
    private LogicScene? _hoverScene;
    private LogicSceneCard? _hoveredCard;
    private LogicSceneEdge? _hoveredEdge;

    // A press that has not been let go of yet.
    private IPointer? _pressed;
    private Point _pressPoint;
    private Point _lastPoint;
    private LogicPanZoom _pressView;
    private LogicPanZoom _panned;
    private LogicHit _pressHit;
    private bool _pans;
    private bool _moved;
    private bool _clicks;

    // The entity whose card the first press of a double click was on.
    private Guid? _firstPressed;

    /// <summary>Creates the canvas.</summary>
    public LogicCanvas()
    {
        ClipToBounds = true;

        // A press here takes the keyboard, so the window's keys act on what
        // was just selected and not on the filter box. Tab passes it by.
        Focusable = true;
        IsTabStop = false;
    }

    /// <summary>
    /// Raised when a click asks for an entity to be selected: a card's own,
    /// or the sender of a wire. The flag is whether to add it to the selection.
    /// </summary>
    public event Action<Guid, bool>? SelectRequested;

    /// <summary>Raised when a double click asks for an entity to be selected and framed in the 3D view.</summary>
    public event Action<Guid>? FrameRequested;

    /// <summary>What the canvas draws.</summary>
    public LogicViewModel? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        // Not in the constructor: the theme is read here, and a canvas may be
        // made before the application has one.
        _painter ??= new LogicGraphPainter();
        _painter.Draw(context, Bounds.Size, Model, _hoveredCard, _hoveredEdge);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ModelProperty)
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
    // The pane this sits in is moved between parents, and the model outlives
    // every one of them. It must not keep a canvas that is gone.
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

        if (Model is { } model)
            model.ViewSize = e.NewSize;
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        PointerPointProperties buttons = e.GetCurrentPoint(this).Properties;
        if (Model is not { } model || _pressed is not null
            || (!buttons.IsLeftButtonPressed && !buttons.IsMiddleButtonPressed))
        {
            return;
        }

        _pressed = e.Pointer;
        _pressPoint = _lastPoint = e.GetPosition(this);
        _pressView = _panned = model.View;
        _pressHit = model.HitTest(_pressPoint);
        _pans = buttons.IsMiddleButtonPressed || _pressHit.Kind == LogicHitKind.None;
        _clicks = buttons.IsLeftButtonPressed;
        _moved = false;

        if (e.ClickCount == 1)
        {
            _firstPressed = _clicks ? EntityOf(_pressHit.Card) : null;
        }
        else if (_clicks && e.ClickCount == 2 && _firstPressed is Guid entity)
        {
            // The second press frames the card the first was on, wherever
            // that card is by now: the first click may have selected it, and
            // near the selection that lays the graph out again. This press
            // selects nothing more and moves nothing.
            _clicks = false;
            _pans = false;
            FrameRequested?.Invoke(entity);
        }

        Focus();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        Point at = e.GetPosition(this);
        if (_pressed is null)
        {
            Hover(at);
            return;
        }

        Vector travelled = at - _pressPoint;
        _moved |= Math.Abs(travelled.X) > LogicDrawMetrics.ClickSlop
            || Math.Abs(travelled.Y) > LogicDrawMetrics.ClickSlop;

        if (_moved && _pans && Model is { } model)
        {
            // Something else moved the graph since the last move: the wheel,
            // a fit. The drag goes on from where that left it.
            if (model.View != _panned)
            {
                _pressView = model.View;
                _pressPoint = _lastPoint;
            }

            // From where the press found the graph, so rounding does not add up.
            model.View = _panned = _pressView.MovedBy(at - _pressPoint);
        }

        _lastPoint = at;
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_pressed is null)
            return;

        bool clicked = _clicks && !_moved;
        LogicHit hit = _pressHit;

        _pressed = null;
        e.Pointer.Capture(null);
        e.Handled = true;

        if (clicked)
            Click(hit, e.KeyModifiers.HasFlag(KeyModifiers.Control));

        Hover(e.GetPosition(this));
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _pressed = null;
        base.OnPointerCaptureLost(e);
    }

    /// <inheritdoc/>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (Model is not { } model || e.Delta.Y == 0)
            return;

        Point at = e.GetPosition(this);
        LogicPanZoom view = model.View;

        model.View = view.ZoomedAbout(at, view.Zoom * Math.Pow(LogicDrawMetrics.WheelZoom, e.Delta.Y));
        Hover(at);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(null, null);
    }

    private static Guid? EntityOf(LogicSceneCard? card) => card is { Card.IsStub: false } ? card.Card.NodeId : null;

    private void Listen(LogicViewModel? model)
    {
        if (ReferenceEquals(model, _heard))
            return;

        if (_heard is not null)
            _heard.Redraw -= OnRedraw;

        _heard = model;
        SetHover(null, null);

        if (model is not null)
        {
            model.Redraw += OnRedraw;
            model.ViewSize = Bounds.Size;
        }

        InvalidateVisual();
    }

    private void OnRedraw()
    {
        // What the pointer was on belongs to a scene that may be gone.
        if (!ReferenceEquals(Model?.Scene, _hoverScene))
            SetHover(null, null);

        InvalidateVisual();
    }

    private void Click(LogicHit hit, bool adds)
    {
        Guid? entity = hit.Kind switch
        {
            LogicHitKind.Card or LogicHitKind.Port => EntityOf(hit.Card),
            LogicHitKind.Label or LogicHitKind.Edge => hit.Edge?.Edge.From.NodeId,
            _ => null,
        };

        if (entity is Guid id)
            SelectRequested?.Invoke(id, adds);
    }

    private void Hover(Point at)
    {
        LogicHit hit = Model?.HitTest(at) ?? LogicHit.None;
        SetHover(hit.Card, hit.Edge);
    }

    private void SetHover(LogicSceneCard? card, LogicSceneEdge? edge)
    {
        _hoverScene = Model?.Scene;
        if (ReferenceEquals(card, _hoveredCard) && ReferenceEquals(edge, _hoveredEdge))
            return;

        _hoveredCard = card;
        _hoveredEdge = edge;

        // A wire says what it does in a sentence. A card says its whole
        // name, which the card itself may have cut short.
        string? tip = edge is not null ? Model?.FaceOf(edge)?.Sentence
            : card is not null ? LogicViewText.Sentence(card.Card)
            : null;

        ToolTip.SetTip(this, tip);
        if (tip is null)
            ToolTip.SetIsOpen(this, false);

        InvalidateVisual();
    }
}
