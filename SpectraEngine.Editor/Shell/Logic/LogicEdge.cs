using SpectraEngine.Core.Entities;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// One line in the wiring graph, from an output of one card to an input of
/// another. A wire whose target matches several entities makes one edge for
/// each. Wires that agree in output, target and input share one edge. What a
/// running script hears or sends is an edge too, between its card and the
/// entity's.
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

    // What a script hears, from the entity's output to the script, or what
    // it sends, from the script to the entity's input.
    internal LogicEdge(LogicCard from, LogicCard to, string port, IReadOnlyList<int> scriptWires, LogicLabel label)
    {
        From = from;
        To = to;
        Output = from.IsScript ? "" : port;
        Input = from.IsScript ? port : "";
        TargetName = to.Name;
        ScriptWires = scriptWires;
        Label = label;
    }

    private LogicEdge(LogicEdge source, LogicLabel label)
    {
        From = source.From;
        To = source.To;
        Output = source.Output;
        Input = source.Input;
        TargetName = source.TargetName;
        WireIndices = source.WireIndices;
        ScriptWires = source.ScriptWires;
        Verdict = source.Verdict;
        Label = label;
    }

    /// <summary>The card that sends.</summary>
    public LogicCard From { get; }

    /// <summary>The card that receives. The sender itself for a wire to <c>!self</c>.</summary>
    public LogicCard To { get; }

    /// <summary>The output that fires the wire. Empty for what a script sends.</summary>
    public string Output { get; }

    /// <summary>The input the wire sends. Empty for what a script hears.</summary>
    public string Input { get; }

    /// <summary>The target as the wire spells it.</summary>
    public string TargetName { get; }

    /// <summary>
    /// The wires this edge draws, as indices into the sender's wire list. More
    /// than one when identical wires share the edge. None for an edge a
    /// script made.
    /// </summary>
    public IReadOnlyList<int> WireIndices { get; } = [];

    /// <summary>
    /// What a script made this edge of, as indices into the wires the engine
    /// lists for the level's scripts: one for each line of code. None for a
    /// wire of the level.
    /// </summary>
    public IReadOnlyList<int> ScriptWires { get; } = [];

    /// <summary>Whether a running script made the edge. It lasts no longer than the level runs.</summary>
    public bool IsScript => ScriptWires.Count > 0;

    /// <summary>The first wire this edge draws. No wire of any entity for an edge a script made.</summary>
    public LogicWireKey Wire => WireIndices.Count > 0 ? new(From.NodeId, WireIndices[0]) : new(default, -1);

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
