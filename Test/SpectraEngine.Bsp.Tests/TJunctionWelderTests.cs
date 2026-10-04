using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <see cref="TJunctionWelder"/>: crossing vertices go into abutting edges in edge
/// order, deduplicated, and untouched polygons come back as the same instance.
/// </summary>
public sealed class TJunctionWelderTests
{
    private static readonly Plane FloorPlane = new(Vector3.UnitY, 0f);
    private static readonly Plane WallPlane = new(Vector3.UnitZ, 0f);

    // 2x2 floor whose edge (2,0,0) -> (0,0,0) meets two unit walls joined at (1,0,0).
    private static (Polygon Floor, Polygon WallLeft, Polygon WallRight) CreateTJunction()
    {
        var floor = new Polygon(
            [new(0f, 0f, 0f), new(0f, 0f, 2f), new(2f, 0f, 2f), new(2f, 0f, 0f)], FloorPlane);
        var wallLeft = new Polygon(
            [new(0f, 0f, 0f), new(1f, 0f, 0f), new(1f, 1f, 0f), new(0f, 1f, 0f)], WallPlane);
        var wallRight = new Polygon(
            [new(1f, 0f, 0f), new(2f, 0f, 0f), new(2f, 1f, 0f), new(1f, 1f, 0f)], WallPlane);
        return (floor, wallLeft, wallRight);
    }

    [Fact]
    public void Crossing_vertex_is_inserted_into_the_abutted_edge()
    {
        var (floor, wallLeft, wallRight) = CreateTJunction();

        Polygon[] welded = TJunctionWelder.Weld([floor, wallLeft, wallRight]);

        welded[0].VertexCount.ShouldBe(5);
        welded[0].Vertices.ShouldContain(v => Vector3.Distance(v, new Vector3(1f, 0f, 0f)) < 1e-5f);
        welded[0].ShouldNotBeSameAs(floor);
    }

    [Fact]
    public void Vertex_counts_match_across_the_shared_edge_after_welding()
    {
        var (floor, wallLeft, wallRight) = CreateTJunction();

        Polygon[] welded = TJunctionWelder.Weld([floor, wallLeft, wallRight]);

        // Shared line is y=0, z=0, x in [0,2]. Expect x = 0, 1, 2 on both sides.
        static bool OnSharedLine(Vector3 v) =>
            MathF.Abs(v.Y) < 1e-5f && MathF.Abs(v.Z) < 1e-5f && v.X > -1e-5f && v.X < 2f + 1e-5f;

        var floorLineKeys = welded[0].Vertices.Where(OnSharedLine)
            .Select(GeometryTestHelpers.LatticeKey).ToHashSet();
        var wallLineKeys = welded[1].Vertices.Concat(welded[2].Vertices).Where(OnSharedLine)
            .Select(GeometryTestHelpers.LatticeKey).ToHashSet();

        floorLineKeys.Count.ShouldBe(3);
        floorLineKeys.SetEquals(wallLineKeys).ShouldBeTrue();
    }

    [Fact]
    public void Polygon_needing_no_insertion_is_returned_as_the_same_instance()
    {
        var (floor, wallLeft, wallRight) = CreateTJunction();

        Polygon[] welded = TJunctionWelder.Weld([floor, wallLeft, wallRight]);

        welded[1].ShouldBeSameAs(wallLeft);
        welded[2].ShouldBeSameAs(wallRight);
    }

    [Fact]
    public void Multiple_insertions_on_one_edge_land_in_edge_order()
    {
        // The floor edge runs (3,0,0) -> (0,0,0), so (2,0,0) comes before (1,0,0).
        var floor = new Polygon(
            [new(0f, 0f, 0f), new(0f, 0f, 1f), new(3f, 0f, 1f), new(3f, 0f, 0f)], FloorPlane);
        var wall0 = new Polygon(
            [new(0f, 0f, 0f), new(1f, 0f, 0f), new(1f, 1f, 0f), new(0f, 1f, 0f)], WallPlane);
        var wall1 = new Polygon(
            [new(1f, 0f, 0f), new(2f, 0f, 0f), new(2f, 1f, 0f), new(1f, 1f, 0f)], WallPlane);
        var wall2 = new Polygon(
            [new(2f, 0f, 0f), new(3f, 0f, 0f), new(3f, 1f, 0f), new(2f, 1f, 0f)], WallPlane);

        Polygon[] welded = TJunctionWelder.Weld([floor, wall0, wall1, wall2]);

        Vector3[] expected =
        [
            new(0f, 0f, 0f), new(0f, 0f, 1f), new(3f, 0f, 1f),
            new(3f, 0f, 0f), new(2f, 0f, 0f), new(1f, 0f, 0f),
        ];
        welded[0].Vertices.ShouldBe(expected);
    }

    [Fact]
    public void Coincident_candidates_from_two_polygons_dedupe_to_one_insertion()
    {
        var (floor, wallLeft, _) = CreateTJunction();
        var wallTwin = new Polygon(
            [new(0f, 0f, 0f), new(1f, 0f, 0f), new(1f, 1f, 0f), new(0f, 1f, 0f)], WallPlane);

        Polygon[] welded = TJunctionWelder.Weld([floor, wallLeft, wallTwin]);

        welded[0].VertexCount.ShouldBe(5);
    }
}
