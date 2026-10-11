using System;
using System.Numerics;
using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Editing.Viewport;

// The arrow that says which way the thing under a glyph faces. It starts
// clear of the glyph, and is not drawn under another icon, where it would
// read as one of that icon's strokes.
internal static class IconArrow
{
    private const float Start = 1.3f;
    private const float End = 2.6f;

    // An arrow that points at the camera is shorter than this on screen for
    // its length, and is drawn whole.
    private const float FlatEnough = 0.05f;

    // Draws the arrow of the icon at this place among all of them, in the
    // pass the pen is in.
    public static void Draw(DebugDraw output, in IconPen pen, SceneIcons icons, int place, Vector3 direction)
    {
        float end = pen.Radius * End;
        Vector3 tip = pen.At + (direction * pen.Radius * End);

        // How long the arrow is on screen for its length in the world.
        var across = new Vector2(Vector3.Dot(direction, pen.Right), Vector3.Dot(direction, pen.Up));
        float flat = across.Length();

        Span<Vector2> covers = stackalloc Vector2[IconCovers.Most];
        int count = 0;
        float tipClear = float.PositiveInfinity;

        if (flat > FlatEnough)
        {
            Vector2 on = icons.OnScreen(pen.At, pen.Pixel);
            count = IconCovers.Find(icons, place, on, on + (across * (end / pen.Pixel)), covers, out tipClear);
        }

        // From pixels along the screen to the world along the arrow.
        float scale = pen.Pixel / MathF.Max(flat, FlatEnough);
        float drawn = pen.Radius * Start;
        Vector3 from = pen.At + (direction * pen.Radius * Start);

        for (int i = 0; i < count && drawn < end; i++)
        {
            float start = covers[i].X * scale;
            if (start > drawn)
                pen.Stroke(output, from, pen.At + (direction * MathF.Min(start, end)));

            if (covers[i].Y * scale > drawn)
            {
                drawn = covers[i].Y * scale;
                from = pen.At + (direction * drawn);
            }
        }

        if (drawn < end)
            pen.Stroke(output, from, tip);

        if (tipClear <= 0f)
            return;

        // Across the screen, so the barbs show whichever way the arrow points.
        Vector3 beside = Vector3.Cross(direction, pen.Toward);
        beside = beside.LengthSquared() > 1e-6f ? Vector3.Normalize(beside) : pen.Right;

        Vector3 barb = beside * (pen.Radius * 0.28f);
        Vector3 neck = tip - (direction * pen.Radius * 0.5f);
        pen.Stroke(output, tip, neck + barb);
        pen.Stroke(output, tip, neck - barb);
    }
}
