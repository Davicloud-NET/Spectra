using Avalonia;
using Avalonia.Controls;

namespace SpectraEngine.Editor.Shell.Logic;

// What the pointer rests on in the Logic canvas, and the tip the canvas
// shows for it.
internal sealed class LogicHover
{
    private readonly Control _owner;
    private LogicScene? _scene;

    public LogicHover(Control owner) => _owner = owner;

    public LogicSceneCard? Card { get; private set; }

    public LogicSceneEdge? Edge { get; private set; }

    // Takes what is under a point of the view.
    public void MoveTo(LogicViewModel? model, Point at)
    {
        LogicHit hit = model?.HitTest(at) ?? LogicHit.None;
        Set(model, hit.Card, hit.Edge);
    }

    public void Clear(LogicViewModel? model) => Set(model, null, null);

    // What the pointer was on belongs to a scene that may be gone.
    public void ClearIfStale(LogicViewModel? model)
    {
        if (!ReferenceEquals(model?.Scene, _scene))
            Clear(model);
    }

    private void Set(LogicViewModel? model, LogicSceneCard? card, LogicSceneEdge? edge)
    {
        _scene = model?.Scene;
        if (ReferenceEquals(card, Card) && ReferenceEquals(edge, Edge))
            return;

        Card = card;
        Edge = edge;

        // A wire says what it does in a sentence. A card says its whole
        // name, which the card itself may have cut short.
        string? tip = edge is not null ? model?.FaceOf(edge)?.Sentence
            : card is not null ? LogicViewText.Sentence(card.Card)
            : null;

        ToolTip.SetTip(_owner, tip);
        if (tip is null)
            ToolTip.SetIsOpen(_owner, false);

        _owner.InvalidateVisual();
    }
}
