using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// The glyph in a card's header and the pen that tints it. Which icon a card
// wears is the registry's answer for its class, the one the scene tree and
// the menus give. This only keeps the theme's geometry and a pen for each.
internal sealed class LogicIcons
{
    private readonly Dictionary<string, Geometry?> _glyphs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IPen> _pens = new(StringComparer.Ordinal);
    private readonly double _stroke = LogicTheme.Size("SpectraLogicIconStroke");

    // The glyph on a 16 box, and the pen that tints it.
    public Geometry? Of(LogicCard card, out IPen pen)
    {
        IconLook look = card.Look;

        if (!_pens.TryGetValue(look.Tint, out IPen? found))
        {
            found = new ImmutablePen(LogicTheme.Brush(look.Tint), _stroke, null, PenLineCap.Round, PenLineJoin.Round);
            _pens.Add(look.Tint, found);
        }

        if (!_glyphs.TryGetValue(look.Icon, out Geometry? glyph))
        {
            glyph = LogicTheme.Icon(look.Icon);
            _glyphs.Add(look.Icon, glyph);
        }

        pen = found;
        return glyph;
    }
}
