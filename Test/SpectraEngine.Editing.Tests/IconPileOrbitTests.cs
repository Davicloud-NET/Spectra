using System;
using System.Numerics;
using SpectraEngine.Editing.Icons;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Two entities near each other stay apart on screen as the camera goes
/// round them, whichever is in front.
/// </summary>
public sealed class IconPileOrbitTests
{
    private const int Steps = 3600;

    // Level, and looking down on them a little.
    [Theory]
    [InlineData(0f)]
    [InlineData(-0.3f)]
    public void Two_entities_side_by_side_are_never_drawn_over_each_other_on_the_way_round(float pitch)
    {
        // Three quarters of an icon's width apart when seen square on.
        var rig = new IconPileRig();
        rig.AddEntity(IconPileRig.Sound, new Vector3(-0.33f, 0f, 0f));
        rig.AddEntity(IconPileRig.Relay, new Vector3(0.33f, 0f, 0f));

        float least = float.PositiveInfinity;
        float largest = 0f;
        Span<Vector2> before = stackalloc Vector2[2];

        for (int step = 0; step <= Steps; step++)
        {
            rig.Harness.Orbit(Vector3.Zero, 20f, step * (MathF.Tau / Steps), pitch);
            ReadOnlySpan<SceneIcon> markers = rig.Place().Markers;
            float width = markers[0].Radius / markers[0].Pixel * ViewportGlyphs.Reach * 2f;

            for (int i = 0; i < 2; i++)
            {
                Vector2 on = rig.Harness.WorldToScreen(markers[i].At);
                if (step > 0)
                    largest = MathF.Max(largest, Vector2.Distance(on, before[i]));

                before[i] = on;
            }

            least = MathF.Min(least, Vector2.Distance(before[0], before[1]) / width);
        }

        // In widths. The two were on one point twice a turn.
        least.ShouldBeGreaterThan(0.95f);

        // In pixels a step, of a tenth of a degree.
        largest.ShouldBeLessThan(1f);
    }
}
