using Avalonia;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    private readonly LogicShownEntities _shown = new();
    private readonly LogicViewPlace _place;

    private EntitySchemaCatalog? _schemas;
    private LogicScopeMode _mode = LogicScopeMode.AroundSelection;
    private int _steps = 2;
    private string _filter = "";
    private LogicStatus _status = LogicStatus.None;
    private LogicEmptyReason _emptyReason;
    private bool _shownChanged;
    private bool _showsScripts = true;

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
        _place = new LogicViewPlace(() => Redraw?.Invoke());
        Wiring = new LogicWiring(() => Redraw?.Invoke(), () => Raise(nameof(Status)));
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
    /// Raised when <see cref="ShownEntityIds"/> changes while there is wiring
    /// to show, so a window can ask the engine for those entities' state. Not
    /// raised when the wiring goes away. The first request of a session, and
    /// the one after the view was hidden, are the window's to send.
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
            _place.AskForFit();
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

    /// <summary>Whether the view says anything of scripts. On unless switched off.</summary>
    public bool ShowsScripts
    {
        get => _showsScripts;
        set
        {
            if (!Set(ref _showsScripts, value))
                return;

            _arrangement.ShowScripts(value);
            if (_play.ShowScripts(value))
                Raise(nameof(Events));

            Refresh();
        }
    }

    /// <summary>What the view says under its legend of solid and dashed wires. Empty when it shows no legend.</summary>
    public string ScriptsHint => _status.ScriptsHint(IsPlaying);

    /// <summary>Where the graph sits in the view.</summary>
    public LogicPanZoom View
    {
        get => _place.View;
        set => _place.View = value;
    }

    /// <summary>
    /// How large the view is. The control that draws the graph keeps this
    /// current. A graph that still sits as a fit left it is fitted again.
    /// </summary>
    public Size ViewSize
    {
        get => _place.Size;
        set
        {
            _place.Resize(value);
            _place.FitIfAsked(Scene, _arrangement.Selection.Ids);
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
    public string TickText => _play.TickText;

    /// <summary>The newest three things wires did, oldest first. Empty while editing.</summary>
    public IReadOnlyList<LogicEventLine> Events => _play.Events;

    /// <summary>The drag that makes a wire, the selected wire, and the edits the view asks for.</summary>
    public LogicWiring Wiring { get; }

    /// <summary>What the status row says, and where its link leads.</summary>
    public LogicStatus Status => Wiring.News.Length == 0 ? _status : _status with { News = Wiring.News };

    /// <summary>The hint at the right of the status row.</summary>
    public string Hint => IsPlaying ? _status.PlayingHint : _status.Hint;

    /// <summary>What the view says in place of a graph, or empty when it has one.</summary>
    public string EmptyText => LogicViewText.Empty(_emptyReason);

    /// <summary>Whether the empty state offers to show the whole level.</summary>
    public bool OffersWholeLevel => _emptyReason == LogicEmptyReason.NoEntitySelected;

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

        _arrangement.SetSelection(snapshot.SelectedIds);
        _arrangement.SetInfo(snapshot.LogicGraph);
        _arrangement.SetScripts(snapshot.LogicPlay?.Scripts);
        Refresh(TakePlay(snapshot.LogicPlay));
    }

    /// <summary>
    /// Forgets the level, when its session stops. What the user chose stays,
    /// and so does where the graph sits: a session that comes back with the
    /// same cards finds them where they were.
    /// </summary>
    public void Reset()
    {
        _arrangement.SetSelection([]);
        _arrangement.SetInfo(null);
        _arrangement.SetScripts(null);

        if (Set(ref _schemas, null, nameof(Schemas)))
            _arrangement.SetSchemas(null);

        TakePlay(null);
        _place.Forget();
        Refresh(redraw: true);
    }

    /// <summary>Whether a card's entity is selected.</summary>
    public bool IsSelected(LogicCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return _arrangement.Selection.Contains(card);
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

    /// <summary>What is under a point of the view. Labels and port rows count only where they are drawn.</summary>
    public LogicHit HitTest(Point viewPoint) => _arrangement.HitTest(
        View.ToScene(viewPoint),
        LogicDrawMetrics.PickReach / View.Zoom,
        LogicDrawMetrics.DetailAt(View.Zoom) == LogicDetail.Full);

    /// <summary>Shows the whole graph, as large as fits and no larger than its own size.</summary>
    public void Fit()
    {
        if (Scene is { Cards.Count: > 0 } scene)
            _place.Fit(scene);
    }

    /// <summary>Shows the graph at its own size, about the middle of the view.</summary>
    public void ShowActualSize() => _place.ShowActualSize();

    /// <summary>Puts an entity's card in the middle of the view, when it has one on show.</summary>
    public void CenterOn(Guid nodeId)
    {
        if (Scene?.CardOf(nodeId) is { } card)
            _place.CenterOn(card.Bounds);
    }

    private void Rescope()
    {
        _arrangement.SetScope(new LogicScope { Mode = _mode, Steps = _steps, Filter = _filter });
        Refresh();
    }

    // Returns whether the cards would be drawn differently.
    private bool TakePlay(LogicPlayInfo? info)
    {
        LogicPlayChange change = _play.Take(info);
        if (change == LogicPlayChange.None)
            return false;

        _arrangement.PlayChanged();

        if (change.HasFlag(LogicPlayChange.Playing))
        {
            Raise(nameof(IsPlaying));
            Raise(nameof(Hint));
            Raise(nameof(ScriptsHint));
        }

        if (change.HasFlag(LogicPlayChange.Tick))
            Raise(TickTextChanged);

        if (change.HasFlag(LogicPlayChange.Events))
            Raise(nameof(Events));

        return change.HasFlag(LogicPlayChange.States);
    }

    private void Refresh(bool redraw = false)
    {
        LogicArrangementChange change = _arrangement.Refresh(_play);

        if (change.HasFlag(LogicArrangementChange.Shown))
            TakeShown();

        // What scripts connect is no picture the user asked for, so it does
        // not follow the selection: the graph stays where the user put it.
        bool follows = IsAroundSelection && !change.HasFlag(LogicArrangementChange.ByScripts);
        if (change.HasFlag(LogicArrangementChange.Scene) && Scene is { Cards.Count: > 0 } scene)
            _place.LaidOut(_shown.Ids, scene.Size, follows);

        redraw |= Wiring.Take(_arrangement.Info, Shown, Scene, IsPlaying);
        if (redraw || change.HasFlag(LogicArrangementChange.Looks))
            Redraw?.Invoke();

        _place.FitIfAsked(Scene, _arrangement.Selection.Ids);

        // Wired cards keep their places, so the card a selection gives an
        // entity with no wires lands past them, perhaps out of sight.
        if (_shown.TakeArrived() is Guid arrived && Scene?.CardOf(arrived) is { } card)
            _place.Reveal(card.Bounds);

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

        if (Set(ref _status, status, nameof(Status)))
        {
            Raise(nameof(Hint));
            Raise(nameof(ScriptsHint));
        }

        if (Set(ref _emptyReason, shown?.EmptyReason ?? LogicEmptyReason.None, nameof(EmptyText)))
            Raise(nameof(OffersWholeLevel));

        // Wiring that went away asks for nothing: a request sent then would
        // undo the one that hid the view.
        _shownChanged |= _shown.Take(shown?.Cards ?? []) && shown is not null;
    }
}
