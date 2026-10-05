using Avalonia;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// The level's wiring on its way to the screen: the graph, the part of it on
// show, that part laid out, and how each wire looks. Each stage is built from
// the one before, and only when what it is built from changed.
internal sealed class LogicArrangement
{
    private static readonly IReadOnlySet<Guid> NoSelection = FrozenSet<Guid>.Empty;

    private readonly ILogicTextMeasure _ruler;
    private readonly LogicWireFaces _faces = new();
    private List<int> _ringed = [];
    private List<int> _ringedBefore = [];

    private LogicGraphInfo? _info;
    private EntitySchemaCatalog? _schemas;
    private LogicScope _scope = new() { Mode = LogicScopeMode.AroundSelection };
    private bool _graphStale;
    private bool _scopeStale;
    private bool _selectionStale;
    private bool _wiresStale;
    private bool _laidOutPlaying;

    public LogicArrangement(ILogicTextMeasure ruler) => _ruler = ruler;

    public LogicGraphInfo? Info => _info;

    public LogicScope Scope => _scope;

    public LogicGraph? Graph { get; private set; }

    public LogicScopedGraph? Shown { get; private set; }

    public LogicScene? Scene { get; private set; }

    public IReadOnlyList<LogicWireFace> Wires => _faces.All;

    private bool IsAroundSelection => _scope.Mode == LogicScopeMode.AroundSelection;

    public LogicWireFace? FaceOf(LogicSceneEdge edge) => _faces.Of(edge);

    // What is under a point of the scene. A label is where its words are
    // drawn. The rest of the room the layout kept for it belongs to whatever
    // wire runs there, its own or another. From too far out to draw them,
    // labels and port rows are not there to be on: a row is its card.
    public LogicHit HitTest(Point at, double reach, bool showsDetail)
    {
        if (Scene is not { } scene)
            return LogicHit.None;

        LogicHit hit = scene.HitTest(at, reach);
        if (!showsDetail && hit is { Kind: LogicHitKind.Port, Card: { } card })
        {
            return card.Bounds.Contains(at)
                ? new LogicHit(LogicHitKind.Card, card, null, null)
                : scene.HitEdge(at, reach);
        }

        if (hit is not { Kind: LogicHitKind.Label, Edge: { } edge })
            return hit;

        if (showsDetail && _faces.Of(edge) is { } face)
        {
            LogicTextStyle style = face.IsMono ? LogicTextStyle.MonoLabel : LogicTextStyle.Label;
            if (face.Pill(_ruler.Width(face.Text, style)) is Rect pill && pill.Contains(at))
                return hit;
        }

        return scene.HitEdge(at, reach);
    }

    // The engine hands on the same instance until the wiring changes.
    public void SetInfo(LogicGraphInfo? info)
    {
        _graphStale |= !ReferenceEquals(info, _info);
        _info = info;
    }

    public void SetSchemas(EntitySchemaCatalog? schemas)
    {
        _graphStale |= !ReferenceEquals(schemas, _schemas);
        _schemas = schemas;
    }

    public void SetScope(LogicScope scope)
    {
        _scope = scope;
        _scopeStale = true;
    }

    public void SelectionChanged() => _selectionStale = true;

    public void PlayChanged() => _wiresStale = true;

    // Rebuilds what is stale and nothing else.
    public LogicArrangementChange Refresh(LogicSelection selection, LogicPlayState play)
    {
        LogicArrangementChange change = LogicArrangementChange.None;
        bool layoutStale = false;

        if (_graphStale)
        {
            Graph = _info is null ? null : LogicGraph.Build(_info, _schemas);
            _scopeStale = true;
        }

        bool rescoped = _scopeStale || (_selectionStale && (IsAroundSelection || ShowsOtherUnwired(selection)));
        if (rescoped)
        {
            LogicScopedGraph? before = Shown;
            Shown = Graph is null ? null : _scope.Apply(Graph, selection.Ids);

            // A filter dims and moves nothing. The cards stay where they stand.
            layoutStale = !ShowsTheSame(before, Shown);
            change |= LogicArrangementChange.Shown | LogicArrangementChange.Looks;
        }

        if ((rescoped || _selectionStale) && TakeRinged(selection))
        {
            // Near the selection, the group that holds it comes first.
            layoutStale |= IsAroundSelection;
            change |= LogicArrangementChange.Looks;
        }

        layoutStale |= Scene is not null && play.IsPlaying != _laidOutPlaying;

        if (layoutStale)
        {
            Arrange(selection, play);
            change |= LogicArrangementChange.Scene | LogicArrangementChange.Looks;
        }
        else if ((_wiresStale || _selectionStale) && _faces.Refresh(play, selection))
        {
            change |= LogicArrangementChange.Looks;
        }

        _graphStale = _scopeStale = _selectionStale = _wiresStale = false;
        return change;
    }

    // In the whole level a selection decides one thing: which entities with
    // no wires have a card. Returns whether those differ from the ones shown.
    private bool ShowsOtherUnwired(LogicSelection selection)
    {
        if (Graph is not { UnwiredCount: > 0 } graph || Shown is not { } shown)
            return false;

        int selected = 0;
        foreach (Guid id in selection.Ids)
            selected += graph.CardOf(id) is { IsWired: false } ? 1 : 0;

        if (selected != shown.Counts.UnwiredCards)
            return true;

        for (int i = 0; i < shown.Cards.Count; i++)
        {
            if (!shown.Cards[i].IsWired && !selection.Contains(shown.Cards[i]))
                return true;
        }

        return false;
    }

    private static bool ShowsTheSame(LogicScopedGraph? before, LogicScopedGraph? now)
    {
        if (before is null || now is null)
            return before is null && now is null;

        if (before.Cards.Count != now.Cards.Count || before.Edges.Count != now.Edges.Count)
            return false;

        for (int i = 0; i < now.Cards.Count; i++)
        {
            if (!ReferenceEquals(before.Cards[i], now.Cards[i]))
                return false;
        }

        for (int i = 0; i < now.Edges.Count; i++)
        {
            if (!ReferenceEquals(before.Edges[i], now.Edges[i]))
                return false;
        }

        return true;
    }

    // Notes which cards on show are selected. Returns whether they differ
    // from the ones before, so a selection of things without cards costs
    // nothing.
    private bool TakeRinged(LogicSelection selection)
    {
        (_ringed, _ringedBefore) = (_ringedBefore, _ringed);
        _ringed.Clear();

        IReadOnlyList<LogicCard> cards = Shown?.Cards ?? [];
        for (int i = 0; i < cards.Count; i++)
        {
            if (selection.Contains(cards[i]))
                _ringed.Add(cards[i].Index);
        }

        if (_ringed.Count != _ringedBefore.Count)
            return true;

        for (int i = 0; i < _ringed.Count; i++)
        {
            if (_ringed[i] != _ringedBefore[i])
                return true;
        }

        return false;
    }

    private void Arrange(LogicSelection selection, LogicPlayState play)
    {
        if (Shown is null)
        {
            Scene = null;
            _faces.Clear();
            return;
        }

        // While a level runs every label keeps room for anything it may come
        // to say, so the scene is laid out once for the whole run.
        _laidOutPlaying = play.IsPlaying;
        LogicScopedGraph drawn = _laidOutPlaying ? LogicLabelRoom.Reserve(Shown, _ruler) : Shown;

        // In the whole level nothing may move when the selection does, so
        // the layout is not told of it there.
        Scene = LogicLayout.Arrange(
            drawn,
            IsAroundSelection ? selection.Ids : NoSelection,
            new LogicLayoutOptions { ShowsState = _laidOutPlaying },
            _ruler);

        _faces.Rebuild(Scene, drawn, Shown);
        _faces.Refresh(play, selection);
    }
}
