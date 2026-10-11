using System;

namespace SpectraEngine.Editing.Viewport;

// When more point entities are near enough for a glyph than may have one, the
// nearest keep it. One that is about to lose it takes less part in the fan
// first, so its pile has closed by the time the glyph goes.
internal sealed class NearestGlyphs
{
    // An icon takes full part once it is this much nearer than the nearest
    // one left out, as a share of that one's distance.
    private const float FadeBand = 0.1f;

    private float[] _nearness = [];

    public void Keep(Span<SceneIcon> markers, int most)
    {
        if (_nearness.Length < markers.Length)
            _nearness = new float[Math.Max(markers.Length, _nearness.Length * 2)];

        int detailed = 0;
        foreach (ref readonly SceneIcon marker in markers)
        {
            if (marker.IsGlyph)
                _nearness[detailed++] = marker.Pixel;
        }

        most = Math.Max(most, 0);
        if (detailed <= most)
            return;

        // A pixel covers more of the world the farther off it is, so the
        // smallest belong to the nearest.
        Array.Sort(_nearness, 0, detailed);
        float farthestKept = most > 0 ? _nearness[most - 1] : float.NegativeInfinity;
        float nearestLeftOut = _nearness[most];

        // Of several just as far off as the farthest kept, the first listed
        // keep theirs. One that is nearer is never left out for them.
        int alike = most;
        while (alike > 0 && _nearness[alike - 1] == farthestKept)
            alike--;

        alike = most - alike;

        foreach (ref SceneIcon marker in markers)
        {
            if (!marker.IsGlyph)
                continue;

            marker.IsGlyph = marker.Pixel < farthestKept || (marker.Pixel == farthestKept && alike-- > 0);

            float nearer = (nearestLeftOut - marker.Pixel) / (nearestLeftOut * FadeBand);
            marker.FanStrength = marker.IsGlyph ? MathF.Min(marker.FanStrength, Math.Clamp(nearer, 0f, 1f)) : 0f;
        }
    }
}
