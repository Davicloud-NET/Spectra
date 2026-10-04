using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using SpectraEngine.Core.Assets;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// A compiled static world: the carved visible surfaces of a set of brushes,
/// per-cell BSP trees for point and ray queries, and per-cell render meshes.
/// </summary>
public sealed partial class CsgWorld
{
    // How far a cell's ray segment may overshoot its interval when accepting a
    // hit, so a crossing that rounds just past a cell boundary is taken by the
    // earlier cell. Must stay at most ChunkGrid.WeldBand: brushes with geometry
    // in the overshoot window are then resident in the current cell too.
    private const float RaycastIntervalEpsilon = Polygon.Epsilon;

    private CsgWorld(
        IReadOnlyList<BrushPlacement> placements, Polygon[]? surfaces, int surfaceCount,
        ChunkGrid chunks, IReadOnlyList<ChunkMesh> chunkMeshes, IReadOnlyList<ChunkCoord>? dirtyCells,
        CsgWorldCarry carry, bool lazyCaches,
        CsgCompileCache? compileCache, CsgCacheStats? cacheStats,
        CsgWeldCache? weldCache, CsgWeldStats? weldStats,
        CsgBspCache? bspCache, CsgBspStats? bspStats,
        CsgMeshCache? meshCache, CsgMeshStats? meshStats,
        long patchBaseId = 0, IReadOnlyList<(ChunkCoord Coord, ChunkMesh? Mesh)>? chunkMeshDelta = null)
    {
        StoragePlacements = placements;
        SourceSnapshot = (placements as PlacementSlotView)?.Snapshot;
        Placements = SourceSnapshot is null ? placements : SourceSnapshot;
        _surfaces = surfaces;
        SurfaceCount = surfaceCount;
        StorageChunks = chunks;
        ChunkMeshesPaged = chunkMeshes as PagedArray<ChunkMesh> ?? PagedArray<ChunkMesh>.From(chunkMeshes);
        DirtyCells = dirtyCells;
        Carry = carry;
        _lazyCaches = lazyCaches;
        _compileCache = compileCache;
        CacheStats = cacheStats;
        _weldCache = weldCache;
        WeldStats = weldStats;
        _bspCache = bspCache;
        BspStats = bspStats;
        _meshCache = meshCache;
        MeshStats = meshStats;
        PatchBaseId = patchBaseId;
        ChunkMeshDelta = chunkMeshDelta;
    }

    // Lazy views. A racing duplicate build produces an identical value, so
    // compare-exchange is enough and no lock is needed.
    private Polygon[]? _surfaces;
    private Brush[]? _brushes;
    private readonly bool _lazyCaches;
    private CsgCompileCache? _compileCache;
    private CsgWeldCache? _weldCache;
    private CsgBspCache? _bspCache;
    private CsgMeshCache? _meshCache;

    private static long _nextId;

    // Process-unique. Background compiles run concurrently, hence Interlocked.
    internal long Id { get; } = Interlocked.Increment(ref _nextId);

    // Id of the world this one was patched from, 0 when built from scratch.
    // An id rather than a reference so superseded worlds stay collectable.
    internal long PatchBaseId { get; }

    // Per-cell mesh changes relative to the PatchBaseId world, ascending.
    // Null mesh: the cell lost its geometry. Cells not listed kept their
    // artifact instance. Null for from-scratch worlds. A consumer may apply
    // it only after checking its state was built from the PatchBaseId world.
    internal IReadOnlyList<(ChunkCoord Coord, ChunkMesh? Mesh)>? ChunkMeshDelta { get; }

    internal CsgWorldCarry Carry { get; }

    /// <summary>
    /// The source brushes, index-aligned with <see cref="Placements"/>. Built on first access.
    /// </summary>
    public IReadOnlyList<Brush> Brushes
    {
        get
        {
            if (_brushes is null)
            {
                var brushes = new Brush[Placements.Count];
                for (int i = 0; i < brushes.Length; i++)
                    brushes[i] = Placements[i].Brush;
                Interlocked.CompareExchange(ref _brushes, brushes, null);
            }
            return _brushes;
        }
    }

    /// <summary>
    /// Each source brush with the world transform it was carved at. Read these
    /// for world-space brush data, not <see cref="Brush.Transform"/>, which
    /// may have moved since the compile.
    /// </summary>
    public IReadOnlyList<BrushPlacement> Placements { get; }
    internal IReadOnlyList<BrushPlacement> StoragePlacements { get; }
    internal PlacementSnapshot? SourceSnapshot { get; }

    /// <summary>
    /// The visible exterior surfaces in world space, in placement order. An
    /// incrementally compiled world flattens the list on first access, which
    /// is O(world); use <see cref="SurfaceCount"/> when only the size is needed.
    /// </summary>
    public IReadOnlyList<Polygon> Surfaces
    {
        get
        {
            if (_surfaces is null)
                Interlocked.CompareExchange(ref _surfaces, SourceSnapshot is null
                    ? Csg.Concatenate(Carry.WeldedPerBrush, SurfaceCount) : CanonicalWorld.SurfacesArray(), null);
            return _surfaces;
        }
    }

    /// <summary>Number of surfaces in <see cref="Surfaces"/>, without building the list.</summary>
    public int SurfaceCount { get; }

    /// <summary>
    /// The sparse chunk partition: every placement bucketed into the cells its
    /// AABB touches, with its surfaces stored under its one owner cell. Each
    /// cell carries its own BSP tree.
    /// </summary>
    public ChunkGrid Chunks => SourceSnapshot is null ? StorageChunks : CanonicalWorld.StorageChunks;
    internal ChunkGrid StorageChunks { get; }

    /// <summary>
    /// One render mesh per cell that owns geometry, in ascending cell order,
    /// split per face material. Unchanged cells keep the same instance across
    /// compiles.
    /// </summary>
    public IReadOnlyList<ChunkMesh> ChunkMeshes => ChunkMeshesPaged;

    internal PagedArray<ChunkMesh> ChunkMeshesPaged { get; }

    /// <summary>
    /// The dirty cells this compile was invoked with, ascending, or null for a
    /// build that compiles everything. Recorded for logs and tests only; no
    /// stage scopes its rebuild by it.
    /// </summary>
    public IReadOnlyList<ChunkCoord>? DirtyCells { get; }

    /// <summary>
    /// The carve cache for the next incremental compile, or null for a
    /// cache-free build. It keeps every brush's carved surfaces alive as long
    /// as the world lives.
    /// </summary>
    public CsgCompileCache? CompileCache
    {
        get
        {
            if (SourceSnapshot is not null && _lazyCaches) return CanonicalWorld.CompileCache;
            if (_compileCache is null && _lazyCaches)
            {
                int n = Placements.Count;
                var entries = new CsgCompileCache.Entry?[n];
                for (int i = 0; i < n; i++)
                    entries[i] = CsgCompileCache.Entry.Create(Placements, i, Carry.CarveNeighbors[i], Carry.CarvedPerBrush[i]);
                Interlocked.CompareExchange(ref _compileCache, CsgCompileCache.FromEntries(Placements, entries), null);
            }
            return _compileCache;
        }
    }

    /// <summary>Carve hit/miss counters, or null for a cache-free build.</summary>
    public CsgCacheStats? CacheStats { get; }

    /// <summary>The per-cell weld cache for the next incremental compile, or null for a cache-free build.</summary>
    public CsgWeldCache? WeldCache
    {
        get
        {
            if (SourceSnapshot is not null && _lazyCaches) return CanonicalWorld.WeldCache;
            if (_weldCache is null && _lazyCaches)
            {
                int n = Placements.Count;
                var perBrush = new Polygon[n][];
                for (int i = 0; i < n; i++)
                    perBrush[i] = Carry.CarvedPerBrush[i];

                // One carve list per distinct candidate set, as the eager weld path does.
                var carvesMemo = new Dictionary<int[], Polygon[][]>(ReferenceEqualityComparer.Instance);
                var entries = new CsgWeldCache.Entry[n];
                for (int i = 0; i < n; i++)
                {
                    int[] candidateSet = Carry.WeldCandidates[i];
                    if (!carvesMemo.TryGetValue(candidateSet, out Polygon[][]? carves))
                    {
                        carves = new Polygon[candidateSet.Length][];
                        for (int k = 0; k < carves.Length; k++)
                            carves[k] = perBrush[candidateSet[k]];
                        carvesMemo[candidateSet] = carves;
                    }
                    entries[i] = new CsgWeldCache.Entry(carves, Carry.WeldedPerBrush[i]);
                }
                Interlocked.CompareExchange(ref _weldCache, CsgWeldCache.FromEntries(perBrush, entries), null);
            }
            return _weldCache;
        }
    }

    /// <summary>Weld reused/welded counters, or null for a cache-free build.</summary>
    public CsgWeldStats? WeldStats { get; }

    /// <summary>The per-cell BSP cache for the next incremental compile, or null for a cache-free build.</summary>
    public CsgBspCache? BspCache
    {
        get
        {
            if (SourceSnapshot is not null && _lazyCaches) return CanonicalWorld.BspCache;
            if (_bspCache is null && _lazyCaches)
            {
                IReadOnlyList<WorldChunk> cells = Chunks.OrderedChunks;
                var entries = new CsgBspCache.Entry[cells.Count];
                for (int c = 0; c < cells.Count; c++)
                {
                    WorldChunk chunk = cells[c];
                    IReadOnlyList<int> residents = chunk.ResidentBrushIndices;
                    var residentWelded = new Polygon[residents.Count][];
                    for (int k = 0; k < residentWelded.Length; k++)
                        residentWelded[k] = Carry.WeldedPerBrush[residents[k]];
                    entries[c] = new CsgBspCache.Entry(residentWelded, chunk.Bsp);
                }
                Interlocked.CompareExchange(ref _bspCache, CsgBspCache.FromEntries(cells, entries), null);
            }
            return _bspCache;
        }
    }

    /// <summary>Per-cell tree reused/built counters, or null for a cache-free build.</summary>
    public CsgBspStats? BspStats { get; }

    /// <summary>The per-cell mesh cache for the next incremental compile, or null for a cache-free build.</summary>
    public CsgMeshCache? MeshCache
    {
        get
        {
            if (SourceSnapshot is not null && _lazyCaches) return CanonicalWorld.MeshCache;
            if (_meshCache is null && _lazyCaches)
            {
                // Geometry cells align 1:1 with ChunkMeshes: both ascending.
                var geometryCells = new List<WorldChunk>(ChunkMeshes.Count);
                foreach (WorldChunk chunk in Chunks.OrderedChunks)
                {
                    if (chunk.WeldedSurfaces.Count > 0)
                        geometryCells.Add(chunk);
                }

                var entries = new CsgMeshCache.Entry[geometryCells.Count];
                for (int c = 0; c < geometryCells.Count; c++)
                {
                    WorldChunk chunk = geometryCells[c];
                    IReadOnlyList<int> owned = chunk.OwnedBrushIndices;
                    var ownedWelded = new Polygon[owned.Count][];
                    for (int k = 0; k < ownedWelded.Length; k++)
                        ownedWelded[k] = Carry.WeldedPerBrush[owned[k]];
                    entries[c] = new CsgMeshCache.Entry(ownedWelded, ChunkMeshes[c]);
                }
                Interlocked.CompareExchange(ref _meshCache, CsgMeshCache.FromEntries(geometryCells, entries), null);
            }
            return _meshCache;
        }
    }

    /// <summary>Per-cell mesh reused/built counters, or null for a cache-free build.</summary>
    public CsgMeshStats? MeshStats { get; }

    /// <summary>Compiles the brushes at their own <see cref="Brush.Transform"/>.</summary>
    public static CsgWorld Build(IReadOnlyList<Brush> brushes)
        => Build(Csg.ToPlacements(brushes));

    /// <summary>
    /// Compiles the placed brushes with no caches. Safe on a background thread
    /// as long as no other thread is carving the same <see cref="Brush"/> instances.
    /// </summary>
    public static CsgWorld Build(IReadOnlyList<BrushPlacement> placements)
    {
        if (placements is PlacementSnapshot snapshot)
            return FromDense(snapshot, Build(snapshot.ToDense(out _)));
        Polygon[][] perBrushSurfaces = Csg.CarvePerBrush(placements, out int[][] neighbors);
        return Assemble(
            placements, perBrushSurfaces, neighbors, dirtyCells: null, compileCache: null, cacheStats: null,
            previousWeldCache: null, previousBspCache: null, previousMeshCache: null);
    }

    /// <summary>
    /// Compiles with a carve cache from the previous compile (null on the
    /// first). The result is bit-identical to a cache-free build.
    /// </summary>
    public static CsgWorld Build(IReadOnlyList<BrushPlacement> placements, CsgCompileCache? previousCache)
        => Build(placements, dirtyCells: null, previousCache, previousWeldCache: null);

    /// <summary>
    /// As the carve-cache overload, and records <paramref name="dirtyCells"/>
    /// in <see cref="DirtyCells"/>. The set does not affect the output.
    /// </summary>
    public static CsgWorld Build(
        IReadOnlyList<BrushPlacement> placements, IReadOnlyList<ChunkCoord>? dirtyCells, CsgCompileCache? previousCache)
        => Build(placements, dirtyCells, previousCache, previousWeldCache: null, previousBspCache: null);

    /// <summary>Compiles with carve and weld caches; every cell's BSP tree builds fresh.</summary>
    public static CsgWorld Build(
        IReadOnlyList<BrushPlacement> placements, IReadOnlyList<ChunkCoord>? dirtyCells,
        CsgCompileCache? previousCache, CsgWeldCache? previousWeldCache)
        => Build(placements, dirtyCells, previousCache, previousWeldCache, previousBspCache: null);

    /// <summary>Compiles with carve, weld and BSP caches; every cell's mesh builds fresh.</summary>
    public static CsgWorld Build(
        IReadOnlyList<BrushPlacement> placements, IReadOnlyList<ChunkCoord>? dirtyCells,
        CsgCompileCache? previousCache, CsgWeldCache? previousWeldCache, CsgBspCache? previousBspCache)
        => Build(placements, dirtyCells, previousCache, previousWeldCache, previousBspCache, previousMeshCache: null);

    /// <summary>
    /// Compiles with all four caches from the previous compile (each may be
    /// null), validating every reuse. The result matches a cache-free build
    /// and carries the successor caches.
    /// </summary>
    public static CsgWorld Build(
        IReadOnlyList<BrushPlacement> placements, IReadOnlyList<ChunkCoord>? dirtyCells,
        CsgCompileCache? previousCache, CsgWeldCache? previousWeldCache, CsgBspCache? previousBspCache,
        CsgMeshCache? previousMeshCache)
    {
        if (placements is PlacementSnapshot snapshot)
            return FromDense(snapshot, Build(snapshot.ToDense(out _), dirtyCells,
                previousCache, previousWeldCache, previousBspCache, previousMeshCache));
        Polygon[][] perBrushSurfaces = Csg.CarvePerBrush(
            placements, previousCache, out CsgCompileCache nextCache, out CsgCacheStats stats, out int[][] neighbors);
        return Assemble(
            placements, perBrushSurfaces, neighbors, dirtyCells, nextCache, stats,
            previousWeldCache, previousBspCache, previousMeshCache);
    }

    /// <summary>
    /// Derives the new world from <paramref name="previous"/>, re-running only
    /// the edit's neighbourhood. With both <paramref name="previous"/> and
    /// <paramref name="dirtyCells"/> non-null the diff is trusted: placements
    /// must be in the previous compile's order, and every changed placement's
    /// old and new footprint must be covered by <paramref name="dirtyCells"/>.
    /// Pass null for either to get the fully validated, O(world) path.
    /// </summary>
    // A broken guarantee gives stale geometry with no error. Debug builds check the order.
    public static CsgWorld Build(
        IReadOnlyList<BrushPlacement> placements, IReadOnlyList<ChunkCoord>? dirtyCells, CsgWorld? previous)
    {
        if (previous is not null && dirtyCells is not null &&
            CsgIncrementalCompiler.TryBuild(placements is PlacementSnapshot snapshot ? snapshot.Storage : placements,
                dirtyCells, previous, out CsgWorld? patched))
        {
            return patched;
        }

        // Fallback: validates every reuse, so it is correct for any placement list.
        return Build(
            placements, dirtyCells,
            previous?.CompileCache, previous?.WeldCache, previous?.BspCache, previous?.MeshCache);
    }

    // Post-carve pipeline: chunk, snap+weld, per-cell BSP, per-cell mesh.
    private static CsgWorld Assemble(
        IReadOnlyList<BrushPlacement> placements, Polygon[][] perBrushSurfaces, int[][] neighbors,
        IReadOnlyList<ChunkCoord>? dirtyCells, CsgCompileCache? compileCache, CsgCacheStats? cacheStats,
        CsgWeldCache? previousWeldCache, CsgBspCache? previousBspCache, CsgMeshCache? previousMeshCache)
    {
        ChunkGrid chunks = ChunkGrid.Build(placements, perBrushSurfaces);

        // Later caches validate by array identity, which only a carve-cache
        // hit chain preserves, so they are produced only alongside one.
        bool caching = compileCache is not null;
        var snappedPerBrush = new Polygon[]?[placements.Count];
        Polygon[][] weldedPerBrush = ChunkWelder.Weld(
            placements, perBrushSurfaces, chunks, previousWeldCache, produceCache: caching,
            out CsgWeldCache? weldCache, out CsgWeldStats weldStats, out int[][] candidates, snappedPerBrush);
        chunks.AttachWeldedSurfaces(placements, weldedPerBrush);

        ChunkBspBuilder.Build(
            chunks, weldedPerBrush, previousBspCache, produceCache: caching,
            out CsgBspCache? bspCache, out CsgBspStats bspStats);

        IReadOnlyList<ChunkMesh> chunkMeshes = ChunkMeshBuilder.Build(
            chunks, weldedPerBrush, previousMeshCache, produceCache: caching,
            out CsgMeshCache? meshCache, out CsgMeshStats meshStats);

        // Placement order, so the flat list matches a monolithic compile bit for bit.
        Polygon[] surfaces = Csg.Concatenate(weldedPerBrush);

        var carry = new CsgWorldCarry(
            PagedArray<Polygon[]>.From(perBrushSurfaces),
            PagedArray<Polygon[]>.From(weldedPerBrush),
            PagedArray<int[]>.From(neighbors),
            PagedArray<int[]>.From(candidates),
            PagedArray<Polygon[]?>.From(snappedPerBrush));

        return new CsgWorld(
            placements, surfaces, surfaces.Length, chunks, chunkMeshes, dirtyCells,
            carry, lazyCaches: false, compileCache, cacheStats,
            weldCache, caching ? weldStats : null, bspCache, caching ? bspStats : null,
            meshCache, caching ? meshStats : null);
    }

    // For CsgIncrementalCompiler: wraps already computed parts, with the flat
    // list and the caches left lazy.
    internal static CsgWorld CreatePatched(
        IReadOnlyList<BrushPlacement> placements, int surfaceCount,
        ChunkGrid chunks, IReadOnlyList<ChunkMesh> chunkMeshes, IReadOnlyList<ChunkCoord> dirtyCells,
        CsgWorldCarry carry, CsgWorld patchedFrom,
        IReadOnlyList<(ChunkCoord Coord, ChunkMesh? Mesh)> chunkMeshDelta,
        CsgCacheStats cacheStats, CsgWeldStats weldStats, CsgBspStats bspStats, CsgMeshStats meshStats)
        => new(
            placements, surfaces: null, surfaceCount, chunks, chunkMeshes, dirtyCells,
            carry, lazyCaches: true, compileCache: null, cacheStats,
            weldCache: null, weldStats, bspCache: null, bspStats,
            meshCache: null, meshStats, patchedFrom.Id, chunkMeshDelta);

    /// <summary>
    /// True when the point lies inside solid space of the compiled static
    /// world. Part brushes, dynamic bodies and model colliders are not in it.
    /// </summary>
    // Disagrees with Scene.Raycast both ways: that one tests authored brush
    // planes (solid inside a doorway), this one sees only world brushes.
    public bool ContainsPoint(Vector3 point) =>
        StorageChunks.TryGet(ChunkCoord.FromPosition(point), out WorldChunk chunk) && chunk.Bsp.ContainsPoint(point);

    /// <summary>
    /// Finds the compiled surface a point lies on, for its material. False when
    /// the point is on no surface of its own cell, which includes a hit on a
    /// cell boundary.
    /// </summary>
    // The BSP reports the plane a ray crossed, not the polygon on it, so the
    // face is looked up here by scanning one cell's surfaces.
    public bool TryResolveSurface(Vector3 point, Vector3 normal, out FaceSurface face)
    {
        face = default;

        if (!StorageChunks.TryGet(ChunkCoord.FromPosition(point), out WorldChunk chunk))
            return false;

        IReadOnlyList<Polygon> surfaces = chunk.WeldedSurfaces.Count > 0
            ? chunk.WeldedSurfaces
            : chunk.Surfaces;

        const float PlaneEpsilon = 1e-3f;
        const float NormalAgreement = 0.9f;

        for (int i = 0; i < surfaces.Count; i++)
        {
            Polygon polygon = surfaces[i];

            // Rejects the opposite face of a thin brush.
            if (Vector3.Dot(polygon.Surface.Normal, normal) < NormalAgreement)
                continue;

            if (MathF.Abs(Plane.DotCoordinate(polygon.Surface, point)) > PlaneEpsilon)
                continue;

            if (!ContainsProjected(polygon, point))
                continue;

            face = polygon.Face;
            return true;
        }

        return false;
    }

    // Assumes a convex polygon.
    private static bool ContainsProjected(Polygon polygon, Vector3 point)
    {
        ReadOnlySpan<Vector3> vertices = polygon.VertexSpan;
        if (vertices.Length < 3)
            return false;

        Vector3 normal = polygon.Surface.Normal;

        // Outward tolerance: a hit point on an edge may land just outside it.
        const float EdgeEpsilon = -1e-3f;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 a = vertices[i];
            Vector3 b = vertices[(i + 1) % vertices.Length];
            if (Vector3.Dot(Vector3.Cross(b - a, point - a), normal) < EdgeEpsilon)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Casts a ray against the compiled static world and reports the first
    /// surface entered. Same scope as <see cref="ContainsPoint"/>.
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out BspRaycastHit hit)
    {
        hit = default;
        if (direction == Vector3.Zero || maxDistance <= 0f)
            return false;

        direction = Vector3.Normalize(direction);

        // Starting inside solid hits at once, as the monolithic tree does.
        if (ContainsPoint(origin))
        {
            hit = new BspRaycastHit(origin, -direction, 0f);
            return true;
        }

        // Clip the walk to the occupied-cell box. Without it a miss walks
        // every cell up to maxDistance, and never ends for an infinite one:
        // tMax saturates in float while the cell integers keep stepping.
        if (!StorageChunks.TryGetCellBounds(out ChunkCoord cellMin, out ChunkCoord cellMax))
            return false;
        float boxEnter = 0f;
        float walkEnd = maxDistance;
        if (!ClipToBox(origin, direction, cellMin.MinCorner, cellMax.MaxCorner, ref boxEnter, ref walkEnd))
            return false;

        // A ray starting far outside skips ahead to one cell before the box.
        // The one-cell back-off absorbs slab-vs-DDA rounding. A ray starting
        // inside keeps baseT at 0, so its arithmetic is unchanged.
        float baseT = 0f;
        Vector3 walkOrigin = origin;
        if (boxEnter > ChunkCoord.CellSize)
        {
            baseT = boxEnter - ChunkCoord.CellSize;
            walkOrigin = origin + direction * baseT;
        }
        float maxLocal = maxDistance - baseT;

        // Amanatides-Woo 3D-DDA. An origin on a cell boundary belongs to the
        // positive-side cell; heading negative from there gives tMax = 0, a
        // zero-length first segment that the loop skips.
        ChunkCoord startCell = ChunkCoord.FromPosition(walkOrigin);
        int x = startCell.X, y = startCell.Y, z = startCell.Z;
        SetupAxis(walkOrigin.X, direction.X, x, out int stepX, out float tMaxX, out float tDeltaX);
        SetupAxis(walkOrigin.Y, direction.Y, y, out int stepY, out float tMaxY, out float tDeltaY);
        SetupAxis(walkOrigin.Z, direction.Z, z, out int stepZ, out float tMaxZ, out float tDeltaZ);

        // One cell of slack past the box exit, for slab-vs-DDA rounding.
        float walkLimit = walkEnd - baseT + ChunkCoord.CellSize;

        float entryT = 0f;
        while (true)
        {
            // t is relative to the walk origin: global t = baseT + local t.
            float exitT = MathF.Min(MathF.Min(tMaxX, tMaxY), tMaxZ);
            float segmentEnd = MathF.Min(exitT, maxLocal);

            if (segmentEnd > entryT && StorageChunks.TryGet(new ChunkCoord(x, y, z), out WorldChunk chunk))
            {
                // Overshoot only at cell boundaries, never past the ray's end.
                float window = segmentEnd - entryT +
                    (segmentEnd < maxLocal ? RaycastIntervalEpsilon : 0f);
                Vector3 segmentStart = walkOrigin + direction * entryT;
                if (chunk.Bsp.Raycast(segmentStart, direction, window, out BspRaycastHit local))
                {
                    float distance = baseT + entryT + local.Distance;
                    if (distance > maxDistance)
                        return false;
                    hit = new BspRaycastHit(local.Point, local.Normal, distance);
                    return true;
                }
            }

            if (exitT >= maxLocal || entryT > walkLimit)
                return false;

            // Ties (edge and corner crossings) step one axis at a time.
            entryT = exitT;
            if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
            {
                x += stepX;
                tMaxX += tDeltaX;
            }
            else if (tMaxY <= tMaxZ)
            {
                y += stepY;
                tMaxY += tDeltaY;
            }
            else
            {
                z += stepZ;
                tMaxZ += tDeltaZ;
            }
        }

        // Boundary arithmetic must match ChunkCoord.MinCorner/MaxCorner bit for bit.
        static void SetupAxis(float origin, float direction, int cell,
            out int step, out float tMax, out float tDelta)
        {
            if (direction > 0f)
            {
                step = 1;
                tMax = ((cell + 1) * ChunkCoord.CellSize - origin) / direction;
                tDelta = ChunkCoord.CellSize / direction;
            }
            else if (direction < 0f)
            {
                step = -1;
                tMax = (cell * ChunkCoord.CellSize - origin) / direction;
                tDelta = ChunkCoord.CellSize / -direction;
            }
            else
            {
                step = 0;
                tMax = float.PositiveInfinity;
                tDelta = float.PositiveInfinity;
            }
        }

        // Slab test. Grows enter and shrinks walkEnd to the box interval.
        static bool ClipToBox(Vector3 origin, Vector3 direction, Vector3 boxMin, Vector3 boxMax,
            ref float enter, ref float walkEnd)
        {
            return ClipAxis(origin.X, direction.X, boxMin.X, boxMax.X, ref enter, ref walkEnd) &&
                   ClipAxis(origin.Y, direction.Y, boxMin.Y, boxMax.Y, ref enter, ref walkEnd) &&
                   ClipAxis(origin.Z, direction.Z, boxMin.Z, boxMax.Z, ref enter, ref walkEnd);
        }

        static bool ClipAxis(float origin, float direction, float min, float max, ref float enter, ref float exit)
        {
            if (direction == 0f)
                return origin >= min && origin <= max;

            float inverse = 1f / direction;
            float t0 = (min - origin) * inverse;
            float t1 = (max - origin) * inverse;
            if (t0 > t1)
                (t0, t1) = (t1, t0);
            if (t0 > enter)
                enter = t0;
            if (t1 < exit)
                exit = t1;
            return enter <= exit;
        }
    }

    /// <summary>
    /// Builds one position+normal+uv mesh (8 floats per vertex) over the whole
    /// world. The engine renders <see cref="ChunkMeshes"/>; this is the
    /// reference for tests, benchmarks and tools.
    /// </summary>
    public (float[] Vertices, uint[] Indices) BuildMesh() => BuildMeshArrays(Surfaces);

    /// <summary>Which slice of <see cref="BuildMesh"/>'s index array belongs to which material.</summary>
    public IReadOnlyList<MaterialRun> BuildMaterialRuns() => BuildMaterialRunArray(Surfaces);

    // Shared by BuildMesh and ChunkMeshBuilder so both emit the same triangles.
    internal static (float[] Vertices, uint[] Indices) BuildMeshArrays(IReadOnlyList<Polygon> surfaces)
    {
        int totalVertices = 0;
        int totalIndices = 0;
        foreach (Polygon poly in surfaces)
        {
            totalVertices += poly.VertexCount;
            // A fan over n vertices emits 3*(n-2) indices, zero for a degenerate face.
            totalIndices += Math.Max(3 * (poly.VertexCount - 2), 0);
        }

        var vertices = new float[totalVertices * 8];
        var indices = new uint[totalIndices];
        int vc = 0;
        int ic = 0;
        uint baseIndex = 0;

        foreach (Polygon poly in surfaces)
        {
            Vector3 normal = poly.Surface.Normal;

            // u = dot(p, UAxis) / UScale + UOffset, likewise v. See FaceSurface.
            FaceSurface face = poly.Face;
            face.ResolveAxes(normal, out Vector3 uAxis, out Vector3 vAxis, out float uScale, out float vScale);

            float invUScale = 1f / uScale;
            float invVScale = 1f / vScale;
            float uOffset = face.UOffset;
            float vOffset = face.VOffset;

            foreach (Vector3 v in poly.VertexSpan)
            {
                vertices[vc++] = v.X; vertices[vc++] = v.Y; vertices[vc++] = v.Z;
                vertices[vc++] = normal.X; vertices[vc++] = normal.Y; vertices[vc++] = normal.Z;
                vertices[vc++] = Vector3.Dot(v, uAxis) * invUScale + uOffset;
                vertices[vc++] = Vector3.Dot(v, vAxis) * invVScale + vOffset;
            }

            for (uint i = 1; i + 1 < poly.VertexCount; i++)
            {
                indices[ic++] = baseIndex;
                indices[ic++] = baseIndex + i;
                indices[ic++] = baseIndex + i + 1;
            }

            baseIndex += (uint)poly.VertexCount;
        }

        return (vertices, indices);
    }

    // Material runs over the index array BuildMeshArrays makes for the same
    // list. Consecutive surfaces of one material merge; nothing is reordered,
    // because surface order is pinned by the mesh arrays.
    internal static MaterialRun[] BuildMaterialRunArray(IReadOnlyList<Polygon> surfaces)
    {
        int runCount = 0;
        MaterialRef previous = default;
        bool started = false;
        for (int i = 0; i < surfaces.Count; i++)
        {
            if (IndexCountOf(surfaces[i]) == 0)
                continue;
            MaterialRef material = surfaces[i].Face.Material;
            if (!started || material != previous)
            {
                runCount++;
                started = true;
                previous = material;
            }
        }

        var runs = new MaterialRun[runCount];
        int cursor = 0;
        int runStart = 0;
        int write = 0;
        started = false;
        previous = default;
        for (int i = 0; i < surfaces.Count; i++)
        {
            int emitted = IndexCountOf(surfaces[i]);
            if (emitted == 0)
                continue;

            MaterialRef material = surfaces[i].Face.Material;
            if (!started)
            {
                runStart = cursor;
                previous = material;
                started = true;
            }
            else if (material != previous)
            {
                runs[write++] = new MaterialRun(previous, runStart, cursor - runStart);
                runStart = cursor;
                previous = material;
            }

            cursor += emitted;
        }

        if (started)
            runs[write] = new MaterialRun(previous, runStart, cursor - runStart);

        return runs;

        // Must match the emit loop in BuildMeshArrays.
        static int IndexCountOf(Polygon poly) => Math.Max(3 * (poly.VertexCount - 2), 0);
    }
}
