using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpectraEngine.Editor.Shell.Logic;

// Text laid out once for each string, ink and room, and kept. A frame that
// draws what an earlier frame drew lays out nothing.
internal sealed class LogicTextCache
{
    // State lines change as a level runs, so the cache is emptied when full.
    private const int MostKept = 8192;

    private readonly Dictionary<Key, FormattedText> _kept = [];
    private readonly LogicPalette _palette;

    public LogicTextCache(LogicPalette palette) => _palette = palette;

    // One line of text, cut short with an ellipsis where it is wider than
    // the room it has.
    public FormattedText Get(string text, LogicInk ink, double room)
    {
        // Rounded up: text that fits its room to the pixel must not be cut.
        var key = new Key(text, ink, Math.Max(1, (int)Math.Ceiling(room)));
        if (_kept.TryGetValue(key, out FormattedText? laidOut))
            return laidOut;

        if (_kept.Count >= MostKept)
            _kept.Clear();

        laidOut = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _palette.Face(ink),
            _palette.Size(ink),
            _palette.Color(ink))
        {
            MaxTextWidth = key.Room,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        return _kept[key] = laidOut;
    }

    private readonly record struct Key(string Text, LogicInk Ink, int Room);
}
