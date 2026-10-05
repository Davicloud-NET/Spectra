using Avalonia;
using System;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// What the rows round the Logic view's graph have room for. As the view
/// narrows the filter box gives up room first, then the words that label the
/// controls, then the controls a narrow pane can do without.
/// </summary>
public readonly record struct LogicViewFit
{
    /// <summary>The narrowest the filter box is still worth showing at.</summary>
    public const double LeastFilterWidth = 72;

    /// <summary>The widest the filter box gets.</summary>
    public const double MostFilterWidth = 220;

    /// <summary>The narrowest the filter box still has room for its whole placeholder.</summary>
    public const double LongPlaceholderWidth = 150;

    /// <summary>The least of a status sentence that is worth reading.</summary>
    public const double LeastNoteWidth = 160;

    /// <summary>The least height the graph keeps before the event strip gives way.</summary>
    public const double LeastGraphHeight = 96;

    // What each part of the toolbar takes with its margin, measured in the
    // shell's fonts with a little to spare. LogicToolbarTests holds the
    // toolbar to them at every width.
    private const double Padding = 16;
    private const double ShowLabel = 40;
    private const double StepsLabel = 68;
    private const double LongNames = 212;
    private const double ShortNames = 116;
    private const double Steps = 42;
    private const double FilterGap = 8;
    private const double Playing = 82;
    private const double Tick = 76;
    private const double ZoomKeys = 92;

    // The rows that are always there, and the strip.
    private const double Rows = 62;
    private const double EventStrip = 64;

    // The status row: its padding, the two counts, the link and the hint.
    private const double StatusPadding = 20;
    private const double Counts = 122;
    private const double Link = 128;
    private const double Hint = 280;

    /// <summary>Everything, as in a wide view of a running level.</summary>
    public static LogicViewFit Everything { get; } = new()
    {
        ShowsLabels = true,
        ShowsTick = true,
        ShowsFilter = true,
        ShowsZoomKeys = true,
        ShowsSteps = true,
        ShowsPlaying = true,
        ShowsCounts = true,
        ShowsNotes = true,
        ShowsHint = true,
        ShowsEvents = true,
    };

    /// <summary>The words Show and Steps away.</summary>
    public bool ShowsLabels { get; init; }

    /// <summary>The tick beside the Playing pill.</summary>
    public bool ShowsTick { get; init; }

    /// <summary>Whether the two mode keys read Selection and Level.</summary>
    public bool UsesShortNames { get; init; }

    /// <summary>The filter box.</summary>
    public bool ShowsFilter { get; init; }

    /// <summary>The Fit and 100% keys.</summary>
    public bool ShowsZoomKeys { get; init; }

    /// <summary>The steps field.</summary>
    public bool ShowsSteps { get; init; }

    /// <summary>The Playing pill.</summary>
    public bool ShowsPlaying { get; init; }

    /// <summary>The counts at the left of the status row.</summary>
    public bool ShowsCounts { get; init; }

    /// <summary>The status row's sentences: what the level has more of than the view shows.</summary>
    public bool ShowsNotes { get; init; }

    /// <summary>The hint at the right of the status row.</summary>
    public bool ShowsHint { get; init; }

    /// <summary>The strip of what the wires just did.</summary>
    public bool ShowsEvents { get; init; }

    /// <summary>How wide the toolbar is without its filter box.</summary>
    public double ToolbarWidth =>
        Padding
        + (ShowsLabels ? ShowLabel + StepsLabel : 0)
        + (UsesShortNames ? ShortNames : LongNames)
        + (ShowsSteps ? Steps : 0)
        + (ShowsPlaying ? Playing : 0)
        + (ShowsTick ? Tick : 0)
        + (ShowsZoomKeys ? ZoomKeys : 0);

    /// <summary>Decides what a view of a size shows.</summary>
    /// <param name="size">The size of the whole view.</param>
    /// <param name="isPlaying">Whether a level runs, which adds the pill, the tick and the strip.</param>
    /// <param name="status">What the status row has to say.</param>
    public static LogicViewFit For(Size size, bool isPlaying, LogicStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        LogicViewFit fit = Everything with
        {
            ShowsTick = isPlaying,
            ShowsPlaying = isPlaying,
            ShowsEvents = isPlaying && size.Height >= Rows + EventStrip + LeastGraphHeight,
        };

        return fit.WithStatus(size.Width, status).WithToolbar(size.Width);
    }

    /// <summary>How wide the filter box is in a view of a width. Zero when it is not shown.</summary>
    public double FilterWidth(double width) =>
        ShowsFilter ? Math.Clamp(width - ToolbarWidth - FilterGap, 0, MostFilterWidth) : 0;

    // The link is the row's news and stays. The counts give way to it, a
    // sentence shows only with room to be read, and the hint comes last.
    private LogicViewFit WithStatus(double width, LogicStatus status)
    {
        bool hasNotes = status.Unwired.Length > 0 || status.Truncated.Length > 0;
        double link = status.GoingNowhere.Length > 0 ? Link : 0;
        double left = width - StatusPadding - link;

        bool counts = link == 0 || left >= Counts;
        left -= counts ? Counts : 0;

        bool notes = hasNotes && left >= LeastNoteWidth;
        left -= notes ? LeastNoteWidth : 0;

        return this with { ShowsCounts = counts, ShowsNotes = notes, ShowsHint = left >= Hint };
    }

    // Each step gives up the next thing, until what is left fits.
    private LogicViewFit WithToolbar(double width)
    {
        LogicViewFit fit = this;
        if (fit.Fits(width)) return fit;
        fit = fit with { ShowsLabels = false };
        if (fit.Fits(width)) return fit;
        fit = fit with { ShowsTick = false };
        if (fit.Fits(width)) return fit;
        fit = fit with { UsesShortNames = true };
        if (fit.Fits(width)) return fit;
        fit = fit with { ShowsFilter = false };
        if (fit.Fits(width)) return fit;
        fit = fit with { ShowsZoomKeys = false };
        if (fit.Fits(width)) return fit;
        fit = fit with { ShowsSteps = false };
        if (fit.Fits(width)) return fit;

        return fit with { ShowsPlaying = false };
    }

    private bool Fits(double width) =>
        ToolbarWidth + (ShowsFilter ? FilterGap + LeastFilterWidth : 0) <= width;
}
