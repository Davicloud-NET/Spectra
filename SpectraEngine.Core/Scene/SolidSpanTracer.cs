using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Scene;

// Finds the brushes a segment passes through and hands the stretch it spends
// in each to a SolidSpanComposer. Keeps its scratch storage, so a trace
// allocates nothing once it has run. Render thread only, like its scene.
internal sealed class SolidSpanTracer
{
    private readonly SolidSpanComposer _composer = new();
    private readonly List<SceneNode> _partScratch = [];

    // Per world placement, the trace that last clipped it. A brush resident in
    // several cells is met once per cell and must count once.
    private int[] _clippedInTrace = [];
    private int _trace;

    public int Trace(
        Scene scene, Vector3 from, Vector3 to, in SceneQueryFilter filter,
        Span<SolidSpan> spans, out bool truncated)
    {
        truncated = false;

        Vector3 offset = to - from;
        float length = offset.Length();
        if (!(length > 0f) || !float.IsFinite(length))
            return 0;

        Vector3 direction = offset / length;

        _composer.Clear();

        if (!filter.ExcludeStaticWorldBrushes)
            AddWorld(scene, from, direction, length);

        AddParts(scene, from, direction, length, in filter);

        return _composer.Compose(spans, out truncated);
    }

    // The live world's placements, or a baked map's hulls. Both are bucketed
    // by cell the same way.
    private void AddWorld(Scene scene, Vector3 from, Vector3 direction, float length)
    {
        IReadOnlyList<BrushPlacement> placements;
        ChunkGrid cells;

        if (scene.StaticWorld is { } world)
        {
            placements = world.StoragePlacements;
            cells = world.StorageChunks;
        }
        else if (scene.CompiledStaticWorld is { } baked)
        {
            placements = baked.CollisionPlacements;
            cells = baked.CollisionCells;
        }
        else
        {
            return;
        }

        BeginTrace(placements.Count);

        var walk = new CellResidents(this, placements, cells, from, direction, length);
        ChunkRayWalk.Cast(walk, from, direction, length, out _);
    }

    private void BeginTrace(int placementCount)
    {
        if (_clippedInTrace.Length < placementCount)
            _clippedInTrace = new int[Math.Max(placementCount, _clippedInTrace.Length * 2)];

        if (_trace == int.MaxValue)
        {
            Array.Clear(_clippedInTrace);
            _trace = 0;
        }

        _trace++;
    }

    private void AddWorldBrush(
        IReadOnlyList<BrushPlacement> placements, int index, Vector3 from, Vector3 direction, float length)
    {
        if (_clippedInTrace[index] == _trace)
            return;
        _clippedInTrace[index] = _trace;

        BrushPlacement placement;
        UInt128 rank;

        // A live world stores placements in slots that edits do not shift, so
        // placement order is a key beside the slot. Elsewhere it is the index.
        if (placements is PlacementSlotView slots)
        {
            if (slots.Snapshot.Slots[index] is not { } entry)
                return;
            placement = entry.Placement;
            rank = entry.Order;
        }
        else
        {
            placement = placements[index];
            rank = (UInt128)index;
        }

        Brush brush = placement.Brush;
        if (!TryClip(brush, placement.Transform, from, direction, length,
                out float start, out float end, out int facePlane))
        {
            return;
        }

        if (brush.Operation == BrushOperation.Subtractive)
            _composer.AddCut(start, end);
        else
            _composer.AddWorldSolid(start, end, brush.FaceSurfaces[facePlane].Material, rank);
    }

    private void AddParts(Scene scene, Vector3 from, Vector3 direction, float length, in SceneQueryFilter filter)
    {
        // World brush nodes are in the index too. The compiled world answers
        // for them.
        SceneQueryFilter partFilter = filter with { ExcludeStaticWorldBrushes = true };

        _partScratch.Clear();
        scene.Bvh.QueryRay(new Ray3(from, direction), length, _partScratch, in partFilter);

        for (int i = 0; i < _partScratch.Count; i++)
        {
            SceneNode node = _partScratch[i];

            // A part that does not collide is not in the way. A subtractive
            // part carves nothing, whatever the filter lets through.
            if (node.BrushKind != BrushKind.Part || !node.CanCollide ||
                node.Brush is not { Operation: BrushOperation.Additive } brush)
            {
                continue;
            }

            if (TryClip(brush, node.WorldMatrix, from, direction, length,
                    out float start, out float end, out int facePlane))
            {
                _composer.AddPart(start, end, brush.FaceSurfaces[facePlane].Material, RankOf(node.Id));
            }
        }

        // Do not keep the nodes alive between traces.
        _partScratch.Clear();
    }

    // The stretch of the segment inside the brush, and the plane whose face
    // names its material: the one entered, or for a segment that starts inside
    // the one it leaves by.
    // Works in the brush's frame, measured from the brush's own position, so a
    // level far from the origin gets the same answer as one at it. A general
    // inverse, because a part's node may carry scale. Distances stay in world
    // units: only the direction is scaled.
    private static bool TryClip(
        Brush brush, in Matrix4x4 world, Vector3 from, Vector3 direction, float length,
        out float start, out float end, out int facePlane)
    {
        start = 0f;
        end = 0f;
        facePlane = -1;

        Matrix4x4 linear = world;
        linear.Translation = Vector3.Zero;
        if (!Matrix4x4.Invert(linear, out Matrix4x4 toLocal))
            return false;

        Vector3 origin = Vector3.TransformNormal(from - world.Translation, toLocal);
        Vector3 localDirection = Vector3.TransformNormal(direction, toLocal);

        float tEnter = float.NegativeInfinity;
        float tExit = float.PositiveInfinity;
        if (!BrushLineClip.Clip(
                brush.LocalPlaneSpan, origin, localDirection, surfaceIsInside: false,
                ref tEnter, ref tExit, out int enterPlane, out int exitPlane))
        {
            return false;
        }

        start = MathF.Max(tEnter, 0f);
        end = MathF.Min(tExit, length);
        if (!(end > start))
            return false;

        facePlane = tEnter >= 0f || exitPlane < 0 ? enterPlane : exitPlane;
        return facePlane >= 0;
    }

    private static UInt128 RankOf(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        return MemoryMarshal.Read<UInt128>(bytes);
    }

    // Lets ChunkRayWalk step through the cells: every cell reports no hit, so
    // the walk goes on to the segment's end, and each cell's residents are
    // clipped on the way.
    private readonly struct CellResidents(
        SolidSpanTracer tracer, IReadOnlyList<BrushPlacement> placements, ChunkGrid cells,
        Vector3 from, Vector3 direction, float length) : IChunkRayCells
    {
        public bool TryGetCellBounds(out ChunkCoord min, out ChunkCoord max) =>
            cells.TryGetCellBounds(out min, out max);

        public bool ContainsPoint(Vector3 point) => false;

        public bool RaycastCell(
            ChunkCoord cell, Vector3 origin, Vector3 cellDirection, float maxDistance, out BspRaycastHit hit)
        {
            hit = default;

            if (cells.TryGet(cell, out WorldChunk chunk))
            {
                IReadOnlyList<int> residents = chunk.ResidentBrushIndices;
                for (int i = 0; i < residents.Count; i++)
                    tracer.AddWorldBrush(placements, residents[i], from, direction, length);
            }

            return false;
        }
    }
}
