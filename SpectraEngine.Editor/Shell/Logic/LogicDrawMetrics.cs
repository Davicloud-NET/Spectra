namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// Where things stand inside a card, and how the view answers the pointer.
/// Where the cards stand is <see cref="LogicMetrics"/>. How thick, round and
/// large things are drawn is in the theme, under the <c>SpectraLogic</c> keys.
/// </summary>
public static class LogicDrawMetrics
{
    /// <summary>The room left and right of a card's header and state row.</summary>
    public const double HeaderPadding = 9;

    /// <summary>The room left and right of a port's name and of the note.</summary>
    public const double RowPadding = 12;

    /// <summary>The box the icon in a card's header is drawn on.</summary>
    public const double IconSize = 16;

    /// <summary>The room between the icon and the name.</summary>
    public const double IconGap = 8;

    /// <summary>How far under a card's top the middle of its name is.</summary>
    public const double NameLine = 13.5;

    /// <summary>How far under a card's top the middle of its class line is.</summary>
    public const double ClassLine = 28.5;

    /// <summary>The room between a state row's label and its value.</summary>
    public const double StateGap = 6;

    /// <summary>The least width a wire is drawn at on screen, however far out the view is.</summary>
    public const double LeastWireOnScreen = 1;

    /// <summary>The closest two grid dots come on screen. Further out every other one is left out.</summary>
    public const double LeastGridStepOnScreen = 14;

    /// <summary>Under this zoom port names, notes, state and labels are left out.</summary>
    public const double CompactZoom = 0.45;

    /// <summary>Under this zoom a card is a plain box with its name.</summary>
    public const double FarZoom = 0.25;

    /// <summary>How much one notch of the wheel zooms.</summary>
    public const double WheelZoom = 1.15;

    /// <summary>How far from a wire, on screen, the pointer still picks it.</summary>
    public const double PickReach = 5;

    /// <summary>How far the pointer may travel between press and release and still click.</summary>
    public const double ClickSlop = 4;

    internal static LogicDetail DetailAt(double zoom) =>
        zoom < FarZoom ? LogicDetail.Far
        : zoom < CompactZoom ? LogicDetail.Compact
        : LogicDetail.Full;
}
