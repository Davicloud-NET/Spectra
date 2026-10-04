using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <see cref="CsgCompileCache"/>: incremental compiles must be bit-identical
/// to from-scratch ones, and an edit re-carves only its overlap neighbourhood.
/// </summary>
public sealed class CsgCompileCacheTests
{
    [Fact]
    public void Twenty_random_single_brush_moves_stay_bit_identical_to_from_scratch_compiles()
    {
        List<BrushPlacement> placements = MakeGrid(4);
        CsgWorld incremental = CsgWorld.Build(placements, previousCache: null);
        incremental.CacheStats.ShouldBe(new CsgCacheStats(Hits: 0, Misses: placements.Count));

        // Fixed-seed LCG so the move sequence is the same on every machine.
        ulong state = 0xC0FFEE0DDBA5EBA1UL;
        float NextFloat01()
        {
            state = state * 6364136223846793005UL + 1442695040888963407UL;
            return (state >> 40) * (1.0f / (1 << 24));
        }

        for (int move = 0; move < 20; move++)
        {
            int index = (int)(NextFloat01() * placements.Count) % placements.Count;
            var delta = new Vector3(
                NextFloat01() - 0.5f, NextFloat01() - 0.5f, NextFloat01() - 0.5f);
            placements[index] = placements[index] with
            {
                Transform = placements[index].Transform * Matrix4x4.CreateTranslation(delta),
            };

            // Both caches chain, as in the scene. This grid sits in one cell,
            // so weld reuse is zero here.
            incremental = CsgWorld.Build(placements, dirtyCells: null, incremental.CompileCache, incremental.WeldCache);
            CsgWorld scratch = CsgWorld.Build(placements);

            ShouldBeIdenticalSurfaces(scratch.Surfaces, incremental.Surfaces, $"move #{move}");
            (float[] expectedVertices, uint[] expectedIndices) = scratch.BuildMesh();
            (float[] actualVertices, uint[] actualIndices) = incremental.BuildMesh();
            actualVertices.SequenceEqual(expectedVertices).ShouldBeTrue($"mesh vertices diverged at move #{move}");
            actualIndices.SequenceEqual(expectedIndices).ShouldBeTrue($"mesh indices diverged at move #{move}");

            // Full recompiles would be trivially identical, so require hits.
            CsgCacheStats stats = incremental.CacheStats.ShouldNotBeNull();
            stats.Hits.ShouldBeGreaterThan(0, $"no cache hits at move #{move}");
            stats.Total.ShouldBe(placements.Count);
        }
    }

    [Fact]
    public void Moving_one_brush_recarves_exactly_itself_and_its_overlap_neighbours()
    {
        List<BrushPlacement> placements = MakeRow(8);
        CsgWorld first = CsgWorld.Build(placements, previousCache: null);

        const int moved = 4;
        List<BrushPlacement> edited = [.. placements];
        edited[moved] = edited[moved] with
        {
            Transform = edited[moved].Transform * Matrix4x4.CreateTranslation(0.1f, 0f, 0f),
        };

        // Expected misses: the moved brush plus everything that overlapped
        // it before or after the move.
        HashSet<int> expectedMisses = [moved];
        expectedMisses.UnionWith(NeighborsOf(placements, moved));
        expectedMisses.UnionWith(NeighborsOf(edited, moved));
        expectedMisses.Count.ShouldBe(3); // itself, left, right

        CsgWorld second = CsgWorld.Build(edited, first.CompileCache);

        second.CacheStats.ShouldBe(new CsgCacheStats(
            Hits: placements.Count - expectedMisses.Count,
            Misses: expectedMisses.Count));
    }

    [Fact]
    public void Removing_a_brush_prunes_its_entry_and_invalidates_former_neighbours()
    {
        List<BrushPlacement> placements = MakeRow(5);
        CsgWorld first = CsgWorld.Build(placements, previousCache: null);

        const int removed = 2;
        Brush removedBrush = placements[removed].Brush;
        List<BrushPlacement> edited = [.. placements];
        edited.RemoveAt(removed);

        CsgWorld second = CsgWorld.Build(edited, first.CompileCache);

        // Neighbours 1 and 3 lost a carver and miss. The ends hit: an index
        // shift alone doesn't invalidate.
        second.CacheStats.ShouldBe(new CsgCacheStats(Hits: 2, Misses: 2));

        CsgCompileCache next = second.CompileCache.ShouldNotBeNull();
        next.Contains(removedBrush).ShouldBeFalse();
        next.Count.ShouldBe(edited.Count);

        ShouldBeIdenticalSurfaces(CsgWorld.Build(edited).Surfaces, second.Surfaces, "removal");
    }

    [Fact]
    public void Adding_a_brush_invalidates_its_new_neighbours()
    {
        List<BrushPlacement> placements = MakeRow(4);
        CsgWorld first = CsgWorld.Build(placements, previousCache: null);

        // Appended, so existing indices and precedence don't change.
        List<BrushPlacement> edited = [.. placements];
        var newcomer = new BrushPlacement(
            CreateUnitBox(),
            Matrix4x4.CreateTranslation(3 * RowSpacing + 1.0f, 0f, 0f));
        edited.Add(newcomer);

        int[] newcomerNeighbors = NeighborsOf(edited, edited.Count - 1);
        newcomerNeighbors.ShouldBe(new[] { 3 });

        CsgWorld second = CsgWorld.Build(edited, first.CompileCache);

        second.CacheStats.ShouldBe(new CsgCacheStats(Hits: 3, Misses: 2));
        second.CompileCache.ShouldNotBeNull().Contains(newcomer.Brush).ShouldBeTrue();

        ShouldBeIdenticalSurfaces(CsgWorld.Build(edited).Surfaces, second.Surfaces, "addition");
    }

    [Fact]
    public void First_compile_with_no_cache_matches_the_cache_free_path_bit_for_bit()
    {
        List<BrushPlacement> placements = MakeGrid(3);

        CsgWorld cacheFree = CsgWorld.Build(placements);
        CsgWorld caching = CsgWorld.Build(placements, previousCache: null);

        cacheFree.CompileCache.ShouldBeNull();
        cacheFree.CacheStats.ShouldBeNull();

        caching.CacheStats.ShouldBe(new CsgCacheStats(Hits: 0, Misses: placements.Count));
        caching.CompileCache.ShouldNotBeNull().Count.ShouldBe(placements.Count);

        ShouldBeIdenticalSurfaces(cacheFree.Surfaces, caching.Surfaces, "first compile");
        (float[] expectedVertices, uint[] expectedIndices) = cacheFree.BuildMesh();
        (float[] actualVertices, uint[] actualIndices) = caching.BuildMesh();
        actualVertices.SequenceEqual(expectedVertices).ShouldBeTrue();
        actualIndices.SequenceEqual(expectedIndices).ShouldBeTrue();
    }

    [Fact]
    public void Unchanged_placements_hit_for_every_brush_and_reproduce_the_world()
    {
        List<BrushPlacement> placements = MakeGrid(3);
        CsgWorld first = CsgWorld.Build(placements, previousCache: null);

        CsgWorld second = CsgWorld.Build(placements, first.CompileCache);

        second.CacheStats.ShouldBe(new CsgCacheStats(Hits: placements.Count, Misses: 0));
        ShouldBeIdenticalSurfaces(first.Surfaces, second.Surfaces, "unchanged recompile");
    }

    [Fact]
    public void Duplicate_brush_references_across_placements_stay_correct()
    {
        // The cache holds one entry per Brush instance, so at most one of
        // these three placements can hit. The others must re-carve.
        Brush shared = CreateUnitBox();
        List<BrushPlacement> placements =
        [
            new(shared, Matrix4x4.CreateTranslation(0f, 0f, 0f)),
            new(shared, Matrix4x4.CreateTranslation(1.5f, 0f, 0f)),
            new(shared, Matrix4x4.CreateTranslation(3.0f, 0f, 0f)),
        ];
        CsgWorld first = CsgWorld.Build(placements, previousCache: null);

        List<BrushPlacement> edited = [.. placements];
        edited[2] = edited[2] with { Transform = Matrix4x4.CreateTranslation(3.2f, 0f, 0f) };

        CsgWorld incremental = CsgWorld.Build(edited, first.CompileCache);
        CsgWorld scratch = CsgWorld.Build(edited);

        ShouldBeIdenticalSurfaces(scratch.Surfaces, incremental.Surfaces, "duplicate brush references");
    }

    private const float RowSpacing = 1.8f;

    private static Brush CreateUnitBox() =>
        Brush.CreateBox(new Vector3(-1f), new Vector3(1f));

    // k³ size-2 cubes at spacing 1.8: axis-adjacent pairs overlap by 0.2.
    private static List<BrushPlacement> MakeGrid(int k)
    {
        var list = new List<BrushPlacement>(k * k * k);
        for (int x = 0; x < k; x++)
            for (int y = 0; y < k; y++)
                for (int z = 0; z < k; z++)
                    list.Add(new BrushPlacement(
                        CreateUnitBox(),
                        Matrix4x4.CreateTranslation(x * RowSpacing, y * RowSpacing, z * RowSpacing)));
        return list;
    }

    // Row of size-2 cubes at spacing 1.8: each overlaps only its neighbours.
    private static List<BrushPlacement> MakeRow(int count)
    {
        var list = new List<BrushPlacement>(count);
        for (int i = 0; i < count; i++)
            list.Add(new BrushPlacement(
                CreateUnitBox(), Matrix4x4.CreateTranslation(i * RowSpacing, 0f, 0f)));
        return list;
    }

    // Asks the broadphase the carve uses, so expectations follow the geometry.
    private static int[] NeighborsOf(List<BrushPlacement> placements, int index)
    {
        var bounds = new Aabb[placements.Count];
        for (int i = 0; i < bounds.Length; i++)
            bounds[i] = placements[i].WorldBounds;
        return BrushBroadphase.FindOverlaps(bounds)[index];
    }

    // Bit-exact, no tolerance: both paths run the same code over the same
    // floats, so any drift is a stale cache hit.
    private static void ShouldBeIdenticalSurfaces(
        IReadOnlyList<Polygon> expected, IReadOnlyList<Polygon> actual, string context)
    {
        actual.Count.ShouldBe(expected.Count, $"surface count diverged ({context})");
        for (int i = 0; i < expected.Count; i++)
        {
            actual[i].Surface.ShouldBe(expected[i].Surface, $"surface plane #{i} diverged ({context})");
            actual[i].VertexCount.ShouldBe(expected[i].VertexCount, $"vertex count of surface #{i} diverged ({context})");
            for (int v = 0; v < expected[i].VertexCount; v++)
                actual[i].Vertices[v].ShouldBe(expected[i].Vertices[v], $"vertex {v} of surface #{i} diverged ({context})");
        }
    }
}
