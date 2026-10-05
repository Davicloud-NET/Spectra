using System;
using System.Numerics;

namespace SpectraEngine.Core.Bsp;

// The stretch of a line inside a brush: where the line enters and leaves, as
// distances along it, and the planes it does so through. Start from the
// stretch of the line that matters and call Clip or ClipSegment.
internal struct BrushLineClip(float enter, float exit)
{
    // Below this the line counts as parallel to a plane.
    private const float ParallelEpsilon = 1e-9f;

    public float Enter = enter;
    public float Exit = exit;

    // -1 while the bound is still the one this started with.
    public int EnterPlane = -1;
    public int ExitPlane = -1;

    // The planes a segment given to ClipSegment lies in. A convex brush has
    // two where the segment runs along one of its edges.
    public int FaceCount;
    public int FirstFace = -1;
    public int SecondFace = -1;

    // Narrows the stretch to the inside of every plane, in the planes' own
    // frame. Planes face outward, so inside is distance <= 0. False when
    // nothing is left. A line lying in a face is inside.
    public bool Clip(ReadOnlySpan<Plane> planes, Vector3 origin, Vector3 direction)
    {
        for (int i = 0; i < planes.Length; i++)
        {
            Plane plane = planes[i];
            float distance = Plane.DotCoordinate(plane, origin);
            float denom = Vector3.Dot(plane.Normal, direction);

            if (MathF.Abs(denom) < ParallelEpsilon)
            {
                if (distance > 0f)
                    return false;
                continue;
            }

            if (!Narrow(i, distance, denom))
                return false;
        }

        return true;
    }

    // As Clip, for the segment that runs length along direction from origin.
    // A plane the whole segment stays within slack of narrows nothing and is
    // counted as a face the segment lies in. Which side of such a face the
    // segment is on is the caller's to say: rounding cannot.
    public bool ClipSegment(
        ReadOnlySpan<Plane> planes, Vector3 origin, Vector3 direction, float length, float slack)
    {
        for (int i = 0; i < planes.Length; i++)
        {
            Plane plane = planes[i];
            float distance = Plane.DotCoordinate(plane, origin);
            float denom = Vector3.Dot(plane.Normal, direction);

            if (MathF.Abs(distance) <= slack && MathF.Abs(distance + denom * length) <= slack)
            {
                AddFace(i);
                continue;
            }

            if (MathF.Abs(denom) < ParallelEpsilon)
            {
                if (distance > 0f)
                    return false;
                continue;
            }

            if (!Narrow(i, distance, denom))
                return false;
        }

        return true;
    }

    private bool Narrow(int plane, float distance, float denom)
    {
        float tPlane = -distance / denom;
        if (denom < 0f)
        {
            if (tPlane > Enter)
            {
                Enter = tPlane;
                EnterPlane = plane;
            }
        }
        else if (tPlane < Exit)
        {
            Exit = tPlane;
            ExitPlane = plane;
        }

        return Enter <= Exit;
    }

    private void AddFace(int plane)
    {
        if (FaceCount == 0)
            FirstFace = plane;
        else if (FaceCount == 1)
            SecondFace = plane;

        FaceCount++;
    }
}
