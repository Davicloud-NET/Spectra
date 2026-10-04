using System;
using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Physics.Character;

/// <summary>
/// Exact distance and sweep between a capsule and a convex piece.
/// </summary>
// Plane distances only reject. Max plane distance measures the sharp-cornered
// offset shape, which sticks out r*sqrt(3) at a box corner where the capsule's
// true distance is r, so it would catch on every brush edge. Contact distance
// comes from the face polygons.
public static class CapsuleGeometry
{
    private const float ParallelEpsilon = 1e-6f;

    private const float InsideEpsilon = 1e-5f;

    /// <summary>
    /// The largest signed distance from the capsule to any of the piece's
    /// planes. Positive proves separation; non-positive proves nothing.
    /// </summary>
    public static float MaxPlaneSeparation(
        in CharacterCapsule capsule, ReadOnlySpan<Plane> planes, out int planeIndex)
    {
        float best = float.NegativeInfinity;
        planeIndex = -1;

        for (int i = 0; i < planes.Length; i++)
        {
            float d1 = Plane.DotCoordinate(planes[i], capsule.Center1);
            float d2 = Plane.DotCoordinate(planes[i], capsule.Center2);
            float separation = MathF.Min(d1, d2) - capsule.Radius;

            if (separation > best)
            {
                best = separation;
                planeIndex = i;
            }
        }

        return best;
    }

    /// <summary>Whether a point lies inside every one of the piece's half-spaces.</summary>
    public static bool ContainsPoint(Vector3 point, ReadOnlySpan<Plane> planes, float slack = 0f)
    {
        for (int i = 0; i < planes.Length; i++)
        {
            if (Plane.DotCoordinate(planes[i], point) > slack)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The exact distance from the capsule's surface to the piece, with the
    /// outward contact normal and the closest point on the piece. Negative
    /// means penetrating.
    /// </summary>
    public static float Distance(
        in CharacterCapsule capsule,
        ReadOnlySpan<Plane> planes,
        ReadOnlySpan<Polygon> faces,
        out Vector3 normal,
        out Vector3 pointOnPiece)
    {
        normal = Vector3.UnitY;
        pointOnPiece = capsule.Center1;

        // The plane value underestimates near corners, so only trust it when
        // well clear.
        float planeSeparation = MaxPlaneSeparation(in capsule, planes, out int planeIndex);
        if (planeSeparation > capsule.Radius)
        {
            normal = planes[planeIndex].Normal;
            pointOnPiece = ClosestPointOnSegment(
                capsule.Center1, capsule.Center2, capsule.Center1) - normal * planeSeparation;
            return planeSeparation;
        }

        // Axis passes through the solid. Needs the exact segment test: a
        // capsule straddling a face has its nearest axis point outside, so the
        // face path would report a small gap with half the body buried.
        // Depth is the least-violated plane, the shortest way out.
        if (planeSeparation < 0f &&
            SegmentIntersectsConvex(capsule.Center1, capsule.Center2, planes))
        {
            normal = planes[planeIndex].Normal;
            pointOnPiece = ClosestPointOnSegment(capsule.Center1, capsule.Center2, capsule.Center1)
                - normal * planeSeparation;
            return planeSeparation;
        }

        // Nearest approach of the axis to the surface, minus the radius. Near
        // a corner the normal is the corner direction, not a face normal.
        float bestSquared = float.MaxValue;
        Vector3 bestOnFace = default;
        Vector3 bestOnAxis = default;

        for (int f = 0; f < faces.Length; f++)
        {
            ClosestBetweenSegmentAndPolygon(
                capsule.Center1, capsule.Center2, faces[f],
                out Vector3 onAxis, out Vector3 onFace);

            float squared = Vector3.DistanceSquared(onAxis, onFace);
            if (squared < bestSquared)
            {
                bestSquared = squared;
                bestOnFace = onFace;
                bestOnAxis = onAxis;
            }
        }

        if (bestSquared == float.MaxValue)
        {
            // No faces: fall back to the plane answer.
            normal = planeIndex >= 0 ? planes[planeIndex].Normal : Vector3.UnitY;
            return planeSeparation;
        }

        float axisDistance = MathF.Sqrt(bestSquared);
        pointOnPiece = bestOnFace;

        Vector3 away = bestOnAxis - bestOnFace;
        if (axisDistance > ParallelEpsilon)
        {
            normal = away / axisDistance;
        }
        else if (planeIndex >= 0)
        {
            // Axis on the surface: no direction to derive.
            normal = planes[planeIndex].Normal;
        }

        // Face distance is unsigned. Axis inside the solid means negative.
        if (ContainsPoint(bestOnAxis, planes, InsideEpsilon))
            return -(axisDistance + capsule.Radius);

        return axisDistance - capsule.Radius;
    }

    /// <summary>
    /// Conservative advancement: the fraction of <paramref name="translation"/>
    /// the capsule may travel before its surface is <paramref name="skinWidth"/>
    /// from the piece. Returns 1 when unobstructed and 0 with a valid plane
    /// when the capsule already overlaps.
    /// </summary>
    public static float Sweep(
        in CharacterCapsule capsule,
        Vector3 translation,
        ReadOnlySpan<Plane> planes,
        ReadOnlySpan<Polygon> faces,
        float skinWidth,
        int maxIterations,
        float tolerance,
        out Vector3 normal,
        out Vector3 pointOnPiece)
    {
        normal = Vector3.UnitY;
        pointOnPiece = capsule.Center1;

        float length = translation.Length();
        if (length <= ParallelEpsilon)
        {
            float d0 = Distance(in capsule, planes, faces, out normal, out pointOnPiece);
            return d0 <= skinWidth ? 0f : 1f;
        }

        Vector3 direction = translation / length;
        float travelled = 0f;

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            CharacterCapsule probe = capsule.Translated(direction * travelled);
            float distance = Distance(in probe, planes, faces, out normal, out pointOnPiece);

            // Check closing speed before contact. A capsule resting on a
            // surface is already at contact distance, and a contact-first test
            // would block tangential or separating motion at fraction 0, which
            // stops a step probe from rising past the riser it touches.
            float closing = -Vector3.Dot(direction, normal);
            if (closing <= ParallelEpsilon)
                return 1f;

            float clearance = distance - skinWidth;
            if (clearance <= tolerance)
                return travelled / length;

            travelled += clearance / closing;
            if (travelled >= length)
                return 1f;
        }

        // Out of iterations: report a hit here. Stopping early beats tunnelling.
        return travelled / length;
    }

    /// <summary>Whether a segment intersects the convex solid bounded by the planes.</summary>
    public static bool SegmentIntersectsConvex(Vector3 a, Vector3 b, ReadOnlySpan<Plane> planes)
    {
        Vector3 direction = b - a;
        float enter = 0f;
        float exit = 1f;

        for (int i = 0; i < planes.Length; i++)
        {
            float distance = Plane.DotCoordinate(planes[i], a);
            float rate = Vector3.Dot(planes[i].Normal, direction);

            if (MathF.Abs(rate) < ParallelEpsilon)
            {
                if (distance > 0f)
                    return false;
                continue;
            }

            float t = -distance / rate;
            if (rate > 0f)
                exit = MathF.Min(exit, t);
            else
                enter = MathF.Max(enter, t);

            if (enter > exit)
                return false;
        }

        return true;
    }

    /// <summary>Closest point to <paramref name="point"/> on the segment.</summary>
    public static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point)
    {
        Vector3 ab = b - a;
        float lengthSquared = Vector3.Dot(ab, ab);
        if (lengthSquared <= ParallelEpsilon)
            return a;

        float t = Math.Clamp(Vector3.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return a + ab * t;
    }

    /// <summary>Nearest points between two segments.</summary>
    public static void ClosestBetweenSegments(
        Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
    {
        Vector3 d1 = q1 - p1;
        Vector3 d2 = q2 - p2;
        Vector3 r = p1 - p2;

        float a = Vector3.Dot(d1, d1);
        float e = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, r);

        float s, t;

        if (a <= ParallelEpsilon && e <= ParallelEpsilon)
        {
            c1 = p1;
            c2 = p2;
            return;
        }

        if (a <= ParallelEpsilon)
        {
            s = 0f;
            t = Math.Clamp(f / e, 0f, 1f);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= ParallelEpsilon)
            {
                t = 0f;
                s = Math.Clamp(-c / a, 0f, 1f);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denom = a * e - b * b;

                s = denom > ParallelEpsilon ? Math.Clamp((b * f - c * e) / denom, 0f, 1f) : 0f;

                t = (b * s + f) / e;
                if (t < 0f)
                {
                    t = 0f;
                    s = Math.Clamp(-c / a, 0f, 1f);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Math.Clamp((b - c) / a, 0f, 1f);
                }
            }
        }

        c1 = p1 + d1 * s;
        c2 = p2 + d2 * t;
    }

    /// <summary>Nearest points between a segment and a convex polygon.</summary>
    public static void ClosestBetweenSegmentAndPolygon(
        Vector3 a, Vector3 b, in Polygon polygon, out Vector3 onSegment, out Vector3 onPolygon)
    {
        ReadOnlySpan<Vector3> verts = polygon.VertexSpan;
        onSegment = a;
        onPolygon = verts.Length > 0 ? verts[0] : a;

        if (verts.Length < 3)
        {
            if (verts.Length == 2)
                ClosestBetweenSegments(a, b, verts[0], verts[1], out onSegment, out onPolygon);
            else if (verts.Length == 1)
                onSegment = ClosestPointOnSegment(a, b, verts[0]);
            return;
        }

        float bestSquared = float.MaxValue;

        // Interior: an endpoint that projects inside the polygon.
        Vector3 normal = polygon.Surface.Normal;
        for (int end = 0; end < 2; end++)
        {
            Vector3 point = end == 0 ? a : b;
            float signed = Plane.DotCoordinate(polygon.Surface, point);
            Vector3 projected = point - normal * signed;

            if (!PointInPolygon(projected, verts, normal))
                continue;

            float squared = Vector3.DistanceSquared(point, projected);
            if (squared < bestSquared)
            {
                bestSquared = squared;
                onSegment = point;
                onPolygon = projected;
            }
        }

        // Edges: this is what gets corners right.
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 e0 = verts[i];
            Vector3 e1 = verts[(i + 1) % verts.Length];
            ClosestBetweenSegments(a, b, e0, e1, out Vector3 c1, out Vector3 c2);

            float squared = Vector3.DistanceSquared(c1, c2);
            if (squared < bestSquared)
            {
                bestSquared = squared;
                onSegment = c1;
                onPolygon = c2;
            }
        }
    }

    /// <summary>Whether a coplanar point lies inside a convex polygon.</summary>
    public static bool PointInPolygon(Vector3 point, ReadOnlySpan<Vector3> verts, Vector3 normal)
    {
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 edge = verts[(i + 1) % verts.Length] - verts[i];
            Vector3 toPoint = point - verts[i];
            if (Vector3.Dot(Vector3.Cross(edge, toPoint), normal) < -InsideEpsilon)
                return false;
        }

        return true;
    }
}
