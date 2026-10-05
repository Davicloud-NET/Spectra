using Avalonia;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// Collects placed cards and routed edges into a LogicScene.
internal sealed class LogicSceneBuilder
{
    private readonly IReadOnlySet<Guid> _selection;
    private readonly List<LogicSceneCard> _cards = [];
    private readonly List<LogicSceneEdge> _edges = [];

    public LogicSceneBuilder(IReadOnlySet<Guid> selection) => _selection = selection;

    public bool IsSelected(LogicCard card) => !card.IsStub && _selection.Contains(card.NodeId);

    public void Add(LogicCardFace face, Point topLeft) => _cards.Add(face.Place(topLeft));

    public void Add(LogicEdge edge, IReadOnlyList<LogicCubic> path, Rect? labelBounds) =>
        _edges.Add(new LogicSceneEdge(edge, path, labelBounds));

    public LogicScene Build(Size size) => new(_cards, _edges, size);
}
