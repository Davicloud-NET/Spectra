using System;
using System.Numerics;
using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Editing.Viewport;

// Shows where the node of a moved icon is: a small mark on the node, and a
// line to it from the icon's rim. Both are hairlines with no backing, in part
// of the icon's colour, and neither is drawn where another icon stands over
// it, as if the icons were solid. A line through the middle of a neighbour
// would read as one of its strokes.
internal static class IconHome
{
    private const float MarkPixels = 2f;

    // More icons than this over one line are not told apart.
    private const int MostCovers = 16;

    // Draws the line home and the mark of the icon at this place among all of them.
    public static void Draw(DebugDraw output, in IconPen pen, SceneIcons icons, int place)
    {
        ref readonly SceneIcon icon = ref icons.All[place];
        float distance = Vector3.Distance(icon.At, icon.Anchor);
        if (distance <= 0f)
            return;

        Vector3 toward = (icon.Anchor - icon.At) / distance;
        Vector3 color = pen.Color * LightOverlay.HomeLineDim;
        float mark = MarkPixels * icon.Pixel;

        Span<Vector2> covers = stackalloc Vector2[MostCovers];
        int count = FindCovers(icons, place, covers, out float clear);

        // The mark shrinks away as a rim passes over the node, so it does
        // not appear from nothing.
        float shown = Math.Clamp(1f + (clear / mark), 0f, 1f);
        if (shown > 0f)
            pen.ThinDiamond(output, icon.Anchor, mark * shown, color);

        float from = icon.Radius;
        for (int i = 0; i < count && from < distance; i++)
        {
            if (covers[i].X > from)
                output.Line(icon.At + (toward * from), icon.At + (toward * MathF.Min(covers[i].X, distance)), color);

            from = MathF.Max(from, covers[i].Y);
        }

        if (from < distance)
            output.Line(icon.At + (toward * from), icon.Anchor, color);
    }

    // The stretches of the line home that other icons stand over, nearest
    // first, each from where it starts to where it ends as distances from the
    // icon's centre. Also how far the node is clear of the nearest rim, the
    // icon's own among them, which is right while it is less than the mark's
    // size.
    private static int FindCovers(SceneIcons icons, int place, Span<Vector2> covers, out float clear)
    {
        ReadOnlySpan<SceneIcon> all = icons.All;
        ref readonly SceneIcon icon = ref all[place];
        float distance = Vector3.Distance(icon.At, icon.Anchor);
        Vector3 toward = (icon.Anchor - icon.At) / distance;
        float mark = MarkPixels * icon.Pixel;
        int count = 0;
        clear = MathF.Min(mark, distance - icon.Radius);

        // No icon's rim is further from its centre than the largest icon's.
        foreach (int beside in icons.BesideLineHome(place, LightOverlay.IconPixels + MarkPixels))
        {
            ref readonly SceneIcon other = ref all[beside];
            Vector3 to = other.At - icon.At;
            float reach = distance + other.Radius + mark;

            // Too far off to be over any of it.
            if (beside == place || to.LengthSquared() >= reach * reach)
                continue;

            clear = MathF.Min(clear, Vector3.Distance(other.At, icon.Anchor) - other.Radius);

            float along = Vector3.Dot(to, toward);
            float halfChordSquared = (other.Radius * other.Radius) - (to.LengthSquared() - (along * along));
            if (halfChordSquared <= 0f || count == covers.Length)
                continue;

            float halfChord = MathF.Sqrt(halfChordSquared);
            if (along + halfChord <= icon.Radius || along - halfChord >= distance)
                continue;

            int slot = count++;
            for (; slot > 0 && covers[slot - 1].X > along - halfChord; slot--)
                covers[slot] = covers[slot - 1];

            covers[slot] = new Vector2(along - halfChord, along + halfChord);
        }

        return count;
    }
}
