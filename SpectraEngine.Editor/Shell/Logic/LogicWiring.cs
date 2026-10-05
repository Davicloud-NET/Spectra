using Avalonia;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// Adding and removing wires in the Logic view: the drag that makes one, the
/// wire that is selected, and what the status row says of the last edit. It
/// asks for an edit and changes nothing itself. Part of the view's model.
/// UI thread only.
/// </summary>
public sealed class LogicWiring
{
    private readonly Action _redraw;
    private readonly Action _newsChanged;

    private LogicGraphInfo? _info;
    private LogicScopedGraph? _shown;
    private LogicScene? _scene;
    private bool _isPlaying;

    // The edit asked for last, until the level's wiring shows it.
    private Guid _awaitedSender;
    private EntityConnection[]? _awaited;
    private string _awaitedNews = "";

    internal LogicWiring(Action redraw, Action newsChanged)
    {
        _redraw = redraw;
        _newsChanged = newsChanged;
    }

    /// <summary>
    /// Raised to ask for an entity's wires to be replaced: the entity, and
    /// its whole list as it should be.
    /// </summary>
    public event Action<Guid, IReadOnlyList<EntityConnection>>? Requested;

    /// <summary>The drag that makes a wire.</summary>
    public LogicWireGesture Gesture { get; } = new();

    /// <summary>The selected wire, or null.</summary>
    public LogicSelectedWire? Selected { get; private set; }

    /// <summary>What the status row says of the last edit. Empty for nothing.</summary>
    public string News { get; private set; } = "";

    /// <summary>Whether wires may change. False while a level plays and before a level is here.</summary>
    public bool CanEdit => !_isPlaying && _info is not null;

    /// <summary>Takes a press of the left button. Returns whether a drag may come of it.</summary>
    /// <param name="at">The pointer, in the view.</param>
    /// <param name="hit">What it is on.</param>
    public bool Press(Point at, LogicHit hit) => Gesture.Press(at, hit, CanEdit);

    /// <summary>Takes a move of the pointer while a button is held.</summary>
    /// <param name="at">The pointer, in the view.</param>
    /// <param name="over">The card under it, or null.</param>
    public void Move(Point at, LogicSceneCard? over)
    {
        bool begins = Gesture.Phase == LogicWirePhase.Pressed;
        if (!Gesture.Move(at, over))
            return;

        if (begins)
            Say("");

        _redraw();
    }

    /// <summary>Takes the release of the button. Returns whether a menu is due.</summary>
    public bool Release()
    {
        bool showedWire = Gesture.ShowsWire;
        bool dropped = Gesture.Release();

        if (showedWire && !dropped)
            _redraw();

        return dropped;
    }

    /// <summary>Gives a drag up, wherever it is.</summary>
    public void Cancel()
    {
        if (Gesture.Cancel())
            _redraw();
    }

    /// <summary>Takes the loss of the pointer.</summary>
    public void LoseCapture()
    {
        if (Gesture.LoseCapture())
            _redraw();
    }

    /// <summary>The menu of the wire that was just dropped, or null when none was.</summary>
    public LogicWireMenu? Menu() =>
        Gesture is { Phase: LogicWirePhase.Dropped, From: { } from, Target: { } to }
            ? LogicWireMenu.For(from.Card, Gesture.Output, to.Card)
            : null;

    /// <summary>
    /// Makes the dropped wire as a line of its menu says. Returns the entity
    /// that sends it when a wire was asked for, so it can be selected.
    /// </summary>
    public Guid? Pick(LogicWireMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (Gesture is not { Phase: LogicWirePhase.Dropped, From: { } from, Target: { } to })
            return null;

        Gesture.Finish();
        _redraw();

        Guid sender = from.Card.NodeId;
        if (!CanEdit || LogicWireList.TargetOf(from.Card, to.Card) is not string target)
            return null;

        EntityConnection wire = LogicWireList.NewWire(item.Output, target, item.Input);
        switch (LogicWireList.Add(_info, sender, wire, out EntityConnection[] wires))
        {
            case LogicWireRefusal.None:
                Ask(sender, wires, LogicWireText.Wired(LogicWireText.Called(from.Card.Name, from.Card.ClassName), wire));
                return sender;

            case LogicWireRefusal.AlreadyThere:
                Say(LogicWireText.AlreadyThere);
                return null;

            default:
                return null;
        }
    }

    /// <summary>Selects the wire an edge draws, or none.</summary>
    public void Select(LogicSceneEdge? edge)
    {
        LogicSelectedWire? wire = edge is null || !CanEdit ? null : LogicSelectedWire.Of(edge.Edge);
        if (wire == Selected)
            return;

        Selected = wire;
        _redraw();
    }

    /// <summary>Whether an edge draws the selected wire.</summary>
    public bool IsSelected(LogicSceneEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return Selected is { } wire && wire.IsDrawnBy(edge.Edge);
    }

    /// <summary>Asks for the selected wire to be removed. Returns whether it did.</summary>
    public bool RemoveSelected()
    {
        if (Selected is not { } wire || !CanEdit
            || LogicWireList.Remove(_info, wire.NodeId, wire.WireIndex, out EntityConnection[] wires) != LogicWireRefusal.None
            || !TryFind(_info, wire.NodeId, out LogicEntityInfo sender))
        {
            return false;
        }

        string news = LogicWireText.Removed(
            LogicWireText.Called(sender.Name, sender.ClassName), sender.Wires[wire.WireIndex], Alike(wire));

        Selected = null;
        _redraw();
        Ask(wire.NodeId, wires, news);
        return true;
    }

    // Takes what the view shows now. Runs on every snapshot, so it does
    // nothing while nothing differs. Returns whether a drawing would differ.
    internal bool Take(LogicGraphInfo? info, LogicScopedGraph? shown, LogicScene? scene, bool isPlaying)
    {
        bool differs = false;

        // What a drag holds belongs to the scene it began on.
        if (!ReferenceEquals(scene, _scene) || isPlaying)
            differs = Gesture.Cancel();

        if (!ReferenceEquals(info, _info))
            TakeNews(info);

        bool looksAgain = !ReferenceEquals(shown, _shown) || isPlaying != _isPlaying;

        _info = info;
        _shown = shown;
        _scene = scene;
        _isPlaying = isPlaying;

        if (looksAgain && Selected is { } wire && (isPlaying || Alike(wire) == 0))
        {
            Selected = null;
            differs = true;
        }

        return differs;
    }

    // The news of an edit is told once the wiring shows the edit, and stands
    // until the wiring changes again.
    private void TakeNews(LogicGraphInfo? info)
    {
        bool arrived = _awaited is not null
            && TryFind(info, _awaitedSender, out LogicEntityInfo sender)
            && Same(sender.Wires, _awaited);

        Say(arrived ? _awaitedNews : "");
        _awaited = null;
    }

    private void Ask(Guid sender, EntityConnection[] wires, string news)
    {
        Say("");
        _awaitedSender = sender;
        _awaited = wires;
        _awaitedNews = news;
        Requested?.Invoke(sender, wires);
    }

    private void Say(string news)
    {
        if (string.Equals(news, News, StringComparison.Ordinal))
            return;

        News = news;
        _newsChanged();
    }

    // How many wires the edge of a wire on show draws. None when it is not on show.
    private int Alike(LogicSelectedWire wire)
    {
        IReadOnlyList<LogicEdge> edges = _shown?.Edges ?? [];
        for (int i = 0; i < edges.Count; i++)
        {
            if (wire.IsDrawnBy(edges[i]))
                return edges[i].WireIndices.Count;
        }

        return 0;
    }

    private static bool TryFind(LogicGraphInfo? info, Guid nodeId, out LogicEntityInfo entity)
    {
        IReadOnlyList<LogicEntityInfo> entities = info?.Entities ?? [];
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i].NodeId == nodeId)
            {
                entity = entities[i];
                return true;
            }
        }

        entity = default;
        return false;
    }

    private static bool Same(IReadOnlyList<EntityConnection> wires, EntityConnection[] wanted)
    {
        if (wires.Count != wanted.Length)
            return false;

        for (int i = 0; i < wanted.Length; i++)
        {
            if (wires[i] != wanted[i])
                return false;
        }

        return true;
    }
}
