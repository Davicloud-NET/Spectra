using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The compiled world's per-cell trees say solid where the brushes are, for
/// whole-number boxes that stand flush round a corner of eight cells.
/// </summary>
// A resident's face can be buried in a brush of another cell. A tree built
// from each resident's whole skin is open there, and a leaf reaching out
// through the gap reads solid as air inside the cell.
public sealed class CellTreeSolidityTests
{
    private const int Levels = 40;

    [Fact]
    public void A_point_in_a_box_whose_end_is_buried_in_another_cell_is_solid()
    {
        CsgWorld world = Compile(TreeSolidityOracle.FiveBoxes());

        world.ContainsPoint(new Vector3(1.3f, -4.7f, 0.3f)).ShouldBeTrue();
    }

    [Fact]
    public void A_ray_down_onto_a_box_whose_end_is_buried_in_another_cell_hits_its_top()
    {
        CsgWorld world = Compile(TreeSolidityOracle.FiveBoxes());

        world.Raycast(new Vector3(1.3f, 25.3f, 0.3f), -Vector3.UnitY, 100f, out BspRaycastHit hit).ShouldBeTrue();

        hit.Distance.ShouldBe(29.3f, 1e-3f);
        hit.Normal.Y.ShouldBe(1f, 1e-3f);
    }

    // The corner of eight cells at the origin and another one, two controls
    // with all five boxes in one cell and in two, and two corners far out,
    // where the weld band is smaller than a float's step.
    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(64f, 64f, 64f)]
    [InlineData(16f, 16f, 16f)]
    [InlineData(16f, 6f, 16f)]
    [InlineData(8000f, 0f, 8000f)]
    [InlineData(-96000f, 32000f, 64000f)]
    public void Five_flush_boxes_are_solid_where_their_union_is(float x, float y, float z)
    {
        Scene level = TreeSolidityOracle.FiveBoxes(new Vector3(x, y, z));
        CsgWorld world = Compile(level);

        TreeSolidityOracle.Tally tally = TreeSolidityOracle.ComparePoints(level, world.ContainsPoint);

        ShouldHaveBoth(tally);
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(64f, 64f, 64f)]
    [InlineData(16f, 16f, 16f)]
    [InlineData(16f, 6f, 16f)]
    [InlineData(8000f, 0f, 8000f)]
    [InlineData(-96000f, 32000f, 64000f)]
    public void Rays_into_five_flush_boxes_stop_where_their_union_starts(float x, float y, float z)
    {
        Scene level = TreeSolidityOracle.FiveBoxes(new Vector3(x, y, z));
        CsgWorld world = Compile(level);

        TreeSolidityOracle.Tally tally = TreeSolidityOracle.CompareRays(level, world.Raycast);

        ShouldHaveBoth(tally);
    }

    [Fact]
    public void Seeded_levels_of_flush_boxes_are_solid_where_their_union_is()
    {
        var tally = new TreeSolidityOracle.Tally();

        for (int seed = 1; seed <= Levels; seed++)
        {
            Scene level = SolidSpanOracle.BuildFlushLevel(seed, cuts: false);
            CsgWorld world = Compile(level);

            tally = tally.Plus(TreeSolidityOracle.ComparePoints(level, world.ContainsPoint));
        }

        ShouldHaveBoth(tally);
    }

    [Fact]
    public void Rays_into_seeded_levels_of_flush_boxes_stop_where_their_union_starts()
    {
        var tally = new TreeSolidityOracle.Tally();

        for (int seed = 1; seed <= Levels; seed++)
        {
            Scene level = SolidSpanOracle.BuildFlushLevel(seed, cuts: false);
            CsgWorld world = Compile(level);

            tally = tally.Plus(TreeSolidityOracle.CompareRays(level, world.Raycast));
        }

        ShouldHaveBoth(tally);
    }

    private static CsgWorld Compile(Scene level)
    {
        level.RebuildStaticWorld(new FakeRenderer());
        return level.StaticWorld.ShouldNotBeNull();
    }

    // Neither all air nor all solid.
    private static void ShouldHaveBoth(TreeSolidityOracle.Tally tally)
    {
        tally.Solid.ShouldBeGreaterThan(tally.Compared / 20, tally.ToString());
        (tally.Compared - tally.Solid).ShouldBeGreaterThan(tally.Compared / 20, tally.ToString());
    }
}
