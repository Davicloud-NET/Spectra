using System;
using System.Numerics;

namespace SpectraEngine.Core.Bsp;

// The stretch of a line inside a brush: where the line enters and leaves, as
// distances along it, and the planes it does so through. Start from the
// stretch of the line that matters and call Clip.
internal struct BrushLineClip(float enter, float exit)
{
    // Below this the line counts as parallel to a plane.
    private const float ParallelEpsilon = 1e-9f;

    public float Enter = enter;
    public float Exit = exit;

    // -1 while the bound is still the one this started with.
    public int EnterPlane = -1;
    public int ExitPlane = -1;

    // Narrows the stretch to the inside of every plane, in the planes' own
    // frame. Planes face outward, so inside is distance <= 0. False when
    // nothing is left. surfaceIsInside says which way a line lying in a face
    // goes.
    public bool Clip(ReadOnlySpan<Plane> planes, Vector3 origin, Vector3 direction, bool surfaceIsInside)
    {
        for (int i = 0; i < planes.Length; i++)
        {
            Plane plane = planes[i];
            float distance = Plane.DotCoordinate(plane, origin);
            float denom = Vector3.Dot(plane.Normal, direction);

            if (MathF.Abs(denom) < ParallelEpsilon)
            {
                if (distance > 0f || (distance == 0f && !surfaceIsInside))
                    return false;
                continue;
            }

            float tPlane = -distance / denom;
            if (denom < 0f)
            {
                if (tPlane > Enter)
                {
                    Enter = tPlane;
                    EnterPlane = i;
                }
            }
            else if (tPlane < Exit)
            {
                Exit = tPlane;
                ExitPlane = i;
            }

            if (Enter > Exit)
                return false;
        }

        return true;
    }
}
