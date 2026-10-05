using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace SpectraEngine.Editor.Shell.Logic;

// What the graph is drawn with, read from the theme once: brushes, pens,
// typefaces and sizes. All of it is immutable, so drawing a frame makes none.
internal sealed class LogicPalette
{
    private readonly IImmutableBrush _emphasis = LogicTheme.Brush("SpectraTextEmphasis");
    private readonly IImmutableBrush _body = LogicTheme.Brush("SpectraTextBody");
    private readonly IImmutableBrush _muted = LogicTheme.Brush("SpectraTextMuted");
    private readonly IImmutableBrush _danger = LogicTheme.Brush("SpectraTextDanger");
    private readonly IImmutableBrush _stubText = LogicTheme.Brush("SpectraLogicStubText");
    private readonly IImmutableBrush _onAccent = LogicTheme.Brush("SpectraTextOnAccent");
    private readonly IImmutableBrush _gridDot = LogicTheme.Brush("SpectraLogicGridDot");
    private ImmutablePen? _gridPen;
    private double _gridPenStep;

    public LogicPalette()
    {
        IImmutableBrush seam = LogicTheme.Brush("SpectraBorderControl");
        IImmutableBrush accent = LogicTheme.Brush("SpectraAccent");
        var dashes = new ImmutableDashStyle([LogicTheme.Size("SpectraLogicStubDash"), LogicTheme.Size("SpectraLogicStubDash")], 0);

        CardEdge = new ImmutablePen(seam);
        HoveredEdge = new ImmutablePen(LogicTheme.Brush("SpectraBorderInputHover"));
        SelectedEdge = new ImmutablePen(accent, LogicTheme.Size("SpectraLogicSelectedEdge"));
        StubEdge = new ImmutablePen(_danger, 1, dashes);
        QuietEdge = new ImmutablePen(LogicTheme.Brush("SpectraBorderInput"), 1, dashes);
        StateRule = new ImmutablePen(CardHead);

        LabelEdge = CardEdge;
        LitLabelEdge = new ImmutablePen(accent);
        BrokenLabelEdge = new ImmutablePen(LogicTheme.Brush("SpectraLogicStubEdge"));
        TravelEdge = new ImmutablePen(accent, LogicTheme.Size("SpectraLogicTravelDotEdge"));
    }

    public LogicFonts Fonts { get; } = LogicFonts.FromTheme();

    public LogicWirePens Wires { get; } = new();

    public LogicIcons Icons { get; } = new();

    public IImmutableBrush Ground { get; } = LogicTheme.Brush("SpectraLogicGround");

    public IImmutableBrush Card { get; } = LogicTheme.Brush("SpectraLogicCard");

    public IImmutableBrush CardHead { get; } = LogicTheme.Brush("SpectraLogicCardHead");

    public IImmutableBrush Stub { get; } = LogicTheme.Brush("SpectraLogicStub");

    public IImmutableBrush StubHead { get; } = LogicTheme.Brush("SpectraLogicStubHead");

    // A selected card seen from far out, where a ring would not show.
    public IImmutableBrush SelectedFill { get; } = LogicTheme.Brush("SpectraAccentRest");

    public IImmutableBrush HoverWash { get; } = LogicTheme.Brush("SpectraRowHover");

    // Laid over the card a dragged wire would land on.
    public IImmutableBrush TargetWash { get; } = LogicTheme.Brush("SpectraLogicTargetWash");

    public IImmutableBrush LabelFill { get; } = LogicTheme.Brush("SpectraBgPanel");

    public IImmutableBrush TravelFill => _emphasis;

    // Laid over what the filter leaves out. Not an opacity pushed on the
    // drawing: that thins each shape by itself, and a wire then shows
    // through the card it runs behind.
    public IImmutableBrush DimWash { get; } = LogicTheme.Brush("SpectraLogicDimWash");

    public double CardRadius { get; } = LogicTheme.Size("SpectraLogicCardRadius");

    public double DotRadius { get; } = LogicTheme.Size("SpectraLogicDotRadius");

    public double TravelDotRadius { get; } = LogicTheme.Size("SpectraLogicTravelDotRadius");

    public double GridStep { get; } = LogicTheme.Size("SpectraLogicGridStep");

    public double GridDot { get; } = LogicTheme.Size("SpectraLogicGridDotSize");

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
        LogicInk.Name or LogicInk.StubName or LogicInk.QuietName
            or LogicInk.FarName or LogicInk.FarSelectedName or LogicInk.FarStubName => Fonts.SansSemiBold,
        LogicInk.StateValue or LogicInk.MonoLabel => Fonts.Mono,
        _ => Fonts.Sans,
    };

    public double Size(LogicInk ink) => ink switch
    {
        LogicInk.Name or LogicInk.StubName or LogicInk.QuietName or LogicInk.Port or LogicInk.WrongPort => Fonts.Regular,
        LogicInk.FarName or LogicInk.FarSelectedName or LogicInk.FarStubName => Fonts.Far,
        _ => Fonts.Small,
    };

    public IImmutableBrush Color(LogicInk ink) => ink switch
    {
        LogicInk.Name or LogicInk.StateValue or LogicInk.LitLabel or LogicInk.FarName => _emphasis,
        LogicInk.StubName or LogicInk.WrongPort or LogicInk.BrokenLabel or LogicInk.FarStubName => _danger,
        LogicInk.StubLine => _stubText,
        LogicInk.ClassLine or LogicInk.Note or LogicInk.StateLabel => _muted,
        LogicInk.FarSelectedName => _onAccent,
        _ => _body,
    };

    // Draws a row of the ground's dots as one dashed line: dashes too short
    // to see, with round ends as wide as a dot. Made again only when the
    // distance between dots changes, which is when the zoom does.
    public IPen GridPen(double step)
    {
        if (_gridPen is not null && step == _gridPenStep)
            return _gridPen;

        // A dash style counts in pen widths.
        const double Dash = 0.01;

        _gridPenStep = step;
        return _gridPen = new ImmutablePen(
            _gridDot,
            GridDot,
            new ImmutableDashStyle([Dash, step / GridDot - Dash], 0),
            PenLineCap.Round);
    }
}
