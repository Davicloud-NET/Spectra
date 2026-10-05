using Avalonia;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// An edge while the graph is laid out.
internal sealed class LogicLayoutWire
{
    public LogicLayoutWire(LogicEdge edge, LogicLayoutNode from, LogicLayoutNode to, double labelWidth)
    {
        Edge = edge;
        From = from;
        To = to;
        LabelWidth = labelWidth;
        FromOffset = from.Face?.AnchorOffset(edge.Output, isOutput: true) ?? 0;
        ToOffset = to.Face?.AnchorOffset(edge.Input, isOutput: false) ?? 0;
        Route = ReferenceEquals(from, to) ? LogicWireRoute.Loop : LogicWireRoute.Forward;
    }

    public LogicEdge Edge { get; }

    public LogicLayoutNode From { get; }

    public LogicLayoutNode To { get; }

    // How far under the sender's top the wire leaves, and under the receiver's it arrives.
    public double FromOffset { get; }

    public double ToOffset { get; }

    // The label's whole width. Zero for no label.
    public double LabelWidth { get; }

    public bool HasLabel => LabelWidth > 0;

    // Its place among its group's wires. Breaks every tie.
    public int Index { get; set; }

    public LogicWireRoute Route { get; set; }

    // The slots a forward wire passes through, one for each column it skips.
    public List<LogicLayoutNode> Slots { get; } = [];

    // Which run a loop or a back wire has, counted from the nearest: under
    // its card for a loop, under its group for a back wire.
    public int Level { get; set; }

    // The height that run is at.
    public double RunY { get; set; }

    // A back wire's place among those going down beside its sender's column,
    // and among those coming up beside its receiver's.
    public int DownRun { get; set; }

    public int UpRun { get; set; }

    public Rect? LabelBounds { get; set; }
}
