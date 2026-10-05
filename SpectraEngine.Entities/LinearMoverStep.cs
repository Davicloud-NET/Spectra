namespace SpectraEngine.Entities;

/// <summary>What one <see cref="LinearMover.Advance"/> did.</summary>
public enum LinearMoverStep
{
    /// <summary>
    /// At rest: there was nowhere to go, the node reached a target part way
    /// along, or the world refused the move.
    /// </summary>
    Stopped,

    /// <summary>Moved one tick and has further to go.</summary>
    Moving,

    /// <summary>Reached the open pose on this tick.</summary>
    ArrivedOpen,

    /// <summary>Reached the closed pose on this tick.</summary>
    ArrivedClosed,
}
