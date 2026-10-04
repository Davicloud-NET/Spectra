using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>The result of one hit test against the gizmo.</summary>
/// <param name="Handle">The picked handle, or <see cref="GizmoHandle.None"/> for a miss.</param>
/// <param name="RayDistance">Distance along the picking ray to the hit, in world units.</param>
/// <param name="PixelDistance">
/// How far the cursor missed by, in pixels. Zero for plane quads and the centre
/// disc, which are hit or not.
/// </param>
/// <param name="Point">
/// The world-space point on the ray at <paramref name="RayDistance"/>. For an
/// axis arrow this is on the ray, not on the arrow.
/// </param>
public readonly record struct GizmoPick(
    GizmoHandle Handle, float RayDistance, float PixelDistance, Vector3 Point)
{
    /// <summary>A miss: no handle, at infinite distance.</summary>
    public static GizmoPick Miss =>
        new(GizmoHandle.None, float.PositiveInfinity, float.PositiveInfinity, Vector3.Zero);

    /// <summary>True when a handle was picked.</summary>
    public bool IsHit => Handle != GizmoHandle.None;
}
