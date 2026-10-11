using System;

namespace SpectraEngine.Editing.Viewport;

// When more point entities are near enough for a glyph than may have one, the
// nearest keep it. One that is about to lose it takes less part in the fan
// first, so its pile has closed by the time the glyph goes.
internal sealed class NearestGlyphs
{
    // An icon takes full part once it is this much nearer than the one whose
    // coming nearer would cost it the glyph, as a share of that one's distance.
    private const float FadeBand = 0.1f;

    private float[] _nearness = [];
    private int[] _places = [];

    // A flat view has no nearest: every icon is as far off as the next, the
    // first listed keep their glyphs, and turning the camera changes nothing.
    public void Keep(Span<SceneIcon> markers, int most, bool isFlat)
    {
        if (_nearness.Length < markers.Length)
        {
            int room = Math.Max(markers.Length, _nearness.Length * 2);
            _nearness = new float[room];
            _places = new int[room];
        }

        int detailed = 0;
        for (int i = 0; i < markers.Length; i++)
        {
            if (!markers[i].IsGlyph)
                continue;

            _nearness[detailed] = markers[i].Pixel;
            _places[detailed++] = i;
        }

        most = Math.Max(most, 0);
        if (detailed <= most)
            return;

        // A pixel covers more of the world the farther off it is, so the
        // smallest belong to the nearest.
        Array.Sort(_nearness, _places, 0, detailed);

        for (int first = 0; first < detailed;)
        {
            int end = first + 1;
            while (end < detailed && _nearness[end] == _nearness[first])
                end++;

            // Of several just as far off, the first listed come first.
            if (end - first > 1)
                Array.Sort(_places, first, end - first);

            for (int rank = first; rank < end; rank++)
            {
                ref SceneIcon marker = ref markers[_places[rank]];
                marker.IsGlyph = rank < most;

                if (!marker.IsGlyph)
                    marker.FanStrength = 0f;
                else if (!isFlat)
                    marker.FanStrength = MathF.Min(marker.FanStrength, Part(markers, rank, end, most, detailed));
            }

            first = end;
        }
    }

    // How much part the icon at this place in the order takes, by how near
    // it is to losing its glyph. Its run of icons just as far off ends at end.
    private float Part(ReadOnlySpan<SceneIcon> markers, int rank, int end, int most, int detailed)
    {
        ref readonly SceneIcon marker = ref markers[_places[rank]];

        // The icons of its own point stay behind it whatever the camera does.
        int ownBehind = 0;
        for (int later = rank + 1; later < end; later++)
            ownBehind += markers[_places[later]].Anchor == marker.Anchor ? 1 : 0;

        // It loses the glyph when this one comes nearer than it is.
        int passer = most + ownBehind;
        if (passer >= detailed)
            return 1f;

        // One just as far off at another point is as good as nearer.
        if (passer < end)
            return 0f;

        return Math.Clamp((_nearness[passer] - marker.Pixel) / (_nearness[passer] * FadeBand), 0f, 1f);
    }
}
