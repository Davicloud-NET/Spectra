using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Icons;

/// <summary>Reads an icon file into the glyph the 3D view draws.</summary>
// Only what an icon file has is read: one viewBox and paths that carry a d.
// The build that makes the shell's resources reads the same two things.
public static class ViewportGlyphFile
{
    // The icon set's stroke on its 16 box, as a part of the box. A path too
    // short to see stands for a round dot that wide.
    private const float DotWidth = 1.25f / 16f;
    private const float DotExtent = 0.02f;

    /// <summary>
    /// The glyph of an icon file, scaled to a unit box. Null when the text is
    /// not an icon this can draw.
    /// </summary>
    /// <param name="flatness">
    /// How far a segment may stray from its curve, as a part of the box.
    /// </param>
    public static ViewportGlyph? Read(string svg, float flatness)
    {
        ArgumentNullException.ThrowIfNull(svg);

        if (!TryReadBox(svg, out Vector2 corner, out Vector2 size))
            return null;

        float longest = MathF.Max(size.X, size.Y);
        var points = new List<Vector2>();
        var starts = new List<int>();

        try
        {
            int at = 0;
            while (TryNextPath(svg, ref at, out ReadOnlySpan<char> data))
                SvgPathFlattener.Flatten(data, flatness * longest, points, starts);
        }
        catch (FormatException)
        {
            // A file with bad path data has no glyph. The caller draws its plain marker.
            return null;
        }

        if (starts.Count == 0)
            return null;

        // Centred, so a box that is not square keeps its shape.
        Vector2 margin = (new Vector2(longest) - size) / (2f * longest);
        for (int i = 0; i < points.Count; i++)
            points[i] = ((points[i] - corner) / longest) + margin;

        WidenDots(points, starts);
        return new ViewportGlyph(points, starts);
    }

    private static bool TryReadBox(string svg, out Vector2 corner, out Vector2 size)
    {
        corner = size = default;

        if (!TryReadAttribute(svg, 0, svg.Length, "viewBox", out ReadOnlySpan<char> text))
            return false;

        try
        {
            var reader = new SvgPathReader(text);
            corner = reader.ReadPoint();
            size = reader.ReadPoint();
        }
        catch (FormatException)
        {
            return false;
        }

        return size.X > 0f && size.Y > 0f;
    }

    private static bool TryNextPath(string svg, ref int at, out ReadOnlySpan<char> data)
    {
        data = default;

        while (true)
        {
            int open = svg.IndexOf("<path", at, StringComparison.Ordinal);
            if (open < 0)
                return false;

            int close = svg.IndexOf('>', open);
            if (close < 0)
                return false;

            at = close + 1;
            if (TryReadAttribute(svg, open, close, "d", out data))
                return true;
        }
    }

    // The value of name="..." between two positions. The name has to follow
    // white space, or "d" would be found at the end of "id".
    private static bool TryReadAttribute(string svg, int from, int to, string name, out ReadOnlySpan<char> value)
    {
        value = default;
        string opening = name + "=\"";

        for (int at = svg.IndexOf(opening, from, StringComparison.Ordinal);
             at >= 0 && at < to;
             at = svg.IndexOf(opening, at + 1, StringComparison.Ordinal))
        {
            if (at == 0 || !char.IsWhiteSpace(svg[at - 1]))
                continue;

            int start = at + opening.Length;
            int end = svg.IndexOf('"', start);
            if (end < 0)
                return false;

            value = svg.AsSpan(start, end - start);
            return true;
        }

        return false;
    }

    // Tabler draws a dot as a line a hair long with a round end. Hairlines
    // have no round end, so the dot becomes a dash as long as the stroke is wide.
    private static void WidenDots(List<Vector2> points, List<int> starts)
    {
        for (int stroke = 0; stroke < starts.Count; stroke++)
        {
            int first = starts[stroke];
            int end = stroke + 1 < starts.Count ? starts[stroke + 1] : points.Count;
            if (end - first != 2)
                continue;

            Vector2 middle = (points[first] + points[first + 1]) * 0.5f;
            if (Vector2.Distance(points[first], points[first + 1]) > DotExtent)
                continue;

            points[first] = middle - new Vector2(DotWidth * 0.5f, 0f);
            points[first + 1] = middle + new Vector2(DotWidth * 0.5f, 0f);
        }
    }
}
