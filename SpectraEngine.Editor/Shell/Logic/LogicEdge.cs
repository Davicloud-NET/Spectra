using SpectraEngine.Core.Entities;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// One line in the wiring graph, from an output of one card to an input of
/// another. A wire whose target matches several entities makes one edge for
/// each. Wires that agree in output, target and input share one edge.
/// </summary>
public sealed class LogicEdge
{
    internal LogicEdge(
        LogicCard from,
        LogicCard to,
        EntityConnection wire,
        IReadOnlyList<int> wireIndices,
        LogicVerdict verdict)
    {
        From = from;
        To = to;
        Output = wire.Output ?? "";
        Input = wire.Input ?? "";
        TargetName = wire.TargetName ?? "";
        WireIndices = wireIndices;
        Verdict = verdict;
        Label = wireIndices.Count > 1 ? LogicLabel.Count(wireIndices.Count) : LogicLabel.Of(wire);
    }

    private LogicEdge(LogicEdge source, LogicLabel label)
    {
        From = source.From;
        To = source.To;
        Output = source.Output;
        Input = source.Input;
        TargetName = source.TargetName;
        WireIndices = source.WireIndices;
        Verdict = source.Verdict;
        Label = label;
    }

    /// <summary>The card that sends.</summary>
    public LogicCard From { get; }

    /// <summary>The card that receives. The sender itself for a wire to <c>!self</c>.</summary>
    public LogicCard To { get; }

    /// <summary>The output that fires the wire.</summary>
    public string Output { get; }

    /// <summary>The input the wire sends.</summary>
    public string Input { get; }

    /// <summary>The target as the wire spells it.</summary>
    public string TargetName { get; }

    /// <summary>
    /// The wires this edge draws, as indices into the sender's wire list. More
    /// than one when identical wires share the edge.
    /// </summary>
    public IReadOnlyList<int> WireIndices { get; }

    /// <summary>The first wire this edge draws.</summary>
    public LogicWireKey Wire => new(From.NodeId, WireIndices[0]);

    /// <summary>What the schemas say about the wire.</summary>
    public LogicVerdict Verdict { get; }

    /// <summary>Whether what the wire sends can never arrive.</summary>
    public bool GoesNowhere => Verdict is LogicVerdict.TargetMissing or LogicVerdict.NoSuchInput;

    /// <summary>Whether the edge leaves and enters the same card.</summary>
    public bool IsLoop => ReferenceEquals(From, To);

    /// <summary>The words drawn on the edge.</summary>
    public LogicLabel Label { get; }

    /// <summary>The same edge with other words on it, for a view that shows a running level.</summary>
    public LogicEdge WithLabel(LogicLabel label) => new(this, label);
}
