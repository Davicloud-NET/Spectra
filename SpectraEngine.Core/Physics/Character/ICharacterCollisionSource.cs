using System;
using System.Numerics;

namespace SpectraEngine.Core.Physics.Character;

/// <summary>
/// The four things a character mover needs from the world: sweep a capsule,
/// gather the planes touching it, solve those planes, and clip velocity.
/// </summary>
// A backend supplies the sweep and the gather. The solve and the clip are
// shared defaults so every source resolves a plane set the same way.
public interface ICharacterCollisionSource
{
    /// <summary>
    /// Moves when the geometry changes. A replay that crosses a change must
    /// be refused.
    /// </summary>
    int Revision { get; }

    /// <summary>The rest offset every method's contract is expressed against.</summary>
    float SkinWidth { get; }

    /// <summary>
    /// Selects the geometry a tick can touch, so every sweep and gather in it
    /// shares one broad phase.
    /// </summary>
    void BeginTick(in Bsp.Aabb volume, in CharacterQueryFilter filter);

    /// <summary>
    /// The fraction of <paramref name="translation"/> travelled before the
    /// capsule's surface is <see cref="SkinWidth"/> from contact; 1 when
    /// unobstructed. A capsule already overlapping returns 0 and still reports
    /// a plane.
    /// </summary>
    float SweepCapsule(
        in CharacterCapsule capsule,
        Vector3 translation,
        in CharacterQueryFilter filter,
        out CharacterContactPlane plane,
        out CharacterContactSource source);

    /// <summary>
    /// Every contact plane whose separation is at or below
    /// <paramref name="maxSeparation"/>, deepest first; returns how many were
    /// written. On overflow the deepest are kept and the rest counted in
    /// <see cref="DroppedPlanes"/>.
    /// </summary>
    int GatherPlanes(
        in CharacterCapsule capsule,
        float maxSeparation,
        in CharacterQueryFilter filter,
        Span<CharacterContactPlane> planes,
        Span<CharacterContactSource> sources);

    /// <summary>Contact planes dropped for want of space.</summary>
    int DroppedPlanes { get; }

    /// <summary>
    /// The largest translation toward <paramref name="targetDelta"/> that leaves
    /// every plane satisfied, writing each plane's realised push.
    /// </summary>
    CharacterPlaneSolveResult SolvePlanes(Vector3 targetDelta, Span<CharacterContactPlane> planes) =>
        CharacterPlaneSolver.Solve(targetDelta, planes);

    /// <summary>Removes the components of <paramref name="velocity"/> driving into any engaged plane.</summary>
    Vector3 ClipVelocity(Vector3 velocity, ReadOnlySpan<CharacterContactPlane> planes) =>
        CharacterPlaneSolver.ClipVelocity(velocity, planes);
}

/// <summary>The outcome of a plane solve.</summary>
public readonly record struct CharacterPlaneSolveResult(Vector3 Delta, int IterationCount);

/// <summary>
/// The shared plane solver: accumulated-push Gauss-Seidel, transcribed from
/// Box3D's mover so a native source would resolve identically.
/// </summary>
public static class CharacterPlaneSolver
{
    // Both constants are Box3D's. Changing them breaks parity with it.

    /// <summary>Solver iterations.</summary>
    public const int Iterations = 20;

    /// <summary>Convergence tolerance, in world units.</summary>
    public const float Tolerance = 0.005f;

    /// <summary>
    /// Finds the translation nearest <paramref name="targetDelta"/> that leaves
    /// every plane's separation non-negative.
    /// </summary>
    // Push is accumulated per plane, so a plane can give back push it no longer
    // needs. That lets a character in a corner settle.
    public static CharacterPlaneSolveResult Solve(Vector3 targetDelta, Span<CharacterContactPlane> planes)
    {
        for (int i = 0; i < planes.Length; i++)
            planes[i].Push = 0f;

        Vector3 delta = targetDelta;
        int iteration = 0;

        for (; iteration < Iterations; iteration++)
        {
            float maxCorrection = 0f;

            for (int i = 0; i < planes.Length; i++)
            {
                // D is the separation at zero translation.
                float separation = Plane.DotCoordinate(planes[i].Plane, delta);
                float correction = -separation;

                float oldPush = planes[i].Push;
                float newPush = Math.Clamp(oldPush + correction, 0f, planes[i].PushLimit);
                correction = newPush - oldPush;
                planes[i].Push = newPush;

                delta += planes[i].Plane.Normal * correction;
                maxCorrection = MathF.Max(maxCorrection, MathF.Abs(correction));
            }

            if (maxCorrection < Tolerance)
            {
                iteration++;
                break;
            }
        }

        return new CharacterPlaneSolveResult(delta, iteration);
    }

    /// <summary>
    /// Removes velocity driving into any plane that engaged. Planes with zero
    /// push are skipped.
    /// </summary>
    public static Vector3 ClipVelocity(Vector3 velocity, ReadOnlySpan<CharacterContactPlane> planes)
    {
        for (int i = 0; i < planes.Length; i++)
        {
            if (!planes[i].ClipVelocity || planes[i].Push == 0f)
                continue;

            float into = Vector3.Dot(velocity, planes[i].Plane.Normal);
            if (into < 0f)
                velocity -= planes[i].Plane.Normal * into;
        }

        return velocity;
    }
}
