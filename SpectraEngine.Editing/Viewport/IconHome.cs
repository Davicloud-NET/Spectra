using System;
using System.Numerics;
using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Editing.Viewport;

// Shows where the node of a moved icon is: a small mark on the node and a
// line to it from the icon's rim, in part of the icon's colour. Neither is
// drawn under another icon, where it would read as one of that icon's strokes.
internal static class IconHome
{
    private const float MarkPixels = 2f;

    // Draws the line home and the mark of the icon at this place among all of them.
    public static void Draw(DebugDraw output, in IconPen pen, SceneIcons icons, int place)
    {
        ref readonly SceneIcon icon = ref icons.All[place];
        float distance = Vector3.Distance(icon.At, icon.Anchor);
        if (distance <= 0f)
            return;

        Vector3 toward = (icon.Anchor - icon.At) / distance;
        Vector3 color = pen.Color * LightOverlay.HomeLineDim;

        Span<Vector2> covers = stackalloc Vector2[IconCovers.Most];
        int count = IconCovers.Find(
            icons, place, icons.OnScreen(icon.At, icon.Pixel), icons.OnScreen(icon.Anchor, icon.Pixel),
            covers, out float clear);

        // The mark shrinks away as a rim passes over the node, the icon's own
        // too, so it does not appear from nothing.
        clear = MathF.Min(clear, (distance - icon.Radius) / icon.Pixel);
        float shown = Math.Clamp(1f + (clear / MarkPixels), 0f, 1f);
        if (shown > 0f)
            pen.ThinDiamond(output, icon.Anchor, MarkPixels * icon.Pixel * shown, color);

        float from = icon.Radius;
        for (int i = 0; i < count && from < distance; i++)
        {
            float start = covers[i].X * icon.Pixel;
            if (start > from)
                output.Line(icon.At + (toward * from), icon.At + (toward * MathF.Min(start, distance)), color);

            from = MathF.Max(from, covers[i].Y * icon.Pixel);
        }

        if (from < distance)
            output.Line(icon.At + (toward * from), icon.Anchor, color);
    }
}
