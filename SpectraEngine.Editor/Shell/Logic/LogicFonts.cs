using Avalonia.Media;

namespace SpectraEngine.Editor.Shell.Logic;

// The typefaces and sizes the graph's text is set in, read from the theme.
// The ruler and the canvas share them, so what is measured is what is drawn.
internal sealed class LogicFonts
{
    private LogicFonts(FontFamily sans, FontFamily mono)
    {
        Sans = new Typeface(sans);
        SansSemiBold = new Typeface(sans, FontStyle.Normal, FontWeight.SemiBold);
        Mono = new Typeface(mono);
    }

    public Typeface Sans { get; }

    public Typeface SansSemiBold { get; }

    public Typeface Mono { get; }

    // The size of a name or a port.
    public double Regular { get; } = LogicTheme.FontSize("SpectraFontBase");

    // The size of a class line, a note, a state row or a label.
    public double Small { get; } = LogicTheme.FontSize("SpectraFontSmall");

    // The size of the name on a card seen from far out.
    public double Far { get; } = LogicTheme.FontSize("SpectraLogicFarNameSize");

    public static LogicFonts FromTheme() => new(
        LogicTheme.Font("SpectraFontUi"),
        LogicTheme.Font("SpectraFontMono"));
}
