using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// A solid-leaf BSP tree built from convex <see cref="Brush"/> solids, for
/// point-containment and ray queries against the static world.
/// </summary>
public sealed class BspTree
{
    // Splitter scoring only, never queries.
    private const float ScoringPlaneEpsilon = 1e-3f;

    // At or below this size a node takes its first polygon's plane unscored.
    private const int ScoreThreshold = 25;

    private const int MaxCandidates = 12;

    private const int MaxScoreSamples = 256;

    // One cut polygon costs this many units of front/back imbalance.
    private const int SplitPenalty = 8;

    // Children build in parallel only while both sides hold this many polygons.
    private const int ParallelThreshold = 256;

    // Splitter scoring only. BuildNode's coplanar consumption needs
    // bit-identical planes.
    private const float CoplanarNormalAgreement = 0.999f;

    private BspTree(BspNode root) => Root = root;

    public BspNode Root { get; }

    /// <summary>
    /// Builds a BSP tree from a set of brushes, running CSG first.
    /// </summary>
    public static BspTree Build(IReadOnlyList<Brush> brushes) =>
        BuildFromSurfaces(Csg.Carve(brushes));

    /// <summary>
    /// Builds a BSP tree from surface polygons, typically the output of
    /// <see cref="Csg.Carve"/>. The same list always gives the same tree.
    /// </summary>
    public static BspTree BuildFromSurfaces(IReadOnlyList<Polygon> surfaces)
    {
        var polygons = new List<Polygon>(surfaces);
        return new BspTree(BuildNode(polygons, solidIfEmpty: false));
    }

    // An empty list is a leaf: solid down a back edge, empty down a front edge.
    // Tests pin this: front passes solidIfEmpty false, back passes true, and
    // coplanar polygons go front when their normal agrees with the splitter.
    private static BspNode BuildNode(List<Polygon> polygons, bool solidIfEmpty)
    {
        if (polygons.Count == 0)
            return BspNode.Leaf(solidIfEmpty);

        int splitterIndex = ChooseSplitterIndex(polygons);
        Plane splitter = polygons[splitterIndex].Surface;

        int hint = polygons.Count / 2 + 4;
        var front = new List<Polygon>(hint);
        var back = new List<Polygon>(hint);

        for (int i = 0; i < polygons.Count; i++)
        {
            // Never classify the splitter's polygon against its own plane.
            // Dropping it is what guarantees termination, even when snapping
            // moved it off its stored plane.
            if (i == splitterIndex)
                continue;

            Polygon poly = polygons[i];
            switch (poly.Classify(splitter))
            {
                case PolygonClassification.Front:
                    front.Add(poly);
                    break;
                case PolygonClassification.Back:
                    back.Add(poly);
                    break;
                case PolygonClassification.Coplanar:
                    // A face on the bit-identical plane is dropped: routed
                    // front it would split again on the same plane and its
                    // back child could never be reached. Brush worlds are
                    // full of these, and one node each made the build quadratic.
                    //
                    // Must be bit-equal, not within epsilon. A near-coincident
                    // plane bounds a slightly different half-space, and
                    // dropping it flips queries in the sliver between the two.
                    Plane surface = poly.Surface;
                    if (surface.Normal.Equals(splitter.Normal) && surface.D == splitter.D)
                        continue;
                    if (Vector3.Dot(surface.Normal, splitter.Normal) >= 0f)
                        front.Add(poly);
                    else
                        back.Add(poly);
                    break;
                case PolygonClassification.Spanning:
                    poly.Split(splitter, out Polygon? f, out Polygon? b);
                    if (f is not null) front.Add(f);
                    if (b is not null) back.Add(b);
                    break;
            }
        }

        // Each subtree depends only on its own list, so scheduling cannot
        // change the result. This thread builds back, then joins front; TPL
        // inlines an unstarted task, so nested spawns don't starve the pool.
        if (front.Count >= ParallelThreshold && back.Count >= ParallelThreshold)
        {
            List<Polygon> frontList = front;
            Task<BspNode> frontTask = Task.Run(() => BuildNode(frontList, solidIfEmpty: false));
            BspNode backChild = BuildNode(back, solidIfEmpty: true);
            return BspNode.Split(splitter, frontTask.GetAwaiter().GetResult(), backChild);
        }

        BspNode frontChild = BuildNode(front, solidIfEmpty: false);
        BspNode backChildSeq = BuildNode(back, solidIfEmpty: true);
        return BspNode.Split(splitter, frontChild, backChildSeq);
    }

    // Scores a strided set of candidate planes by splits * SplitPenalty +
    // |front - back|, using an AABB-vs-plane test. It only ranks, so it can be
    // loose. Ties keep the earliest candidate, which keeps the build deterministic.
    private static int ChooseSplitterIndex(List<Polygon> polygons)
    {
        int count = polygons.Count;
        if (count <= ScoreThreshold)
            return 0;

        int candidateStep = Math.Max(1, count / MaxCandidates);
        int sampleStep = Math.Max(1, count / MaxScoreSamples);

        // Planes already scored. Sign matters: a flipped plane is a different splitter.
        Span<Plane> seen = stackalloc Plane[MaxCandidates];
        int seenCount = 0;

        int bestIndex = 0;
        long bestScore = long.MaxValue;

        for (int c = 0, idx = 0; c < MaxCandidates && idx < count; c++, idx += candidateStep)
        {
            Plane candidate = polygons[idx].Surface;

            bool duplicate = false;
            for (int s = 0; s < seenCount; s++)
            {
                if (Vector3.Dot(seen[s].Normal, candidate.Normal) >= CoplanarNormalAgreement &&
                    MathF.Abs(seen[s].D - candidate.D) <= ScoringPlaneEpsilon)
                {
                    duplicate = true;
                    break;
                }
            }
            if (duplicate)
                continue;
            seen[seenCount++] = candidate;

            Vector3 n = candidate.Normal;
            Vector3 absN = Vector3.Abs(n);
            int frontCount = 0, backCount = 0, splitCount = 0;

            for (int s = 0; s < count; s += sampleStep)
            {
                // Centre distance vs the box's projected radius. Slanted
                // coplanar faces count as splits, which only biases the score.
                Aabb bounds = polygons[s].Bounds;
                float centerDist = Plane.DotCoordinate(candidate, bounds.Center);
                float radius = Vector3.Dot((bounds.Max - bounds.Min) * 0.5f, absN);

                if (centerDist - radius > Polygon.Epsilon)
                {
                    frontCount++;
                }
                else if (centerDist + radius < -Polygon.Epsilon)
                {
                    backCount++;
                }
                else if (radius <= Polygon.Epsilon)
                {
                    // Coplanar. Same-facing faces count as consumed, which
                    // favours splitters that retire many of them.
                    float agreement = Vector3.Dot(polygons[s].Surface.Normal, n);
                    if (agreement >= CoplanarNormalAgreement)
                        continue;
                    if (agreement >= 0f)
                        frontCount++;
                    else
                        backCount++;
                }
                else
                {
                    splitCount++;
                }
            }

            long score = (long)splitCount * SplitPenalty + Math.Abs(frontCount - backCount);
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = idx;
            }
        }

        return bestIndex;
    }

    /// <summary>True when the point lies inside solid space.</summary>
    public bool ContainsPoint(Vector3 point)
    {
        BspNode node = Root;
        while (!node.IsLeaf)
        {
            float d = Plane.DotCoordinate(node.Plane, point);
            node = d >= 0f ? node.Front! : node.Back!;
        }
        return node.IsSolid;
    }

    /// <summary>
    /// Casts a ray against solid space and reports the first surface entered.
    /// A ray that starts in solid hits at distance 0.
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out BspRaycastHit hit)
    {
        hit = default;
        if (direction == Vector3.Zero || maxDistance <= 0f)
            return false;

        direction = Vector3.Normalize(direction);

        if (ContainsPoint(origin))
        {
            hit = new BspRaycastHit(origin, -direction, 0f);
            return true;
        }

        Vector3 end = origin + direction * maxDistance;
        if (TraceSegment(Root, origin, end, direction, hasEntry: false, default, out hit))
        {
            hit = hit with { Distance = Vector3.Distance(origin, hit.Point) };
            return true;
        }
        return false;
    }

    // Finds where a..b first crosses from empty into solid. Entry is decided
    // by the leaf each sub-segment starts in, and the last plane crossed is
    // the entry surface. Don't probe a point past the crossing instead: that
    // invents hits across thin gaps, tunnels through thin solids, and samples
    // outside [a, b], which the per-cell routing in CsgWorld relies on.
    private static bool TraceSegment(
        BspNode node, Vector3 a, Vector3 b, Vector3 direction,
        bool hasEntry, Vector3 entryNormal, out BspRaycastHit hit)
    {
        if (node.IsLeaf)
        {
            if (!node.IsSolid)
            {
                hit = default;
                return false;
            }

            // a is the crossing point on the entry plane. No entry plane means
            // the ray started in solid, which Raycast already handled.
            hit = new BspRaycastHit(a, hasEntry ? entryNormal : -direction, 0f);
            return true;
        }

        float da = Plane.DotCoordinate(node.Plane, a);
        float db = Plane.DotCoordinate(node.Plane, b);

        if (da >= 0f && db >= 0f)
            return TraceSegment(node.Front!, a, b, direction, hasEntry, entryNormal, out hit);
        if (da < 0f && db < 0f)
            return TraceSegment(node.Back!, a, b, direction, hasEntry, entryNormal, out hit);

        float t = da / (da - db);
        Vector3 mid = Vector3.Lerp(a, b, t);

        BspNode near = da >= 0f ? node.Front! : node.Back!;
        BspNode far = da >= 0f ? node.Back! : node.Front!;

        if (TraceSegment(near, a, mid, direction, hasEntry, entryNormal, out hit))
            return true;

        // The crossed plane, facing the incoming side, is the candidate entry surface.
        Vector3 crossingNormal = da >= 0f ? node.Plane.Normal : -node.Plane.Normal;
        return TraceSegment(far, mid, b, direction, hasEntry: true, crossingNormal, out hit);
    }
}
