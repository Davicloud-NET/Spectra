using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// Where a group's columns stand from left to right. The gap between two
// columns is a lane: labels sit in its middle, and wires that run back go
// down and up along its sides.
internal sealed class LogicLanes
{
    private readonly double[] _left;
    private readonly double[] _width;
    private readonly double[] _laneWidth;

    public LogicLanes(int columnCount, IReadOnlyList<LogicLayoutWire> wires, double minimumLaneWidth)
    {
        _left = new double[columnCount];
        _width = new double[columnCount];
        _laneWidth = new double[columnCount];
        Array.Fill(_width, LogicMetrics.CardWidth);

        // Counted per column: wires going down on its right, coming up on its left.
        var down = new int[columnCount];
        var up = new int[columnCount];
        var widestLabel = new double[columnCount];

        foreach (LogicLayoutWire wire in BackWiresByReach(wires))
        {
            wire.DownRun = down[wire.From.Column]++;
            wire.UpRun = up[wire.To.Column]++;
        }

        foreach (LogicLayoutWire wire in wires)
        {
            if (wire.Route == LogicWireRoute.Loop)
            {
                // A loop's label sits under its card, so the column has to hold it.
                _width[wire.From.Column] = Math.Max(_width[wire.From.Column], wire.LabelWidth);
            }
            else if (wire.Route == LogicWireRoute.Forward)
            {
                int lane = wire.To.Column - 1;
                widestLabel[lane] = Math.Max(widestLabel[lane], wire.LabelWidth);
            }
        }

        double x = up[0] > 0 ? LogicMetrics.BackRunStep * (up[0] + 1) : 0;
        for (int column = 0; column < columnCount; column++)
        {
            _left[column] = x;

            if (column < columnCount - 1)
            {
                int runs = Math.Max(down[column], up[column + 1]);
                double margin = LogicMetrics.LaneMargin + LogicMetrics.BackRunStep * runs;
                _laneWidth[column] = Math.Max(minimumLaneWidth, widestLabel[column] + 2 * margin);
            }
            else if (down[column] > 0)
            {
                _laneWidth[column] = LogicMetrics.BackRunStep * (down[column] + 1);
            }

            x += _width[column] + _laneWidth[column];
        }

        Right = x;
    }

    // The right edge of the group, past any wire that runs down beside the last column.
    public double Right { get; }

    public double ColumnLeft(int column) => _left[column];

    public double ColumnRight(int column) => _left[column] + _width[column];

    // Cards stand in the middle of their column.
    public double CardLeft(LogicLayoutNode node) =>
        Math.Floor(_left[node.Column] + (_width[node.Column] - LogicMetrics.CardWidth) / 2);

    // The middle of the lane after a column.
    public double LaneCenter(int column) => ColumnRight(column) + _laneWidth[column] / 2;

    // Where a back wire runs down, beside its sender's column.
    public double DownRunX(LogicLayoutWire wire) =>
        ColumnRight(wire.From.Column) + LogicMetrics.BackRunStep * (wire.DownRun + 1);

    // Where a back wire runs up, beside its receiver's column.
    public double UpRunX(LogicLayoutWire wire) =>
        ColumnLeft(wire.To.Column) - LogicMetrics.BackRunStep * (wire.UpRun + 1);

    // Short wires first. They get the runs nearest the cards and the group,
    // and longer ones pass outside them.
    private static List<LogicLayoutWire> BackWiresByReach(IReadOnlyList<LogicLayoutWire> wires)
    {
        var back = new List<LogicLayoutWire>();
        foreach (LogicLayoutWire wire in wires)
        {
            if (wire.Route == LogicWireRoute.Back)
                back.Add(wire);
        }

        back.Sort((a, b) =>
        {
            int byReach = (a.From.Column - a.To.Column).CompareTo(b.From.Column - b.To.Column);
            return byReach != 0 ? byReach : a.Index.CompareTo(b.Index);
        });

        for (int i = 0; i < back.Count; i++)
            back[i].Level = i;

        return back;
    }
}
