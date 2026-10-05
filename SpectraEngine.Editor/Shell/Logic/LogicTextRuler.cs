using Avalonia.Media;
using System.Collections.Generic;
using System.Globalization;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>Measures a label's text in the fonts the canvas draws it in.</summary>
public sealed class LogicTextRuler : ILogicTextMeasure
{
    private const int MostRemembered = 4096;

    private readonly Dictionary<(string Text, LogicTextStyle Style), double> _widths = [];
    private LogicFonts? _fonts;

    /// <inheritdoc/>
    public double Width(string text, LogicTextStyle style)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        if (_widths.TryGetValue((text, style), out double width))
            return width;

        // Not in the constructor: a model may be made before the theme is loaded.
        _fonts ??= LogicFonts.FromTheme();

        var laidOut = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            style == LogicTextStyle.MonoLabel ? _fonts.Mono : _fonts.Sans,
            _fonts.Small,
            foreground: null);

        if (_widths.Count >= MostRemembered)
            _widths.Clear();

        return _widths[(text, style)] = laidOut.WidthIncludingTrailingWhitespace;
    }
}
