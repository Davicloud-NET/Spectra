using Avalonia;
using System;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// What the rows round the Logic view's graph have room for. As the view
/// narrows the filter box gives up room first, then the words that label the
/// controls, then the controls a narrow pane can do without.
/// </summary>
/// <param name="ShowsLabels">The words Show and Steps away.</param>
/// <param name="ShowsTick">The tick beside the Playing pill.</param>
/// <param name="UsesShortNames">Whether the two mode keys read Selection and Level.</param>
/// <param name="ShowsFilter">The filter box.</param>
/// <param name="ShowsZoomKeys">The Fit and 100% keys.</param>
/// <param name="ShowsSteps">The steps field.</param>
/// <param name="ShowsPlaying">The Playing pill.</param>
/// <param name="ShowsCounts">The counts at the left of the status row.</param>
/// <param name="ShowsHint">The hint at the right of the status row.</param>
/// <param name="ShowsEvents">The strip of what the wires just did.</param>
public readonly record struct LogicViewFit(
    bool ShowsLabels,
    bool ShowsTick,
    bool UsesShortNames,
    bool ShowsFilter,
    bool ShowsZoomKeys,
    bool ShowsSteps,
    bool ShowsPlaying,
    bool ShowsCounts,
    bool ShowsHint,
    bool ShowsEvents)
{
    /// <summary>The narrowest the filter box is still worth showing at.</summary>
    public const double LeastFilterWidth = 72;

    /// <summary>The widest the filter box gets.</summary>
    public const double MostFilterWidth = 220;

    /// <summary>The narrowest the filter box still has room for its whole placeholder.</summary>
    public const double LongPlaceholderWidth = 150;

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

    // The status row: its padding, the two counts, the link, the hint, and
    // the least of a sentence that is worth reading.
    private const double StatusPadding = 20;
    private const double Counts = 122;
    private const double Link = 128;
    private const double Hint = 280;
    private const double LeastNote = 240;

    /// <summary>Everything, as in a wide view.</summary>
    public static LogicViewFit Everything { get; } = new(true, true, false, true, true, true, true, true, true, true);

    /// <summary>Decides what a view of a size shows.</summary>
    /// <param name="size">The size of the whole view.</param>
    /// <param name="isPlaying">Whether a level runs, which adds the pill, the tick and the strip.</param>
    /// <param name="status">What the status row has to say.</param>
    public static LogicViewFit For(Size size, bool isPlaying, LogicStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        bool hasLink = status.GoingNowhere.Length > 0;
        bool hasNote = status.Unwired.Length > 0 || status.Truncated.Length > 0;
        double said = StatusPadding + Counts + (hasLink ? Link : 0);

        LogicViewFit fit = Everything with
        {
            ShowsTick = isPlaying,
            ShowsPlaying = isPlaying,
            ShowsCounts = !hasLink || size.Width >= said,
            ShowsHint = size.Width >= said + Hint + (hasNote ? LeastNote : 0),
            ShowsEvents = isPlaying && size.Height >= Rows + EventStrip + LeastGraphHeight,
        };

        // Each step gives up the next thing, until what is left fits.
        if (fit.Fits(size.Width)) return fit;
        fit = fit with { ShowsLabels = false };
        if (fit.Fits(size.Width)) return fit;
        fit = fit with { ShowsTick = false };
        if (fit.Fits(size.Width)) return fit;
        fit = fit with { UsesShortNames = true };
        if (fit.Fits(size.Width)) return fit;
        fit = fit with { ShowsFilter = false };
        if (fit.Fits(size.Width)) return fit;
        fit = fit with { ShowsZoomKeys = false };
        if (fit.Fits(size.Width)) return fit;
        fit = fit with { ShowsSteps = false };
        if (fit.Fits(size.Width)) return fit;

        return fit with { ShowsPlaying = false };
    }

    /// <summary>How wide the toolbar is without its filter box.</summary>
    public double ToolbarWidth =>
        Padding
        + (ShowsLabels ? ShowLabel + StepsLabel : 0)
        + (UsesShortNames ? ShortNames : LongNames)
        + (ShowsSteps ? Steps : 0)
        + (ShowsPlaying ? Playing : 0)
        + (ShowsTick ? Tick : 0)
        + (ShowsZoomKeys ? ZoomKeys : 0);

    /// <summary>How wide the filter box is in a view of a width. Zero when it is not shown.</summary>
    public double FilterWidth(double width) =>
        ShowsFilter ? Math.Clamp(width - ToolbarWidth - FilterGap, 0, MostFilterWidth) : 0;

    private bool Fits(double width) =>
        ToolbarWidth + (ShowsFilter ? FilterGap + LeastFilterWidth : 0) <= width;
}
