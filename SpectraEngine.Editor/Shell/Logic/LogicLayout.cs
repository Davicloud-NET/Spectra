using Avalonia;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// Places a wiring graph on the page. Cards stand in columns, left to right
/// by cause, and nothing with text in it touches anything else with text in
/// it. The same graph always gets the same picture, whatever order the scene
/// lists its entities in.
/// </summary>
public static class LogicLayout
{
    /// <summary>Lays out what a scope shows.</summary>
    /// <param name="graph">The cards and edges to place. Each edge is drawn with the label it carries.</param>
    /// <param name="selection">
    /// The selected nodes. A group that holds one of their cards comes first.
    /// Cards with no wires come last all the same.
    /// </param>
    /// <param name="options">What the cards show and how far apart the columns stand.</param>
    /// <param name="measure">How wide a label's text is drawn.</param>
    public static LogicScene Arrange(
        LogicScopedGraph graph,
        IReadOnlySet<Guid> selection,
        LogicLayoutOptions options,
        ILogicTextMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(measure);

        if (!double.IsFinite(options.MinimumLaneWidth) || options.MinimumLaneWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "The least lane width must be zero or more.");

        var scene = new LogicSceneBuilder(selection);
        if (graph.Cards.Count == 0)
            return scene.Build(default);

        double width = 0;
        double left = 0;
        double top = LogicMetrics.ScenePadding;
        double rowHeight = 0;

        // Groups that share no wire fill a row like words fill a line, so a
        // level of many small groups is not one tall column in a wide pane.
        foreach (LogicGroupLayout group in LogicGroups.Split(graph, scene, options, measure))
        {
            if (left > 0 && left + group.Size.Width > LogicMetrics.PageWidth)
            {
                top += rowHeight + LogicMetrics.GroupGap;
                left = 0;
                rowHeight = 0;
            }

            group.Emit(new Point(LogicMetrics.ScenePadding + left, top), scene);
            width = Math.Max(width, left + group.Size.Width);
            left += group.Size.Width + LogicMetrics.GroupGap;
            rowHeight = Math.Max(rowHeight, group.Size.Height);
        }

        return scene.Build(new Size(
            width + 2 * LogicMetrics.ScenePadding,
            top + rowHeight + LogicMetrics.ScenePadding));
    }
}
