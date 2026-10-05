using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>What a caller may choose about how the graph is laid out.</summary>
public sealed class LogicLayoutOptions
{
    /// <summary>Whether every entity's card has a state row, as it does while the level plays.</summary>
    public bool ShowsState { get; init; }

    /// <summary>
    /// The entities whose cards list every port their class declares. The
    /// rest list only the wired ones.
    /// </summary>
    public IReadOnlySet<Guid> ExpandedCards { get; init; } = FrozenSet<Guid>.Empty;

    /// <summary>The least gap between two columns. A lane with a wide label grows past it.</summary>
    public double MinimumLaneWidth { get; init; } = LogicMetrics.LaneWidth;
}
