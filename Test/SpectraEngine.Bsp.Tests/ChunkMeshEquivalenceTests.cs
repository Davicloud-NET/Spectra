using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Per-cell chunk meshes must union to the monolithic
/// <see cref="CsgWorld.BuildMesh"/> triangle multiset, bit for bit.
/// </summary>
public sealed class ChunkMeshEquivalenceTests
{
    [Fact]
    public void Chunk_triangles_union_to_the_monolithic_mesh_for_a_border_pair()
        => AssertTriangleOracle(TwoBoxOverlapOnBorder());

    [Fact]
    public void Chunk_triangles_union_to_the_monolithic_mesh_for_boundary_exact_brushes()
        => AssertTriangleOracle(BoundaryExactWorld());

    [Fact]
    public void Chunk_triangles_union_to_the_monolithic_mesh_for_a_200_part_scattered_world()
    {
        List<BrushPlacement> placements = ScatteredWorld(structures: 50, seed: 0xC0FFEE0DDBA5EBA1UL);
        placements.Count.ShouldBe(200);

        // Guard against a vacuous pass: the scatter must span many cells.
        CsgWorld world = CsgWorld.Build(placements);
        world.ChunkMeshes.Count.ShouldBeGreaterThan(20);

        AssertTriangleOracle(placements);
    }

    [Fact]
    public void Chunk_triangles_union_to_the_monolithic_mesh_for_a_dense_grid_world()
        => AssertTriangleOracle(DenseGridWorld());

    [Fact]
    public void Render_bounds_enclose_every_chunk_vertex()
    {
        CsgWorld world = CsgWorld.Build(ScatteredWorld(structures: 25, seed: 0xBADC0DEBADC0DE01UL));
        world.ChunkMeshes.ShouldNotBeEmpty();

        foreach (ChunkMesh mesh in world.ChunkMeshes)
        {
            Aabb bounds = mesh.RenderBounds;
            foreach (ChunkSubmesh submesh in mesh.Submeshes)
            {
                float[] vertices = submesh.Vertices;
                for (int i = 0; i < vertices.Length; i += 8) // 8 floats per vertex, position first
                {
                    var p = new Vector3(vertices[i], vertices[i + 1], vertices[i + 2]);
                    (p.X >= bounds.Min.X && p.X <= bounds.Max.X &&
                     p.Y >= bounds.Min.Y && p.Y <= bounds.Max.Y &&
                     p.Z >= bounds.Min.Z && p.Z <= bounds.Max.Z).ShouldBeTrue(
                        $"vertex {p} of chunk {mesh.Coord} escapes its render bounds {bounds.Min}..{bounds.Max}");
                }
            }
        }
    }

    [Fact]
    public void Render_bounds_of_a_border_spanning_owner_extend_past_the_cell_box()
    {
        // The first box crosses x=32 and its owner cell holds all its surfaces.
        CsgWorld world = CsgWorld.Build(TwoBoxOverlapOnBorder());

        bool foundOverhang = false;
        foreach (ChunkMesh mesh in world.ChunkMeshes)
        {
            Aabb cell = mesh.Coord.Bounds;
            Aabb render = mesh.RenderBounds;
            if (render.Max.X > cell.Max.X || render.Min.X < cell.Min.X ||
                render.Max.Y > cell.Max.Y || render.Min.Y < cell.Min.Y ||
                render.Max.Z > cell.Max.Z || render.Min.Z < cell.Min.Z)
            {
                foundOverhang = true;
            }
        }
        foundOverhang.ShouldBeTrue("no chunk's render bounds left its cell — the fixture should force an overhang");
    }

    [Fact]
    public void Two_builds_of_the_same_placements_produce_bit_identical_chunk_meshes()
    {
        List<BrushPlacement> placements = ScatteredWorld(structures: 25, seed: 0xBADC0DEBADC0DE01UL);
        CsgWorld first = CsgWorld.Build(placements);
        CsgWorld second = CsgWorld.Build(placements);

        second.ChunkMeshes.Count.ShouldBe(first.ChunkMeshes.Count);
        for (int c = 0; c < first.ChunkMeshes.Count; c++)
        {
            ChunkMesh a = first.ChunkMeshes[c];
            ChunkMesh b = second.ChunkMeshes[c];
            b.Coord.ShouldBe(a.Coord);
            AssertSubmeshesIdentical(a, b, $"cell {a.Coord}");
            b.RenderBounds.Min.ShouldBe(a.RenderBounds.Min);
            b.RenderBounds.Max.ShouldBe(a.RenderBounds.Max);
        }
    }

    [Fact]
    public void Clean_cells_keep_their_previous_artifact_instances()
    {
        // The far box lives many cells from the border pair. Moving it must
        // leave the pair's ChunkMesh instances as they are, not just equal.
        List<BrushPlacement> placements =
            [.. TwoBoxOverlapOnBorder(), new(Box(1f), Translation(200f, 16f, 16f))];
        CsgWorld first = CsgWorld.Build(
            placements, dirtyCells: null, previousCache: null, previousWeldCache: null,
            previousBspCache: null, previousMeshCache: null);

        List<BrushPlacement> edited = [.. placements];
        edited[2] = edited[2] with
        {
            Transform = edited[2].Transform * Matrix4x4.CreateTranslation(0f, 0f, 1.5f),
        };

        CsgWorld incremental = CsgWorld.Build(
            edited, dirtyCells: null, first.CompileCache, first.WeldCache, first.BspCache, first.MeshCache);

        incremental.MeshStats.ShouldBe(new CsgMeshStats(Reused: 2, Built: 1));

        foreach (ChunkMesh before in first.ChunkMeshes)
        {
            if (before.Coord.X >= 6)
                continue; // the far box's cells
            incremental.ChunkMeshes.ShouldContain(before,
                $"cell {before.Coord} rebuilt its mesh arrays needlessly");
        }
    }

    [Fact]
    public void Moving_one_brush_and_recompiling_incrementally_matches_scratch()
    {
        List<BrushPlacement> placements = ScatteredWorld(structures: 50, seed: 0xC0FFEE0DDBA5EBA1UL);
        CsgWorld first = CsgWorld.Build(
            placements, dirtyCells: null, previousCache: null, previousWeldCache: null,
            previousBspCache: null, previousMeshCache: null);

        List<BrushPlacement> edited = [.. placements];
        edited[1] = edited[1] with
        {
            Transform = edited[1].Transform * Matrix4x4.CreateTranslation(0.3f, 0f, 0.2f),
        };

        CsgWorld incremental = CsgWorld.Build(
            edited, dirtyCells: null, first.CompileCache, first.WeldCache, first.BspCache, first.MeshCache);
        CsgWorld scratch = CsgWorld.Build(edited);

        CsgMeshStats stats = incremental.MeshStats.ShouldNotBeNull();
        stats.Total.ShouldBe(incremental.ChunkMeshes.Count);
        stats.Built.ShouldBeGreaterThan(0);
        stats.Reused.ShouldBeGreaterThan(incremental.ChunkMeshes.Count / 2,
            "a one-brush edit re-meshed most of the world's cells");

        incremental.ChunkMeshes.Count.ShouldBe(scratch.ChunkMeshes.Count);
        for (int c = 0; c < scratch.ChunkMeshes.Count; c++)
        {
            ChunkMesh e = scratch.ChunkMeshes[c];
            ChunkMesh a = incremental.ChunkMeshes[c];
            a.Coord.ShouldBe(e.Coord);
            AssertSubmeshesIdentical(e, a, $"cell {e.Coord}");
        }
    }

    [Fact]
    public void Cache_free_builds_report_no_mesh_stats_or_cache()
    {
        CsgWorld world = CsgWorld.Build(TwoBoxOverlapOnBorder());
        world.MeshStats.ShouldBeNull();
        world.MeshCache.ShouldBeNull();
        world.ChunkMeshes.ShouldNotBeEmpty();
    }

    private static Brush Box(float h) => Brush.CreateBox(new Vector3(-h), new Vector3(h));

    private static Matrix4x4 Translation(float x, float y, float z) => Matrix4x4.CreateTranslation(x, y, z);

    // Overlapping pair straddling the x=32 cell border.
    private static BrushPlacement[] TwoBoxOverlapOnBorder() =>
    [
        new(Box(4f), Translation(29f, 16f, 16f)),
        new(Box(4f), Translation(35f, 18f, 16f)),
    ];

    // Faces and centres land bitwise on x=32 and the (32,32,32) corner.
    private static BrushPlacement[] BoundaryExactWorld() =>
    [
        new(Box(4f), Translation(28f, 16f, 16f)),
        new(Box(4f), Translation(33f, 17f, 16f)),
        new(Box(4f), Translation(32f, 32f, 32f)),
        new(Box(4f), Translation(35f, 34f, 33f)),
    ];

    // Four-part structures (a slab with three overlapping pillars) scattered
    // by a fixed-seed LCG over ±160 units.
    private static List<BrushPlacement> ScatteredWorld(int structures, ulong seed)
    {
        ulong state = seed;
        float NextFloat01()
        {
            state = state * 6364136223846793005UL + 1442695040888963407UL;
            return (state >> 40) * (1.0f / (1 << 24));
        }

        var placements = new List<BrushPlacement>(structures * 4);
        for (int s = 0; s < structures; s++)
        {
            var p = new Vector3(
                (NextFloat01() - 0.5f) * 320f,
                (NextFloat01() - 0.5f) * 320f,
                (NextFloat01() - 0.5f) * 320f);

            placements.Add(new BrushPlacement(
                Brush.CreateBox(new Vector3(-4f, -1f, -4f), new Vector3(4f, 1f, 4f)),
                Translation(p.X, p.Y, p.Z)));
            placements.Add(new BrushPlacement(Box(1f), Translation(p.X + 3.5f, p.Y + 1.5f, p.Z)));
            placements.Add(new BrushPlacement(Box(1f), Translation(p.X - 3.5f, p.Y + 1.5f, p.Z + 1.5f)));
            placements.Add(new BrushPlacement(Box(1f), Translation(p.X, p.Y + 1.5f, p.Z - 3.5f)));
        }
        return placements;
    }

    // 6x6x6 size-2 cubes at spacing 1.8, straddling the (32,32,32) cell corner.
    private static List<BrushPlacement> DenseGridWorld()
    {
        var placements = new List<BrushPlacement>(216);
        for (int x = 0; x < 6; x++)
            for (int y = 0; y < 6; y++)
                for (int z = 0; z < 6; z++)
                    placements.Add(new BrushPlacement(
                        Box(1f), Translation(27.5f + x * 1.8f, 27.5f + y * 1.8f, 27.5f + z * 1.8f)));
        return placements;
    }

    private static void AssertTriangleOracle(IReadOnlyList<BrushPlacement> placements)
    {
        CsgWorld world = CsgWorld.Build(placements);

        (float[] monoVertices, uint[] monoIndices) = world.BuildMesh();
        List<float[]> expected = FlattenTriangles(monoVertices, monoIndices);

        var actual = new List<float[]>();
        foreach (ChunkMesh mesh in world.ChunkMeshes)
        {
            foreach (ChunkSubmesh submesh in mesh.Submeshes)
                actual.AddRange(FlattenTriangles(submesh.Vertices, submesh.Indices));
        }

        expected.ShouldNotBeEmpty("the monolithic mesh has no triangles — vacuous oracle");
        actual.Count.ShouldBe(expected.Count, "per-cell meshes emitted a different triangle count");

        expected.Sort(CompareTriangles);
        actual.Sort(CompareTriangles);
        for (int t = 0; t < expected.Count; t++)
        {
            actual[t].SequenceEqual(expected[t]).ShouldBeTrue(
                $"triangle multiset diverged at sorted position {t}");
        }
    }

    private static void AssertSubmeshesIdentical(ChunkMesh expected, ChunkMesh actual, string context)
    {
        actual.Submeshes.Count.ShouldBe(expected.Submeshes.Count, $"submesh count diverged for {context}");
        for (int s = 0; s < expected.Submeshes.Count; s++)
        {
            ChunkSubmesh e = expected.Submeshes[s];
            ChunkSubmesh a = actual.Submeshes[s];
            a.Material.ShouldBe(e.Material, $"submesh #{s} material diverged for {context}");
            a.Vertices.SequenceEqual(e.Vertices).ShouldBeTrue($"submesh #{s} vertices diverged for {context}");
            a.Indices.SequenceEqual(e.Indices).ShouldBeTrue($"submesh #{s} indices diverged for {context}");
        }
    }

    private static List<float[]> FlattenTriangles(float[] vertices, uint[] indices)
    {
        var triangles = new List<float[]>(indices.Length / 3);
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            var triangle = new float[24];
            for (int corner = 0; corner < 3; corner++)
                Array.Copy(vertices, (int)indices[i + corner] * 8, triangle, corner * 8, 8);
            triangles.Add(triangle);
        }
        return triangles;
    }

    // CompareTo is fine: inputs are finite and equal triangles are bit-equal.
    private static int CompareTriangles(float[] a, float[] b)
    {
        for (int i = 0; i < a.Length; i++)
        {
            int c = a[i].CompareTo(b[i]);
            if (c != 0)
                return c;
        }
        return 0;
    }
}
