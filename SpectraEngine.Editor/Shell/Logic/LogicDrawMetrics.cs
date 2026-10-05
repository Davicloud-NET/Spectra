namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// The sizes the Logic view's graph is drawn with, in pixels at 100 percent.
/// Where things stand is <see cref="LogicMetrics"/>.
/// </summary>
public static class LogicDrawMetrics
{
    /// <summary>The radius of a card's corners.</summary>
    public const double CardRadius = 6;

    /// <summary>The room left and right of a card's header and state row.</summary>
    public const double HeaderPadding = 9;

    /// <summary>The room left and right of a port's name and of the note.</summary>
    public const double RowPadding = 12;

    /// <summary>How large the icon in a card's header is.</summary>
    public const double IconSize = 16;

    /// <summary>The room between the icon and the name.</summary>
    public const double IconGap = 8;

    /// <summary>How thick an icon's lines are.</summary>
    public const double IconStroke = 1.3;

    /// <summary>How far under a card's top the middle of its name is.</summary>
    public const double NameLine = 13.5;

    /// <summary>How far under a card's top the middle of its class line is.</summary>
    public const double ClassLine = 28.5;

    /// <summary>The room between a state row's label and its value.</summary>
    public const double StateGap = 6;

    /// <summary>How thick the ring round a selected card is.</summary>
    public const double SelectedEdge = 2;

    /// <summary>The length of a dash and of a gap on the edge of a card nothing is named.</summary>
    public const double StubDash = 4;

    /// <summary>The radius of the dot at each end of a wire.</summary>
    public const double DotRadius = 4.5;

    /// <summary>The radius of the dot that travels along a waiting wire.</summary>
    public const double TravelDotRadius = 5.5;

    /// <summary>How thick that dot's edge is.</summary>
    public const double TravelDotEdge = 2;

    /// <summary>How thick a wire nothing is to be said about is.</summary>
    public const double PlainWire = 1.6;

    /// <summary>How thick a wire that touches the selection, or waits, is.</summary>
    public const double FocusWire = 2;

    /// <summary>How thick a wire that fired a moment ago is.</summary>
    public const double FiringWire = 2.6;

    /// <summary>How thick a wire that fired earlier, or is broken, is.</summary>
    public const double FiredWire = 1.8;

    /// <summary>The length of a dash on a broken wire.</summary>
    public const double BrokenDash = 6;

    /// <summary>The length of a gap on a broken wire.</summary>
    public const double BrokenGap = 5;

    /// <summary>The length of a dash on a waiting wire. With round ends it reads as a dot.</summary>
    public const double WaitingDash = 2;

    /// <summary>The length of a gap on a waiting wire.</summary>
    public const double WaitingGap = 6;

    /// <summary>How much wider than a wire the glow under a hovered one is.</summary>
    public const double HoverGlow = 5;

    /// <summary>The least width a wire is drawn at on screen, however far out the view is.</summary>
    public const double LeastWireOnScreen = 1;

    /// <summary>The distance between two dots of the ground's grid.</summary>
    public const double GridStep = 24;

    /// <summary>How wide one of those dots is.</summary>
    public const double GridDot = 2;

    /// <summary>The closest two grid dots come on screen. Further out every other one is left out.</summary>
    public const double LeastGridStepOnScreen = 14;

    /// <summary>Under this zoom port names, notes, state and labels are left out.</summary>
    public const double CompactZoom = 0.45;

    /// <summary>Under this zoom a card is a plain box with its name.</summary>
    public const double FarZoom = 0.25;

    /// <summary>The size of the name on such a box.</summary>
    public const double FarNameSize = 26;

    /// <summary>How much one notch of the wheel zooms.</summary>
    public const double WheelZoom = 1.15;

    /// <summary>How far from a wire, on screen, the pointer still picks it.</summary>
    public const double PickReach = 5;

    /// <summary>How far the pointer may travel between press and release and still click.</summary>
    public const double ClickSlop = 4;
}
