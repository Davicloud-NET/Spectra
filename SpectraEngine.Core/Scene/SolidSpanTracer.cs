using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Scene;

// Finds the brushes a segment passes through and hands the stretch it spends
// in each to a SolidSpanComposer. Keeps its scratch storage, so a trace
// allocates nothing once it has run. Render thread only, like its scene.
internal sealed class SolidSpanTracer(Scene scene)
{
    // A segment that stays this close to a face plane lies in it. The carve
    // reads a point this close to a plane as on it.
    private const float FaceSlack = Polygon.Epsilon;

    private readonly SolidSpanComposer _composer = new();
    private readonly SolidSpanSides _sides = new();
    private readonly List<SceneNode> _partScratch = [];

    // Per world placement, the trace that last clipped it. A brush resident in
    // several cells is met once per cell and must count once.
    private int[] _clippedInTrace = [];
    private int _trace;

    // Brushes the last trace clipped, world and part. For tests of the cell walk.
    public int BrushesClipped { get; private set; }

    public int Trace(
        Vector3 from, Vector3 to, in SceneQueryFilter filter, Span<SolidSpan> spans, out bool truncated)
    {
        truncated = false;

        // Nothing shorter can hold a span thick enough to report.
        Vector3 offset = to - from;
        float length = offset.Length();
        if (!(length >= SolidSpan.Tolerance) || !float.IsFinite(length))
            return 0;

        var segment = new Segment(from, offset / length, length);

        _composer.Clear();
        _sides.Clear();
        BrushesClipped = 0;

        if (!filter.ExcludeStaticWorldBrushes)
            AddWorld(in segment);

        AddParts(in segment, in filter);

        return _sides.Any
            ? _sides.Compose(_composer, segment.Direction, spans, out truncated)
            : _composer.Compose(default, spans, out truncated);
    }

    // The live world's placements, or a baked map's hulls. Both are bucketed
    // by cell the same way.
    private void AddWorld(in Segment segment)
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

        _sides.Add(stretch.Faces);
        if (brush.Operation == BrushOperation.Subtractive)
            _composer.AddCut(stretch.Start, stretch.End, stretch.LeftBy, rank, stretch.Faces);
        else
            _composer.AddWorldSolid(stretch.Start, stretch.End, stretch.EnteredBy, rank, stretch.Faces);
    }

    private void AddParts(in Segment segment, in SceneQueryFilter filter)
    {
        // World brush nodes are in the index too. The compiled world answers
        // for them.
        SceneQueryFilter partFilter = filter with { ExcludeStaticWorldBrushes = true };

        _partScratch.Clear();
        scene.Bvh.QueryRay(
            new Ray3(segment.From, segment.Direction), segment.Length, FaceSlack, _partScratch, in partFilter);

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
            if (!TryClip(brush, node.WorldMatrix, in segment, out Stretch stretch))
                continue;

            _sides.Add(stretch.Faces);
            _composer.AddPart(stretch.Start, stretch.End, stretch.EnteredBy, RankOf(node.Id), stretch.Faces);
        }

        // Do not keep the nodes alive between traces.
        _partScratch.Clear();
    }

    // The stretch of the segment inside the brush or, for a segment that lies
    // in its faces, along them.
    private static bool TryClip(Brush brush, in Matrix4x4 world, in Segment segment, out Stretch stretch)
    {
        stretch = default;

        // The brush's frame, measured from the brush's own position: far from
        // the world origin that subtraction keeps the digits a full inverse
        // would lose. General, because a part's node may carry scale. Only the
        // direction is scaled, so distances stay in world units.
        Matrix4x4 linear = world;
        linear.Translation = Vector3.Zero;
        if (!Matrix4x4.Invert(linear, out Matrix4x4 toLocal))
            return false;

        Vector3 origin = Vector3.TransformNormal(segment.From - world.Translation, toLocal);
        Vector3 direction = Vector3.TransformNormal(segment.Direction, toLocal);

        ReadOnlySpan<Plane> planes = brush.LocalPlaneSpan;
        var clip = new BrushLineClip(float.NegativeInfinity, float.PositiveInfinity);
        if (!clip.ClipSegment(planes, origin, direction, segment.Length, FaceSlack) || clip.FaceCount > 2)
            return false;

        float start = MathF.Max(clip.Enter, 0f);
        float end = MathF.Min(clip.Exit, segment.Length);
        if (!(end > start))
            return false;

        // A segment that starts on a face computes its entry a hair to either
        // side of zero. Within the tolerance it still entered by that face.
        bool entered = clip.Enter >= -SolidSpan.Tolerance || clip.ExitPlane < 0;
        int enteredBy = entered ? clip.EnterPlane : clip.ExitPlane;
        if (enteredBy < 0)
            enteredBy = clip.FirstFace;
        if (enteredBy < 0)
            return false;

        var faces = new LineFaces(
            WorldNormal(planes, clip.FirstFace, in toLocal), WorldNormal(planes, clip.SecondFace, in toLocal));

        IReadOnlyList<FaceSurface> surfaces = brush.FaceSurfaces;
        MaterialRef leftBy = clip.ExitPlane >= 0 ? surfaces[clip.ExitPlane].Material : default;
        stretch = new Stretch(start, end, surfaces[enteredBy].Material, leftBy, faces);
        return true;
    }

    // Zero for no plane. A normal goes to world space through the inverse's
    // transpose, which is what keeps it square to its face under scale.
    private static Vector3 WorldNormal(ReadOnlySpan<Plane> planes, int plane, in Matrix4x4 toLocal) =>
        plane < 0
            ? Vector3.Zero
            : Vector3.Normalize(Vector3.TransformNormal(planes[plane].Normal, Matrix4x4.Transpose(toLocal)));

    private static UInt128 RankOf(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        return MemoryMarshal.Read<UInt128>(bytes);
    }

    // Direction is unit length, so a distance along it is in world units.
    private readonly record struct Segment(Vector3 From, Vector3 Direction, float Length);

    // EnteredBy is the material of the face the segment enters the brush by,
    // or leaves it by when it starts inside. LeftBy is that of the face it
    // leaves by.
    private readonly record struct Stretch(
        float Start, float End, MaterialRef EnteredBy, MaterialRef LeftBy, LineFaces Faces);

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
