using System;
using System.Numerics;

namespace SpectraEngine.Core.Bsp;

// Clips a line against a brush's planes, in the brush's own frame.
internal static class BrushLineClip
{
    // Below this the line counts as parallel to a plane.
    private const float ParallelEpsilon = 1e-9f;

    // Narrows [tEnter, tExit] to the stretch of origin + t * direction that is
    // inside every plane. Planes face outward, so inside is distance <= 0.
    // False when nothing is left. enterPlane and exitPlane name the planes that
    // moved the two bounds, or -1 for a bound the caller's value still holds.
    // surfaceIsInside says which way a line lying in a face goes.
    public static bool Clip(
        ReadOnlySpan<Plane> planes, Vector3 origin, Vector3 direction, bool surfaceIsInside,
        ref float tEnter, ref float tExit, out int enterPlane, out int exitPlane)
    {
        enterPlane = -1;
        exitPlane = -1;

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
                if (tPlane > tEnter)
                {
                    tEnter = tPlane;
                    enterPlane = i;
                }
            }
            else if (tPlane < tExit)
            {
                tExit = tPlane;
                exitPlane = i;
            }

            if (tEnter > tExit)
                return false;
        }

        return true;
    }
}
