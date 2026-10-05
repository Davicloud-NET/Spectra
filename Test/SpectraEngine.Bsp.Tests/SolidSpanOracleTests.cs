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
        var tally = new SolidSpanOracle.Tally();

        for (int seed = 1; seed <= 40; seed++)
        {
            Scene scene = SolidSpanOracle.BuildLevel(seed);
            scene.RebuildStaticWorld(new FakeRenderer());
            CsgWorld world = scene.StaticWorld.ShouldNotBeNull();

            tally = tally.Plus(SolidSpanOracle.Check(scene, world.ContainsPoint, seed));
        }

        // Nearly every sample was far enough from a boundary to compare, and
        // they were not all air or all solid.
        tally.Compared.ShouldBeGreaterThan(tally.Skipped * 20, tally.ToString());
        tally.Solid.ShouldBeGreaterThan(tally.Compared / 10, tally.ToString());
        (tally.Compared - tally.Solid).ShouldBeGreaterThan(tally.Compared / 10, tally.ToString());
    }

    [Fact]
    public void Along_lines_that_lie_in_brush_faces_a_point_is_inside_a_span_when_there_is_solid_all_round_it()
    {
        var tally = new SolidSpanOracle.Tally();

        for (int seed = 1; seed <= 40; seed++)
        {
            Scene scene = SolidSpanOracle.BuildFlushLevel(seed);
            scene.RebuildStaticWorld(new FakeRenderer());

            tally = tally.Plus(
                SolidSpanOracle.CheckWholeNumberLines(scene, SolidSpanOracle.CarveRule(scene), seed));
        }

        tally.Compared.ShouldBeGreaterThan(tally.Skipped, tally.ToString());
        tally.Solid.ShouldBeGreaterThan(tally.Compared / 20, tally.ToString());
        (tally.Compared - tally.Solid).ShouldBeGreaterThan(tally.Compared / 10, tally.ToString());
    }
}
