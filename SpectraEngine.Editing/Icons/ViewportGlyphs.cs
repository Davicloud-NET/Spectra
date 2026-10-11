using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Editing.Icons;

/// <summary>
/// The glyph of every world icon file, read once when the first is asked for.
/// </summary>
public static class ViewportGlyphs
{
    /// <summary>
    /// How far a segment may stray from its curve, as a part of the icon's
    /// box. A glyph is at most about 31 pixels across, so this is a third of
    /// a pixel.
    /// </summary>
    public const float Flatness = 0.011f;

    /// <summary>
    /// How far a glyph's box reaches from the centre of the icon it is drawn
    /// for, in icon radii.
    /// </summary>
    // The files keep a margin inside their box, so the drawing itself stays
    // about inside the radius that is clicked.
    public const float Reach = 1.1f;

    private static readonly Dictionary<string, ViewportGlyph> ByKey = ReadAll();

    /// <summary>How many icon files have a glyph.</summary>
    public static int Count => ByKey.Count;

    /// <summary>The glyph of the icon file with this key.</summary>
    public static bool TryGet(string key, [NotNullWhen(true)] out ViewportGlyph? glyph) =>
        ByKey.TryGetValue(key, out glyph);

    private static Dictionary<string, ViewportGlyph> ReadAll()
    {
        var glyphs = new Dictionary<string, ViewportGlyph>(StringComparer.Ordinal);

        foreach (string key in WorldIconFiles.Keys)
        {
            if (ViewportGlyphFile.Read(WorldIconFiles.Read(key), Flatness) is { } glyph)
                glyphs.Add(key, glyph);
        }

        return glyphs;
    }
}
