using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;

namespace SpectraEngine.Editor.Shell.Logic;

// The brushes, pens and typefaces the graph is drawn with, read from the
// theme once. Everything is immutable, so drawing a frame makes none of them.
internal sealed class LogicPalette
{
    private readonly IImmutableBrush _emphasis = LogicTheme.Brush("SpectraTextEmphasis");
    private readonly IImmutableBrush _body = LogicTheme.Brush("SpectraTextBody");
    private readonly IImmutableBrush _muted = LogicTheme.Brush("SpectraTextMuted");
    private readonly IImmutableBrush _danger = LogicTheme.Brush("SpectraTextDanger");
    private readonly IImmutableBrush _stubText = LogicTheme.Brush("SpectraLogicStubText");
    private ImmutablePen? _gridPen;
    private double _gridStep;

    public LogicPalette()
    {
        IImmutableBrush seam = LogicTheme.Brush("SpectraBorderControl");
        IImmutableBrush accent = LogicTheme.Brush("SpectraAccent");
        double dash = LogicDrawMetrics.StubDash;

        CardEdge = new ImmutablePen(seam);
        HoveredEdge = new ImmutablePen(LogicTheme.Brush("SpectraBorderInputHover"));
        SelectedEdge = new ImmutablePen(accent, LogicDrawMetrics.SelectedEdge);
        StubEdge = new ImmutablePen(_danger, 1, new ImmutableDashStyle([dash, dash], 0));
        QuietEdge = new ImmutablePen(LogicTheme.Brush("SpectraBorderInput"), 1, new ImmutableDashStyle([dash, dash], 0));
        StateRule = new ImmutablePen(CardHead);

        LabelEdge = CardEdge;
        LitLabelEdge = new ImmutablePen(accent);
        BrokenLabelEdge = new ImmutablePen(LogicTheme.Brush("SpectraLogicStubEdge"));
        TravelEdge = new ImmutablePen(accent, LogicDrawMetrics.TravelDotEdge);
    }

    public LogicFonts Fonts { get; } = LogicFonts.FromTheme();

    public LogicWirePens Wires { get; } = new();

    public LogicIcons Icons { get; } = new();

    public IImmutableBrush Ground { get; } = LogicTheme.Brush("SpectraLogicGround");

    public IImmutableBrush Card { get; } = LogicTheme.Brush("SpectraLogicCard");

    public IImmutableBrush CardHead { get; } = LogicTheme.Brush("SpectraLogicCardHead");

    public IImmutableBrush Stub { get; } = LogicTheme.Brush("SpectraLogicStub");

    public IImmutableBrush StubHead { get; } = LogicTheme.Brush("SpectraLogicStubHead");

    // The fill of a selected card seen from far out, where a ring would not show.
    public IImmutableBrush SelectedFill { get; } = LogicTheme.Brush("SpectraAccentRest");

    public IImmutableBrush HoverWash { get; } = LogicTheme.Brush("SpectraRowHover");

    public IImmutableBrush LabelFill { get; } = LogicTheme.Brush("SpectraBgPanel");

    public IImmutableBrush TravelFill => _emphasis;

    public double DimOpacity { get; } = LogicTheme.Opacity("SpectraLogicDimOpacity");

    public IPen CardEdge { get; }

    public IPen HoveredEdge { get; }

    public IPen SelectedEdge { get; }

    public IPen StubEdge { get; }

    // The edge of the activator's card.
    public IPen QuietEdge { get; }

    public IPen StateRule { get; }

    public IPen LabelEdge { get; }

    public IPen LitLabelEdge { get; }

    public IPen BrokenLabelEdge { get; }

    public IPen TravelEdge { get; }

    public Typeface Face(LogicInk ink) => ink switch
    {
        LogicInk.Name or LogicInk.StubName or LogicInk.QuietName or LogicInk.FarName or LogicInk.FarStubName
            => Fonts.SansSemiBold,
        LogicInk.StateValue or LogicInk.MonoLabel => Fonts.Mono,
        _ => Fonts.Sans,
    };

    public double Size(LogicInk ink) => ink switch
    {
        LogicInk.Name or LogicInk.StubName or LogicInk.QuietName
            or LogicInk.Port or LogicInk.QuietPort or LogicInk.WrongPort => Fonts.Regular,
        LogicInk.FarName or LogicInk.FarStubName => LogicDrawMetrics.FarNameSize,
        _ => Fonts.Small,
    };

    public IImmutableBrush Color(LogicInk ink) => ink switch
    {
        LogicInk.Name or LogicInk.StateValue or LogicInk.LitLabel or LogicInk.FarName => _emphasis,
        LogicInk.StubName or LogicInk.WrongPort or LogicInk.BrokenLabel or LogicInk.FarStubName => _danger,
        LogicInk.StubLine => _stubText,
        LogicInk.ClassLine or LogicInk.QuietPort or LogicInk.Note or LogicInk.StateLabel => _muted,
        _ => _body,
    };

    // A pen that draws a row of the ground's dots as one dashed line: dashes
    // too short to see, with round ends as wide as a dot.
    public IPen GridPen(double step)
    {
        if (_gridPen is not null && step == _gridStep)
            return _gridPen;

        double dot = LogicDrawMetrics.GridDot;
        double dash = Math.Min(0.01, step / dot / 2);

        _gridStep = step;
        return _gridPen = new ImmutablePen(
            LogicTheme.Brush("SpectraLogicGridDot"),
            dot,
            new ImmutableDashStyle([dash, step / dot - dash], 0),
            PenLineCap.Round);
    }
}
