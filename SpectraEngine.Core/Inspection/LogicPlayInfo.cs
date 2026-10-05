using System.Collections.Generic;

namespace SpectraEngine.Core.Inspection;

/// <summary>
/// What a running level's wiring is doing, for a view that lights it. State,
/// not a stream: a reader that skips one loses nothing.
/// </summary>
public sealed class LogicPlayInfo
{
    /// <summary>The entity world's tick.</summary>
    public long Tick { get; init; }

    /// <summary>The entity world's time in seconds.</summary>
    public float Time { get; init; }

    /// <summary>Every wire that has done something, in the order they first fired.</summary>
    public IReadOnlyList<LogicWireActivity> Wires { get; init; } = [];

    /// <summary>One line of state for each entity the view asked about.</summary>
    public IReadOnlyList<LogicEntityState> States { get; init; } = [];

    /// <summary>The newest events, oldest first.</summary>
    public IReadOnlyList<LogicEventInfo> Recent { get; init; } = [];
}
