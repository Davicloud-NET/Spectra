using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;

namespace SpectraEngine.Editor.Shell.Logic;

// The pens the wires are drawn with, one for each look. Far out they thicken,
// so a wire never thins to nothing.
internal sealed class LogicWirePens
{
    private static readonly int Looks = Enum.GetValues<LogicWireLook>().Length;

    private readonly IImmutableBrush[] _brushes = new IImmutableBrush[Looks];
    private readonly double[] _widths = new double[Looks];
    private readonly ImmutablePen[] _pens = new ImmutablePen[Looks];
    private readonly ImmutablePen[] _glows = new ImmutablePen[Looks];
    private readonly IImmutableBrush _glow = LogicTheme.Brush("SpectraControlPressed");
    private readonly double _glowWidth = LogicTheme.Size("SpectraLogicHoverGlow");
    private readonly double _brokenDash = LogicTheme.Size("SpectraLogicWireBrokenDash");
    private readonly double _brokenGap = LogicTheme.Size("SpectraLogicWireBrokenGap");
    private readonly double _waitingDash = LogicTheme.Size("SpectraLogicWireWaitingDash");
    private readonly double _waitingGap = LogicTheme.Size("SpectraLogicWireWaitingGap");
    private double _least = double.NaN;

    public LogicWirePens()
    {
        IImmutableBrush accent = LogicTheme.Brush("SpectraAccent");
        double focus = LogicTheme.Size("SpectraLogicWireFocusWidth");
        double fired = LogicTheme.Size("SpectraLogicWireFiredWidth");

        Set(LogicWireLook.Plain, LogicTheme.Brush("SpectraLogicWire"), LogicTheme.Size("SpectraLogicWireWidth"));
        Set(LogicWireLook.Focus, accent, focus);
        Set(LogicWireLook.Broken, LogicTheme.Brush("SpectraTextDanger"), fired);
        Set(LogicWireLook.Firing, LogicTheme.Brush("SpectraLogicWireFiring"), LogicTheme.Size("SpectraLogicWireFiringWidth"));
        Set(LogicWireLook.Fired, LogicTheme.Brush("SpectraLogicWireFired"), fired);
        Set(LogicWireLook.Waiting, accent, focus);

        SetZoom(1);
    }

    // The colour of a wire, for the dots at its ends.
    public IImmutableBrush Brush(LogicWireLook look) => _brushes[(int)look];

    public IPen Pen(LogicWireLook look) => _pens[(int)look];

    // The wide, faint stroke under a wire the pointer is on.
    public IPen Glow(LogicWireLook look) => _glows[(int)look];

    // Makes the pens again only when the zoom changes how thick a wire is.
    public void SetZoom(double zoom)
    {
        double least = LogicDrawMetrics.LeastWireOnScreen / zoom;
        if (least <= _widths[(int)LogicWireLook.Plain])
            least = 0;

        if (least == _least)
            return;

        _least = least;
        for (int look = 0; look < Looks; look++)
        {
            double width = Math.Max(_widths[look], least);

            _pens[look] = new ImmutablePen(
                _brushes[look], width, Dashes((LogicWireLook)look, width), PenLineCap.Round, PenLineJoin.Round);
            _glows[look] = new ImmutablePen(_glow, width + _glowWidth, null, PenLineCap.Round, PenLineJoin.Round);
        }
    }

    private void Set(LogicWireLook look, IImmutableBrush brush, double width)
    {
        _brushes[(int)look] = brush;
        _widths[(int)look] = width;
    }

    // A dash style counts in pen widths.
    private ImmutableDashStyle? Dashes(LogicWireLook look, double width) => look switch
    {
        LogicWireLook.Broken => new ImmutableDashStyle([_brokenDash / width, _brokenGap / width], 0),
        LogicWireLook.Waiting => new ImmutableDashStyle([_waitingDash / width, _waitingGap / width], 0),
        _ => null,
    };
}
