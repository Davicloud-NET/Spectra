using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>Which view panes the centre of the window shows.</summary>
public enum ViewArrangement
{
    /// <summary>The 3D view alone. The default.</summary>
    Single,

    /// <summary>The Logic view under the 3D view.</summary>
    LogicBelow,

    /// <summary>The Logic view to the right of the 3D view.</summary>
    LogicBeside,
}

/// <summary>One axis of the pane grid: a pane, the gutter, a second pane.</summary>
/// <param name="First">The first pane's length.</param>
/// <param name="Gutter">The gap the splitter fills. Zero when the axis has one pane.</param>
/// <param name="Second">The second pane's length, or zero.</param>
/// <param name="FirstMin">The least the first pane may be.</param>
/// <param name="SecondMin">The least the second may be, or zero.</param>
// The pane lengths are pixels when a size was given and shares when none was.
// A grid takes the shares as star weights and does the clamping itself.
public readonly record struct ViewPaneAxis(
    double First,
    double Gutter,
    double Second,
    double FirstMin,
    double SecondMin)
{
    /// <summary>Whether the axis has two panes with a splitter between them.</summary>
    public bool IsSplit => Gutter > 0;

    /// <summary>The least the whole axis may be.</summary>
    public double Min => FirstMin + Gutter + SecondMin;
}

/// <summary>A pane's place in the pane grid. Panes sit in tracks 0 and 2.</summary>
public readonly record struct ViewPaneCell(int Column, int Row);

/// <summary>The pane grid for one arrangement: three tracks each way.</summary>
/// <param name="View">Where the 3D view sits. Always the first cell.</param>
/// <param name="Logic">Where the Logic view sits when it shows.</param>
public readonly record struct ViewPaneGrid(
    ViewPaneAxis Columns,
    ViewPaneAxis Rows,
    ViewPaneCell View,
    ViewPaneCell Logic,
    bool ShowsLogic);

/// <summary>
/// How the centre of the window is divided between the view panes.
/// </summary>
// A split is the first pane's share of what the gutter leaves, 0 to 1. The
// first pane is always the 3D view, and it never goes under its minimum.
public static class ViewPaneLayout
{
    /// <summary>The Logic pane's least width.</summary>
    public const double LogicMinWidth = 180;

    /// <summary>The Logic pane's least height, its header included.</summary>
    public const double LogicMinHeight = 90;

    /// <summary>The 3D view's share of the width beside the Logic view.</summary>
    public const double DefaultColumnSplit = 0.6;

    /// <summary>The 3D view's share of the height above the Logic view.</summary>
    public const double DefaultRowSplit = 0.45;

    /// <summary>Where the Logic view opens the first time.</summary>
    public const ViewArrangement DefaultSplit = ViewArrangement.LogicBelow;

    /// <summary>
    /// The pane grid for an arrangement, with each pane's length as its share
    /// of the space. What a grid is set up from.
    /// </summary>
    public static ViewPaneGrid Arrange(ViewArrangement arrangement, double columnSplit, double rowSplit) =>
        Arrange(arrangement, columnSplit, rowSplit, 0, 0);

    /// <summary>
    /// The pane grid for an arrangement in a space of the given size, with
    /// each pane's length in pixels. What the grid comes to once the minimums
    /// have had their say.
    /// </summary>
    public static ViewPaneGrid Arrange(
        ViewArrangement arrangement, double columnSplit, double rowSplit, double width, double height)
    {
        bool beside = SplitsColumns(arrangement);
        bool below = SplitsRows(arrangement);

        ViewPaneAxis columns = beside
            ? Divide(columnSplit, width, WorkspaceLayout.ViewportMinWidth, LogicMinWidth)
            : Whole(width, WorkspaceLayout.ViewportMinWidth);

        ViewPaneAxis rows = below
            ? Divide(rowSplit, height, WorkspaceLayout.ViewportMinHeight, LogicMinHeight)
            : Whole(height, WorkspaceLayout.ViewportMinHeight);

        var logic = new ViewPaneCell(beside ? 2 : 0, below ? 2 : 0);

        return new ViewPaneGrid(columns, rows, new ViewPaneCell(0, 0), logic, beside || below);
    }

    /// <summary>The least space an arrangement needs.</summary>
    public static (double Width, double Height) MinimumSize(ViewArrangement arrangement)
    {
        ViewPaneGrid grid = Arrange(arrangement, DefaultColumnSplit, DefaultRowSplit);

        return (grid.Columns.Min, grid.Rows.Min);
    }

    /// <summary>Whether every pane of an arrangement gets its minimum in this space.</summary>
    public static bool Fits(ViewArrangement arrangement, double width, double height)
    {
        (double minWidth, double minHeight) = MinimumSize(arrangement);

        return width >= minWidth && height >= minHeight;
    }

    /// <summary>
    /// The split nearest the one asked for that leaves both panes their
    /// minimum. When both cannot have it, the first pane keeps its own.
    /// </summary>
    public static double ClampSplit(double split, double available, double firstMin, double secondMin)
    {
        double usable = available - WorkspaceLayout.Gutter;
        if (!(usable > 0))
            return split;

        return Math.Min(FirstLength(split, usable, firstMin, secondMin) / usable, 1);
    }

    /// <summary>
    /// The split two pane tracks stand for, read after a splitter drag. False
    /// when either track has no length.
    /// </summary>
    public static bool TryReadSplit(double first, double second, out double split)
    {
        split = 0;
        if (!(first > 0) || !(second > 0))
            return false;

        split = first / (first + second);
        return IsSplit(split);
    }

    /// <summary>Whether a number can be a split: finite, and between 0 and 1.</summary>
    public static bool IsSplit(double split) => double.IsFinite(split) && split > 0 && split < 1;

    /// <summary>
    /// What the Logic view's key does: hide it when it shows, else bring it
    /// back where it last was.
    /// </summary>
    public static ViewArrangement Flip(ViewArrangement current, ViewArrangement lastSplit)
    {
        if (current != ViewArrangement.Single)
            return ViewArrangement.Single;

        return lastSplit == ViewArrangement.Single ? DefaultSplit : lastSplit;
    }

    /// <summary>The arrangement's name, for settings.</summary>
    public static string NameOf(ViewArrangement arrangement) => arrangement switch
    {
        ViewArrangement.LogicBelow => "logicBelow",
        ViewArrangement.LogicBeside => "logicBeside",
        _ => "single",
    };

    /// <summary>
    /// Reads an arrangement name. An unknown word leaves
    /// <paramref name="arrangement"/> at single and returns false.
    /// </summary>
    public static bool TryParse(string? name, out ViewArrangement arrangement)
    {
        arrangement = ViewArrangement.Single;

        if (string.Equals(name, "logicBelow", StringComparison.OrdinalIgnoreCase))
        {
            arrangement = ViewArrangement.LogicBelow;
            return true;
        }

        if (string.Equals(name, "logicBeside", StringComparison.OrdinalIgnoreCase))
        {
            arrangement = ViewArrangement.LogicBeside;
            return true;
        }

        return string.Equals(name, "single", StringComparison.OrdinalIgnoreCase);
    }

    // A four-pane arrangement answers true to both.
    private static bool SplitsColumns(ViewArrangement arrangement) =>
        arrangement == ViewArrangement.LogicBeside;

    private static bool SplitsRows(ViewArrangement arrangement) =>
        arrangement == ViewArrangement.LogicBelow;

    private static ViewPaneAxis Whole(double available, double min) =>
        new(available > 0 ? available : 1, 0, 0, min, 0);

    private static ViewPaneAxis Divide(double split, double available, double firstMin, double secondMin)
    {
        double gutter = WorkspaceLayout.Gutter;
        double usable = available - gutter;

        if (!(usable > 0))
            return new ViewPaneAxis(split, gutter, 1 - split, firstMin, secondMin);

        double first = FirstLength(split, usable, firstMin, secondMin);

        return new ViewPaneAxis(first, gutter, Math.Max(usable - first, 0), firstMin, secondMin);
    }

    private static double FirstLength(double split, double usable, double firstMin, double secondMin)
    {
        double most = Math.Max(usable - secondMin, firstMin);

        return Math.Clamp(split * usable, firstMin, most);
    }
}
