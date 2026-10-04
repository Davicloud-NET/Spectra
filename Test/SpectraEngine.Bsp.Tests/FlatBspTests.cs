using System.Numerics;
using System.Runtime.CompilerServices;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The flat BSP a compiled map bakes must answer the same as the live
/// <see cref="BspTree"/> it was flattened from. Compared exactly, no tolerance.
/// </summary>
public sealed class FlatBspTests
{
    [Fact]
    public void The_flat_node_is_exactly_the_twenty_four_bytes_the_format_casts_into()
    {
        // File bytes are cast into these types, and Plane's layout is not documented.
        Unsafe.SizeOf<Plane>().ShouldBe(16);
        Unsafe.SizeOf<FlatBspNode>().ShouldBe(24);
    }

    [Fact]
    public void Two_flattens_of_one_tree_are_element_identical()
    {
        BspTree tree = BuildTree(ChunkBspEquivalenceTests.ScatteredWorld(structures: 25, seed: 0xBADC0DEBADC0DE01UL));

        FlatBspNode[] first = BspFlattener.Flatten(tree, out int firstRoot);
        FlatBspNode[] second = BspFlattener.Flatten(tree, out int secondRoot);

        first.Length.ShouldBeGreaterThan(100, "fixture too small to be an oracle");
        secondRoot.ShouldBe(firstRoot);
        second.Length.ShouldBe(first.Length);
        for (int i = 0; i < first.Length; i++)
        {
            (second[i].Plane == first[i].Plane).ShouldBeTrue($"node {i} plane diverged");
            second[i].Front.ShouldBe(first[i].Front, $"node {i} front diverged");
            second[i].Back.ShouldBe(first[i].Back, $"node {i} back diverged");
        }
    }

    [Fact]
    public void The_flat_array_mirrors_the_live_tree_in_pre_order_with_the_front_child_first()
    {
        // Scattered, not the dense grid: that unions into one block with six
        // splitters, too few to show an emission order.
        BspTree tree = BuildTree(ChunkBspEquivalenceTests.ScatteredWorld(structures: 30, seed: 0xC0FFEE0DDBA5EBA1UL));
        FlatBspNode[] nodes = BspFlattener.Flatten(tree, out int rootIndex);

        nodes.Length.ShouldBeGreaterThan(100, "fixture too small to be an oracle");
        rootIndex.ShouldBe(0, "a tree with a split at its root emits that split first");

        var stack = new Stack<(BspNode Live, int Child)>();
        stack.Push((tree.Root, rootIndex));
        int internalNodes = 0;

        while (stack.Count > 0)
        {
            (BspNode live, int child) = stack.Pop();

            if (live.IsLeaf)
            {
                child.ShouldBe(live.IsSolid ? FlatBspNode.SolidLeaf : FlatBspNode.EmptyLeaf,
                    "a leaf must encode as its own child code and occupy no array slot");
                continue;
            }

            FlatBspNode.IsLeaf(child).ShouldBeFalse("an internal node must encode as an array index");
            internalNodes++;

            FlatBspNode node = nodes[child];
            (node.Plane == live.Plane).ShouldBeTrue($"splitter plane diverged at node {child}");

            // Pre-order, front first: the front child is the next slot and the
            // back subtree starts after the whole front one.
            if (!live.Front!.IsLeaf)
                node.Front.ShouldBe(child + 1, $"node {child} front is not the next slot");
            if (!live.Back!.IsLeaf)
                node.Back.ShouldBeGreaterThan(child, $"node {child} back does not point forward");

            stack.Push((live.Back!, node.Back));
            stack.Push((live.Front!, node.Front));
        }

        internalNodes.ShouldBe(nodes.Length, "the array holds exactly the tree's internal nodes");
    }

    [Fact]
    public void Flat_containment_matches_the_live_tree_over_a_dense_grid_world() =>
        AssertContainmentIdentical(ChunkBspEquivalenceTests.DenseGridWorld(), samplesPerAxis: 30);

    [Fact]
    public void Flat_containment_matches_the_live_tree_over_a_scattered_parts_world() =>
        AssertContainmentIdentical(
            ChunkBspEquivalenceTests.ScatteredWorld(structures: 50, seed: 0xC0FFEE0DDBA5EBA1UL),
            samplesPerAxis: 26);

    [Fact]
    public void Probes_lying_exactly_on_splitter_planes_take_the_same_side_as_the_live_tree()
    {
        // Integer probes on an integer lattice: the only place DotCoordinate
        // returns exactly 0f, so the only place `>= 0f` differs from `> 0f`.
        List<BrushPlacement> placements = IntegerLatticeWorld();
        (BspTree live, FlatBspTree flat) = BuildPair(placements);
        FlatBspNode[] nodes = flat.Nodes.ToArray();
        nodes.Length.ShouldBeGreaterThan(20, "the lattice collapsed, too few planes to probe");

        int probes = 0, solid = 0, onPlane = 0, mismatches = 0;
        Vector3 firstMismatch = default;

        for (int x = -4; x <= 19; x++)
        {
            for (int y = -4; y <= 16; y++)
            {
                for (int z = -4; z <= 16; z++)
                {
                    var point = new Vector3(x, y, z);
                    bool expected = live.ContainsPoint(point);
                    if (expected)
                        solid++;
                    if (flat.ContainsPoint(point) != expected && mismatches++ == 0)
                        firstMismatch = point;
                    onPlane += CountExactPlaneHits(nodes, flat.RootIndex, point);
                    probes++;
                }
            }
        }

        onPlane.ShouldBeGreaterThan(0,
            "no probe produced an exact zero plane distance, so the >= convention was never exercised");
        solid.ShouldBeGreaterThan(0, "no probe landed in solid, vacuous oracle");
        solid.ShouldBeLessThan(probes, "every probe landed in solid, vacuous oracle");
        mismatches.ShouldBe(0, $"containment diverged {mismatches} times, first at {firstMismatch}");
    }

    [Fact]
    public void Flat_raycasts_match_the_live_tree_over_a_dense_grid_world() =>
        AssertRaycastsIdentical(
            ChunkBspEquivalenceTests.DenseGridWorld(), rayCount: 400, seed: 0xFACEFEEDDEADF00DUL);

    [Fact]
    public void Flat_raycasts_match_the_live_tree_over_a_scattered_parts_world() =>
        AssertRaycastsIdentical(
            ChunkBspEquivalenceTests.ScatteredWorld(structures: 50, seed: 0xC0FFEE0DDBA5EBA1UL),
            rayCount: 400, seed: 0x0DDBA11DEADBEEFUL);

    [Fact]
    public void Axis_aligned_rays_report_the_same_entry_normal_as_the_live_tree()
    {
        // Axis-aligned rays meet each surface head on, so a flipped entry normal shows.
        List<BrushPlacement> placements = IntegerLatticeWorld();
        (BspTree live, FlatBspTree flat) = BuildPair(placements);

        Vector3[] directions =
        [
            Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY,
            -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ,
        ];

        int hits = 0, rays = 0;
        for (float a = -3.5f; a <= 15f; a += 0.75f)
        {
            for (float b = -3.25f; b <= 15f; b += 0.75f)
            {
                foreach (Vector3 direction in directions)
                {
                    Vector3 origin = direction.X != 0f ? new Vector3(-40f * direction.X, a, b)
                        : direction.Y != 0f ? new Vector3(a, -40f * direction.Y, b)
                        : new Vector3(a, b, -40f * direction.Z);

                    if (CompareRay(live, flat, origin, direction, 120f, $"axis ray from {origin} along {direction}"))
                        hits++;
                    rays++;
                }
            }
        }

        hits.ShouldBeGreaterThan(0, "no axis ray hit solid, vacuous oracle");
        hits.ShouldBeLessThan(rays, "every axis ray hit solid, vacuous oracle");
    }

    [Fact]
    public void Rays_that_start_inside_solid_report_the_same_immediate_hit()
    {
        List<BrushPlacement> placements =
            ChunkBspEquivalenceTests.ScatteredWorld(structures: 20, seed: 0xBADC0DEBADC0DE01UL);
        (BspTree live, FlatBspTree flat) = BuildPair(placements);

        Vector3 direction = Vector3.Normalize(new Vector3(1f, 2f, 3f));
        foreach (BrushPlacement placement in placements)
        {
            Vector3 origin = placement.WorldBounds.Center;
            bool expected = live.Raycast(origin, direction, 50f, out BspRaycastHit expectedHit);
            bool actual = flat.Raycast(origin, direction, 50f, out BspRaycastHit actualHit);

            expected.ShouldBeTrue($"the live tree missed from inside {origin}");
            actual.ShouldBe(expected, $"hit flag diverged from inside {origin}");
            actualHit.ShouldBe(expectedHit, $"inside-solid hit diverged at {origin}");
        }
    }

    [Fact]
    public void Degenerate_ray_arguments_are_refused_exactly_as_the_live_tree_refuses_them()
    {
        (BspTree live, FlatBspTree flat) = BuildPair(IntegerLatticeWorld());
        var origin = new Vector3(-40f, 0f, 0f);

        flat.Raycast(origin, Vector3.Zero, 100f, out BspRaycastHit zeroDirection)
            .ShouldBe(live.Raycast(origin, Vector3.Zero, 100f, out _));
        zeroDirection.ShouldBe(default(BspRaycastHit));

        flat.Raycast(origin, Vector3.UnitX, 0f, out BspRaycastHit zeroDistance)
            .ShouldBe(live.Raycast(origin, Vector3.UnitX, 0f, out _));
        zeroDistance.ShouldBe(default(BspRaycastHit));

        CompareRay(live, flat, origin, new Vector3(17f, 0f, 0f), 100f, "unnormalised direction");
    }

    [Fact]
    public void A_tree_that_is_one_empty_leaf_flattens_to_no_nodes_and_contains_nothing()
    {
        BspTree tree = BspTree.BuildFromSurfaces([]);
        tree.Root.IsLeaf.ShouldBeTrue();

        FlatBspNode[] nodes = BspFlattener.Flatten(tree, out int rootIndex);
        nodes.ShouldBeEmpty();
        rootIndex.ShouldBe(FlatBspNode.EmptyLeaf);

        var flat = new FlatBspTree(nodes, rootIndex);
        flat.NodeCount.ShouldBe(0);
        flat.ContainsPoint(Vector3.Zero).ShouldBe(tree.ContainsPoint(Vector3.Zero));
        flat.ContainsPoint(new Vector3(1e6f, -3f, 7f)).ShouldBeFalse();
        flat.Raycast(Vector3.Zero, Vector3.UnitX, 1000f, out BspRaycastHit hit).ShouldBeFalse();
        hit.ShouldBe(default(BspRaycastHit));
    }

    [Fact]
    public void A_root_that_is_a_solid_leaf_reports_solid_everywhere_and_hits_immediately()
    {
        var flat = new FlatBspTree(Array.Empty<FlatBspNode>(), FlatBspNode.SolidLeaf);

        flat.ContainsPoint(new Vector3(12f, -4f, 900f)).ShouldBeTrue();
        flat.Raycast(Vector3.Zero, Vector3.UnitX, 10f, out BspRaycastHit hit).ShouldBeTrue();
        hit.Point.ShouldBe(Vector3.Zero);
        hit.Normal.ShouldBe(-Vector3.UnitX);
        hit.Distance.ShouldBe(0f);
    }

    [Fact]
    public void A_trace_deeper_than_the_inline_frame_stack_still_finds_the_first_solid()
    {
        // Hand-built: no fixture makes one ray cross more splitters than the
        // trace's stackalloc'd frames. Each node sends the near side into the
        // next, so the ray defers one far side per node.
        const int depth = 100;
        var nodes = new FlatBspNode[depth];
        for (int i = 0; i < depth; i++)
        {
            var plane = new Plane(Vector3.UnitX, -(depth - i));
            nodes[i] = i < depth - 1
                ? new FlatBspNode(plane, FlatBspNode.EmptyLeaf, i + 1)
                : new FlatBspNode(plane, FlatBspNode.SolidLeaf, FlatBspNode.EmptyLeaf);
        }

        var flat = new FlatBspTree(nodes, 0);
        flat.ContainsPoint(Vector3.Zero).ShouldBeFalse();
        flat.ContainsPoint(new Vector3(1.5f, 0f, 0f)).ShouldBeTrue();

        flat.Raycast(Vector3.Zero, Vector3.UnitX, 200f, out BspRaycastHit hit).ShouldBeTrue();
        hit.Point.ShouldBe(new Vector3(1f, 0f, 0f));
        hit.Normal.ShouldBe(-Vector3.UnitX, "the entry normal faces the side the ray came from");
        hit.Distance.ShouldBe(1f);
    }

    [Fact]
    public void A_root_index_outside_the_node_block_is_refused()
    {
        FlatBspNode[] nodes = [new(new Plane(Vector3.UnitX, 0f), FlatBspNode.SolidLeaf, FlatBspNode.EmptyLeaf)];

        Should.Throw<ArgumentOutOfRangeException>(() => new FlatBspTree(nodes, 1));
        Should.Throw<ArgumentOutOfRangeException>(() => new FlatBspTree(nodes, -3));
        Should.NotThrow(() => new FlatBspTree(nodes, 0));
        Should.NotThrow(() => new FlatBspTree(nodes, FlatBspNode.SolidLeaf));
    }

    private static Brush Box(float h) => Brush.CreateBox(new Vector3(-h), new Vector3(h));

    private static Matrix4x4 Translation(float x, float y, float z) => Matrix4x4.CreateTranslation(x, y, z);

    // 3x3x3 half-extent-2 cubes at spacing 6, each with a partner offset three
    // units along x so it gets carved. The gaps keep the union from collapsing
    // into one block, and every face and seam lands on an integer coordinate.
    private static List<BrushPlacement> IntegerLatticeWorld()
    {
        var placements = new List<BrushPlacement>(54);
        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                for (int z = 0; z < 3; z++)
                {
                    placements.Add(new BrushPlacement(Box(2f), Translation(x * 6, y * 6, z * 6)));
                    placements.Add(new BrushPlacement(Box(2f), Translation(x * 6 + 3, y * 6, z * 6)));
                }
            }
        }
        return placements;
    }

    private static BspTree BuildTree(IReadOnlyList<BrushPlacement> placements) =>
        BspTree.BuildFromSurfaces(CsgWorld.Build(placements).Surfaces);

    private static (BspTree Live, FlatBspTree Flat) BuildPair(IReadOnlyList<BrushPlacement> placements)
    {
        BspTree live = BuildTree(placements);
        FlatBspNode[] nodes = BspFlattener.Flatten(live, out int rootIndex);
        return (live, new FlatBspTree(nodes, rootIndex));
    }

    // Counts nodes on the descent whose plane distance is exactly zero.
    // Without one, `>=` and `>` agree and the sidedness test proves nothing.
    private static int CountExactPlaneHits(FlatBspNode[] nodes, int rootIndex, Vector3 point)
    {
        int count = 0;
        int index = rootIndex;
        while (index >= 0)
        {
            FlatBspNode node = nodes[index];
            float d = Plane.DotCoordinate(node.Plane, point);
            if (d == 0f)
                count++;
            index = d >= 0f ? node.Front : node.Back;
        }
        return count;
    }

    // A lattice over the expanded bounds, plus a cluster per placement so a
    // sparse world still gets solid probes.
    private static void AssertContainmentIdentical(IReadOnlyList<BrushPlacement> placements, int samplesPerAxis)
    {
        (BspTree live, FlatBspTree flat) = BuildPair(placements);
        Aabb bounds = WorldBounds(placements).Expanded(3f);

        int probes = 0, solid = 0, mismatches = 0;
        Vector3 firstMismatch = default;

        void Probe(Vector3 point)
        {
            bool expected = live.ContainsPoint(point);
            if (expected)
                solid++;
            if (flat.ContainsPoint(point) != expected && mismatches++ == 0)
                firstMismatch = point;
            probes++;
        }

        for (int i = 0; i < samplesPerAxis; i++)
        {
            float x = Lerp(bounds.Min.X, bounds.Max.X, i, samplesPerAxis);
            for (int j = 0; j < samplesPerAxis; j++)
            {
                float y = Lerp(bounds.Min.Y, bounds.Max.Y, j, samplesPerAxis);
                for (int k = 0; k < samplesPerAxis; k++)
                    Probe(new Vector3(x, y, Lerp(bounds.Min.Z, bounds.Max.Z, k, samplesPerAxis)));
            }
        }

        foreach (BrushPlacement placement in placements)
        {
            Aabb box = placement.WorldBounds.Expanded(0.5f);
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    for (int k = 0; k < 3; k++)
                        Probe(new Vector3(
                            Lerp(box.Min.X, box.Max.X, i, 3),
                            Lerp(box.Min.Y, box.Max.Y, j, 3),
                            Lerp(box.Min.Z, box.Max.Z, k, 3)));
        }

        probes.ShouldBeGreaterThanOrEqualTo(15_000, "probe set too sparse to be an oracle");
        solid.ShouldBeGreaterThan(0, "no probe landed in solid, vacuous oracle");
        solid.ShouldBeLessThan(probes, "every probe landed in solid, vacuous oracle");
        mismatches.ShouldBe(0, $"ContainsPoint diverged {mismatches} times, first at {firstMismatch}");
    }

    private static float Lerp(float min, float max, int step, int count) =>
        count <= 1 ? min : min + (max - min) * step / (count - 1);

    private static void AssertRaycastsIdentical(IReadOnlyList<BrushPlacement> placements, int rayCount, ulong seed)
    {
        (BspTree live, FlatBspTree flat) = BuildPair(placements);
        Aabb bounds = WorldBounds(placements).Expanded(8f);
        Vector3 size = bounds.Size;
        float maxDistance = size.Length() + 16f;

        ulong state = seed;
        float NextFloat01()
        {
            state = state * 6364136223846793005UL + 1442695040888963407UL;
            return (state >> 40) * (1.0f / (1 << 24));
        }
        Vector3 NextDirection()
        {
            while (true)
            {
                var v = new Vector3(
                    NextFloat01() * 2f - 1f, NextFloat01() * 2f - 1f, NextFloat01() * 2f - 1f);
                float lengthSquared = v.LengthSquared();
                if (lengthSquared is > 1e-4f and <= 1f)
                    return v / MathF.Sqrt(lengthSquared);
            }
        }

        int hits = 0;
        for (int r = 0; r < rayCount; r++)
        {
            Vector3 origin = bounds.Min + size * new Vector3(NextFloat01(), NextFloat01(), NextFloat01());
            Vector3 direction = NextDirection();
            if (CompareRay(live, flat, origin, direction, maxDistance, $"ray #{r} from {origin} along {direction}"))
                hits++;
        }

        hits.ShouldBeGreaterThan(0, "no ray hit solid, vacuous oracle");
        hits.ShouldBeLessThan(rayCount, "every ray hit solid, vacuous oracle");
    }

    // Returns whether the ray hit, so callers can check some rays did.
    private static bool CompareRay(
        BspTree live, FlatBspTree flat, Vector3 origin, Vector3 direction, float maxDistance, string context)
    {
        bool expected = live.Raycast(origin, direction, maxDistance, out BspRaycastHit expectedHit);
        bool actual = flat.Raycast(origin, direction, maxDistance, out BspRaycastHit actualHit);

        actual.ShouldBe(expected, $"hit flag diverged ({context})");
        if (!expected)
            return false;

        actualHit.ShouldBe(expectedHit, $"hit diverged ({context}); live {expectedHit}, flat {actualHit}");
        return true;
    }

    private static Aabb WorldBounds(IReadOnlyList<BrushPlacement> placements)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (BrushPlacement placement in placements)
        {
            Aabb b = placement.WorldBounds;
            min = Vector3.Min(min, b.Min);
            max = Vector3.Max(max, b.Max);
        }
        return new Aabb(min, max);
    }
}
