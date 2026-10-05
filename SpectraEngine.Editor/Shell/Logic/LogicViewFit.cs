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

    /// <summary>The least height the graph keeps before the event strip gives way.</summary>
    public const double LeastGraphHeight = 200;

    // The toolbar and the status row, which are always there, and the event
    // strip. LogicViewFitTests holds them to the theme's heights.
    internal const double Rows = 62;
    internal const double EventStrip = 64;

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

    // The status row's padding, and the gap that sets one of its parts off
    // from the next. The parts themselves are measured: they are sentences
    // with names and counts in them.
    private const double StatusPadding = 20;
    private const double StatusGap = 14;

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

    /// <summary>Whether the sentence about entities with no wires is the short one.</summary>
    public bool UsesShortNotes { get; init; }

    /// <summary>The hint at the right of the status row.</summary>
    public bool ShowsHint { get; init; }

    /// <summary>Whether the hint is cut down to its first sentence.</summary>
    public bool UsesShortHint { get; init; }

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
    /// <param name="ruler">Measures the status row's sentences.</param>
    public static LogicViewFit For(Size size, bool isPlaying, LogicStatus status, ILogicTextMeasure ruler)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(ruler);

        LogicViewFit fit = Everything with
        {
            ShowsTick = isPlaying,
            ShowsPlaying = isPlaying,
            ShowsEvents = isPlaying && size.Height >= Rows + EventStrip + LeastGraphHeight,
        };

        return fit.WithStatus(size.Width, status, isPlaying, ruler).WithToolbar(size.Width);
    }

    /// <summary>How wide the filter box is in a view of a width. Zero when it is not shown.</summary>
    public double FilterWidth(double width) =>
        ShowsFilter ? Math.Clamp(width - ToolbarWidth - FilterGap, 0, MostFilterWidth) : 0;

    // The link and what the last edit did are the row's news and stay. The
    // counts give way to them. A sentence shows whole or not at all, the
    // short one where the long one has no room, and none beside an edit's
    // news. The hint comes last, and loses its second sentence first.
    private LogicViewFit WithStatus(double width, LogicStatus status, bool isPlaying, ILogicTextMeasure ruler)
    {
        double news = Part(status.GoingNowhere, ruler) + Part(status.News, ruler);
        double counts = Part(status.Entities, ruler) + Part(status.Wires, ruler);
        double left = width - StatusPadding - news;

        bool showsCounts = news == 0 || left >= counts;
        left -= showsCounts ? counts : 0;

        bool hasNotes = status.News.Length == 0;
        double truncated = Part(status.Truncated, ruler);
        double longNotes = truncated + Part(status.Unwired, ruler);
        double shortNotes = truncated + Part(status.UnwiredShort, ruler);

        bool showsLong = hasNotes && longNotes > 0 && left >= longNotes;
        bool showsShort = hasNotes && !showsLong && status.UnwiredShort.Length > 0 && left >= shortNotes;
        left -= showsLong ? longNotes : showsShort ? shortNotes : 0;

        string hint = isPlaying ? LogicViewText.PlayingHint : LogicViewText.EditingHint;
        bool showsWhole = left >= Part(hint, ruler);
        bool showsShortHint = !showsWhole && !isPlaying && left >= Part(LogicViewText.EditingHintShort, ruler);

        return this with
        {
            ShowsCounts = showsCounts,
            ShowsNotes = showsLong || showsShort,
            UsesShortNotes = showsShort,
            ShowsHint = showsWhole || showsShortHint,
            UsesShortHint = showsShortHint,
        };
    }

    // What a part of the status row takes with the gap that sets it off.
    // Nothing when it has nothing to say.
    private static double Part(string text, ILogicTextMeasure ruler) =>
        text.Length == 0 ? 0 : Math.Ceiling(ruler.Width(text, LogicTextStyle.Status)) + StatusGap;

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
