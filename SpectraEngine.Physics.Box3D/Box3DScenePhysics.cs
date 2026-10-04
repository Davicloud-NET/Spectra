using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Physics.Box3D.Native;

namespace SpectraEngine.Physics.Box3D;

/// <summary>
/// Box3D-backed <see cref="IScenePhysics"/>. The static world becomes one static
/// body per chunk cell, with one convex hull per authored brush.
/// </summary>
// Hulls come from the authored brushes, which are convex; the carved skin is not.
// Each body sits at its cell's corner so hull coordinates stay small however
// far out the cell is.
public sealed class Box3DScenePhysics : IScenePhysics
{
    private readonly ILogger _logger;
    private readonly Dictionary<ChunkCoord, ChunkBody> _chunkBodies = [];

    // Cached for one sync only. A shape copies its hull, so keeping ours
    // longer buys nothing; within a sync one Brush often backs many placements.
    private readonly Dictionary<Brush, nint> _syncHulls = new(BrushReferenceComparer.Instance);
    private readonly List<ChunkCoord> _removalScratch = [];

    private B3WorldId _world;
    private CsgWorld? _syncedWorld;
    private bool _disposed;

    /// <summary>Creates the world. Throws if the loaded box3d library is a double-precision build.</summary>
    public Box3DScenePhysics(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        // Every struct in the binding assumes the float build.
        if (B3.IsDoublePrecision())
        {
            throw new InvalidOperationException(
                "The loaded box3d library was built with BOX3D_DOUBLE_PRECISION, which invalidates " +
                "every struct layout this binding declares. Rebuild it with native/build-box3d.ps1.");
        }

        // Before DefaultWorldDef: the default defs bake in the length scale
        // when they are called.
        B3.SetLengthUnitsPerMeter(PhysicsDefaults.MetresPerUnit);

        B3WorldDef def = B3.DefaultWorldDef();
        def.Gravity = B3Vec3.From(PhysicsDefaults.Gravity);

        _world = B3.CreateWorld(in def);
        if (_world.Index1 == 0)
        {
            throw new InvalidOperationException(
                "Box3D refused to create a world. In a release build a zeroed id is the only " +
                "signal it gives — the usual cause is exceeding the library's world limit.");
        }

        int workers = B3.World_GetWorkerCount(_world);
        B3Version version = B3.GetVersion();
        _logger.LogInformation(
            "Box3D {Version} world created: {Workers} worker(s), gravity {Gravity} sunit/s², " +
            "1 sunit = {Metres} m, fixed tick {Hz} Hz",
            version, workers, PhysicsDefaults.Gravity.Y, PhysicsDefaults.MetresPerUnit,
            PhysicsDefaults.TicksPerSecond);

        if (workers != 1)
        {
            // Not fatal, but extra worker threads change determinism.
            _logger.LogWarning(
                "Box3D reports {Workers} workers; the serial path was expected. Simulation is no " +
                "longer single-threaded.", workers);
        }
    }

    /// <inheritdoc/>
    public bool IsSimulating => true;

    /// <inheritdoc/>
    public int BodyCount => _chunkBodies.Count;

    /// <inheritdoc/>
    public int StaticShapeCount { get; private set; }

    /// <summary>
    /// Brushes cut by a subtractive brush whose collision ignores the cut, as of
    /// the last sync. A convex hull per brush cannot express the hole, so a
    /// doorway through such a brush is still solid to the solver.
    /// </summary>
    public int CutBrushesWithoutCollision { get; private set; }

    internal B3WorldId World => _world;

    /// <inheritdoc/>
    public void SyncStaticWorld(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ThrowIfDisposed();

        CsgWorld? world = scene.StaticWorld;

        // A landed compile is always a new CsgWorld instance.
        if (ReferenceEquals(world, _syncedWorld))
            return;

        if (world is null)
        {
            DestroyAllChunkBodies();
            _staticShapeChurnSinceRebuild = 0;
            _syncedWorld = null;
            return;
        }

        IReadOnlyList<ChunkCoord>? dirty = world.DirtyCells;
        try
        {
            if (dirty is null)
            {
                // Full compile: any cell may have changed or vanished.
                DestroyAllChunkBodies();
                CutBrushesWithoutCollision = 0;
                foreach (WorldChunk chunk in world.Chunks.OrderedChunks)
                    BuildChunkBody(world, chunk);

                RebuildStaticTree();
            }
            else
            {
                // Incremental: only dirty cells are rebuilt, and the tree is not
                // rebuilt per sync. Box3D inserts and removes static leaves as
                // shapes come and go, so the tree stays correct; a rebuild only
                // improves quality and costs O(world log world).
                for (int i = 0; i < dirty.Count; i++)
                {
                    ChunkCoord coord = dirty[i];
                    int destroyed = DestroyChunkBody(coord);
                    int created = 0;
                    if (world.Chunks.TryGet(coord, out WorldChunk chunk))
                        created = BuildChunkBody(world, chunk);

                    // Net change only. An animating brush rebuilds the same cell
                    // with near-identical AABBs every compile, which barely
                    // degrades the tree; counting it gross would trigger a
                    // world-sized rebuild every few hundred frames.
                    _staticShapeChurnSinceRebuild += Math.Abs(created - destroyed);
                }

                // Amortised: rebuild once a quarter of the world's shapes changed.
                if (_staticShapeChurnSinceRebuild > Math.Max(RebuildChurnFloor, StaticShapeCount / 4))
                    RebuildStaticTree();
            }
        }
        finally
        {
            ReleaseSyncHulls();
        }

        _syncedWorld = world;
    }

    // Below this much churn the tree is never rebuilt.
    private const int RebuildChurnFloor = 256;

    private int _staticShapeChurnSinceRebuild;

    internal int StaticTreeRebuilds { get; private set; }

    private void RebuildStaticTree()
    {
        B3.World_RebuildStaticTree(_world);
        _staticShapeChurnSinceRebuild = 0;
        StaticTreeRebuilds++;
    }

    /// <inheritdoc/>
    public void PushKinematicTargets(float fixedDt)
    {
        ThrowIfDisposed();
        // Nothing is kinematic yet.
    }

    /// <inheritdoc/>
    public void Step(float fixedDt)
    {
        ThrowIfDisposed();
        B3.World_Step(_world, fixedDt, SubStepCount);
    }

    /// <inheritdoc/>
    public void DrainEvents()
    {
        ThrowIfDisposed();
        // No dynamic bodies yet, so nothing to drain.
    }

    /// <inheritdoc/>
    public void PublishRenderPoses(float alpha)
    {
        ThrowIfDisposed();
        // Nothing simulated yet.
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ReleaseSyncHulls();

        // Destroying the world destroys its bodies and shapes.
        _chunkBodies.Clear();
        StaticShapeCount = 0;

        if (_world.Index1 != 0)
        {
            // Once only: box3d decrements its world count before validating
            // the id, so a double destroy corrupts the count.
            B3.DestroyWorld(_world);
            _world = default;
        }
    }

    // Box3D's default. Not tuned here.
    private const int SubStepCount = 4;

    // Returns the number of shapes created.
    private int BuildChunkBody(CsgWorld world, WorldChunk chunk)
    {
        IReadOnlyList<int> owned = chunk.OwnedBrushIndices;
        if (owned.Count == 0)
            return 0;

        Vector3 origin = chunk.Coord.MinCorner;

        B3BodyDef bodyDef = B3.DefaultBodyDef();
        bodyDef.Type = B3BodyType.Static;
        bodyDef.Position = B3Pos.From(origin);

        B3BodyId body = B3.CreateBody(_world, in bodyDef);
        if (body.Index1 == 0)
        {
            _logger.LogError("Box3D refused a static body for chunk {Coord}.", chunk.Coord);
            return 0;
        }

        B3ShapeDef shapeDef = B3.DefaultShapeDef();
        // Static bodies have no mass; skip the per-shape recompute.
        shapeDef.UpdateBodyMass = 0;

        int shapes = 0;
        IReadOnlyList<BrushPlacement> placements = world.Placements;

        for (int i = 0; i < owned.Count; i++)
        {
            BrushPlacement placement = placements[owned[i]];
            Brush brush = placement.Brush;

            // A hole gets no hull. It does not cut the hulls of the brushes it
            // overlaps either; see CutBrushesWithoutCollision.
            if (brush.Operation == BrushOperation.Subtractive)
                continue;

            if (IsCutBySubtractiveBrush(world, chunk, placement))
                CutBrushesWithoutCollision++;

            nint hull = AcquireHull(brush);
            if (hull == 0)
                continue;

            if (!TryDecomposeRigid(placement.Transform, origin, out B3Transform local))
            {
                _logger.LogError(
                    "Brush placement in chunk {Coord} has a non-rigid transform and was given no " +
                    "collision. Brush node transforms must be rotation and translation only.",
                    chunk.Coord);
                continue;
            }

            B3ShapeId shape = B3.CreateTransformedHullShape(
                body, in shapeDef, hull, local, new B3Vec3(1f, 1f, 1f));

            if (shape.Index1 == 0)
                _logger.LogError("Box3D refused a hull shape in chunk {Coord}.", chunk.Coord);
            else
                shapes++;
        }

        if (shapes == 0)
        {
            B3.DestroyBody(body);
            return 0;
        }

        _chunkBodies[chunk.Coord] = new ChunkBody(body, shapes);
        StaticShapeCount += shapes;
        return shapes;
    }

    // AABB test only, so it can over-report. Fine for a warning.
    private static bool IsCutBySubtractiveBrush(CsgWorld world, WorldChunk chunk, BrushPlacement placement)
    {
        Aabb bounds = placement.WorldBounds;
        IReadOnlyList<int> resident = chunk.ResidentBrushIndices;
        IReadOnlyList<BrushPlacement> placements = world.Placements;

        for (int i = 0; i < resident.Count; i++)
        {
            BrushPlacement other = placements[resident[i]];
            if (other.Brush.Operation == BrushOperation.Subtractive && other.WorldBounds.Intersects(bounds))
                return true;
        }

        return false;
    }

    private nint AcquireHull(Brush brush)
    {
        if (_syncHulls.TryGetValue(brush, out nint cached))
            return cached;

        HullRefusal refusal = BrushHullBuilder.TryCreate(brush, out nint hull, out string detail);
        if (refusal != HullRefusal.None)
        {
            _logger.LogError("Brush has no collision ({Refusal}). {Detail}", refusal, detail);
            hull = 0;
        }

        // Cache failures too: one log line per bad brush per sync.
        _syncHulls[brush] = hull;
        return hull;
    }

    private void ReleaseSyncHulls()
    {
        foreach (nint hull in _syncHulls.Values)
            BrushHullBuilder.Destroy(hull);
        _syncHulls.Clear();
    }

    // Returns the number of shapes destroyed.
    private int DestroyChunkBody(ChunkCoord coord)
    {
        if (!_chunkBodies.Remove(coord, out ChunkBody entry))
            return 0;

        B3.DestroyBody(entry.Body);
        StaticShapeCount -= entry.ShapeCount;
        return entry.ShapeCount;
    }

    private void DestroyAllChunkBodies()
    {
        _removalScratch.Clear();
        foreach (ChunkCoord coord in _chunkBodies.Keys)
            _removalScratch.Add(coord);

        for (int i = 0; i < _removalScratch.Count; i++)
            DestroyChunkBody(_removalScratch[i]);

        _removalScratch.Clear();
    }

    // False for a non-rigid matrix. The scene rejects scaled brush placements
    // before a compile, so one arriving here is a bug upstream.
    private static bool TryDecomposeRigid(Matrix4x4 world, Vector3 origin, out B3Transform local)
    {
        local = default;

        if (!Matrix4x4.Decompose(world, out Vector3 scale, out Quaternion rotation, out Vector3 translation))
            return false;

        const float scaleTolerance = 1e-3f;
        if (MathF.Abs(scale.X - 1f) > scaleTolerance ||
            MathF.Abs(scale.Y - 1f) > scaleTolerance ||
            MathF.Abs(scale.Z - 1f) > scaleTolerance)
        {
            return false;
        }

        local = new B3Transform
        {
            P = B3Vec3.From(translation - origin),
            Q = B3Quat.From(Quaternion.Normalize(rotation)),
        };
        return true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private readonly record struct ChunkBody(B3BodyId Body, int ShapeCount);

    private sealed class BrushReferenceComparer : IEqualityComparer<Brush>
    {
        public static BrushReferenceComparer Instance { get; } = new();

        public bool Equals(Brush? x, Brush? y) => ReferenceEquals(x, y);

        public int GetHashCode(Brush obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
