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

    // Brushes the last trace clipped, world and part. For tests of the cell walk.
    public int BrushesClipped { get; private set; }

    public int Trace(
        Scene scene, Vector3 from, Vector3 to, in SceneQueryFilter filter,
        Span<SolidSpan> spans, out bool truncated)
    {
        truncated = false;

        Vector3 offset = to - from;
        float length = offset.Length();
        if (!(length > 0f) || !float.IsFinite(length))
            return 0;

        var segment = new Segment(from, offset / length, length);

        _composer.Clear();
        BrushesClipped = 0;

        if (!filter.ExcludeStaticWorldBrushes)
            AddWorld(scene, in segment);

        AddParts(scene, in segment, in filter);

        return _composer.Compose(spans, out truncated);
    }

    // The live world's placements, or a baked map's hulls. Both are bucketed
    // by cell the same way.
    private void AddWorld(Scene scene, in Segment segment)
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

        var walk = new CellResidents(this, placements, cells, segment);
        ChunkRayWalk.Cast(walk, segment.From, segment.Direction, segment.Length, out _);
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

    private void AddWorldBrush(IReadOnlyList<BrushPlacement> placements, int index, in Segment segment)
    {
        if (_clippedInTrace[index] == _trace)
            return;
        _clippedInTrace[index] = _trace;
        BrushesClipped++;

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
        if (!TryClip(brush, placement.Transform, in segment, out Stretch stretch))
            return;

        if (brush.Operation == BrushOperation.Subtractive)
        {
            _composer.AddCut(stretch.Start, stretch.End);
            return;
        }

        _composer.AddWorldSolid(
            stretch.Start, stretch.End, brush.FaceSurfaces[stretch.FacePlane].Material, rank);
    }

    private void AddParts(Scene scene, in Segment segment, in SceneQueryFilter filter)
    {
        // World brush nodes are in the index too. The compiled world answers
        // for them.
        SceneQueryFilter partFilter = filter with { ExcludeStaticWorldBrushes = true };

        _partScratch.Clear();
        scene.Bvh.QueryRay(
            new Ray3(segment.From, segment.Direction), segment.Length, _partScratch, in partFilter);

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

            BrushesClipped++;
            if (TryClip(brush, node.WorldMatrix, in segment, out Stretch stretch))
            {
                _composer.AddPart(
                    stretch.Start, stretch.End, brush.FaceSurfaces[stretch.FacePlane].Material, RankOf(node.Id));
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
    private static bool TryClip(Brush brush, in Matrix4x4 world, in Segment segment, out Stretch stretch)
    {
        stretch = default;

        Matrix4x4 linear = world;
        linear.Translation = Vector3.Zero;
        if (!Matrix4x4.Invert(linear, out Matrix4x4 toLocal))
            return false;

        Vector3 origin = Vector3.TransformNormal(segment.From - world.Translation, toLocal);
        Vector3 direction = Vector3.TransformNormal(segment.Direction, toLocal);

        var clip = new BrushLineClip(float.NegativeInfinity, float.PositiveInfinity);
        if (!clip.Clip(brush.LocalPlaneSpan, origin, direction, surfaceIsInside: false))
            return false;

        float start = MathF.Max(clip.Enter, 0f);
        float end = MathF.Min(clip.Exit, segment.Length);
        if (!(end > start))
            return false;

        int facePlane = clip.Enter >= 0f || clip.ExitPlane < 0 ? clip.EnterPlane : clip.ExitPlane;
        stretch = new Stretch(start, end, facePlane);
        return facePlane >= 0;
    }

    private static UInt128 RankOf(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        return MemoryMarshal.Read<UInt128>(bytes);
    }

    // Direction is unit length, so a distance along it is in world units.
    private readonly record struct Segment(Vector3 From, Vector3 Direction, float Length);

    private readonly record struct Stretch(float Start, float End, int FacePlane);

    // Lets ChunkRayWalk step through the cells: every cell reports no hit, so
    // the walk goes on to the segment's end, and each cell's residents are
    // clipped on the way.
    private readonly struct CellResidents(
        SolidSpanTracer tracer, IReadOnlyList<BrushPlacement> placements, ChunkGrid cells, Segment segment)
        : IChunkRayCells
    {
        public bool TryGetCellBounds(out ChunkCoord min, out ChunkCoord max) =>
            cells.TryGetCellBounds(out min, out max);

        public bool ContainsPoint(Vector3 point) => false;

        public bool RaycastCell(
            ChunkCoord cell, Vector3 origin, Vector3 direction, float maxDistance, out BspRaycastHit hit)
        {
            hit = default;

            if (cells.TryGet(cell, out WorldChunk chunk))
            {
                IReadOnlyList<int> residents = chunk.ResidentBrushIndices;
                for (int i = 0; i < residents.Count; i++)
                    tracer.AddWorldBrush(placements, residents[i], in segment);
            }

            return false;
        }
    }
}
