namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>How far a drag that makes a wire has come.</summary>
public enum LogicWirePhase
{
    /// <summary>Nothing has been pressed.</summary>
    Idle,

    /// <summary>A card is pressed. Let go now, it is a click.</summary>
    Pressed,

    /// <summary>A wire follows the pointer.</summary>
    Dragging,

    /// <summary>The wire was let go on a card, and a menu asks what it sends.</summary>
    Dropped,

    /// <summary>The last drag made a wire.</summary>
    Done,

    /// <summary>The last drag was given up.</summary>
    Cancelled,
}
