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
    /// <param name="selection">The selected nodes. Their cards are marked and their group comes first.</param>
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

        var scene = new LogicSceneBuilder(graph, selection);
        if (graph.Cards.Count == 0)
            return scene.Build(default);

        double width = 0;
        double top = LogicMetrics.ScenePadding;

        // Groups that share no wire stack from the top.
        foreach (LogicGroupLayout group in LogicGroups.Split(graph, scene, options, measure))
        {
            group.Emit(new Point(LogicMetrics.ScenePadding, top), scene);
            width = Math.Max(width, group.Size.Width);
            top += group.Size.Height + LogicMetrics.GroupGap;
        }

        return scene.Build(new Size(
            width + 2 * LogicMetrics.ScenePadding,
            top - LogicMetrics.GroupGap + LogicMetrics.ScenePadding));
    }
}
