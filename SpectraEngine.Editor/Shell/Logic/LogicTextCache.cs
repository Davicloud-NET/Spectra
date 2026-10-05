using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpectraEngine.Editor.Shell.Logic;

// Text laid out once for each string, ink and room, and kept. A frame that
// draws what an earlier frame drew lays out nothing.
internal sealed class LogicTextCache
{
    private const int MostKept = 8192;

    // What a running level writes changes all the time, so it has a small
    // table of its own. Emptying it never costs the names and ports theirs.
    private const int MostRunningKept = 512;

    private readonly Dictionary<Key, FormattedText> _kept = [];
    private readonly Dictionary<Key, FormattedText> _running = [];
    private readonly LogicPalette _palette;

    public LogicTextCache(LogicPalette palette) => _palette = palette;

    // One line of text that stays as it is while the scene does: a name, a
    // class, a port, a note, an authored label. Cut short with an ellipsis
    // where it is wider than the room it has.
    public FormattedText Get(string text, LogicInk ink, double room) => Get(_kept, MostKept, text, ink, room);

    // The same for text a running level writes: a state line, a count.
    public FormattedText GetRunning(string text, LogicInk ink, double room) =>
        Get(_running, MostRunningKept, text, ink, room);

    private FormattedText Get(Dictionary<Key, FormattedText> table, int most, string text, LogicInk ink, double room)
    {
        // Rounded up: text that fits its room to the pixel must not be cut.
        var key = new Key(text, ink, Math.Max(1, (int)Math.Ceiling(room)));
        if (table.TryGetValue(key, out FormattedText? laidOut))
            return laidOut;

        if (table.Count >= most)
            table.Clear();

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

        return table[key] = laidOut;
    }

    private readonly record struct Key(string Text, LogicInk Ink, int Room);
}
