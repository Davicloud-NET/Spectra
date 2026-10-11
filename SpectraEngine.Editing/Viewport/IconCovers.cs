using System;
using System.Numerics;

namespace SpectraEngine.Editing.Viewport;

// The stretches of a straight line that other icons stand over, as if the
// icons were solid. Measured on screen, in pixels: icons on one screen point
// can be far apart in the world.
internal static class IconCovers
{
    // More icons than this over one line are not told apart.
    public const int Most = 16;

    // Fills covers with where each stretch starts and ends, as distances from
    // the line's start, nearest first. Also says how far the line's end is
    // clear of the nearest rim, which is less than nothing under an icon.
    // The icon at this place is the line's own and covers none of it.
    public static int Find(
        SceneIcons icons, int place, Vector2 from, Vector2 to, Span<Vector2> covers, out float endClear)
    {
        ReadOnlySpan<SceneIcon> all = icons.All;
        Vector2 line = to - from;
        float length = line.Length();
        Vector2 toward = length > 0f ? line / length : Vector2.UnitX;
        int count = 0;
        endClear = float.PositiveInfinity;

        // No icon's rim is further from its centre than the largest icon's.
        float reach = LightOverlay.IconPixels * 2f;

        foreach (int beside in icons.Between(MathF.Min(from.X, to.X) - reach, MathF.Max(from.X, to.X) + reach))
        {
            if (beside == place)
                continue;

            ref readonly SceneIcon other = ref all[beside];
            float radius = other.Radius / other.Pixel;
            Vector2 center = icons.OnScreen(other.At, other.Pixel) - from;
            endClear = MathF.Min(endClear, Vector2.Distance(center, line) - radius);

            float along = Vector2.Dot(center, toward);
            float halfChordSquared = (radius * radius) - (center.LengthSquared() - (along * along));
            if (halfChordSquared <= 0f || count == covers.Length)
                continue;

            float halfChord = MathF.Sqrt(halfChordSquared);
            if (along + halfChord <= 0f || along - halfChord >= length)
                continue;

            int slot = count++;
            for (; slot > 0 && covers[slot - 1].X > along - halfChord; slot--)
                covers[slot] = covers[slot - 1];

            covers[slot] = new Vector2(along - halfChord, along + halfChord);
        }

        return count;
    }
}
