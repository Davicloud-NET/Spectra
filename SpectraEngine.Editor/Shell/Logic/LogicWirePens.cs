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
    private readonly IImmutableBrush _glow;
    private readonly ImmutablePen[] _pens = new ImmutablePen[Looks];
    private readonly ImmutablePen[] _glows = new ImmutablePen[Looks];
    private double _least = double.NaN;

    public LogicWirePens()
    {
        IImmutableBrush accent = LogicTheme.Brush("SpectraAccent");

        _brushes[(int)LogicWireLook.Plain] = LogicTheme.Brush("SpectraLogicWire");
        _brushes[(int)LogicWireLook.Focus] = accent;
        _brushes[(int)LogicWireLook.Broken] = LogicTheme.Brush("SpectraTextDanger");
        _brushes[(int)LogicWireLook.Firing] = LogicTheme.Brush("SpectraLogicWireFiring");
        _brushes[(int)LogicWireLook.Fired] = LogicTheme.Brush("SpectraLogicWireFired");
        _brushes[(int)LogicWireLook.Waiting] = accent;
        _glow = LogicTheme.Brush("SpectraControlPressed");

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
        if (least <= LogicDrawMetrics.PlainWire)
            least = 0;

        if (least == _least)
            return;

        _least = least;
        for (int look = 0; look < Looks; look++)
        {
            double width = Math.Max(Width((LogicWireLook)look), least);

            _pens[look] = new ImmutablePen(
                _brushes[look], width, Dashes((LogicWireLook)look, width), PenLineCap.Round, PenLineJoin.Round);
            _glows[look] = new ImmutablePen(
                _glow, width + LogicDrawMetrics.HoverGlow, null, PenLineCap.Round, PenLineJoin.Round);
        }
    }

    private static double Width(LogicWireLook look) => look switch
    {
        LogicWireLook.Focus or LogicWireLook.Waiting => LogicDrawMetrics.FocusWire,
        LogicWireLook.Firing => LogicDrawMetrics.FiringWire,
        LogicWireLook.Fired or LogicWireLook.Broken => LogicDrawMetrics.FiredWire,
        _ => LogicDrawMetrics.PlainWire,
    };

    // A dash style counts in pen widths.
    private static ImmutableDashStyle? Dashes(LogicWireLook look, double width) => look switch
    {
        LogicWireLook.Broken => new ImmutableDashStyle(
            [LogicDrawMetrics.BrokenDash / width, LogicDrawMetrics.BrokenGap / width], 0),
        LogicWireLook.Waiting => new ImmutableDashStyle(
            [LogicDrawMetrics.WaitingDash / width, LogicDrawMetrics.WaitingGap / width], 0),
        _ => null,
    };
}
