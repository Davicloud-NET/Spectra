using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Spectra.Kitchen.Maps;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;

// Static-world compile and BSP query benchmark.
//
//   dotnet run -c Release --project Benchmarks/CsgBench                # all scenarios
//   dotnet run -c Release --project Benchmarks/CsgBench -- grid query  # a subset
//
// Filters match scenario-name prefixes: grid | floorplan | tower | query |
// incremental | openworld | bake.
//
// Baselines are only comparable between runs whose printed protocol lines match.

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

// The perf gate uses 3 too. Keep them equal or baselines stop comparing.
const int TimedReps = 3;

string[] scenarios = ["grid", "floorplan", "tower", "query", "incremental", "openworld", "bake"];
foreach (string arg in args)
{
    if (!scenarios.Any(s => s.StartsWith(arg, StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine($"Unknown scenario filter '{arg}'. Filters (prefixes ok): {string.Join(" | ", scenarios)}");
        return 2;
    }
}

bool ShouldRun(string scenario) =>
    args.Length == 0 || args.Any(a => scenario.StartsWith(a, StringComparison.OrdinalIgnoreCase));

Console.WriteLine($"ProcessorCount: {Environment.ProcessorCount}");
Console.WriteLine($"GC server mode: {System.Runtime.GCSettings.IsServerGC}");

// The csproj turns tiering off. Echo the runtime knob so an environment
// override shows up in the output it skewed.
object? tieredKnob = AppContext.GetData("System.Runtime.TieredCompilation");
Console.WriteLine($"TieredCompilation: {tieredKnob ?? "runtime default (enabled)"}");
Console.WriteLine($"Protocol: median of {TimedReps} timed reps, 2 untimed warmups, forced blocking GC between reps");

if (ShouldRun("grid") || ShouldRun("floorplan") || ShouldRun("tower"))
{
    Console.WriteLine();
    Console.WriteLine("scenario   | brushes | surfaces | meshVerts | meshIdx  | carve ms | snap ms | weld ms | bsp ms  | mesh ms | sum ms  | e2e ms   | alloc MiB");
    Console.WriteLine("-----------|---------|----------|-----------|----------|----------|---------|---------|---------|---------|---------|----------|----------");
}

if (ShouldRun("grid"))
    foreach (int k in new[] { 2, 3, 4, 6, 8, 10, 13 })
        RunConfig($"grid k={k}", MakeGrid(k));

if (ShouldRun("floorplan"))
    foreach (int k in new[] { 5, 10, 20, 32, 48 })
        RunConfig($"floor k={k}", MakeFloorplan(k));

if (ShouldRun("tower"))
    foreach (int n in new[] { 2, 4, 8, 16, 32 })
        RunConfig($"tower N={n}", MakeTower(n));

if (ShouldRun("query"))
    RunQueryBench();

if (ShouldRun("incremental"))
    RunIncrementalBench();

if (ShouldRun("openworld"))
    RunOpenWorldBench();

if (ShouldRun("bake"))
    RunBakeBench();

return 0;

static List<BrushPlacement> MakeGrid(int k)
{
    // k*k*k cubes, size 2.0, spacing 1.8 -> each overlaps its 6 neighbors by 0.2.
    const float size = 2.0f, spacing = 1.8f, half = size * 0.5f;
    var list = new List<BrushPlacement>(k * k * k);
    for (int x = 0; x < k; x++)
        for (int y = 0; y < k; y++)
            for (int z = 0; z < k; z++)
            {
                var brush = Brush.CreateBox(new Vector3(-half), new Vector3(half));
                var t = Matrix4x4.CreateTranslation(x * spacing, y * spacing, z * spacing);
                list.Add(new BrushPlacement(brush, t));
            }
    return list;
}

static List<BrushPlacement> MakeFloorplan(int k)
{
    // k*k floor tiles 4x0.5x4 at spacing 3.9 (0.1 pairwise overlap) plus a
    // perimeter of wall boxes (one per perimeter tile, 4k walls total).
    const float tile = 4.0f, spacing = 3.9f, tHalf = tile * 0.5f;
    const float floorHalfH = 0.25f;
    const float wallH = 3.0f, wallHalfH = wallH * 0.5f, wallHalfT = 0.25f;

    var list = new List<BrushPlacement>(k * k + 4 * k);

    for (int i = 0; i < k; i++)
        for (int j = 0; j < k; j++)
        {
            var brush = Brush.CreateBox(
                new Vector3(-tHalf, -floorHalfH, -tHalf),
                new Vector3(tHalf, floorHalfH, tHalf));
            list.Add(new BrushPlacement(brush, Matrix4x4.CreateTranslation(i * spacing, 0f, j * spacing)));
        }

    float minEdge = -tHalf;
    float maxEdge = (k - 1) * spacing + tHalf;
    // Wall bottoms at y=0 overlap the floor slab (top at +0.25) by 0.25.
    float wallY = wallHalfH;

    for (int i = 0; i < k; i++)
    {
        var wx = Brush.CreateBox(
            new Vector3(-tHalf, -wallHalfH, -wallHalfT),
            new Vector3(tHalf, wallHalfH, wallHalfT));
        list.Add(new BrushPlacement(wx, Matrix4x4.CreateTranslation(i * spacing, wallY, minEdge)));
        var wx2 = Brush.CreateBox(
            new Vector3(-tHalf, -wallHalfH, -wallHalfT),
            new Vector3(tHalf, wallHalfH, wallHalfT));
        list.Add(new BrushPlacement(wx2, Matrix4x4.CreateTranslation(i * spacing, wallY, maxEdge)));

        var wz = Brush.CreateBox(
            new Vector3(-wallHalfT, -wallHalfH, -tHalf),
            new Vector3(wallHalfT, wallHalfH, tHalf));
        list.Add(new BrushPlacement(wz, Matrix4x4.CreateTranslation(minEdge, wallY, i * spacing)));
        var wz2 = Brush.CreateBox(
            new Vector3(-wallHalfT, -wallHalfH, -tHalf),
            new Vector3(wallHalfT, wallHalfH, tHalf));
        list.Add(new BrushPlacement(wz2, Matrix4x4.CreateTranslation(maxEdge, wallY, i * spacing)));
    }

    return list;
}

static List<BrushPlacement> MakeTower(int n)
{
    // N size-2 cubes on one spot, each shifted 0.1 on Y. Worst case for carve.
    var list = new List<BrushPlacement>(n);
    for (int i = 0; i < n; i++)
    {
        var brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        list.Add(new BrushPlacement(brush, Matrix4x4.CreateTranslation(0f, i * 0.1f, 0f)));
    }
    return list;
}

static void RunConfig(string name, List<BrushPlacement> placements)
{
    // Two warmup rounds: one does not always spin the thread pool up on wide machines.
    for (int warm = 0; warm < 2; warm++)
    {
        var s = Csg.Carve(placements);
        s = VertexSnapper.Snap(s);
        s = TJunctionWelder.Weld(s);
        _ = BspTree.BuildFromSurfaces(s);
        var w = CsgWorld.Build(placements);
        _ = w.BuildMesh();
    }

    const int reps = TimedReps;
    var carve = new double[reps];
    var snap = new double[reps];
    var weld = new double[reps];
    var bsp = new double[reps];
    var mesh = new double[reps];
    var e2e = new double[reps];
    int surfaces = 0, meshVerts = 0, meshIdx = 0;

    for (int r = 0; r < reps; r++)
    {
        Collect();

        var sw = Stopwatch.StartNew();
        Polygon[] carved = Csg.Carve(placements);
        sw.Stop(); carve[r] = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        Polygon[] snapped = VertexSnapper.Snap(carved);
        sw.Stop(); snap[r] = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        Polygon[] welded = TJunctionWelder.Weld(snapped);
        sw.Stop(); weld[r] = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        BspTree tree = BspTree.BuildFromSurfaces(welded);
        sw.Stop(); bsp[r] = sw.Elapsed.TotalMilliseconds;

        // End-to-end cross-check
        Collect();
        sw.Restart();
        CsgWorld world = CsgWorld.Build(placements);
        sw.Stop(); e2e[r] = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        (float[] verts, uint[] idx) = world.BuildMesh();
        sw.Stop(); mesh[r] = sw.Elapsed.TotalMilliseconds;

        surfaces = world.Surfaces.Count;
        meshVerts = verts.Length / 8;
        meshIdx = idx.Length;
        GC.KeepAlive(tree);
    }

    // Allocation is measured outside the timed reps.
    Collect();
    long allocBefore = GC.GetTotalAllocatedBytes(precise: true);
    {
        var w = CsgWorld.Build(placements);
        _ = w.BuildMesh();
    }
    long allocBytes = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;

    Collect();

    double carveMed = Median(carve), snapMed = Median(snap), weldMed = Median(weld);
    double bspMed = Median(bsp), meshMed = Median(mesh), e2eMed = Median(e2e);
    double sum = carveMed + snapMed + weldMed + bspMed + meshMed;

    Console.WriteLine(
        $"{name,-10} | {placements.Count,7} | {surfaces,8} | {meshVerts,9} | {meshIdx,8} | " +
        $"{carveMed,8:F2} | {snapMed,7:F2} | {weldMed,7:F2} | {bspMed,7:F2} | {meshMed,7:F2} | {sum,7:F2} | {e2eMed,8:F2} | {allocBytes / (1024.0 * 1024.0),8:F1}");
}

// Regression guard for BSP splitter and routing changes: tree shape may
// change, the checksums must not.
static void RunQueryBench()
{
    Console.WriteLine();
    Console.WriteLine("QUERY (floorplan k=20) — splitter/routing changes may reshape the per-cell trees; the checksums must not change.");

    List<BrushPlacement> placements = MakeFloorplan(20);
    CsgWorld world = CsgWorld.Build(placements);

    // A surface can sit in several cells' trees, so node totals overcount.
    TreeStats stats = MeasureCellTrees(world);
    Console.WriteLine(
        $"  trees: {world.Chunks.Count:N0} cells, {stats.Nodes:N0} nodes, {stats.Leaves:N0} leaves, " +
        $"avg leaf depth {stats.AvgLeafDepth:F1}, max depth {stats.MaxDepth}");

    // Margin so some points land outside the world.
    Aabb bounds = WorldBounds(placements).Expanded(2f);
    Vector3 min = bounds.Min, size = bounds.Size;

    const int PointCount = 1_000_000;
    const int RayCount = 100_000;
    const float RayMaxDistance = 40f;

    // Own LCG: System.Random is not stable across runtimes.
    var lcg = new Lcg(0x5EEDC0DE12345678UL);
    var points = new Vector3[PointCount];
    for (int i = 0; i < points.Length; i++)
        points[i] = min + size * new Vector3(lcg.NextFloat01(), lcg.NextFloat01(), lcg.NextFloat01());

    var origins = new Vector3[RayCount];
    var directions = new Vector3[RayCount];
    for (int i = 0; i < RayCount; i++)
    {
        origins[i] = min + size * new Vector3(lcg.NextFloat01(), lcg.NextFloat01(), lcg.NextFloat01());
        directions[i] = lcg.NextDirection();
    }

    (double pointSeconds, long inside) = MeasurePointQueries(world, points);
    Console.WriteLine(
        $"  ContainsPoint: {PointCount:N0} calls in {pointSeconds * 1000:F1} ms -> " +
        $"{PointCount / pointSeconds / 1e6:F2} M calls/s | checksum inside={inside:N0} ({100.0 * inside / PointCount:F2} %)");

    (double raySeconds, long hits) = MeasureRayQueries(world, origins, directions, RayMaxDistance);
    Console.WriteLine(
        $"  Raycast:       {RayCount:N0} calls in {raySeconds * 1000:F1} ms -> " +
        $"{RayCount / raySeconds / 1e6:F2} M calls/s | checksum hits={hits:N0} ({100.0 * hits / RayCount:F2} %)");
}

static (double Seconds, long Inside) MeasurePointQueries(CsgWorld world, Vector3[] points)
{
    long inside = 0;
    for (int i = 0; i < points.Length; i++)       // warmup, also fixes the checksum
        if (world.ContainsPoint(points[i])) inside++;

    var elapsed = new double[TimedReps];
    for (int r = 0; r < TimedReps; r++)
    {
        Collect();
        long count = 0;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < points.Length; i++)
            if (world.ContainsPoint(points[i])) count++;
        sw.Stop();
        elapsed[r] = sw.Elapsed.TotalSeconds;
        if (count != inside)
            Console.WriteLine($"  WARNING: ContainsPoint checksum unstable across reps ({count} vs {inside})!");
    }
    return (Median(elapsed), inside);
}

static (double Seconds, long Hits) MeasureRayQueries(
    CsgWorld world, Vector3[] origins, Vector3[] directions, float maxDistance)
{
    long hits = 0;
    for (int i = 0; i < origins.Length; i++)      // warmup, also fixes the checksum
        if (world.Raycast(origins[i], directions[i], maxDistance, out _)) hits++;

    var elapsed = new double[TimedReps];
    for (int r = 0; r < TimedReps; r++)
    {
        Collect();
        long count = 0;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < origins.Length; i++)
            if (world.Raycast(origins[i], directions[i], maxDistance, out _)) count++;
        sw.Stop();
        elapsed[r] = sw.Elapsed.TotalSeconds;
        if (count != hits)
            Console.WriteLine($"  WARNING: Raycast checksum unstable across reps ({count} vs {hits})!");
    }
    return (Median(elapsed), hits);
}

static TreeStats MeasureCellTrees(CsgWorld world)
{
    int nodes = 0, leaves = 0, maxDepth = 0;
    double leafDepthSum = 0;
    foreach (WorldChunk chunk in world.Chunks.OrderedChunks)
    {
        TreeStats s = MeasureTree(chunk.Bsp);
        nodes += s.Nodes;
        leaves += s.Leaves;
        if (s.MaxDepth > maxDepth) maxDepth = s.MaxDepth;
        leafDepthSum += s.AvgLeafDepth * s.Leaves;
    }
    return new TreeStats(nodes, leaves, maxDepth, leaves == 0 ? 0.0 : leafDepthSum / leaves);
}

// Explicit stack: a degenerate splitter order can make the tree very deep.
static TreeStats MeasureTree(BspTree tree)
{
    int nodes = 0, leaves = 0, maxDepth = 0;
    long leafDepthSum = 0;

    var stack = new Stack<(BspNode Node, int Depth)>();
    stack.Push((tree.Root, 0));
    while (stack.Count > 0)
    {
        (BspNode node, int depth) = stack.Pop();
        nodes++;
        if (node.IsLeaf)
        {
            leaves++;
            leafDepthSum += depth;
            if (depth > maxDepth) maxDepth = depth;
        }
        else
        {
            stack.Push((node.Front!, depth + 1));
            stack.Push((node.Back!, depth + 1));
        }
    }

    return new TreeStats(nodes, leaves, maxDepth, leaves == 0 ? 0.0 : (double)leafDepthSum / leaves);
}

static Aabb WorldBounds(List<BrushPlacement> placements)
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

// A one-brush edit: full recompile against the carve cache.
static void RunIncrementalBench()
{
    Console.WriteLine();
    Console.WriteLine("INCREMENTAL (grid k=10) — recompile after translating ONE center brush by 0.3.");

    const int K = 10;
    List<BrushPlacement> placements = MakeGrid(K);

    // Built through the caching path so it seeds the cache the edit consumes.
    CsgWorld before = CsgWorld.Build(placements, previousCache: null);
    _ = before.BuildMesh();
    CsgCompileCache cache = before.CompileCache!;

    int center = (K / 2) * K * K + (K / 2) * K + (K / 2);
    var edited = new List<BrushPlacement>(placements);
    edited[center] = edited[center] with
    {
        Transform = edited[center].Transform * Matrix4x4.CreateTranslation(0.3f, 0f, 0f),
    };

    const int reps = TimedReps;

    // Full recompile, no cache: the baseline.
    for (int warm = 0; warm < 2; warm++)
    {
        CsgWorld w = CsgWorld.Build(edited);
        _ = w.BuildMesh();
    }

    var full = new double[reps];
    for (int r = 0; r < reps; r++)
    {
        Collect();
        var sw = Stopwatch.StartNew();
        CsgWorld w = CsgWorld.Build(edited);
        (float[] verts, uint[] idx) = w.BuildMesh();
        sw.Stop();
        full[r] = sw.Elapsed.TotalMilliseconds;
        GC.KeepAlive(verts);
        GC.KeepAlive(idx);
    }

    Collect();
    long allocBefore = GC.GetTotalAllocatedBytes(precise: true);
    {
        CsgWorld w = CsgWorld.Build(edited);
        _ = w.BuildMesh();
    }
    long fullAllocBytes = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;

    // Cached recompile. The cache is immutable, so every rep reuses it.
    for (int warm = 0; warm < 2; warm++)
    {
        CsgWorld w = CsgWorld.Build(edited, cache);
        _ = w.BuildMesh();
    }

    var cachedCarve = new double[reps];
    var cachedTotal = new double[reps];
    CsgCacheStats stats = default;
    for (int r = 0; r < reps; r++)
    {
        Collect();
        var sw = Stopwatch.StartNew();
        Polygon[] carved = Csg.Carve(edited, cache, out _, out stats);
        sw.Stop();
        cachedCarve[r] = sw.Elapsed.TotalMilliseconds;
        GC.KeepAlive(carved);

        Collect();
        sw.Restart();
        CsgWorld w = CsgWorld.Build(edited, cache);
        (float[] verts, uint[] idx) = w.BuildMesh();
        sw.Stop();
        cachedTotal[r] = sw.Elapsed.TotalMilliseconds;
        GC.KeepAlive(verts);
        GC.KeepAlive(idx);
    }

    Collect();
    allocBefore = GC.GetTotalAllocatedBytes(precise: true);
    {
        CsgWorld w = CsgWorld.Build(edited, cache);
        _ = w.BuildMesh();
    }
    long cachedAllocBytes = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;

    // The cached recompile must be bit-identical to the full one.
    {
        (float[] fullVerts, uint[] fullIdx) = CsgWorld.Build(edited).BuildMesh();
        (float[] cachedVerts, uint[] cachedIdx) = CsgWorld.Build(edited, cache).BuildMesh();
        if (!cachedVerts.SequenceEqual(fullVerts) || !cachedIdx.SequenceEqual(fullIdx))
            Console.WriteLine("  WARNING: cached recompile diverged from the full recompile!");
    }

    Console.WriteLine(
        $"  full recompile:   {Median(full),6:F2} ms | alloc {fullAllocBytes / (1024.0 * 1024.0),5:F1} MiB");
    Console.WriteLine(
        $"  cached recompile: {Median(cachedTotal),6:F2} ms | alloc {cachedAllocBytes / (1024.0 * 1024.0),5:F1} MiB | " +
        $"carve {Median(cachedCarve):F2} ms | {stats.Hits} hits / {stats.Misses} misses");
    GC.KeepAlive(before);
}

// Scattered parts at three world sizes, constant density. Moving one part
// must cost the same at every size.
static void RunOpenWorldBench()
{
    Console.WriteLine();
    Console.WriteLine("OPENWORLD — scattered parts at constant density (85% isolated / 10% touching pairs / 5% overlapping clusters),");
    Console.WriteLine("half of every world placed around +8,000 units (position-independent precision). HEADLINE: 'edit' columns —");
    Console.WriteLine("recompile after moving ONE isolated part by 0.3 with the previous-world carry (incremental compile), no monolithic mesh.");
    Console.WriteLine("'add' is the OTHER common gesture: appending one isolated part, which the patch path refuses (count change) and the");
    Console.WriteLine("validated caching fallback resolves — its O(world) validation floor is a real per-placement cost, measured here.");
    Console.WriteLine("parts  | cells  | surfaces | initial ms | init MiB | edit ms | noop ms | add ms  | edit MiB | dirty | carveMiss | weld | bspBuilt | meshBuilt");
    Console.WriteLine("-------|--------|----------|------------|----------|---------|---------|---------|----------|-------|-----------|------|----------|----------");

    int[] sizes = [1_000, 10_000, 50_000];
    var editMedians = new double[sizes.Length];
    var noopMedians = new double[sizes.Length];
    var addMedians = new double[sizes.Length];
    CsgWorld? world10k = null;
    OpenWorld? open10k = null;

    for (int i = 0; i < sizes.Length; i++)
    {
        (editMedians[i], noopMedians[i], addMedians[i], CsgWorld world, OpenWorld open) = RunOpenWorldSize(sizes[i]);
        if (sizes[i] == 10_000)
        {
            // For the query section below.
            world10k = world;
            open10k = open;
        }
    }

    // Covers the move gesture only. 1.5x is the noise band: an O(world) stage
    // would show about 50x. Add and remove pay the fallback in the 'add' column.
    double ratio = editMedians[^1] / editMedians[0];
    Console.WriteLine(
        $"  verdict (MOVE gesture): one-part edit at 50k parts = {ratio:F2}x the edit at 1k parts -> " +
        (ratio <= 1.5
            ? "world-size independent (within noise)"
            : "NOT world-size independent — investigate before accepting this as a baseline"));

    // The no-op build is the patch path's bookkeeping floor only. The validated
    // path's O(world) floor is the 'add' column.
    Console.WriteLine(
        $"  attribution: patch-path no-op (changed-empty short-circuit) {string.Join(" / ", noopMedians.Select(m => $"{m:F2}"))} ms; " +
        $"edit-scoped work (edit - noop) {string.Join(" / ", sizes.Select((_, i) => $"{editMedians[i] - noopMedians[i]:F2}"))} ms; " +
        $"validated fallback floor (add one part) {string.Join(" / ", addMedians.Select(m => $"{m:F2}"))} ms " +
        "at 1k / 10k / 50k parts");

    RunOpenWorldQuery(world10k!, open10k!);
}

static (double EditMedianMs, double NoopMedianMs, double AddMedianMs, CsgWorld World, OpenWorld Open) RunOpenWorldSize(int parts)
{
    OpenWorld open = MakeOpenWorld(parts);
    List<BrushPlacement> placements = open.Placements;

    // Initial compile through the caching path. One warmup: sizes run
    // ascending, so the code is already hot.
    CsgWorld world = OpenWorldCompile(placements);

    var initial = new double[TimedReps];
    for (int r = 0; r < TimedReps; r++)
    {
        Collect();
        var sw = Stopwatch.StartNew();
        world = OpenWorldCompile(placements);
        sw.Stop();
        initial[r] = sw.Elapsed.TotalMilliseconds;
    }

    Collect();
    long allocBefore = GC.GetTotalAllocatedBytes(precise: true);
    _ = OpenWorldCompile(placements);
    long initialAlloc = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;
    Collect();

    // The edit: move one isolated part 0.3 on X.
    BrushPlacement before = placements[open.EditIndex];
    BrushPlacement after = before with
    {
        Transform = before.Transform * Matrix4x4.CreateTranslation(0.3f, 0f, 0f),
    };
    var edited = new List<BrushPlacement>(placements);
    edited[open.EditIndex] = after;

    // Dirty cells as the scene computes them: old and new footprints, sorted.
    var dirtySet = new HashSet<ChunkCoord>(ChunkGrid.ComputeFootprint(in before));
    dirtySet.UnionWith(ChunkGrid.ComputeFootprint(in after));
    var dirtyCells = new ChunkCoord[dirtySet.Count];
    dirtySet.CopyTo(dirtyCells);
    Array.Sort(dirtyCells);

    // No BuildMesh in the timed section: the editor uploads per-cell meshes,
    // so a whole-world flatten would be a cost the engine does not pay.
    //
    // One edit compile is about 0.1 ms, below timer jitter, so each rep times
    // a burst from the same previous world and reports the mean.
    const int BurstIterations = 16;
    for (int warm = 0; warm < 2; warm++)
        _ = CsgWorld.Build(edited, dirtyCells, world);

    var edit = new double[TimedReps];
    CsgWorld editedWorld = null!;
    for (int r = 0; r < TimedReps; r++)
    {
        Collect();
        var sw = Stopwatch.StartNew();
        for (int k = 0; k < BurstIterations; k++)
            editedWorld = CsgWorld.Build(edited, dirtyCells, world);
        sw.Stop();
        edit[r] = sw.Elapsed.TotalMilliseconds / BurstIterations;
    }

    Collect();
    allocBefore = GC.GetTotalAllocatedBytes(precise: true);
    _ = CsgWorld.Build(edited, dirtyCells, world);
    long editAlloc = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;
    Collect();

    // No-op recompile: same placements, empty dirty set. Measures the patch
    // path's bookkeeping floor, which must not scale with world size.
    for (int warm = 0; warm < 2; warm++)
        _ = CsgWorld.Build(placements, [], world);

    var noop = new double[TimedReps];
    for (int r = 0; r < TimedReps; r++)
    {
        Collect();
        var sw = Stopwatch.StartNew();
        for (int k = 0; k < BurstIterations; k++)
            _ = CsgWorld.Build(placements, [], world);
        sw.Stop();
        noop[r] = sw.Elapsed.TotalMilliseconds / BurstIterations;
    }
    Collect();

    // Add one isolated part. A count change takes the validated caching path,
    // which has an O(world) floor even when every cache hits. The previous
    // world is the patched one, so the warmup also pays its lazy cache
    // materialization. No burst: this path takes milliseconds.
    var addedPlacements = new List<BrushPlacement>(edited)
    {
        new BrushPlacement(
            Brush.CreateBox(new Vector3(-1f), new Vector3(1f)),
            Matrix4x4.CreateTranslation(-400f, 120f, -400f)),
    };
    BrushPlacement addedPart = addedPlacements[^1];
    ChunkCoord[] addDirty = ChunkGrid.ComputeFootprint(in addedPart);

    for (int warm = 0; warm < 2; warm++)
        _ = CsgWorld.Build(addedPlacements, addDirty, editedWorld);

    var add = new double[TimedReps];
    for (int r = 0; r < TimedReps; r++)
    {
        Collect();
        var sw = Stopwatch.StartNew();
        _ = CsgWorld.Build(addedPlacements, addDirty, editedWorld);
        sw.Stop();
        add[r] = sw.Elapsed.TotalMilliseconds;
    }
    Collect();

    // Spot-check the benchmark's own wiring at the smallest size.
    if (parts == 1_000)
    {
        (float[] freshVerts, uint[] freshIdx) = CsgWorld.Build(edited).BuildMesh();
        (float[] cachedVerts, uint[] cachedIdx) = editedWorld.BuildMesh();
        if (!cachedVerts.SequenceEqual(freshVerts) || !cachedIdx.SequenceEqual(freshIdx))
            Console.WriteLine("  WARNING: cached edit recompile diverged from the cache-free recompile!");
    }

    CsgCacheStats carveStats = editedWorld.CacheStats ?? default;
    CsgWeldStats weldStats = editedWorld.WeldStats ?? default;
    CsgBspStats bspStats = editedWorld.BspStats ?? default;
    CsgMeshStats meshStats = editedWorld.MeshStats ?? default;
    Console.WriteLine(
        $"{parts,6} | {world.Chunks.Count,6} | {world.Surfaces.Count,8} | {Median(initial),10:F1} | " +
        $"{initialAlloc / (1024.0 * 1024.0),8:F1} | {Median(edit),7:F2} | {Median(noop),7:F2} | {Median(add),7:F2} | " +
        $"{editAlloc / (1024.0 * 1024.0),8:F2} | " +
        $"{dirtyCells.Length,5} | {carveStats.Misses,9} | {weldStats.Welded,4} | {bspStats.Built,8} | {meshStats.Built,9}");

    return (Median(edit), Median(noop), Median(add), world, open);
}

// Produces all four caches and consumes none, like an editor session's first compile.
static CsgWorld OpenWorldCompile(List<BrushPlacement> placements) =>
    CsgWorld.Build(
        placements, dirtyCells: null, previousCache: null,
        previousWeldCache: null, previousBspCache: null, previousMeshCache: null);

// Samples alternate between the two regions. The void between them is not
// sampled: it would only measure dictionary misses.
static void RunOpenWorldQuery(CsgWorld world, OpenWorld open)
{
    Console.WriteLine();
    Console.WriteLine("  QUERY (openworld 10k) — routed point->cell / ray->DDA; samples alternate near-origin and +8,000 regions.");

    Aabb near = open.NearBounds.Expanded(2f);
    Aabb far = open.FarBounds.Expanded(2f);

    const int PointCount = 1_000_000;
    const int RayCount = 100_000;
    const float RayMaxDistance = 40f;

    var lcg = new Lcg(0xB0B0F0FA57F00D5EUL);
    var points = new Vector3[PointCount];
    for (int i = 0; i < points.Length; i++)
    {
        Aabb box = (i & 1) == 0 ? near : far;
        points[i] = box.Min + box.Size * new Vector3(lcg.NextFloat01(), lcg.NextFloat01(), lcg.NextFloat01());
    }

    var origins = new Vector3[RayCount];
    var directions = new Vector3[RayCount];
    for (int i = 0; i < RayCount; i++)
    {
        Aabb box = (i & 1) == 0 ? near : far;
        origins[i] = box.Min + box.Size * new Vector3(lcg.NextFloat01(), lcg.NextFloat01(), lcg.NextFloat01());
        directions[i] = lcg.NextDirection();
    }

    (double pointSeconds, long inside) = MeasurePointQueries(world, points);
    Console.WriteLine(
        $"  ContainsPoint: {PointCount:N0} calls in {pointSeconds * 1000:F1} ms -> " +
        $"{PointCount / pointSeconds / 1e6:F2} M calls/s | checksum inside={inside:N0} ({100.0 * inside / PointCount:F2} %)");

    (double raySeconds, long hits) = MeasureRayQueries(world, origins, directions, RayMaxDistance);
    Console.WriteLine(
        $"  Raycast:       {RayCount:N0} calls in {raySeconds * 1000:F1} ms -> " +
        $"{RayCount / raySeconds / 1e6:F2} M calls/s | checksum hits={hits:N0} ({100.0 * hits / RayCount:F2} %)");
}

// Sites on a 20-unit grid in two equal regions, one on the origin and one
// around +8,000 on X and Z. Each site's geometry stays inside its own square
// (jitter [5,11] plus a reach of 6), so sites never touch and the mix is exact.
// Per 100 parts: 85 isolated boxes, 5 touching pairs, 1 cluster of 5.
static OpenWorld MakeOpenWorld(int partCount)
{
    const int PartsPerBlock = 100;
    const int SitesPerBlock = 91;       // 85 isolated + 5 pairs + 1 cluster
    const int IsolatedPerBlock = 85;
    const int PairsPerBlock = 5;
    const float SiteSpacing = 20f;      // ~1.1 parts per 400 units^2, every size
    const float FarOffset = 8_000f;

    if (partCount % 200 != 0)
        throw new ArgumentException("openworld part counts must be multiples of 200", nameof(partCount));

    int totalSites = partCount / PartsPerBlock * SitesPerBlock;
    int sitesPerRegion = totalSites / 2;
    int sideSites = (int)MathF.Ceiling(MathF.Sqrt(sitesPerRegion));
    float nearOrigin = -sideSites * SiteSpacing * 0.5f;   // centre region A on the origin

    // The edited part: the isolated site nearest the middle of the near region.
    int editSite = sitesPerRegion / 2;
    while (editSite % SitesPerBlock >= IsolatedPerBlock)
        editSite--;

    var lcg = new Lcg(0x9E3779B97F4A7C15UL);
    var placements = new List<BrushPlacement>(partCount);
    int editIndex = -1;
    var nearMin = new Vector3(float.MaxValue);
    var nearMax = new Vector3(float.MinValue);
    var farMin = new Vector3(float.MaxValue);
    var farMax = new Vector3(float.MinValue);

    for (int site = 0; site < totalSites; site++)
    {
        bool farRegion = site >= sitesPerRegion;
        int local = farRegion ? site - sitesPerRegion : site;
        float baseX = (farRegion ? FarOffset : nearOrigin) + local % sideSites * SiteSpacing;
        float baseZ = (farRegion ? FarOffset : nearOrigin) + local / sideSites * SiteSpacing;

        if (site == editSite)
            editIndex = placements.Count;

        float cx = baseX + 5f + lcg.NextFloat01() * 6f;
        float cz = baseZ + 5f + lcg.NextFloat01() * 6f;
        Vector3 halfA = RandomHalfExtent(ref lcg);
        float bottomA = lcg.NextFloat01() * 2f;
        var centerA = new Vector3(cx, bottomA + halfA.Y, cz);
        AddPart(centerA, halfA);

        int kind = site % SitesPerBlock;
        if (kind < IsolatedPerBlock)
        {
            // isolated: the one box above is the whole site
        }
        else if (kind < IsolatedPerBlock + PairsPerBlock)
        {
            // Touching pair: coincident faces on +X, same bottom so y ranges overlap.
            Vector3 halfB = RandomHalfExtent(ref lcg);
            AddPart(new Vector3(cx + halfA.X + halfB.X, bottomA + halfB.Y, cz), halfB);
        }
        else
        {
            // Cluster: four satellites. Offsets stay under the minimum
            // half-extent sum of 1.0, so each one overlaps the base.
            for (int i = 0; i < 4; i++)
            {
                Vector3 h = RandomHalfExtent(ref lcg);
                float ox = (0.3f + lcg.NextFloat01() * 0.6f) * (i % 2 == 0 ? 1f : -1f);
                float oz = (0.3f + lcg.NextFloat01() * 0.6f) * (i < 2 ? 1f : -1f);
                float oy = lcg.NextFloat01() * 0.5f;
                AddPart(centerA + new Vector3(ox, oy, oz), h);
            }
        }

        void AddPart(Vector3 center, Vector3 half)
        {
            var brush = Brush.CreateBox(-half, half);
            placements.Add(new BrushPlacement(brush, Matrix4x4.CreateTranslation(center)));
            if (farRegion)
            {
                farMin = Vector3.Min(farMin, center - half);
                farMax = Vector3.Max(farMax, center + half);
            }
            else
            {
                nearMin = Vector3.Min(nearMin, center - half);
                nearMax = Vector3.Max(nearMax, center + half);
            }
        }
    }

    return new OpenWorld(placements, editIndex, new Aabb(nearMin, nearMax), new Aabb(farMin, farMax));

    // [0.5, 2.0] per axis: small next to the 32-unit cells.
    static Vector3 RandomHalfExtent(ref Lcg lcg) => new(
        0.5f + lcg.NextFloat01() * 1.5f,
        0.5f + lcg.NextFloat01() * 1.5f,
        0.5f + lcg.NextFloat01() * 1.5f);
}

// A clean map cook, split into compile and serialize and timed separately.
// The serializer must stay a small fraction of the compile.
static void RunBakeBench()
{
    Console.WriteLine();
    Console.WriteLine("BAKE - one clean cook of a map, split into COMPILE and SERIALIZE.");
    Console.WriteLine("COMPILE   = cache-free CsgWorld.Build (the overload ScmapBake calls: no previous world, no carve cache,");
    Console.WriteLine("            because a bake must be a pure function of its source) plus BspFlattener.Flatten per cell.");
    Console.WriteLine("SERIALIZE = ScmapBuilder staging (AddNode per brush, AddChunk per cell) plus Build, which emits");
    Console.WriteLine("            STRT/ASTB/META/NODE/CHDR/CMSH/CBSP. The bake's own glue is counted HERE deliberately: it is");
    Console.WriteLine("            bookkeeping paid only because a file is being written, and putting it on this side can only");
    Console.WriteLine("            make the serializer look worse than it is.");
    Console.WriteLine("NEITHER HALF IS INFERRED BY SUBTRACTION. The compile's own cost swings by two orders of magnitude across");
    Console.WriteLine("these content sets, so a serialize time taken as total-minus-compile would be flattered by exactly the");
    Console.WriteLine("content that compiles slowest. A clean cook is legitimately O(world) and nothing here argues otherwise -");
    Console.WriteLine("the incremental story belongs to the cook cache and its own no-op-cook test. What this guards is the SHARE.");
    Console.WriteLine("content     | brushes | cells | surfaces | build ms | flat ms | compile ms | node ms | cell ms | emit ms | serial ms | file MiB | share");
    Console.WriteLine("------------|---------|-------|----------|----------|---------|------------|---------|---------|---------|-----------|----------|------");

    var results = new List<BakeResult>();

    foreach (int k in new[] { 4, 8, 13 })
        results.Add(RunBakeConfig($"grid k={k}", MakeGrid(k)));

    foreach (int parts in new[] { 1_000, 10_000, 50_000 })
        results.Add(RunBakeConfig($"open {parts / 1_000}k", MakeOpenWorld(parts).Placements));

    // The compile should stay at least three times the writer. Worst measured
    // share is 16 to 22%. A faster compile can also trip this.
    const double ShareCeiling = 0.33;

    BakeResult worst = results[0];
    foreach (BakeResult result in results)
        if (result.Share > worst.Share) worst = result;

    Console.WriteLine(
        $"  verdict (SERIALIZE share): worst case {worst.Name} at {worst.Share:P1} of its own compile -> " +
        (worst.Share <= ShareCeiling
            ? $"a small fraction of the compile (ceiling {ShareCeiling:P0})"
            : $"NOT a small fraction (ceiling {ShareCeiling:P0}) - the map writer has become a cost centre in its " +
              "own right, investigate before accepting this as a baseline"));

    // A share cannot see a quadratic writer while the compile grows too, so
    // check per-cell staging cost across the two largest worlds. Flat is 0.7
    // to 1.6x; an O(cells) term per call tracks the cell growth (about 4.9x).
    const double CellCostCeiling = 2.5;

    BakeResult[] bySize = [.. results.OrderByDescending(r => r.Cells)];
    BakeResult largest = bySize[0], next = bySize[1];

    double costGrowth = largest.CostPerCellUs / next.CostPerCellUs;
    double cellGrowth = (double)largest.Cells / next.Cells;
    Console.WriteLine(
        $"  verdict (CELL staging): {largest.CostPerCellUs:F2} us per cell at {largest.Cells} cells = " +
        $"{costGrowth:F2}x the cost at {next.Cells}, over a {cellGrowth:F2}x bigger world -> " +
        (costGrowth <= CellCostCeiling
            ? $"flat per cell (ceiling {CellCostCeiling:F1}x), no term scaling with the world"
            : $"NOT flat per cell (ceiling {CellCostCeiling:F1}x) - the writer has a term that scales with the " +
              "world rather than with the cell, which a share cannot show while the compile grows too"));

    Console.WriteLine(
        "  attribution: emit " +
        string.Join(" / ", results.Select(r => $"{r.FileMib / (r.EmitMs / 1000.0):F0}")) +
        " MiB/s; cell staging " +
        string.Join(" / ", results.Select(r => $"{r.CostPerCellUs:F2}")) +
        " us per cell, over " +
        string.Join(" / ", results.Select(r => r.Cells.ToString())) +
        " cells");
}

static BakeResult RunBakeConfig(string name, List<BrushPlacement> placements)
{
    // Fixture data, built outside the timed sections. Ids come from the index
    // so two runs write the same bytes.
    var ids = new Guid[placements.Count];
    var names = new string[placements.Count];
    var transforms = new Transform[placements.Count];
    for (int i = 0; i < placements.Count; i++)
    {
        ids[i] = new Guid(i, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);
        names[i] = $"brush{i}";
        transforms[i] = new Transform { Position = placements[i].Transform.Translation };
    }

    for (int warm = 0; warm < 2; warm++)
    {
        CsgWorld warmWorld = CsgWorld.Build(placements);
        ScmapBuilder warmBuilder = new(name);
        StageNodes(warmBuilder, ids, names, transforms);
        StageCells(warmBuilder, warmWorld, FlattenCells(warmWorld));
        _ = warmBuilder.Build(UInt128.Zero, EngineInfo.MapFormatVersion);
    }

    const int reps = TimedReps;
    var build = new double[reps];
    var flatten = new double[reps];
    var node = new double[reps];
    var cell = new double[reps];
    var emit = new double[reps];
    int cells = 0, surfaces = 0;
    long fileBytes = 0;

    for (int r = 0; r < reps; r++)
    {
        Collect();

        var sw = Stopwatch.StartNew();
        CsgWorld world = CsgWorld.Build(placements);
        sw.Stop(); build[r] = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        FlatCell[] flat = FlattenCells(world);
        sw.Stop(); flatten[r] = sw.Elapsed.TotalMilliseconds;

        // So the writer is not timed while paying for the compile's garbage.
        Collect();

        ScmapBuilder builder = new(name);

        sw.Restart();
        StageNodes(builder, ids, names, transforms);
        sw.Stop(); node[r] = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        StageCells(builder, world, flat);
        sw.Stop(); cell[r] = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        byte[] file = builder.Build(UInt128.Zero, EngineInfo.MapFormatVersion);
        sw.Stop(); emit[r] = sw.Elapsed.TotalMilliseconds;

        cells = world.Chunks.Count;
        surfaces = world.Surfaces.Count;
        fileBytes = file.Length;
        GC.KeepAlive(file);
    }

    Collect();

    double buildMed = Median(build), flatMed = Median(flatten);
    double nodeMed = Median(node), cellMed = Median(cell), emitMed = Median(emit);
    double compile = buildMed + flatMed;
    double serialize = nodeMed + cellMed + emitMed;
    double fileMib = fileBytes / (1024.0 * 1024.0);

    Console.WriteLine(
        $"{name,-11} | {placements.Count,7} | {cells,5} | {surfaces,8} | {buildMed,8:F2} | {flatMed,7:F2} | " +
        $"{compile,10:F2} | {nodeMed,7:F2} | {cellMed,7:F2} | {emitMed,7:F2} | {serialize,9:F2} | " +
        $"{fileMib,8:F2} | {serialize / compile,5:P1}");

    return new BakeResult(name, cells, serialize / compile, cellMed, emitMed, fileMib);
}

// Counted as compile. Same OrderedChunks order as StageCells, which indexes
// the result by position.
static FlatCell[] FlattenCells(CsgWorld world)
{
    IReadOnlyList<WorldChunk> cells = world.Chunks.OrderedChunks;
    var flat = new FlatCell[cells.Count];

    for (int i = 0; i < cells.Count; i++)
    {
        // Null means no tree. An empty array is a tree of one bare leaf, and
        // the format tells them apart.
        flat[i] = cells[i].Bsp is { } tree
            ? new FlatCell(BspFlattener.Flatten(tree, out int root), root)
            : new FlatCell(null, FlatBspNode.EmptyLeaf);
    }

    return flat;
}

// StageNodes and StageCells mirror ScmapBake.WriteNodes/WriteChunks, which
// need a MapDocument and a bound Scene these content sets do not have. Keep
// them in step with the bake: asset indices, never MaterialRef.Id, and
// submeshes sorted by asset index.
static void StageNodes(ScmapBuilder builder, Guid[] ids, string[] names, Transform[] transforms)
{
    for (int i = 0; i < ids.Length; i++)
    {
        builder.AddNode(new ScmapNodeSource(
            ids[i], names[i], -1, transforms[i], ScmapPayloadKind.StaticWorldBrush));
    }
}

static void StageCells(ScmapBuilder builder, CsgWorld world, FlatCell[] flat)
{
    var materials = new Dictionary<int, uint>();

    var meshes = new Dictionary<ChunkCoord, ChunkMesh>(world.ChunkMeshes.Count);
    for (int i = 0; i < world.ChunkMeshes.Count; i++)
        meshes[world.ChunkMeshes[i].Coord] = world.ChunkMeshes[i];

    IReadOnlyList<WorldChunk> cells = world.Chunks.OrderedChunks;
    for (int i = 0; i < cells.Count; i++)
    {
        WorldChunk cell = cells[i];
        meshes.TryGetValue(cell.Coord, out ChunkMesh? mesh);

        builder.AddChunk(new ScmapChunkSource(
            cell.Coord,

            // A cell with no mesh gets its own cube as bounds.
            mesh?.RenderBounds ?? cell.Coord.Bounds,
            BakeSubmeshes(mesh, materials, builder),
            flat[i].Nodes,
            flat[i].RootIndex));
    }
}

// Mirrors ScmapBake.SubmeshesOf. Every face here wears the default material,
// so each cell emits one submesh with the NoAssetIndex sentinel.
static ScmapSubmeshSource[]? BakeSubmeshes(
    ChunkMesh? mesh, Dictionary<int, uint> materials, ScmapBuilder builder)
{
    if (mesh is null || mesh.Submeshes.Count == 0) return null;

    var submeshes = new ScmapSubmeshSource[mesh.Submeshes.Count];
    for (int i = 0; i < submeshes.Length; i++)
    {
        ChunkSubmesh submesh = mesh.Submeshes[i];
        submeshes[i] = new ScmapSubmeshSource(
            BakeMaterialIndex(submesh.Material, materials, builder), submesh.Vertices, submesh.Indices);
    }

    Array.Sort(submeshes, static (a, b) => a.AssetIndex.CompareTo(b.AssetIndex));
    return submeshes;
}

// The default material has no asset row. Sentinel, not row 0, which is a real asset.
static uint BakeMaterialIndex(MaterialRef material, Dictionary<int, uint> lookup, ScmapBuilder builder)
{
    if (material.IsDefault) return ScmapFormat.NoAssetIndex;
    if (lookup.TryGetValue(material.Id, out uint existing)) return existing;

    uint index = builder.AddMaterial(material);
    lookup[material.Id] = index;
    return index;
}

static void Collect()
{
    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
}

static double Median(double[] values)
{
    var c = (double[])values.Clone();
    Array.Sort(c);
    return c[c.Length / 2];
}

internal readonly record struct TreeStats(int Nodes, int Leaves, int MaxDepth, double AvgLeafDepth);

// Null Nodes: the cell has no tree. Empty: a tree of one bare leaf.
internal readonly record struct FlatCell(FlatBspNode[]? Nodes, int RootIndex);

internal readonly record struct BakeResult(
    string Name, int Cells, double Share, double CellMs, double EmitMs, double FileMib)
{
    public double CostPerCellUs => CellMs * 1000.0 / Cells;
}

// EditIndex is the isolated part the edit benchmark moves.
internal sealed record OpenWorld(
    List<BrushPlacement> Placements, int EditIndex, Aabb NearBounds, Aabb FarBounds);

// 64-bit LCG (Knuth MMIX constants). Not System.Random: the query checksums
// need inputs that are bit-identical across runtimes.
internal struct Lcg(ulong seed)
{
    private ulong _state = seed;

    // Uniform in [0, 1), from the high 24 bits.
    public float NextFloat01()
    {
        _state = _state * 6364136223846793005UL + 1442695040888963407UL;
        return (_state >> 40) * (1.0f / (1 << 24));
    }

    // Rejection sampling keeps the direction uniform over the sphere.
    public Vector3 NextDirection()
    {
        while (true)
        {
            var v = new Vector3(
                NextFloat01() * 2f - 1f,
                NextFloat01() * 2f - 1f,
                NextFloat01() * 2f - 1f);
            float lengthSquared = v.LengthSquared();
            if (lengthSquared is > 1e-4f and <= 1f)
                return v / MathF.Sqrt(lengthSquared);
        }
    }
}
