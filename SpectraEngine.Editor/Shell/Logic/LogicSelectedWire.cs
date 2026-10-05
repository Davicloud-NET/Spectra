using System;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// The wire the Logic view has selected. A wire has no id, so it is named by
/// who sends it and its place in that entity's list, and known again by what
/// it says.
/// </summary>
/// <param name="NodeId">The entity that sends it.</param>
/// <param name="WireIndex">Where it is in that entity's wire list.</param>
/// <param name="Output">The output that fires it.</param>
/// <param name="TargetName">The target as it spells it.</param>
/// <param name="Input">The input it sends.</param>
public readonly record struct LogicSelectedWire(
    Guid NodeId,
    int WireIndex,
    string Output,
    string TargetName,
    string Input)
{
    /// <summary>The wire an edge draws. The newest, when identical wires share the edge.</summary>
    public static LogicSelectedWire Of(LogicEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return new LogicSelectedWire(edge.From.NodeId, edge.WireIndices[^1], edge.Output, edge.TargetName, edge.Input);
    }

    /// <summary>
    /// Whether an edge draws this wire. A wire whose target matches several
    /// entities is drawn by an edge to each.
    /// </summary>
    public bool IsDrawnBy(LogicEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);

        if (edge.From.NodeId != NodeId
            || !string.Equals(edge.Output, Output, StringComparison.Ordinal)
            || !string.Equals(edge.TargetName, TargetName, StringComparison.Ordinal)
            || !string.Equals(edge.Input, Input, StringComparison.Ordinal))
        {
            return false;
        }

        // Indexed: this runs for every wire of every frame drawn.
        for (int i = 0; i < edge.WireIndices.Count; i++)
        {
            if (edge.WireIndices[i] == WireIndex)
                return true;
        }

        return false;
    }
}
