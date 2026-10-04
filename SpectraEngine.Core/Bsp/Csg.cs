using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// CSG over convex brushes. Carving removes the parts of each brush's faces
/// buried inside other brushes, leaving the visible skin of the solid.
/// </summary>
// Each brush is carved in its own local frame, so precision does not depend
// on distance from the world origin. Fragments go to world space at the end.
public static class Csg
{
    private const float NormalEpsilon = 1e-4f;
    private const float OffsetEpsilon = 1e-3f;

    // Thread-static: tests run in parallel, and a process-wide counter would
    // be moved by a compile on any other thread.
    [ThreadStatic]
    private static long _carveInvocations;

    /// <summary>
    /// How many times a carve has been entered on the calling thread.
    /// Diagnostics only: take a delta, on one thread.
    /// </summary>
    public static long CarveInvocationsOnThisThread => _carveInvocations;

    /// <summary>
    /// Carves brushes placed by their own <see cref="Brush.Transform"/> and returns
    /// the visible surface polygons in world space. For standalone and test use.
    /// </summary>
    public static Polygon[] Carve(IReadOnlyList<Brush> brushes)
        => Carve(ToPlacements(brushes));

    /// <summary>
    /// Carves placed brushes and returns the visible surface polygons in world space.
    /// Reads only the placements, so a snapshot can be carved on a background thread.
    /// </summary>
    public static Polygon[] Carve(IReadOnlyList<BrushPlacement> placements)
        => Concatenate(CarvePerBrush(placements));

    /// <summary>
    /// Carves with a cache from the previous compile and produces the cache for the
    /// next one. Output is bit-identical to the cache-free overload.
    /// </summary>
    public static Polygon[] Carve(
        IReadOnlyList<BrushPlacement> placements,
        CsgCompileCache? previousCache,
        out CsgCompileCache nextCache,
        out CsgCacheStats stats)
        => Concatenate(CarvePerBrush(placements, previousCache, out nextCache, out stats));

    // Result is index-aligned with the placements.
    internal static Polygon[][] CarvePerBrush(IReadOnlyList<BrushPlacement> placements)
        => CarveCore(placements, previousCache: null, cacheEntries: null, out _, out _);

    internal static Polygon[][] CarvePerBrush(IReadOnlyList<BrushPlacement> placements, out int[][] neighbors)
        => CarveCore(placements, previousCache: null, cacheEntries: null, out _, out neighbors);

    internal static Polygon[][] CarvePerBrush(
        IReadOnlyList<BrushPlacement> placements,
        CsgCompileCache? previousCache,
        out CsgCompileCache nextCache,
        out CsgCacheStats stats)
        => CarvePerBrush(placements, previousCache, out nextCache, out stats, out _);

    // neighbors: each brush's broadphase list in carve order. The incremental
    // compile patches it rather than re-running the broadphase.
    internal static Polygon[][] CarvePerBrush(
        IReadOnlyList<BrushPlacement> placements,
        CsgCompileCache? previousCache,
        out CsgCompileCache nextCache,
        out CsgCacheStats stats,
        out int[][] neighbors)
    {
        var entries = new CsgCompileCache.Entry?[placements.Count];
        Polygon[][] perBrush = CarveCore(placements, previousCache, entries, out int hits, out neighbors);
        nextCache = CsgCompileCache.FromEntries(placements, entries);
        stats = new CsgCacheStats(hits, placements.Count - hits);
        return perBrush;
    }

    // A non-null cacheEntries is filled with one entry per placement.
    private static Polygon[][] CarveCore(
        IReadOnlyList<BrushPlacement> placements,
        CsgCompileCache? previousCache,
        CsgCompileCache.Entry?[]? cacheEntries,
        out int hitCount,
        out int[][] neighborsOut)
    {
        // Counted before the early-out: carving nothing is still a carve.
        _carveInvocations++;

        hitCount = 0;
        int n = placements.Count;
        if (n == 0)
        {
            neighborsOut = [];
            return [];
        }

        var worldBounds = new Aabb[n];
        for (int i = 0; i < n; i++)
            worldBounds[i] = placements[i].WorldBounds;

        int[][] neighbors = BrushBroadphase.FindOverlaps(worldBounds);
        neighborsOut = neighbors;

        // Clipping is in authored placement order. The broadphase only picks
        // the members; cache validation compares the ordered sequence.

        bool[]? hitFlags = previousCache is not null ? new bool[n] : null;
        var perBrush = new Polygon[n][];

        // One CarveScratch per worker, reused across its brushes.
        Parallel.For(0, n,
            static () => CarveScratch.Rent(),
            (b, _, scratch) =>
            {
                int[] neighborIndices = neighbors[b];

                if (previousCache is not null &&
                    previousCache.TryGetValid(placements, b, neighborIndices, out CsgCompileCache.Entry? cachedEntry))
                {
                    perBrush[b] = cachedEntry.Surfaces;
                    if (cacheEntries is not null)
                        cacheEntries[b] = cachedEntry;
                    hitFlags![b] = true;
                    return scratch;
                }

                Polygon[] worldSurfaces = CarveSingle(placements, b, neighborIndices, scratch);
                perBrush[b] = worldSurfaces;
                if (cacheEntries is not null)
                    cacheEntries[b] = CsgCompileCache.Entry.Create(placements, b, neighborIndices, worldSurfaces);
                return scratch;
            },
            static scratch => scratch.Dispose());

        if (hitFlags is not null)
        {
            foreach (bool hit in hitFlags)
            {
                if (hit)
                    hitCount++;
            }
        }

        return perBrush;
    }

    // Carves one brush against the given carvers. Shared with the incremental
    // compile so both produce identical fragments. neighborIndices must be in
    // carve order.
    internal static Polygon[] CarveSingle(
        IReadOnlyList<BrushPlacement> placements, int b, int[] neighborIndices, CarveScratch scratch)
    {
        _carveInvocations++;

        BrushPlacement placement = placements[b];
        if (placement.Brush is null) return []; // vacant stable slot

        // Size the plane buffer up front: CarverInFrame slices must not move
        // while it is being filled.
        int planeTotal = 0;
        for (int k = 0; k < neighborIndices.Length; k++)
            planeTotal += placements[neighborIndices[k]].Brush.LocalPlanes.Count;
        scratch.EnsureCarverCapacity(neighborIndices.Length, planeTotal);

        CarverInFrame[] carvers = scratch.Carvers;
        Plane[] planeBuffer = scratch.PlaneBuffer;
        int planeStart = 0;
        for (int k = 0; k < neighborIndices.Length; k++)
        {
            int o = neighborIndices[k];
            bool wins = placements is PlacementSlotView ordered ? ordered.Compare(o, b) < 0 : o < b;
            carvers[k] = CarverInFrame.Build(placements[o], placement, wins, planeBuffer, planeStart);
            planeStart += carvers[k].PlaneCount;
        }

        List<Polygon> localSurfaces = scratch.LocalSurfaces;
        List<Polygon> current = scratch.Current;
        List<Polygon> next = scratch.Next;
        localSurfaces.Clear();

        // A subtractive brush emits no skin of its own, so its carved array is
        // always empty. Its cavity walls are seeded into the brushes it cuts.
        bool additive = placement.Brush.Operation == BrushOperation.Additive;
        if (additive)
        {
            foreach (Polygon face in placement.Brush.LocalFaces)
            {
                current.Clear();
                current.Add(face);

                for (int k = 0; k < neighborIndices.Length; k++)
                {
                    next.Clear();
                    foreach (Polygon fragment in current)
                        CarveFragment(fragment, carvers, neighborIndices.Length, planeBuffer, next, seedIsWall: false, seedOrigin: -1, carverIndex: k);

                    (current, next) = (next, current);
                    if (current.Count == 0)
                        break;
                }

                localSurfaces.AddRange(current);
            }

            // Cavity walls belong to the cut brush's slot. A wall lies inside
            // this brush's solid, so ownership, weld candidates and render
            // bounds need no changes.
            //
            // Order is fixed (after the faces, carver-list order, then the
            // negative's LocalFaces order) because the BSP and mesh output
            // depend on it.
            for (int k = 0; k < neighborIndices.Length; k++)
            {
                if (!carvers[k].Subtractive || !carvers[k].Bounds.Intersects(placement.Brush.LocalBounds))
                    continue;

                Brush negative = placements[neighborIndices[k]].Brush;
                foreach (Polygon negativeFace in negative.LocalFaces)
                {
                    // Transformed carries the FaceSurface, so the wall wears
                    // the negative's material. Flip before clipping: the clip
                    // loops assume solid is behind the surface.
                    Polygon? wall = negativeFace.Transformed(carvers[k].Combined).Flipped();

                    // Clip to inside this brush. Split puts a coplanar polygon
                    // on the front and we keep the back, so a wall lying on one
                    // of this brush's own planes is dropped here.
                    IReadOnlyList<Plane> ownPlanes = placement.Brush.LocalPlanes;
                    for (int i = 0; i < ownPlanes.Count && wall is not null; i++)
                    {
                        wall.Split(ownPlanes[i], out _, out Polygon? inside);
                        wall = inside;
                    }

                    if (wall is null)
                        continue;

                    current.Clear();
                    current.Add(wall);

                    for (int c = 0; c < neighborIndices.Length; c++)
                    {
                        next.Clear();
                        foreach (Polygon fragment in current)
                            CarveFragment(fragment, carvers, neighborIndices.Length, planeBuffer, next, seedIsWall: true, seedOrigin: k, carverIndex: c);

                        (current, next) = (next, current);
                        if (current.Count == 0)
                            break;
                    }

                    localSurfaces.AddRange(current);
                }
            }
        }

        // Fresh array: this becomes part of the result, never scratch.
        var worldSurfaces = new Polygon[localSurfaces.Count];
        for (int i = 0; i < worldSurfaces.Length; i++)
            worldSurfaces[i] = localSurfaces[i].Transformed(placement.Transform);
        return worldSurfaces;
    }

    // Placement-index order, whichever brushes hit the cache.
    internal static Polygon[] Concatenate(Polygon[][] perBrush)
    {
        int total = 0;
        foreach (Polygon[] list in perBrush)
            total += list.Length;

        var all = new Polygon[total];
        int offset = 0;
        foreach (Polygon[] list in perBrush)
        {
            Array.Copy(list, 0, all, offset, list.Length);
            offset += list.Length;
        }
        return all;
    }

    internal static Polygon[] Concatenate(PagedArray<Polygon[]> perBrush, int totalSurfaces)
    {
        var all = new Polygon[totalSurfaces];
        int offset = 0;
        for (int i = 0; i < perBrush.Count; i++)
        {
            Polygon[] list = perBrush[i];
            Array.Copy(list, 0, all, offset, list.Length);
            offset += list.Length;
        }
        return all;
    }

    // Worker-local scratch, owned by one worker at a time. Nothing stored here
    // may escape into a result. Entries past the counts the current brush
    // wrote are stale.
    internal sealed class CarveScratch : IDisposable
    {
        [ThreadStatic] private static CarveScratch? _available;
        private bool _leased;
        internal static CarveScratch Rent()
        {
            var scratch = _available ?? new CarveScratch();
            _available = null;
            scratch._leased = true;
            return scratch;
        }

        public void Dispose()
        {
            if (!_leased) return;
            _leased = false;
            Clear(LocalSurfaces);
            Clear(Current);
            Clear(Next);
            if (Carvers.Length > 4096) Carvers = [];
            else Array.Clear(Carvers);
            if (PlaneBuffer.Length > 4096) PlaneBuffer = [];
            else Array.Clear(PlaneBuffer);
            _available ??= this;
        }

        private static void Clear(List<Polygon> list)
        {
            list.Clear();
            if (list.Capacity > 4096) list.Capacity = 0;
        }

        public readonly List<Polygon> LocalSurfaces = [];
        public readonly List<Polygon> Current = [];
        public readonly List<Polygon> Next = [];

        // Both valid only for the brush currently being carved. PlaneBuffer
        // holds every carver's planes back to back.
        public CarverInFrame[] Carvers = [];

        public Plane[] PlaneBuffer = [];

        public void EnsureCarverCapacity(int carverCount, int planeCount)
        {
            if (Carvers.Length < carverCount)
                Carvers = new CarverInFrame[Math.Max(carverCount, Carvers.Length * 2)];
            if (PlaneBuffer.Length < planeCount)
                PlaneBuffer = new Plane[Math.Max(planeCount, PlaneBuffer.Length * 2)];
        }
    }

    internal static BrushPlacement[] ToPlacements(IReadOnlyList<Brush> brushes)
    {
        var placements = new BrushPlacement[brushes.Count];
        for (int i = 0; i < brushes.Count; i++)
            placements[i] = new BrushPlacement(brushes[i], brushes[i].Transform);
        return placements;
    }

    // Appends the parts of fragment that lie outside the carver.
    // seedIsWall: the fragment comes from a cavity wall, not a face.
    // seedOrigin: carver-list position of the negative that made the wall, -1 for a face.
    //
    // Seed / carver:
    //   face / additive      coplanar rule by precedence, else split.
    //   face / subtractive   drop the footprint when Split-coplanar and
    //                        same-facing, else split.
    //   wall / its origin    skip the carver.
    //   wall / additive      Wins ? as face/additive : skip the carver.
    //   wall / other subtr.  as face/subtractive, plus a tie-break on list
    //                        position when opposite-facing.
    private static void CarveFragment(
        Polygon fragment, CarverInFrame[] carvers, int carverCount, Plane[] planes, List<Polygon> output,
        bool seedIsWall, int seedOrigin, int carverIndex)
    {
        ref readonly CarverInFrame carver = ref carvers[carverIndex];

        if (!fragment.Bounds.Intersects(carver.Bounds))
        {
            output.Add(fragment);
            return;
        }

        if (seedIsWall)
        {
            // Must skip the wall's own negative: its plane is coincident and
            // opposite, and the tie-break below would delete the wall.
            //
            // Also skip an additive carver that does not win. The wall is in
            // the open interior of the brush it was seeded into, so that
            // carver can only touch its boundary.
            if (carverIndex == seedOrigin || (!carver.Subtractive && !carver.Wins))
            {
                output.Add(fragment);
                return;
            }
        }

        Polygon? remaining = fragment;
        bool onCarverPlane = false;
        int end = carver.PlaneStart + carver.PlaneCount;
        for (int p = carver.PlaneStart; p < end; p++)
        {
            if (remaining is null)
                return;

            Plane plane = planes[p];

            if (carver.Subtractive)
            {
                // Not CoplanarOrientation: its offset tolerance is 1e-3 against
                // Split's 1e-4. The footprint must be dropped at the same
                // tolerance that decides whether the cavity wall survives, or a
                // thin ring opens around the cavity mouth.
                if (remaining.Classify(plane) == PolygonClassification.Coplanar)
                {
                    bool sameFacing = Vector3.Dot(remaining.Surface.Normal, plane.Normal) > 0f;

                    // Same-facing: a flush through-cut, drop the footprint.
                    // Opposite-facing: the negative only rests on this surface
                    // and removes nothing, so the fragment stays. Two coincident
                    // negatives make two identical walls; list position picks
                    // the one that emits.
                    if (sameFacing)
                        continue;

                    if (seedIsWall && seedOrigin >= carverIndex)
                        continue;

                    output.Add(remaining);
                    return;
                }

                remaining.Split(plane, out Polygon? outside, out Polygon? behind);
                if (outside is not null)
                    output.Add(outside);
                remaining = behind;
                continue;
            }

            int orientation = CoplanarOrientation(remaining.Surface, plane);
            if (orientation != 0)
            {
                // Opposite-facing: interior interface, both brushes drop the
                // footprint. Same-facing: duplicate surface, precedence decides.
                bool removeFootprint = orientation < 0 || carver.Wins;
                if (!removeFootprint)
                {
                    output.Add(remaining);
                    return;
                }

                // The other planes carve out the footprint.
                onCarverPlane = true;
                continue;
            }

            remaining.Split(plane, out Polygon? front, out Polygon? back);
            if (front is not null)
                output.Add(front);
            remaining = back;
        }

        // What is left is buried in the carver and dropped, with one exception.
        // When a negative cuts flush through the carver on the coincident
        // plane, the carver's face is gone over the cut and the cavity wall
        // was dropped at seeding, so nothing bounds the cavity. Re-emit that part.
        //
        // Only after a coincident-plane skip: elsewhere the seeded cavity wall
        // exists and this would duplicate it.
        if (onCarverPlane && remaining is not null && !carver.Subtractive)
            EmitHollowedRemainder(remaining, 0, carvers, carverCount, carverIndex, planes, output);
    }

    // Emits the parts of a buried fragment that lie inside one of the negatives
    // at [first, carverCount). The rest stays buried.
    private static void EmitHollowedRemainder(
        Polygon buried, int first, CarverInFrame[] carvers, int carverCount, int carverIndex,
        Plane[] planes, List<Polygon> output)
    {
        for (int s = first; s < carverCount; s++)
        {
            ref readonly CarverInFrame negative = ref carvers[s];
            if (s == carverIndex || !negative.Subtractive)
                continue;
            if (!negative.Bounds.Intersects(carvers[carverIndex].Bounds) || !negative.Bounds.Intersects(buried.Bounds))
                continue;

            // The patch must rest on one of the negative's faces, coplanar and
            // facing into the cavity. Classify, not CoplanarOrientation: same
            // tolerance as the seed clip.
            int rest = -1;
            int end = negative.PlaneStart + negative.PlaneCount;
            for (int p = negative.PlaneStart; p < end; p++)
            {
                if (Vector3.Dot(buried.Surface.Normal, planes[p].Normal) < 0f
                    && buried.Classify(planes[p]) == PolygonClassification.Coplanar)
                {
                    rest = p;
                    break;
                }
            }

            // Only when that face lies on one of the carver's own planes, which
            // is when the seed clip dropped the cavity wall. Otherwise the wall
            // exists and this would duplicate it.
            if (rest < 0 || !LiesOnOwnPlane(in carvers[carverIndex], planes, planes[rest]))
                continue;

            Polygon? inside = buried;
            for (int p = negative.PlaneStart; p < end && inside is not null; p++)
            {
                if (p == rest)
                    continue;

                inside.Split(planes[p], out Polygon? outside, out Polygon? behind);
                if (outside is not null)
                    EmitHollowedRemainder(outside, s + 1, carvers, carverCount, carverIndex, planes, output);
                inside = behind;
            }

            if (inside is not null)
                output.Add(inside);
            return;
        }
    }

    // Uses Polygon.Epsilon to agree with the cavity-wall seed clip.
    private static bool LiesOnOwnPlane(in CarverInFrame carver, Plane[] planes, in Plane plane)
    {
        int end = carver.PlaneStart + carver.PlaneCount;
        for (int p = carver.PlaneStart; p < end; p++)
        {
            if (Vector3.Dot(planes[p].Normal, plane.Normal) > 1f - NormalEpsilon
                && MathF.Abs(planes[p].D - plane.D) < Polygon.Epsilon)
                return true;
        }

        return false;
    }

    // 0 = not coplanar, +1 = same plane and facing, -1 = same plane, opposed.
    private static int CoplanarOrientation(Plane a, Plane b)
    {
        float dot = Vector3.Dot(a.Normal, b.Normal);
        if (dot > 1f - NormalEpsilon)
            return MathF.Abs(a.D - b.D) < OffsetEpsilon ? 1 : 0;
        if (dot < -1f + NormalEpsilon)
            return MathF.Abs(a.D + b.D) < OffsetEpsilon ? -1 : 0;
        return 0;
    }

    // A carver in the carved brush's local frame. Its planes sit in the worker's
    // plane buffer at [PlaneStart, PlaneStart + PlaneCount), so this is valid
    // only while that brush is being carved.
    internal readonly struct CarverInFrame
    {
        public int PlaneStart { get; }
        public int PlaneCount { get; }
        public Aabb Bounds { get; }
        public bool Wins { get; }

        public bool Subtractive { get; }

        // Carver-local to carved-local.
        public Matrix4x4 Combined { get; }

        private CarverInFrame(
            int planeStart, int planeCount, Aabb bounds, bool wins,
            bool subtractive, Matrix4x4 combined)
        {
            PlaneStart = planeStart;
            PlaneCount = planeCount;
            Bounds = bounds;
            Wins = wins;
            Subtractive = subtractive;
            Combined = combined;
        }

        // Writes the carver's planes into planeBuffer at planeStart.
        public static CarverInFrame Build(in BrushPlacement carver, in BrushPlacement carved, bool carverWins, Plane[] planeBuffer, int planeStart)
        {
            // v_carved = v_carver * carver.Transform * Invert(carved.Transform)
            // Throw on a singular transform; any fallback gives wrong geometry.
            if (!Matrix4x4.Invert(carved.Transform, out Matrix4x4 carvedInverse))
                throw new InvalidOperationException(
                    "Carved brush transform is singular and cannot be inverted. " +
                    "Brush transforms must be rigid (rotation and translation only).");
            Matrix4x4 combined = carver.Transform * carvedInverse;

            Brush carverBrush = carver.Brush;
            IReadOnlyList<Plane> localPlanes = carverBrush.LocalPlanes;
            for (int i = 0; i < localPlanes.Count; i++)
                planeBuffer[planeStart + i] = Plane.Transform(localPlanes[i], combined);

            Aabb bounds = carverBrush.LocalBounds.Transform(combined);
            return new CarverInFrame(
                planeStart, localPlanes.Count, bounds, carverWins,
                carverBrush.Operation == BrushOperation.Subtractive, combined);
        }
    }
}
