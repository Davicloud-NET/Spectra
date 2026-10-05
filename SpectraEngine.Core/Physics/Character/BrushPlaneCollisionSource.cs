using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Physics.Character;

/// <summary>
/// A character collision source built from authored brush planes, with no
/// native dependency. Render-thread only.
/// </summary>
// A \ N is the union of A ∩ {hk >= 0} over N's planes: an overlapping cover of
// convex plane lists. That is how a cut doorway is walkable, which one hull per
// brush cannot express. A sweep is the minimum over elements.
// World brushes come from the compiled static world, part brushes live from the
// spatial index. A part is never cut by anything.
public sealed class BrushPlaneCollisionSource : ICharacterCollisionSource
{
    /// <summary>Cover elements one additive brush may expand to before it is refused.</summary>
    public const int MaxCoverPieces = 32;

    /// <summary>A cover element thinner than this along its generating plane is dropped.</summary>
    // Looser than the carve epsilon: a kept sliver is an invisible wall, a
    // dropped one is a gap the skin width covers.
    public const float PieceEmptyEpsilon = 1e-3f;

    private const int MaxSweepIterations = 12;

    /// <summary>How far beyond the tick volume the world lane is built.</summary>
    // About five seconds of sprinting, so walking rebuilds the lane a few
    // times a minute.
    public const float RegionMargin = 24f;

    private readonly Scene.Scene _scene;
    private readonly CharacterTuning _tuning;

    private readonly List<ConvexPiece> _worldPieces = [];
    private int _builtCompileCount = -1;
    private Aabb _builtRegion;
    private bool _hasRegion;

    // What the lane was built from, compared against a fresh selection when a
    // compile lands.
    private readonly List<BrushPlacement> _builtAdditives = [];
    private readonly List<BrushPlacement> _builtNegatives = [];
    private readonly List<BrushPlacement> _scratchAdditives = [];
    private readonly List<BrushPlacement> _scratchNegatives = [];
    private readonly HashSet<int> _selectionIndices = [];
    private readonly HashSet<int> _cutterIndices = [];
    private readonly List<int> _selectionOrder = [];
    private Aabb _dependencyBounds;
    internal int WorldSelections { get; private set; }

    // Rebuilt per tick.
    private readonly List<ConvexPiece> _partPieces = [];
    private long _partSignature;
    private bool _hasPartSignature;
    private readonly List<SceneNode> _partScratch = [];
    private readonly List<ConvexPiece> _candidates = [];

    // CanCollide decides what blocks. A part hidden from queries is still solid.
    private static readonly SceneQueryFilter PartLaneFilter = new() { IgnoreQueryFlags = true };

    public BrushPlaneCollisionSource(Scene.Scene scene, CharacterTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(tuning);
        _scene = scene;
        _tuning = tuning;
    }

    /// <inheritdoc/>
    public int Revision { get; private set; }

    /// <inheritdoc/>
    public float SkinWidth => _tuning.SkinWidth;

    /// <inheritdoc/>
    public int DroppedPlanes { get; private set; }

    /// <summary>
    /// Additive brushes cut by too many negatives to cover. Non-zero means the
    /// character collides with geometry that is not drawn.
    /// </summary>
    public int UncoveredCutBrushes { get; private set; }

    /// <summary>Cover elements the world lane currently holds.</summary>
    public int WorldPieceCount => _worldPieces.Count;

    /// <summary>Pieces the last <see cref="BeginTick"/> selected as candidates.</summary>
    public int CandidateCount => _candidates.Count;

    /// <summary>
    /// Times the world lane has been rebuilt. Should not climb with the frame
    /// counter.
    /// </summary>
    public int WorldLaneRebuilds { get; private set; }

    /// <summary>
    /// Selects the pieces a tick can touch, so every sweep and gather in that
    /// tick shares one broad phase.
    /// </summary>
    // Nothing in the scene moves during a tick.
    public void BeginTick(in Aabb volume, in CharacterQueryFilter filter)
    {
        RebuildWorldLaneIfStale(in volume);
        RebuildPartLane(in volume, in filter);

        _candidates.Clear();
        for (int i = 0; i < _worldPieces.Count; i++)
        {
            if (_worldPieces[i].Bounds.Intersects(volume))
                _candidates.Add(_worldPieces[i]);
        }

        for (int i = 0; i < _partPieces.Count; i++)
        {
            if (_partPieces[i].Bounds.Intersects(volume))
                _candidates.Add(_partPieces[i]);
        }
    }

    /// <inheritdoc/>
    public float SweepCapsule(
        in CharacterCapsule capsule,
        Vector3 translation,
        in CharacterQueryFilter filter,
        out CharacterContactPlane plane,
        out CharacterContactSource source)
    {
        plane = default;
        source = default;

        float best = 1f;
        bool hit = false;

        for (int i = 0; i < _candidates.Count; i++)
        {
            ConvexPiece piece = _candidates[i];
            if (IsSelf(in piece, in filter))
                continue;

            float fraction = CapsuleGeometry.Sweep(
                in capsule, translation, piece.Planes, piece.Faces,
                _tuning.SkinWidth, MaxSweepIterations, CharacterPlaneSolver.Tolerance * 0.25f,
                out Vector3 normal, out Vector3 point);

            if (fraction >= best)
                continue;

            // A face buried in a sibling is not a surface. The element that
            // owns that part of the boundary reports the real hit.
            if (IsInternalContact(in piece, point))
                continue;

            best = fraction;
            hit = true;

            // D is relative to the capsule at the hit position.
            CharacterCapsule atHit = capsule.Translated(translation * fraction);
            float separation = SurfaceSeparation(in atHit, normal, point);
            plane = CharacterContactPlane.Rigid(normal, separation);
            source = new CharacterContactSource
            {
                Node = piece.Node,
                Brush = piece.Brush,
                PlaneIndex = piece.PlaneIndex,
                Point = point,
            };
        }

        return hit ? best : 1f;
    }

    /// <inheritdoc/>
    public int GatherPlanes(
        in CharacterCapsule capsule,
        float maxSeparation,
        in CharacterQueryFilter filter,
        Span<CharacterContactPlane> planes,
        Span<CharacterContactSource> sources)
    {
        int count = 0;

        for (int i = 0; i < _candidates.Count; i++)
        {
            ConvexPiece piece = _candidates[i];
            if (IsSelf(in piece, in filter))
                continue;

            float distance = CapsuleGeometry.Distance(
                in capsule, piece.Planes, piece.Faces, out Vector3 normal, out Vector3 point);

            if (distance > maxSeparation)
                continue;

            if (IsInternalContact(in piece, point))
                continue;

            var candidatePlane = CharacterContactPlane.Rigid(normal, distance);
            var candidateSource = new CharacterContactSource
            {
                Node = piece.Node,
                Brush = piece.Brush,
                PlaneIndex = piece.PlaneIndex,
                Point = point,
            };

            // Overlapping brushes put several faces on one plane (stairs sunk
            // into a floor). Merge them or copies of the floor use up the
            // contact budget and a wall gets dropped. Deeper one wins.
            int duplicate = -1;
            for (int j = 0; j < count; j++)
            {
                if (Vector3.Dot(planes[j].Plane.Normal, normal) > DuplicateContactDot &&
                    MathF.Abs(planes[j].Plane.D - distance) < DuplicateContactOffset)
                {
                    duplicate = j;
                    break;
                }
            }

            if (duplicate >= 0)
            {
                if (distance < planes[duplicate].Plane.D)
                {
                    planes[duplicate] = candidatePlane;
                    sources[duplicate] = candidateSource;
                }
                continue;
            }

            if (count < planes.Length)
            {
                planes[count] = candidatePlane;
                sources[count] = candidateSource;
                count++;
                continue;
            }

            // Full: keep the deepest and count the drop.
            int shallowest = 0;
            for (int j = 1; j < count; j++)
            {
                if (planes[j].Plane.D > planes[shallowest].Plane.D)
                    shallowest = j;
            }

            DroppedPlanes++;
            if (distance < planes[shallowest].Plane.D)
            {
                planes[shallowest] = candidatePlane;
                sources[shallowest] = candidateSource;
            }
        }

        // Deepest first, so a truncating consumer keeps what matters.
        for (int i = 1; i < count; i++)
        {
            for (int j = i; j > 0 && planes[j].Plane.D < planes[j - 1].Plane.D; j--)
            {
                (planes[j], planes[j - 1]) = (planes[j - 1], planes[j]);
                (sources[j], sources[j - 1]) = (sources[j - 1], sources[j]);
            }
        }

        return count;
    }

    // True when the point is inside a sibling cover element. The slack keeps a
    // point on a boundary two elements share from counting as buried.
    private static bool IsInternalContact(in ConvexPiece piece, Vector3 point)
    {
        Plane[][]? siblings = piece.Siblings;
        if (siblings is null)
            return false;

        for (int i = 0; i < siblings.Length; i++)
        {
            if (ReferenceEquals(siblings[i], piece.Planes))
                continue;
            if (CapsuleGeometry.ContainsPoint(point, siblings[i], -InternalContactSlack))
                return true;
        }

        return false;
    }

    private const float InternalContactSlack = 1e-3f;

    private const float DuplicateContactDot = 0.999f;

    // Separations must also agree, so a step above a floor stays two planes.
    private const float DuplicateContactOffset = 1e-3f;

    // Null check first: world pieces have no node, and a filter with no Self
    // would otherwise match all of them.
    private static bool IsSelf(in ConvexPiece piece, in CharacterQueryFilter filter) =>
        filter.Self is not null && ReferenceEquals(piece.Node, filter.Self);

    private static float SurfaceSeparation(in CharacterCapsule capsule, Vector3 normal, Vector3 point)
    {
        float d1 = Vector3.Dot(normal, capsule.Center1 - point);
        float d2 = Vector3.Dot(normal, capsule.Center2 - point);
        return MathF.Min(d1, d2) - capsule.Radius;
    }

    private void RebuildWorldLaneIfStale(in Aabb volume)
    {
        int compileCount = _scene.StaticWorldCompileCount;
        bool covered = _hasRegion && Contains(in _builtRegion, in volume);
        if (compileCount == _builtCompileCount && covered)
            return;
        if (covered && !_scene.WorldChangedSince(_builtCompileCount, _dependencyBounds))
        {
            _builtCompileCount = compileCount;
            return;
        }

        // Don't re-centre while the character is still inside the region.
        Aabb region = covered ? _builtRegion : volume.Expanded(RegionMargin);

        SelectPlacements(in region, _scratchAdditives, _scratchNegatives);

        // Rebuild on content, not on the compile counter: an animating scene
        // compiles every frame. Moving the region is not a content change and
        // must not bump the revision.
        bool contentChanged =
            !SameSelection(_builtAdditives, _scratchAdditives) ||
            !SameSelection(_builtNegatives, _scratchNegatives);

        _builtCompileCount = compileCount;
        _builtRegion = region;
        _hasRegion = true;
        Vector3 dependencyMin = region.Min, dependencyMax = region.Max;
        foreach (BrushPlacement placement in _scratchAdditives)
        {
            dependencyMin = Vector3.Min(dependencyMin, placement.WorldBounds.Min);
            dependencyMax = Vector3.Max(dependencyMax, placement.WorldBounds.Max);
        }
        _dependencyBounds = new Aabb(dependencyMin, dependencyMax);

        if (!contentChanged)
            return;

        WorldLaneRebuilds++;
        BumpRevision();
        _worldPieces.Clear();
        UncoveredCutBrushes = 0;

        _builtAdditives.Clear();
        _builtAdditives.AddRange(_scratchAdditives);
        _builtNegatives.Clear();
        _builtNegatives.AddRange(_scratchNegatives);

        for (int i = 0; i < _builtAdditives.Count; i++)
            AddCoveredPieces(_builtAdditives[i], _builtNegatives);
    }

    // Cutters are collected against each additive's full bounds: one outside
    // the region can still cut a brush inside it.
    private void SelectPlacements(in Aabb region, List<BrushPlacement> additives, List<BrushPlacement> negatives)
    {
        WorldSelections++;
        additives.Clear();
        negatives.Clear();

        if (_scene.StaticWorld is not { } world)
            return;

        IReadOnlyList<BrushPlacement> placements = world.StoragePlacements;
        _selectionIndices.Clear();
        _cutterIndices.Clear();
        world.StorageChunks.CollectResidents(region, _selectionIndices);
        _selectionOrder.Clear();
        _selectionOrder.AddRange(_selectionIndices);
        _selectionOrder.Sort(placements as IComparer<int>);
        foreach (int i in _selectionOrder)
        {
            BrushPlacement placement = placements[i];
            if (placement.Brush.Operation == BrushOperation.Additive && placement.WorldBounds.Intersects(region))
            {
                additives.Add(placement);
                world.StorageChunks.CollectResidents(placement.WorldBounds, _cutterIndices);
            }
        }
        _selectionOrder.Clear();
        _selectionOrder.AddRange(_cutterIndices);
        _selectionOrder.Sort(placements as IComparer<int>);
        foreach (int i in _selectionOrder)
        {
            BrushPlacement cutter = placements[i];
            if (cutter.Brush.Operation != BrushOperation.Subtractive) continue;
            foreach (BrushPlacement additive in additives)
                if (cutter.WorldBounds.Intersects(additive.WorldBounds)) { negatives.Add(cutter); break; }
        }
    }

    private static bool SameSelection(List<BrushPlacement> built, List<BrushPlacement> fresh)
    {
        if (built.Count != fresh.Count)
            return false;

        for (int i = 0; i < built.Count; i++)
        {
            if (!ReferenceEquals(built[i].Brush, fresh[i].Brush) ||
                built[i].Transform != fresh[i].Transform)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(in Aabb outer, in Aabb inner) =>
        inner.Min.X >= outer.Min.X && inner.Max.X <= outer.Max.X &&
        inner.Min.Y >= outer.Min.Y && inner.Max.Y <= outer.Max.Y &&
        inner.Min.Z >= outer.Min.Z && inner.Max.Z <= outer.Max.Z;

    private void AddCoveredPieces(in BrushPlacement placement, List<BrushPlacement> negatives)
    {
        Brush brush = placement.Brush;
        Matrix4x4 transform = placement.Transform;
        Aabb bounds = placement.WorldBounds;

        var cutters = new List<BrushPlacement>();
        for (int i = 0; i < negatives.Count; i++)
        {
            if (negatives[i].WorldBounds.Intersects(bounds))
                cutters.Add(negatives[i]);
        }

        Plane[] basePlanes = WorldPlanes(brush, transform);

        if (cutters.Count == 0)
        {
            _worldPieces.Add(new ConvexPiece(
                basePlanes, WorldFaces(brush, transform), bounds, brush, null, -1));
            return;
        }

        // A \ (N1 ∪ N2 ∪ ...) is the intersection over cutters of (A \ Ni), each
        // a union over Ni's planes of A ∩ {flipped plane}. That is a Cartesian
        // product; most combinations are empty and get pruned below.
        var pieces = new List<List<Plane>> { new(basePlanes) };

        for (int c = 0; c < cutters.Count; c++)
        {
            Plane[] cutterPlanes = WorldPlanes(cutters[c].Brush, cutters[c].Transform);
            var next = new List<List<Plane>>();

            for (int p = 0; p < pieces.Count; p++)
            {
                for (int k = 0; k < cutterPlanes.Length; k++)
                {
                    // The half-space outside this face of the negative.
                    var flipped = new Plane(-cutterPlanes[k].Normal, -cutterPlanes[k].D);

                    var combined = new List<Plane>(pieces[p].Count + 1);
                    combined.AddRange(pieces[p]);

                    // A flush cut repeats a plane the brush already has. Skip it.
                    bool redundant = false;
                    for (int e = 0; e < combined.Count; e++)
                    {
                        if (ConvexFaceBuilder.SameDirectedPlane(combined[e], flipped))
                        {
                            redundant = true;
                            break;
                        }
                    }

                    if (!redundant)
                        combined.Add(flipped);

                    next.Add(combined);
                }
            }

            pieces = next;
            if (pieces.Count > MaxCoverPieces * cutterPlanes.Length)
                break;
        }

        // Every piece needs the full sibling set, so build them all first.
        var survivingPlanes = new List<Plane[]>();
        var survivingFaces = new List<Polygon[]>();

        for (int p = 0; p < pieces.Count && survivingPlanes.Count < MaxCoverPieces; p++)
        {
            Plane[] planes = [.. pieces[p]];
            Polygon[] faces = ConvexFaceBuilder.Build(planes, PieceEmptyEpsilon);
            if (faces.Length < 4)
                continue;   // empty or degenerate

            survivingPlanes.Add(planes);
            survivingFaces.Add(faces);
        }

        int emitted = survivingPlanes.Count;
        Plane[][] siblings = [.. survivingPlanes];

        for (int p = 0; p < emitted; p++)
        {
            _worldPieces.Add(new ConvexPiece(
                survivingPlanes[p], survivingFaces[p],
                ConvexFaceBuilder.Bounds(survivingFaces[p]), brush, null, -1)
            {
                Siblings = siblings,
            });
        }

        if (emitted == 0)
        {
            // The negative swallowed the whole brush.
            return;
        }

        if (emitted >= MaxCoverPieces)
        {
            UncoveredCutBrushes++;
        }
    }

    // Revision is a replay guard: it must move when geometry the mover can see
    // moves, and only then. Missing a change is the dangerous direction.
    private void BumpRevision() => Revision++;

    private void RebuildPartLane(in Aabb volume, in CharacterQueryFilter filter)
    {
        _partPieces.Clear();

        long signature = 0L;

        if (!filter.IncludeParts)
        {
            NotePartLane(signature);
            return;
        }

        _partScratch.Clear();
        _scene.GetPartBoundsInBox(in volume, _partScratch, in PartLaneFilter);

        for (int i = 0; i < _partScratch.Count; i++)
        {
            SceneNode node = _partScratch[i];
            if (node.Brush is not { } brush)
                continue;

            // World brushes are already in the compiled lane. A subtractive
            // part is inert and must not cut anything.
            if (node.BrushKind != BrushKind.Part)
                continue;
            if (brush.Operation == BrushOperation.Subtractive)
                continue;
            if (!node.CanCollide)
                continue;

            Matrix4x4 world = node.WorldMatrix;

            // Summed, so BVH traversal order does not matter. Hash the whole
            // basis: a part rotating in place moves its planes, not its origin.
            signature += node.Id.GetHashCode()
                + (long)HashCode.Combine(world.M11, world.M12, world.M13, world.M21)
                + (long)HashCode.Combine(world.M22, world.M23, world.M31, world.M32)
                + (long)HashCode.Combine(world.M33, world.M41, world.M42, world.M43);

            _partPieces.Add(new ConvexPiece(
                WorldPlanes(brush, world),
                WorldFaces(brush, world),
                brush.LocalBounds.Transform(world),
                brush,
                node,
                -1));
        }

        NotePartLane(signature);
    }

    // A hash collision can miss a change. Accepted until rollback needs better.
    private void NotePartLane(long signature)
    {
        if (_hasPartSignature && signature == _partSignature)
            return;

        bool first = !_hasPartSignature;
        _partSignature = signature;
        _hasPartSignature = true;

        // First tick is the baseline, not a change.
        if (!first)
            BumpRevision();
    }

    private static Plane[] WorldPlanes(Brush brush, Matrix4x4 transform)
    {
        IReadOnlyList<Plane> local = brush.LocalPlanes;
        var planes = new Plane[local.Count];
        for (int i = 0; i < planes.Length; i++)
            planes[i] = Plane.Transform(local[i], transform);
        return planes;
    }

    private static Polygon[] WorldFaces(Brush brush, Matrix4x4 transform)
    {
        IReadOnlyList<Polygon> local = brush.LocalFaces;
        var faces = new Polygon[local.Count];
        for (int i = 0; i < faces.Length; i++)
            faces[i] = local[i].Transformed(transform);
        return faces;
    }

    private readonly struct ConvexPiece(
        Plane[] planes, Polygon[] faces, Aabb bounds, Brush? brush, SceneNode? node, int planeIndex)
    {
        // Cover elements of the same brush, null for an uncut one. Elements
        // overlap, so a cut face lies inside the union and a contact has to be
        // checked against the siblings before it counts.
        public Plane[][]? Siblings { get; init; }

        public Plane[] Planes { get; } = planes;

        public Polygon[] Faces { get; } = faces;

        public Aabb Bounds { get; } = bounds;

        public Brush? Brush { get; } = brush;

        public SceneNode? Node { get; } = node;

        public int PlaneIndex { get; } = planeIndex;
    }
}
