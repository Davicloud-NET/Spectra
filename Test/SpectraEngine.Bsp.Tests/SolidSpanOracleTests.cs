using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The span query agrees with the compiled world's trees about where solid is,
/// over seeded random levels of overlapping additive and subtractive brushes.
/// </summary>
public sealed class SolidSpanOracleTests
{
    [Fact]
    public void Along_random_segments_a_point_is_inside_a_span_when_the_live_world_says_it_is_solid()
    {
        int compared = 0, skipped = 0, solid = 0;

        for (int seed = 1; seed <= 40; seed++)
        {
            Scene scene = SolidSpanOracle.BuildLevel(seed);
            scene.RebuildStaticWorld(new FakeRenderer());
            CsgWorld world = scene.StaticWorld.ShouldNotBeNull();

            SolidSpanOracle.Tally tally = SolidSpanOracle.Check(scene, world.ContainsPoint, seed);
            compared += tally.Compared;
            skipped += tally.Skipped;
            solid += tally.Solid;
        }

        // Most samples were far enough from a boundary to compare, and they
        // were not all air or all solid.
        string counts = $"compared {compared}, skipped {skipped}, solid {solid}";
        compared.ShouldBeGreaterThan(skipped * 3, counts);
        solid.ShouldBeGreaterThan(compared / 10, counts);
        (compared - solid).ShouldBeGreaterThan(compared / 10, counts);
    }
}
