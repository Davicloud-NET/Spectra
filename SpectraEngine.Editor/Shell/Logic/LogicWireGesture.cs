using Avalonia;
using System;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// A drag that makes a wire, from the press on a card to the pick in the
/// menu. It is told where the pointer is and what is under it, and knows
/// nothing else of the pointer.
/// </summary>
public sealed class LogicWireGesture
{
    private Point _pressedAt;
    private bool _hasLeftSender;

    /// <summary>How far the drag has come.</summary>
    public LogicWirePhase Phase { get; private set; }

    /// <summary>The card that sends, while a drag is going on.</summary>
    public LogicSceneCard? From { get; private set; }

    /// <summary>The output the drag began on, or null when it began elsewhere on the card.</summary>
    public string? Output { get; private set; }

    /// <summary>Where the wire leaves the sender, in the scene.</summary>
    public Point Start { get; private set; }

    /// <summary>Where the pointer is, or where the wire was let go, in the view.</summary>
    public Point Pointer { get; private set; }

    /// <summary>The card the wire would land on, or null over anything that cannot take one.</summary>
    public LogicSceneCard? Target { get; private set; }

    /// <summary>Whether a press has not come to an end yet.</summary>
    public bool IsActive =>
        Phase is LogicWirePhase.Pressed or LogicWirePhase.Dragging or LogicWirePhase.Dropped;

    /// <summary>Whether there is a wire to draw.</summary>
    public bool ShowsWire => Phase is LogicWirePhase.Dragging or LogicWirePhase.Dropped;

    /// <summary>Takes a press of the left button. Returns whether a drag may come of it.</summary>
    /// <param name="at">The pointer, in the view.</param>
    /// <param name="hit">What it is on.</param>
    /// <param name="canEdit">Whether wires may change. False while a level plays.</param>
    public bool Press(Point at, LogicHit hit, bool canEdit)
    {
        if (IsActive || !canEdit
            || hit.Kind is not (LogicHitKind.Card or LogicHitKind.Port)
            || hit.Card is not { Card.IsStub: false } card)
        {
            return false;
        }

        Phase = LogicWirePhase.Pressed;
        From = card;
        Target = null;
        _pressedAt = Pointer = at;
        _hasLeftSender = false;

        if (hit.Port is { IsOutput: true } port)
        {
            Output = port.Name;
            Start = port.Anchor;
        }
        else
        {
            Output = null;
            Start = new Point(card.Bounds.Right, card.Header.Center.Y);
        }

        return true;
    }

    /// <summary>Takes a move of the pointer. Returns whether a drawing would differ.</summary>
    /// <param name="at">The pointer, in the view.</param>
    /// <param name="over">The card under it, or null.</param>
    public bool Move(Point at, LogicSceneCard? over)
    {
        if (Phase == LogicWirePhase.Pressed)
        {
            Vector travelled = at - _pressedAt;
            if (Math.Abs(travelled.X) <= LogicDrawMetrics.ClickSlop
                && Math.Abs(travelled.Y) <= LogicDrawMetrics.ClickSlop)
            {
                return false;
            }

            Phase = LogicWirePhase.Dragging;
        }
        else if (Phase != LogicWirePhase.Dragging)
        {
            return false;
        }

        _hasLeftSender |= !ReferenceEquals(over, From);
        Pointer = at;
        Target = Takes(over) ? over : null;
        return true;
    }

    /// <summary>
    /// Takes the release of the button. Returns whether the wire was let go
    /// on a card, so a menu is due.
    /// </summary>
    public bool Release()
    {
        switch (Phase)
        {
            case LogicWirePhase.Pressed:
                End(LogicWirePhase.Idle);
                return false;

            case LogicWirePhase.Dragging when Target is not null:
                Phase = LogicWirePhase.Dropped;
                return true;

            case LogicWirePhase.Dragging:
                End(LogicWirePhase.Cancelled);
                return false;

            default:
                return false;
        }
    }

    /// <summary>Gives the drag up, wherever it is. Returns whether a drawing would differ.</summary>
    public bool Cancel()
    {
        if (!IsActive)
            return false;

        bool showedWire = ShowsWire;
        End(LogicWirePhase.Cancelled);
        return showedWire;
    }

    /// <summary>
    /// Takes the loss of the pointer. A wire that waits for its menu has let
    /// go of the pointer already and stays.
    /// </summary>
    public bool LoseCapture() =>
        Phase is (LogicWirePhase.Pressed or LogicWirePhase.Dragging) && Cancel();

    /// <summary>Ends a drag whose menu made the wire.</summary>
    public void Finish()
    {
        if (Phase == LogicWirePhase.Dropped)
            End(LogicWirePhase.Done);
    }

    // The sender's own card counts once the pointer has been off it. A press
    // that slips a few pixels on a card would otherwise ask for a wire.
    private bool Takes(LogicSceneCard? over) =>
        over is { Card.IsStub: false }
        && From is not null
        && (_hasLeftSender || !ReferenceEquals(over, From))
        && LogicWireList.TargetOf(From.Card, over.Card) is not null;

    private void End(LogicWirePhase phase)
    {
        Phase = phase;
        From = null;
        Target = null;
        Output = null;
    }
}
