using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Maps.Compiled;
using System.Numerics;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// A baked world's trees say solid where the level's brushes are, for
/// whole-number boxes that stand flush round a corner of eight cells.
/// </summary>
// The cook bakes the trees the live compile builds, so CellTreeSolidityTests
// in the scene suite says what goes wrong.
public sealed class CompiledMapSolidityTests
{
    private const int Levels = 12;

    [Fact]
    public void A_baked_world_of_five_flush_boxes_is_solid_where_their_union_is()
    {
        using CookedLevel level = CookedLevel.Bake(TreeSolidityOracle.FiveBoxes());
        CompiledStaticWorld baked = level.Scene.CompiledStaticWorld.ShouldNotBeNull();

        baked.ContainsPoint(new Vector3(1.3f, -4.7f, 0.3f)).ShouldBeTrue();
        ShouldHaveBoth(TreeSolidityOracle.ComparePoints(level.Authored, baked.ContainsPoint));
    }

    [Fact]
    public void Rays_into_a_baked_world_of_five_flush_boxes_stop_where_their_union_starts()
    {
        using CookedLevel level = CookedLevel.Bake(TreeSolidityOracle.FiveBoxes());
        CompiledStaticWorld baked = level.Scene.CompiledStaticWorld.ShouldNotBeNull();

        ShouldHaveBoth(TreeSolidityOracle.CompareRays(level.Authored, baked.Raycast));
    }

    [Fact]
    public void Baked_levels_of_flush_boxes_are_solid_where_their_union_is()
    {
        var tally = new TreeSolidityOracle.Tally();

        for (int seed = 1; seed <= Levels; seed++)
        {
            using CookedLevel level = CookedLevel.Bake(SolidSpanOracle.BuildFlushLevel(seed, cuts: false));
            CompiledStaticWorld baked = level.Scene.CompiledStaticWorld.ShouldNotBeNull();

            tally = tally.Plus(TreeSolidityOracle.ComparePoints(level.Authored, baked.ContainsPoint));
        }

        ShouldHaveBoth(tally);
    }

    [Fact]
    public void Rays_into_baked_levels_of_flush_boxes_stop_where_their_union_starts()
    {
        var tally = new TreeSolidityOracle.Tally();

        for (int seed = 1; seed <= Levels; seed++)
        {
            using CookedLevel level = CookedLevel.Bake(SolidSpanOracle.BuildFlushLevel(seed, cuts: false));
            CompiledStaticWorld baked = level.Scene.CompiledStaticWorld.ShouldNotBeNull();

            tally = tally.Plus(TreeSolidityOracle.CompareRays(level.Authored, baked.Raycast));
        }

        ShouldHaveBoth(tally);
    }

    // Neither all air nor all solid.
    private static void ShouldHaveBoth(TreeSolidityOracle.Tally tally)
    {
        tally.Solid.ShouldBeGreaterThan(tally.Compared / 20, tally.ToString());
        (tally.Compared - tally.Solid).ShouldBeGreaterThan(tally.Compared / 20, tally.ToString());
    }
}
