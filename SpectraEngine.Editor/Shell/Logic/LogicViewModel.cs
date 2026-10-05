using Avalonia;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// What the Logic view shows and what it remembers: how much of the level,
/// the filter, and where the graph sits. The view can be taken out of the
/// window and put back, so nothing it must keep lives in a control.
/// UI thread only.
/// </summary>
public sealed class LogicViewModel : ObservableObject
{
    /// <summary>The fewest steps away from the selection the view shows.</summary>
    public const int MinimumSteps = 1;

    /// <summary>The most steps away from the selection the view shows.</summary>
    public const int MaximumSteps = 6;

    private static readonly PropertyChangedEventArgs TickTextChanged = new(nameof(TickText));

    private readonly LogicArrangement _arrangement;
    private readonly LogicPlayState _play = new();
    private readonly LogicSelection _selection = new();
    private readonly LogicShownEntities _shown = new();
    private readonly LogicFitRule _fit = new();

    private EntitySchemaCatalog? _schemas;
    private LogicScopeMode _mode = LogicScopeMode.AroundSelection;
    private int _steps = 2;
    private string _filter = "";
    private LogicPanZoom _view = LogicPanZoom.Identity;
    private Size _viewSize;
    private LogicStatus _status = LogicStatus.None;
    private LogicEmptyReason _emptyReason;
    private string _tickText = "";
    private bool _shownChanged;

    /// <summary>Creates the model of a view that measures text with the fonts it draws in.</summary>
    public LogicViewModel()
        : this(new LogicTextRuler())
    {
    }

    /// <summary>Creates the model of a view that measures text with <paramref name="ruler"/>.</summary>
    public LogicViewModel(ILogicTextMeasure ruler)
    {
        ArgumentNullException.ThrowIfNull(ruler);

        _arrangement = new LogicArrangement(ruler);
        FitCommand = new RelayCommand(Fit);
        ActualSizeCommand = new RelayCommand(ShowActualSize);
        WholeLevelCommand = new RelayCommand(() => Mode = LogicScopeMode.WholeLevel);
        AroundSelectionCommand = new RelayCommand(() => Mode = LogicScopeMode.AroundSelection);
    }

    /// <summary>
    /// Raised when a drawing of the graph would differ. <see cref="Scene"/>,
    /// <see cref="Wires"/> and <see cref="View"/> are read then. They raise
    /// no change of their own.
    /// </summary>
    public event Action? Redraw;

    /// <summary>
    /// Raised when <see cref="ShownEntityIds"/> changes, so a window can ask
    /// the engine for those entities' state.
    /// </summary>
    public event Action? ShownEntitiesChanged;

    /// <summary>The session's entity classes. Without them every class is unknown.</summary>
    public EntitySchemaCatalog? Schemas
    {
        get => _schemas;
        set
        {
            if (!Set(ref _schemas, value))
                return;

            _arrangement.SetSchemas(value);
            Refresh();
        }
    }

    /// <summary>The whole level, or only what is near the selection.</summary>
    public LogicScopeMode Mode
    {
        get => _mode;
        set
        {
            if (!Set(ref _mode, value))
                return;

            Raise(nameof(IsAroundSelection));
            Raise(nameof(IsWholeLevel));
            _fit.Ask();
            Rescope();
        }
    }

    /// <summary>Whether the view follows the selection.</summary>
    public bool IsAroundSelection => _mode == LogicScopeMode.AroundSelection;

    /// <summary>Whether the view shows every card.</summary>
    public bool IsWholeLevel => _mode == LogicScopeMode.WholeLevel;

    /// <summary>How many wires away from the selection a card may be, from 1 to 6.</summary>
    public int Steps
    {
        get => _steps;
        set
        {
            int steps = Math.Clamp(value, MinimumSteps, MaximumSteps);

            // A field that shows what was typed has to be told it was not taken.
            if (!Set(ref _steps, steps))
            {
                if (steps != value)
                    Raise();

                return;
            }

            if (IsAroundSelection)
                Rescope();
        }
    }

    /// <summary>Text to look for in a card's name or class. Cards without it are dimmed.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value ?? ""))
                Rescope();
        }
    }

    /// <summary>Where the graph sits in the view.</summary>
    public LogicPanZoom View
    {
        get => _view;
        set
        {
            if (_view == value)
                return;

            _view = value;
            Redraw?.Invoke();
        }
    }

    /// <summary>How large the view is. The control that draws the graph keeps this current.</summary>
    public Size ViewSize
    {
        get => _viewSize;
        set
        {
            _viewSize = value;
            FitIfAsked();
        }
    }

    /// <summary>The level's wiring, or null before a snapshot brought it.</summary>
    public LogicGraph? Graph => _arrangement.Graph;

    /// <summary>The part of the wiring on show and what the filter dims, or null without wiring.</summary>
    public LogicScopedGraph? Shown => _arrangement.Shown;

    /// <summary>The graph laid out, or null without wiring.</summary>
    public LogicScene? Scene => _arrangement.Scene;

    /// <summary>The wires as they are drawn now, one for each of the scene's edges and in their order.</summary>
    public IReadOnlyList<LogicWireFace> Wires => _arrangement.Wires;

    /// <summary>Whether a level is running.</summary>
    public bool IsPlaying => _play.IsPlaying;

    /// <summary>The running level's tick.</summary>
    public long Tick => _play.Tick;

    /// <summary>The same as text, for a readout. Empty while editing.</summary>
    public string TickText => _tickText;

    /// <summary>The newest three things wires did, oldest first. Empty while editing.</summary>
    public IReadOnlyList<LogicEventLine> Events => _play.Events;

    /// <summary>What the status row says, and where its link leads.</summary>
    public LogicStatus Status => _status;

    /// <summary>The hint at the right of the status row.</summary>
    public string Hint => IsPlaying ? LogicViewText.PlayingHint : LogicViewText.EditingHint;

    /// <summary>What the view says in place of a graph, or empty when it has one.</summary>
    public string EmptyText => LogicViewText.Empty(_emptyReason);

    /// <summary>Whether the empty state offers to show the whole level.</summary>
    public bool OffersWholeLevel => _emptyReason == LogicEmptyReason.NothingSelected;

    /// <summary>The entities that have a card on show, in the graph's order.</summary>
    public IReadOnlyList<Guid> ShownEntityIds => _shown.Ids;

    /// <summary>Shows the whole graph.</summary>
    public ICommand FitCommand { get; }

    /// <summary>Shows the graph at its own size.</summary>
    public ICommand ActualSizeCommand { get; }

    /// <summary>Shows every card.</summary>
    public ICommand WholeLevelCommand { get; }

    /// <summary>Shows what is near the selection.</summary>
    public ICommand AroundSelectionCommand { get; }

    /// <summary>Takes one published snapshot.</summary>
    public void Apply(FrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (_selection.Take(snapshot.SelectedIds))
            _arrangement.SelectionChanged();

        _arrangement.SetInfo(snapshot.LogicGraph);
        Refresh(TakePlay(snapshot.LogicPlay));
    }

    /// <summary>
    /// Forgets the level, when its session stops. What the user chose stays,
    /// and so does where the graph sits: a session that comes back with the
    /// same cards finds them where they were.
    /// </summary>
    public void Reset()
    {
        _selection.Clear();
        _arrangement.SelectionChanged();
        _arrangement.SetInfo(null);

        if (Set(ref _schemas, null, nameof(Schemas)))
            _arrangement.SetSchemas(null);

        TakePlay(null);
        _fit.Forget();
        Refresh(redraw: true);
    }

    /// <summary>Whether a card's entity is selected.</summary>
    public bool IsSelected(LogicCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return _selection.Contains(card);
    }

    /// <summary>Whether the filter leaves a card out.</summary>
    public bool IsDimmed(LogicCard card) => Shown?.IsDimmed(card) ?? false;

    /// <summary>Whether the filter leaves out both ends of an edge.</summary>
    public bool IsDimmed(LogicEdge edge) => Shown?.IsDimmed(edge) ?? false;

    /// <summary>The line of state a running entity last gave, when it gave one.</summary>
    public bool TryGetState(Guid nodeId, out LogicEntityState state) => _play.TryGetState(nodeId, out state);

    /// <summary>How one of the scene's edges is drawn now, or null when it is not in the scene.</summary>
    public LogicWireFace? FaceOf(LogicSceneEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return _arrangement.FaceOf(edge);
    }

    /// <summary>What is under a point of the view. A label that shows no words counts as its wire.</summary>
    public LogicHit HitTest(Point viewPoint) => _arrangement.HitTest(
        _view.ToScene(viewPoint),
        LogicDrawMetrics.PickReach / _view.Zoom,
        LogicDrawMetrics.DetailAt(_view.Zoom) == LogicDetail.Full);

    /// <summary>Shows the whole graph, as large as fits and no larger than its own size.</summary>
    public void Fit()
    {
        if (Scene is { Cards.Count: > 0 } scene)
            View = LogicPanZoom.Fit(scene.Size, _viewSize);
    }

    /// <summary>Shows the graph at its own size, about the middle of the view.</summary>
    public void ShowActualSize() =>
        View = _view.ZoomedAbout(new Point(_viewSize.Width / 2, _viewSize.Height / 2), 1);

    /// <summary>Puts an entity's card in the middle of the view, when it has one on show.</summary>
    public void CenterOn(Guid nodeId)
    {
        if (Scene?.CardOf(nodeId) is { } card)
            View = _view.CenteredOn(card.Bounds, _viewSize);
    }

    private void Rescope()
    {
        _arrangement.SetScope(new LogicScope { Mode = _mode, Steps = _steps, Filter = _filter });
        Refresh();
    }

    // Returns whether the cards would be drawn differently.
    private bool TakePlay(LogicPlayInfo? info)
    {
        bool wasPlaying = _play.IsPlaying;
        long tick = _play.Tick;
        IReadOnlyList<LogicEventLine> events = _play.Events;

        if (!_play.Take(info))
            return false;

        _arrangement.PlayChanged();

        if (_play.IsPlaying != wasPlaying)
        {
            Raise(nameof(IsPlaying));
            Raise(nameof(Hint));
        }

        if (_play.Tick != tick || _play.IsPlaying != wasPlaying)
        {
            _tickText = _play.IsPlaying ? _play.Tick.ToString(CultureInfo.InvariantCulture) : "";
            Raise(TickTextChanged);
        }

        if (!ReferenceEquals(_play.Events, events))
            Raise(nameof(Events));

        return _play.StatesChanged;
    }

    private void Refresh(bool redraw = false)
    {
        LogicArrangementChange change = _arrangement.Refresh(_selection, _play);

        if (change.HasFlag(LogicArrangementChange.Shown))
            TakeShown();

        if (change.HasFlag(LogicArrangementChange.Scene) && Scene is { Cards.Count: > 0 } scene)
            _fit.Placed(_shown.Ids, scene.Size, followsSelection: IsAroundSelection);

        if (redraw || change.HasFlag(LogicArrangementChange.Looks))
            Redraw?.Invoke();

        FitIfAsked();

        // Last, so whoever listens finds a finished scene.
        if (_shownChanged)
        {
            _shownChanged = false;
            ShownEntitiesChanged?.Invoke();
        }
    }

    private void TakeShown()
    {
        LogicScopedGraph? shown = Shown;
        LogicGraphInfo? info = _arrangement.Info;

        LogicStatus status = shown is null || info is null ? LogicStatus.None : LogicStatus.Of(shown, info, _mode);

        Set(ref _status, status, nameof(Status));
        if (Set(ref _emptyReason, shown?.EmptyReason ?? LogicEmptyReason.None, nameof(EmptyText)))
            Raise(nameof(OffersWholeLevel));

        _shownChanged |= _shown.Take(shown?.Cards ?? []);
    }

    private void FitIfAsked()
    {
        if (_viewSize.Width <= 0 || _viewSize.Height <= 0 || Scene is not { Cards.Count: > 0 })
            return;

        if (_fit.Take())
            Fit();
    }
}
