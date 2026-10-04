using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The incremental (previous-world) compile must match a from-scratch compile
/// bit for bit, carry untouched cells forward as the same instances, and fall
/// back to the validated path on structural edits.
/// </summary>
// A compiled world owns its placement list, so every edit builds a fresh one.
public sealed class IncrementalCompileTests
{
    [Fact]
    public void Noop_recompile_shares_every_artifact_instance()
    {
        List<BrushPlacement> placements = MakeScatteredWorld();
        CsgWorld first = CsgWorld.Build(placements, previousCache: null);

        CsgWorld second = CsgWorld.Build([.. placements], [], first);

        second.CacheStats.ShouldBe(new CsgCacheStats(Hits: placements.Count, Misses: 0));
        second.WeldStats.ShouldBe(new CsgWeldStats(Reused: placements.Count, Welded: 0));
        second.BspStats.ShouldNotBeNull().Built.ShouldBe(0);
        second.MeshStats.ShouldNotBeNull().Built.ShouldBe(0);

        ReferenceEquals(second.Chunks, first.Chunks).ShouldBeTrue("grid instance not shared");
        second.ChunkMeshes.Count.ShouldBe(first.ChunkMeshes.Count);
        for (int i = 0; i < first.ChunkMeshes.Count; i++)
            ReferenceEquals(second.ChunkMeshes[i], first.ChunkMeshes[i]).ShouldBeTrue($"chunk mesh #{i} not shared");

        second.SurfaceCount.ShouldBe(first.Surfaces.Count);
        ShouldBeIdenticalSurfaces(first.Surfaces, second.Surfaces, "noop");
    }

    [Fact]
    public void Random_single_part_walk_stays_bit_identical_to_from_scratch_compiles()
    {
        List<BrushPlacement> placements = MakeScatteredWorld();
        CsgWorld incremental = CsgWorld.Build(placements, previousCache: null);

        ulong state = 0xD1FF00D5EEDF00D1UL;
        float NextFloat01()
        {
            state = state * 6364136223846793005UL + 1442695040888963407UL;
            return (state >> 40) * (1.0f / (1 << 24));
        }

        int patchedCompiles = 0;
        for (int move = 0; move < 40; move++)
        {
            int index = (int)(NextFloat01() * placements.Count) % placements.Count;
            // Small nudges stay inside a part's site so the patch path engages.
            // The larger vertical hops cross the 32-unit cell boundary.
            var delta = new Vector3(
                (NextFloat01() - 0.5f) * 2f,
                (NextFloat01() - 0.5f) * (move % 3 == 0 ? 40f : 2f),
                (NextFloat01() - 0.5f) * 2f);

            BrushPlacement before = placements[index];
            BrushPlacement after = before with
            {
                Transform = before.Transform * Matrix4x4.CreateTranslation(delta),
            };
            placements = With(placements, index, after);

            CsgWorld previous = incremental;
            incremental = CsgWorld.Build(placements, DirtyUnion(before, after), incremental);
            CsgWorld scratch = CsgWorld.Build(placements);

            // The fallback rebuilds every WorldChunk; the patch carries untouched
            // ones forward. Shared instances mean this compile patched.
            foreach (WorldChunk chunk in previous.Chunks.OrderedChunks)
            {
                if (incremental.Chunks.TryGet(chunk.Coord, out WorldChunk now) && ReferenceEquals(now, chunk))
                {
                    patchedCompiles++;
                    break;
                }
            }

            ShouldBeIdenticalSurfaces(scratch.Surfaces, incremental.Surfaces, $"move #{move}");
            (float[] expectedVertices, uint[] expectedIndices) = scratch.BuildMesh();
            (float[] actualVertices, uint[] actualIndices) = incremental.BuildMesh();
            actualVertices.SequenceEqual(expectedVertices).ShouldBeTrue($"mesh vertices diverged at move #{move}");
            actualIndices.SequenceEqual(expectedIndices).ShouldBeTrue($"mesh indices diverged at move #{move}");

            incremental.Chunks.Count.ShouldBe(scratch.Chunks.Count, $"cell count diverged at move #{move}");
            for (int c = 0; c < scratch.Chunks.OrderedChunks.Count; c++)
            {
                WorldChunk expected = scratch.Chunks.OrderedChunks[c];
                WorldChunk actual = incremental.Chunks.OrderedChunks[c];
                actual.Coord.ShouldBe(expected.Coord, $"cell order diverged at move #{move}");
                actual.ResidentBrushIndices.ShouldBe(expected.ResidentBrushIndices, $"residents diverged at move #{move}");
                actual.OwnedBrushIndices.ShouldBe(expected.OwnedBrushIndices, $"owners diverged at move #{move}");
                incremental.Chunks.TryGet(expected.Coord, out WorldChunk lookedUp).ShouldBeTrue();
                ReferenceEquals(lookedUp, actual).ShouldBeTrue("layered lookup disagrees with ordered enumeration");
            }

            // Either path: a one-part move re-carves a handful of brushes at most.
            CsgCacheStats stats = incremental.CacheStats.ShouldNotBeNull();
            stats.Misses.ShouldBeLessThanOrEqualTo(6, $"move #{move} re-carved too much");
        }

        // A walk of nothing but fallbacks would match trivially.
        patchedCompiles.ShouldBeGreaterThan(25, "the incremental path barely engaged");
    }

    [Fact]
    public void Patched_world_answers_queries_like_a_monolithic_tree()
    {
        List<BrushPlacement> placements = MakeScatteredWorld();
        CsgWorld world = CsgWorld.Build(placements, previousCache: null);

        BrushPlacement before = placements[3];
        BrushPlacement after = before with
        {
            Transform = before.Transform * Matrix4x4.CreateTranslation(0.4f, 0.2f, -0.3f),
        };
        placements = With(placements, 3, after);
        world = CsgWorld.Build(placements, DirtyUnion(before, after), world);

        BspTree mono = BspTree.BuildFromSurfaces(world.Surfaces);
        Aabb bounds = WorldBounds(placements).Expanded(4f);
        Vector3 size = bounds.Size;
        float maxDistance = size.Length() + 8f;

        ulong state = 0xBADC0DE5EEDBEEFUL;
        float NextFloat01()
        {
            state = state * 6364136223846793005UL + 1442695040888963407UL;
            return (state >> 40) * (1.0f / (1 << 24));
        }

        int inside = 0;
        for (int i = 0; i < 20_000; i++)
        {
            Vector3 point = bounds.Min + size * new Vector3(NextFloat01(), NextFloat01(), NextFloat01());
            bool expected = mono.ContainsPoint(point);
            world.ContainsPoint(point).ShouldBe(expected, $"ContainsPoint diverged at {point}");
            if (expected)
                inside++;
        }
        inside.ShouldBeGreaterThan(0, "no probe landed in solid — vacuous oracle");

        int hits = 0;
        for (int r = 0; r < 2_000; r++)
        {
            Vector3 origin = bounds.Min + size * new Vector3(NextFloat01(), NextFloat01(), NextFloat01());
            Vector3 direction;
            while (true)
            {
                var v = new Vector3(NextFloat01() * 2f - 1f, NextFloat01() * 2f - 1f, NextFloat01() * 2f - 1f);
                float lengthSquared = v.LengthSquared();
                if (lengthSquared is > 1e-4f and <= 1f)
                {
                    direction = v / MathF.Sqrt(lengthSquared);
                    break;
                }
            }

            bool expected = mono.Raycast(origin, direction, maxDistance, out BspRaycastHit expectedHit);
            bool actual = world.Raycast(origin, direction, maxDistance, out BspRaycastHit actualHit);
            actual.ShouldBe(expected, $"Raycast hit flag diverged for ray #{r} from {origin} along {direction}");
            if (!expected)
                continue;
            hits++;
            actualHit.Distance.ShouldBe(expectedHit.Distance, 1e-4f, $"ray #{r} distance diverged");
        }
        hits.ShouldBeGreaterThan(0, "no ray hit solid — vacuous oracle");
    }

    [Fact]
    public void Isolated_move_carries_every_untouched_cell_instance_forward()
    {
        List<BrushPlacement> placements = MakeScatteredWorld();
        CsgWorld first = CsgWorld.Build(placements, previousCache: null);

        const int moved = 0;
        BrushPlacement before = placements[moved];
        BrushPlacement after = before with
        {
            Transform = before.Transform * Matrix4x4.CreateTranslation(0.3f, 0f, 0f),
        };
        List<BrushPlacement> edited = With(placements, moved, after);
        CsgWorld second = CsgWorld.Build(edited, DirtyUnion(before, after), first);

        // Ground truth is the classic validated path over the same edit and
        // caches: the cells it rebuilds are the ones the patch must replace.
        CsgWorld classic = CsgWorld.Build(
            edited, dirtyCells: null, first.CompileCache, first.WeldCache, first.BspCache, first.MeshCache);
        HashSet<ChunkCoord> expectedReplaced = ReplacedCoords(first, classic);
        HashSet<ChunkCoord> actualReplaced = ReplacedCoords(first, second);

        actualReplaced.ShouldBe(expectedReplaced, ignoreOrder: true);
        actualReplaced.Count.ShouldBeGreaterThan(0, "the edit replaced no artifact — vacuous");
        actualReplaced.Count.ShouldBeLessThanOrEqualTo(4, "an isolated move rebuilt more than its neighbourhood");

        // Same for the per-cell trees; cells not rebuilt keep their chunk instance.
        second.BspStats.ShouldBe(classic.BspStats);
        int carriedChunks = 0;
        foreach (WorldChunk chunk in first.Chunks.OrderedChunks)
        {
            if (second.Chunks.TryGet(chunk.Coord, out WorldChunk carried) && ReferenceEquals(carried, chunk))
                carriedChunks++;
        }
        CsgBspStats bspStats = second.BspStats.ShouldNotBeNull();
        carriedChunks.ShouldBe(second.Chunks.Count - bspStats.Built, "a non-rebuilt cell lost its chunk instance");
    }

    // Cells whose mesh artifact is a different instance in `next`. Removed
    // cells count as replaced.
    private static HashSet<ChunkCoord> ReplacedCoords(CsgWorld previous, CsgWorld next)
    {
        var previousByCoord = new Dictionary<ChunkCoord, ChunkMesh>();
        foreach (ChunkMesh mesh in previous.ChunkMeshes)
            previousByCoord[mesh.Coord] = mesh;

        var replaced = new HashSet<ChunkCoord>();
        foreach (ChunkMesh mesh in next.ChunkMeshes)
        {
            if (!previousByCoord.TryGetValue(mesh.Coord, out ChunkMesh? old) || !ReferenceEquals(old, mesh))
                replaced.Add(mesh.Coord);
            previousByCoord.Remove(mesh.Coord);
        }
        foreach (ChunkCoord coord in previousByCoord.Keys)
            replaced.Add(coord); // cells that lost their mesh
        return replaced;
    }

    [Fact]
    public void Identical_inputs_produce_bit_identical_patched_worlds()
    {
        List<BrushPlacement> placements = MakeScatteredWorld();
        CsgWorld first = CsgWorld.Build(placements, previousCache: null);

        BrushPlacement before = placements[5];
        BrushPlacement after = before with
        {
            Transform = before.Transform * Matrix4x4.CreateTranslation(-0.7f, 0.1f, 0.2f),
        };
        List<BrushPlacement> edited = With(placements, 5, after);
        ChunkCoord[] dirty = DirtyUnion(before, after);

        CsgWorld a = CsgWorld.Build([.. edited], dirty, first);
        CsgWorld b = CsgWorld.Build([.. edited], dirty, first);

        (float[] verticesA, uint[] indicesA) = a.BuildMesh();
        (float[] verticesB, uint[] indicesB) = b.BuildMesh();
        verticesA.SequenceEqual(verticesB).ShouldBeTrue("patched compiles of identical inputs diverged (vertices)");
        indicesA.SequenceEqual(indicesB).ShouldBeTrue("patched compiles of identical inputs diverged (indices)");

        a.ChunkMeshes.Count.ShouldBe(b.ChunkMeshes.Count);
        for (int i = 0; i < a.ChunkMeshes.Count; i++)
        {
            a.ChunkMeshes[i].Coord.ShouldBe(b.ChunkMeshes[i].Coord);
            IReadOnlyList<ChunkSubmesh> subsA = a.ChunkMeshes[i].Submeshes;
            IReadOnlyList<ChunkSubmesh> subsB = b.ChunkMeshes[i].Submeshes;
            subsA.Count.ShouldBe(subsB.Count, $"per-cell submesh count diverged at {a.ChunkMeshes[i].Coord}");
            for (int s = 0; s < subsA.Count; s++)
            {
                subsA[s].Material.ShouldBe(subsB[s].Material);
                subsA[s].Vertices.SequenceEqual(subsB[s].Vertices).ShouldBeTrue(
                    $"per-cell vertices diverged at {a.ChunkMeshes[i].Coord}");
            }
        }
    }

    [Fact]
    public void Overlap_forming_move_falls_back_and_stays_correct()
    {
        // Dropping one part onto another makes a new overlap pair. The patch
        // path can't place it in the existing carver order, so it must fall back.
        List<BrushPlacement> placements = MakeScatteredWorld();
        CsgWorld firstWorld = CsgWorld.Build(placements, previousCache: null);

        BrushPlacement before = placements[10];
        BrushPlacement joined = before with { Transform = placements[0].Transform };
        List<BrushPlacement> joinedList = With(placements, 10, joined);

        CsgWorld incremental = CsgWorld.Build(joinedList, DirtyUnion(before, joined), firstWorld);
        CsgWorld scratch = CsgWorld.Build(joinedList);

        ShouldBeIdenticalSurfaces(scratch.Surfaces, incremental.Surfaces, "overlap-forming move");

        // Separating them again patches.
        List<BrushPlacement> separatedList = With(joinedList, 10, before);
        CsgWorld separated = CsgWorld.Build(separatedList, DirtyUnion(joined, before), incremental);
        ShouldBeIdenticalSurfaces(CsgWorld.Build(separatedList).Surfaces, separated.Surfaces, "separating move");
    }

    [Fact]
    public void Shared_neighbour_rank_crossing_falls_back_and_stays_identical()
    {
        // j is a slab overlapped by both k and c; c and k never overlap.
        // Dragging c from x[2,4] to x[7,9] crosses k's min-X rank (5), so a
        // fresh sweep finds j's pairs as [k, c], not the carried [c, k].
        // Re-carving j in the carried order fragments it differently, so the
        // two-hop rank gate has to refuse the patch.
        List<BrushPlacement> placements =
        [
            // j: slab x[0,10] y[0,2] z[0,2], min-X rank 0.
            new BrushPlacement(
                Brush.CreateBox(new Vector3(-5f, -1f, -1f), new Vector3(5f, 1f, 1f)),
                Matrix4x4.CreateTranslation(5f, 1f, 1f)),
            // k: box x[5,6] y[1,3] z[0,2], cuts j's top face, min-X rank 5.
            new BrushPlacement(
                Brush.CreateBox(new Vector3(-0.5f, -1f, -1f), new Vector3(0.5f, 1f, 1f)),
                Matrix4x4.CreateTranslation(5.5f, 2f, 1f)),
            // c: box x[2,4] y[1,3] z[0,2], also cuts j's top face, rank 2.
            new BrushPlacement(
                Brush.CreateBox(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f)),
                Matrix4x4.CreateTranslation(3f, 2f, 1f)),
        ];
        CsgWorld first = CsgWorld.Build(placements, previousCache: null);

        // c to x[7,9]: still overlaps j, still clear of k, so every single-hop
        // gate passes, but its min-X rank (7) is now past k's (5).
        BrushPlacement before = placements[2];
        BrushPlacement after = before with { Transform = Matrix4x4.CreateTranslation(8f, 2f, 1f) };
        List<BrushPlacement> edited = With(placements, 2, after);

        CsgWorld incremental = CsgWorld.Build(edited, DirtyUnion(before, after), first);
        CsgWorld scratch = CsgWorld.Build(edited);

        ShouldBeIdenticalSurfaces(scratch.Surfaces, incremental.Surfaces, "shared-neighbour rank crossing");
        (float[] expectedVertices, uint[] expectedIndices) = scratch.BuildMesh();
        (float[] actualVertices, uint[] actualIndices) = incremental.BuildMesh();
        actualVertices.SequenceEqual(expectedVertices).ShouldBeTrue("mesh vertices diverged after the rank crossing");
        actualIndices.SequenceEqual(expectedIndices).ShouldBeTrue("mesh indices diverged after the rank crossing");

        // A rank-stable nudge afterwards patches on top of the fallback's carry.
        BrushPlacement nudged = after with { Transform = Matrix4x4.CreateTranslation(8.3f, 2f, 1f) };
        List<BrushPlacement> edited2 = With(edited, 2, nudged);
        CsgWorld incremental2 = CsgWorld.Build(edited2, DirtyUnion(after, nudged), incremental);
        ShouldBeIdenticalSurfaces(CsgWorld.Build(edited2).Surfaces, incremental2.Surfaces, "post-fallback nudge");
    }

    [Fact]
    public void Placement_count_change_falls_back_and_stays_correct()
    {
        List<BrushPlacement> placements = MakeScatteredWorld();
        CsgWorld first = CsgWorld.Build(placements, previousCache: null);

        List<BrushPlacement> edited = [.. placements];
        edited.Add(new BrushPlacement(
            Brush.CreateBox(new Vector3(-1f), new Vector3(1f)),
            Matrix4x4.CreateTranslation(100f, 50f, -70f)));

        // The dirty set for an addition is the added part's footprint.
        BrushPlacement added = edited[^1];
        CsgWorld incremental = CsgWorld.Build(edited, ChunkGrid.ComputeFootprint(in added), first);
        CsgWorld scratch = CsgWorld.Build(edited);

        ShouldBeIdenticalSurfaces(scratch.Surfaces, incremental.Surfaces, "added part");
        incremental.Chunks.Count.ShouldBe(scratch.Chunks.Count);
    }

#if DEBUG
    [Fact]
    public void Debug_builds_reject_a_change_the_dirty_set_does_not_cover()
    {
        List<BrushPlacement> placements = MakeScatteredWorld();
        CsgWorld first = CsgWorld.Build(placements, previousCache: null);

        // A moved part with an empty dirty set breaks the trusted-diff contract.
        List<BrushPlacement> edited = With(placements, 2, placements[2] with
        {
            Transform = placements[2].Transform * Matrix4x4.CreateTranslation(1f, 0f, 0f),
        });

        Should.Throw<InvalidOperationException>(() => CsgWorld.Build(edited, [], first));
    }
#endif

    [Fact]
    public void Patched_worlds_lazy_caches_seed_the_validated_path_correctly()
    {
        List<BrushPlacement> placements = MakeScatteredWorld();
        CsgWorld world = CsgWorld.Build(placements, previousCache: null);

        // One patched edit first, so the caches have to be derived from the carry.
        BrushPlacement before = placements[4];
        BrushPlacement after = before with
        {
            Transform = before.Transform * Matrix4x4.CreateTranslation(0.5f, 0f, 0.5f),
        };
        List<BrushPlacement> edited = With(placements, 4, after);
        world = CsgWorld.Build(edited, DirtyUnion(before, after), world);

        CsgWorld revalidated = CsgWorld.Build(
            edited, dirtyCells: null, world.CompileCache, world.WeldCache, world.BspCache, world.MeshCache);

        revalidated.CacheStats.ShouldBe(new CsgCacheStats(Hits: edited.Count, Misses: 0));
        revalidated.WeldStats.ShouldBe(new CsgWeldStats(Reused: edited.Count, Welded: 0));
        ShouldBeIdenticalSurfaces(world.Surfaces, revalidated.Surfaces, "revalidation");
        revalidated.BspStats.ShouldNotBeNull().Built.ShouldBe(0);
        revalidated.MeshStats.ShouldNotBeNull().Built.ShouldBe(0);
    }

    [Fact]
    public void Moving_a_part_across_a_cell_boundary_updates_the_grid_exactly()
    {
        List<BrushPlacement> placements = MakeScatteredWorld();
        CsgWorld first = CsgWorld.Build(placements, previousCache: null);

        // Part 0 is near the origin. Move it far away into unoccupied cells.
        BrushPlacement before = placements[0];
        BrushPlacement after = before with
        {
            Transform = Matrix4x4.CreateTranslation(-200f, 90f, -140f),
        };
        List<BrushPlacement> edited = With(placements, 0, after);
        CsgWorld second = CsgWorld.Build(edited, DirtyUnion(before, after), first);
        CsgWorld scratch = CsgWorld.Build(edited);

        second.Chunks.Count.ShouldBe(scratch.Chunks.Count);
        foreach (WorldChunk expected in scratch.Chunks.OrderedChunks)
        {
            second.Chunks.TryGet(expected.Coord, out WorldChunk actual).ShouldBeTrue($"missing cell {expected.Coord}");
            actual.ResidentBrushIndices.ShouldBe(expected.ResidentBrushIndices);
            actual.OwnedBrushIndices.ShouldBe(expected.OwnedBrushIndices);
        }

        foreach (ChunkCoord cell in ChunkGrid.ComputeFootprint(in before))
        {
            if (!scratch.Chunks.TryGet(cell, out _))
                second.Chunks.TryGet(cell, out _).ShouldBeFalse($"vacated cell {cell} still occupied");
        }

        ShouldBeIdenticalSurfaces(scratch.Surfaces, second.Surfaces, "cross-cell move");
    }

    // Parts on a 5x5 site grid at spacing 8 with one layer at y=40, plus a
    // touching pair and an overlapping pair. Site jitter is bounded, so parts
    // never touch across sites and single-part nudges stay isolated.
    private static List<BrushPlacement> MakeScatteredWorld()
    {
        var placements = new List<BrushPlacement>();
        ulong state = 0x5CA77E12EDB0B5EDUL;
        float NextFloat01()
        {
            state = state * 6364136223846793005UL + 1442695040888963407UL;
            return (state >> 40) * (1.0f / (1 << 24));
        }

        for (int gx = 0; gx < 5; gx++)
        {
            for (int gz = 0; gz < 5; gz++)
            {
                var half = new Vector3(
                    0.5f + NextFloat01(), 0.5f + NextFloat01(), 0.5f + NextFloat01());
                float y = (gx + gz) % 2 == 0 ? 0f : 40f;
                placements.Add(new BrushPlacement(
                    Brush.CreateBox(-half, half),
                    Matrix4x4.CreateTranslation(
                        gx * 8f + NextFloat01(), y + NextFloat01(), gz * 8f + NextFloat01())));
            }
        }

        // The touching and overlapping pairs, away from the site grid.
        var pairHalf = new Vector3(1f, 1f, 1f);
        placements.Add(new BrushPlacement(Brush.CreateBox(-pairHalf, pairHalf), Matrix4x4.CreateTranslation(60f, 0f, 0f)));
        placements.Add(new BrushPlacement(Brush.CreateBox(-pairHalf, pairHalf), Matrix4x4.CreateTranslation(62f, 0f, 0f)));
        placements.Add(new BrushPlacement(Brush.CreateBox(-pairHalf, pairHalf), Matrix4x4.CreateTranslation(60f, 0f, 20f)));
        placements.Add(new BrushPlacement(Brush.CreateBox(-pairHalf, pairHalf), Matrix4x4.CreateTranslation(61.2f, 0.4f, 20.6f)));
        return placements;
    }

    // Returns a copy: a compiled world owns the list it was built from.
    private static List<BrushPlacement> With(List<BrushPlacement> source, int index, BrushPlacement placement)
    {
        List<BrushPlacement> next = [.. source];
        next[index] = placement;
        return next;
    }

    // Same rule the scene uses: old and new footprints, sorted.
    private static ChunkCoord[] DirtyUnion(in BrushPlacement before, in BrushPlacement after)
    {
        var cells = new HashSet<ChunkCoord>(ChunkGrid.ComputeFootprint(in before));
        cells.UnionWith(ChunkGrid.ComputeFootprint(in after));
        var dirty = new ChunkCoord[cells.Count];
        cells.CopyTo(dirty);
        Array.Sort(dirty);
        return dirty;
    }

    private static Aabb WorldBounds(List<BrushPlacement> placements)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (BrushPlacement p in placements)
        {
            Aabb b = p.WorldBounds;
            min = Vector3.Min(min, b.Min);
            max = Vector3.Max(max, b.Max);
        }
        return new Aabb(min, max);
    }

    // Bit-exact: both paths run the same code over the same floats, so any
    // difference is a stale carried artifact, not FP noise.
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
