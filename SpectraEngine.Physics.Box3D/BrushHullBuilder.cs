using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Physics.Box3D.Native;

namespace SpectraEngine.Physics.Box3D;

/// <summary>Why a brush could not become a collision hull.</summary>
public enum HullRefusal
{
    /// <summary>The hull was built.</summary>
    None = 0,

    /// <summary>Fewer than four distinct vertices or faces.</summary>
    Degenerate,

    /// <summary>More unique vertices than Box3D accepts.</summary>
    TooManyVertices,

    /// <summary>More faces than Box3D accepts.</summary>
    TooManyFaces,

    /// <summary>Vertices plus faces exceed the implied edge limit.</summary>
    TooManyEdges,

    /// <summary>Box3D rejected the point set for a reason of its own.</summary>
    LibraryRejected,
}

/// <summary>
/// Turns a <see cref="Brush"/> into a Box3D convex hull in the brush's local
/// frame. Refuses a brush over Box3D's limits; it never simplifies.
/// </summary>
// Box3D builds hulls from points only, so the input is the face vertices.
// Brush.Transform is ignored, as in the render path: the node's world matrix
// places a brush. CreateBox centres the solid, so a floor authored as
// y in [-1, 0] has local faces at y in [-0.5, 0.5].
public static class BrushHullBuilder
{
    /// <summary>Maximum unique vertices Box3D accepts.</summary>
    public const int MaxVertices = 128;

    /// <summary>Maximum faces Box3D accepts.</summary>
    public const int MaxFaces = 128;

    /// <summary>Maximum full edges Box3D accepts.</summary>
    public const int MaxEdges = 128;

    /// <summary>
    /// The limit that binds in practice. Box3D checks edges, and for a convex
    /// polyhedron E = V + F - 2.
    /// </summary>
    public const int MaxVerticesPlusFaces = MaxEdges + 2;

    /// <summary>
    /// Distance below which two vertices are the same point. Same value as the
    /// CSG polygon epsilon.
    /// </summary>
    public const float WeldEpsilon = 1e-4f;

    /// <summary>Collects a brush's unique face vertices, in the brush's own frame.</summary>
    public static List<B3Vec3> CollectPoints(Brush brush)
    {
        ArgumentNullException.ThrowIfNull(brush);

        var points = new List<B3Vec3>();
        IReadOnlyList<Polygon> faces = brush.LocalFaces;

        for (int f = 0; f < faces.Count; f++)
        {
            ReadOnlySpan<Vector3> verts = faces[f].VertexSpan;
            for (int v = 0; v < verts.Length; v++)
            {
                if (!ContainsWithin(points, verts[v], WeldEpsilon))
                    points.Add(B3Vec3.From(verts[v]));
            }
        }

        return points;
    }

    /// <summary>Checks vertex and face counts against Box3D's limits without calling it.</summary>
    public static HullRefusal CheckLimits(int uniqueVertexCount, int faceCount)
    {
        if (uniqueVertexCount < 4 || faceCount < 4)
            return HullRefusal.Degenerate;
        if (uniqueVertexCount > MaxVertices)
            return HullRefusal.TooManyVertices;
        if (faceCount > MaxFaces)
            return HullRefusal.TooManyFaces;
        if (uniqueVertexCount + faceCount > MaxVerticesPlusFaces)
            return HullRefusal.TooManyEdges;

        return HullRefusal.None;
    }

    /// <summary>
    /// Builds a convex hull for <paramref name="brush"/>, or says why not in
    /// <paramref name="detail"/>.
    /// </summary>
    /// <param name="hull">
    /// The native hull, or zero. The caller owns it and releases it with <see cref="Destroy"/>.
    /// </param>
    public static HullRefusal TryCreate(Brush brush, out nint hull, out string detail)
    {
        ArgumentNullException.ThrowIfNull(brush);

        hull = 0;
        List<B3Vec3> points = CollectPoints(brush);
        int faceCount = brush.LocalFaces.Count;

        HullRefusal refusal = CheckLimits(points.Count, faceCount);
        if (refusal != HullRefusal.None)
        {
            detail = Describe(refusal, points.Count, faceCount);
            return refusal;
        }

        // Pass the library's cap, not this brush's count: Box3D simplifies down
        // to maxVertexCount, and the cap means it never does.
        unsafe
        {
            Span<B3Vec3> span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(points);
            fixed (B3Vec3* p = span)
            {
                hull = B3.CreateHull(p, points.Count, MaxVertices);
            }
        }

        if (hull == 0)
        {
            detail =
                $"Box3D declined a hull for a brush with {points.Count} unique vertices and " +
                $"{faceCount} faces, having passed the pre-check. The library logs its own " +
                "reason; the brush is not collidable.";
            return HullRefusal.LibraryRejected;
        }

        detail = string.Empty;
        return HullRefusal.None;
    }

    /// <summary>Releases a hull built by <see cref="TryCreate"/>. Zero is ignored.</summary>
    public static void Destroy(nint hull)
    {
        // b3DestroyHull dereferences null.
        if (hull != 0)
            B3.DestroyHull(hull);
    }

    private static string Describe(HullRefusal refusal, int vertices, int faces) => refusal switch
    {
        HullRefusal.Degenerate =>
            $"Brush has {vertices} unique vertices and {faces} faces — too few to bound a volume " +
            "(a convex solid needs at least four of each).",
        HullRefusal.TooManyVertices =>
            $"Brush has {vertices} unique vertices; Box3D accepts at most {MaxVertices}. " +
            "Split it into simpler convex pieces — it will not be simplified for you, because a " +
            "simplified collision hull is a player clipping through a wall that renders correctly.",
        HullRefusal.TooManyFaces =>
            $"Brush has {faces} faces; Box3D accepts at most {MaxFaces}. Split it into simpler " +
            "convex pieces.",
        HullRefusal.TooManyEdges =>
            $"Brush has {vertices} unique vertices plus {faces} faces = {vertices + faces}, over the " +
            $"limit of {MaxVerticesPlusFaces} implied by Box3D's {MaxEdges}-edge cap (Euler: " +
            "E = V + F - 2). This binds long before the vertex and face caps do. Split the brush " +
            "into simpler convex pieces.",
        _ => string.Empty,
    };

    private static bool ContainsWithin(List<B3Vec3> points, Vector3 candidate, float epsilon)
    {
        float epsilonSquared = epsilon * epsilon;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 delta = points[i].ToVector3() - candidate;
            if (Vector3.Dot(delta, delta) <= epsilonSquared)
                return true;
        }

        return false;
    }
}
