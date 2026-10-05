using Avalonia;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
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

    private static readonly IReadOnlySet<Guid> NoSelection = FrozenSet<Guid>.Empty;

    private readonly ILogicTextMeasure _ruler;
    private readonly LogicPlayState _play = new();
    private readonly LogicSelection _selection = new();
    private readonly LogicWireFaces _faces = new();
    private Guid[] _shownIds = [];

    private EntitySchemaCatalog? _schemas;
    private LogicGraphInfo? _info;
    private LogicScopeMode _mode = LogicScopeMode.AroundSelection;
    private int _steps = 2;
    private string _filter = "";
    private LogicPanZoom _view = LogicPanZoom.Identity;
    private Size _viewSize;
    private LogicStatus _status = LogicStatus.None;
    private string _emptyText = "";
    private bool _offersWholeLevel;

    private bool _graphStale;
    private bool _scopeStale;
    private bool _dimStale;
    private bool _layoutStale;
    private bool _wiresStale;
    private bool _fitPending = true;
    private int _reservedDigits;

    /// <summary>Creates the model of a view that measures text with the fonts it draws in.</summary>
    public LogicViewModel()
        : this(new LogicTextRuler())
    {
    }

    /// <summary>Creates the model of a view that measures text with <paramref name="ruler"/>.</summary>
    public LogicViewModel(ILogicTextMeasure ruler)
    {
        ArgumentNullException.ThrowIfNull(ruler);

        _ruler = ruler;
        FitCommand = new RelayCommand(Fit);
        ActualSizeCommand = new RelayCommand(ShowActualSize);
    }

    /// <summary>Raised when a drawing of the graph would differ.</summary>
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

            _graphStale = true;
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
            _scopeStale = true;
            _fitPending = true;
            Refresh();
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
            if (!Set(ref _steps, Math.Clamp(value, MinimumSteps, MaximumSteps)))
                return;

            _scopeStale |= IsAroundSelection;
            Refresh();
        }
    }

    /// <summary>Text to look for in a card's name or class. Cards without it are dimmed.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (!Set(ref _filter, value ?? ""))
                return;

            _dimStale = true;
            Refresh();
        }
    }

    /// <summary>Where the graph sits in the view.</summary>
    public LogicPanZoom View
    {
        get => _view;
        set
        {
            if (Set(ref _view, value))
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
            FitIfPending();
        }
    }

    /// <summary>The level's wiring, or null before a snapshot brought it.</summary>
    public LogicGraph? Graph { get; private set; }

    /// <summary>The part of the wiring on show and what the filter dims, or null without wiring.</summary>
    public LogicScopedGraph? Shown { get; private set; }

    /// <summary>The graph laid out, or null without wiring.</summary>
    public LogicScene? Scene { get; private set; }

    /// <summary>The wires as they are drawn now, one for each of the scene's edges and in their order.</summary>
    public IReadOnlyList<LogicWireFace> Wires => _faces.All;

    /// <summary>Whether a level is running.</summary>
    public bool IsPlaying => _play.IsPlaying;

    /// <summary>The running level's tick.</summary>
    public long Tick => _play.Tick;

    /// <summary>The newest three things wires did, oldest first. Empty while editing.</summary>
    public IReadOnlyList<LogicEventLine> Events => _play.Events;

    /// <summary>What the status row says.</summary>
    public LogicStatus Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>The hint at the right of the status row.</summary>
    public string Hint => IsPlaying ? LogicViewText.PlayingHint : LogicViewText.EditingHint;

    /// <summary>What the view says in place of a graph, or empty when it has one.</summary>
    public string EmptyText
    {
        get => _emptyText;
        private set => Set(ref _emptyText, value);
    }

    /// <summary>Whether the empty state offers to show the whole level.</summary>
    public bool OffersWholeLevel
    {
        get => _offersWholeLevel;
        private set => Set(ref _offersWholeLevel, value);
    }

    /// <summary>The sender of the first wire on show that goes nowhere, or null.</summary>
    public Guid? GoingNowhereSender { get; private set; }

    /// <summary>The entities that have a card on show, in the graph's order.</summary>
    public IReadOnlyList<Guid> ShownEntityIds => _shownIds;

    /// <summary>Shows the whole graph.</summary>
    public ICommand FitCommand { get; }

    /// <summary>Shows the graph at its own size.</summary>
    public ICommand ActualSizeCommand { get; }

    /// <summary>Takes one published snapshot.</summary>
    public void Apply(FrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        bool redraw = false;
        if (_selection.Take(snapshot.SelectedIds))
        {
            _scopeStale |= IsAroundSelection;
            _wiresStale = true;
            redraw = true;
        }

        if (!ReferenceEquals(snapshot.LogicGraph, _info))
        {
            _info = snapshot.LogicGraph;
            _graphStale = true;
        }

        redraw |= TakePlay(snapshot.LogicPlay);
        Refresh(redraw);
    }

    /// <summary>Forgets the level, when its session stops. What the user chose stays.</summary>
    public void Reset()
    {
        _selection.Clear();
        _info = null;
        _graphStale = true;
        _fitPending = true;

        Set(ref _schemas, null, nameof(Schemas));
        TakePlay(null);
        Refresh(redraw: true);
        View = LogicPanZoom.Identity;
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
        return _faces.Of(edge);
    }

    /// <summary>Shows the whole graph, as large as fits and no larger than its own size.</summary>
    public void Fit()
    {
        if (Scene is { Cards.Count: > 0 })
            View = LogicPanZoom.Fit(Scene.Size, _viewSize);
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

    // Returns whether the cards would be drawn differently.
    private bool TakePlay(LogicPlayInfo? info)
    {
        bool wasPlaying = _play.IsPlaying;
        long tick = _play.Tick;
        IReadOnlyList<LogicEventLine> events = _play.Events;

        if (!_play.Take(info))
            return false;

        _wiresStale = true;
        _layoutStale |= _play.IsPlaying != wasPlaying || (_play.IsPlaying && _play.Digits != _reservedDigits);

        if (_play.IsPlaying != wasPlaying)
        {
            Raise(nameof(IsPlaying));
            Raise(nameof(Hint));
        }

        if (_play.Tick != tick)
            Raise(nameof(Tick));

        if (!ReferenceEquals(_play.Events, events))
            Raise(nameof(Events));

        return _play.StatesChanged;
    }

    // Rebuilds what is stale and nothing else, each stage from the one before.
    private void Refresh(bool redraw = false)
    {
        if (_graphStale)
        {
            Graph = _info is null ? null : LogicGraph.Build(_info, _schemas);
            _scopeStale = true;
        }

        if (_scopeStale)
        {
            Rescope();
            _layoutStale = true;
        }
        else if (_dimStale)
        {
            // The filter only dims. The same cards stay where they stand.
            Shown = Scope();
            redraw = true;
        }

        if (_layoutStale)
        {
            Arrange();
            redraw = true;
        }
        else if (_wiresStale)
        {
            redraw |= _faces.Refresh(_play, _selection);
        }

        _graphStale = _scopeStale = _dimStale = _layoutStale = _wiresStale = false;

        if (redraw)
            Redraw?.Invoke();

        FitIfPending();
    }

    private LogicScopedGraph? Scope() => Graph is null
        ? null
        : new LogicScope { Mode = _mode, Steps = _steps, Filter = _filter }.Apply(Graph, _selection.Ids);

    private void Rescope()
    {
        Shown = Scope();

        LogicEmptyReason reason = Shown?.EmptyReason ?? LogicEmptyReason.None;
        Status = Shown is null || _info is null ? LogicStatus.None : LogicStatus.Of(Shown, _info, _mode);
        EmptyText = LogicViewText.Empty(reason);
        OffersWholeLevel = reason == LogicEmptyReason.NothingSelected;
        GoingNowhereSender = FirstGoingNowhere();

        if (TakeShownIds() && IsAroundSelection)
            _fitPending = true;
    }

    private Guid? FirstGoingNowhere()
    {
        foreach (LogicEdge edge in Shown?.Edges ?? [])
        {
            if (Shown is not null && Shown.Graph.GoesNowhere(edge.Wire))
                return edge.From.NodeId;
        }

        return null;
    }

    // Returns whether the entities on show changed.
    private bool TakeShownIds()
    {
        var shown = new List<Guid>();
        foreach (LogicCard card in Shown?.Cards ?? [])
        {
            if (!card.IsStub)
                shown.Add(card.NodeId);
        }

        if (System.Runtime.InteropServices.CollectionsMarshal.AsSpan(shown).SequenceEqual(_shownIds))
            return false;

        _shownIds = [.. shown];
        ShownEntitiesChanged?.Invoke();
        return true;
    }

    private void Arrange()
    {
        Size before = Scene?.Size ?? default;

        if (Shown is null)
        {
            Scene = null;
            _faces.Clear();
            return;
        }

        bool playing = _play.IsPlaying;
        _reservedDigits = _play.Digits;
        LogicScopedGraph drawn = playing ? LogicLabelRoom.Reserve(Shown, _reservedDigits, _ruler) : Shown;

        // In the whole level nothing may move when the selection does, so
        // the layout is not told of it there.
        Scene = LogicLayout.Arrange(
            drawn,
            IsAroundSelection ? _selection.Ids : NoSelection,
            new LogicLayoutOptions { ShowsState = playing },
            _ruler);

        _faces.Rebuild(Scene, drawn, Shown);
        _faces.Refresh(_play, _selection);
        _fitPending |= IsAroundSelection && Scene.Size != before;
    }

    // The first scene of a session is fitted, and near the selection every
    // new one. In the whole level a new scene keeps the place the user chose.
    private void FitIfPending()
    {
        if (!_fitPending || _viewSize.Width <= 0 || _viewSize.Height <= 0 || Scene is not { Cards.Count: > 0 })
            return;

        _fitPending = false;
        Fit();
    }
}
