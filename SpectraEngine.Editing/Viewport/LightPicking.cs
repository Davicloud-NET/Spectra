using System;
using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// How big the icon of a light or a point entity is, and the ray test a click
/// on one uses. <see cref="SceneIcons"/> says where each icon stands.
/// </summary>
public static class LightPicking
{
    /// <summary>
    /// The icon's world radius at <paramref name="at"/>. Zero when the point is
    /// outside the view.
    /// </summary>
    public static float WorldRadius(Camera camera, Vector2 viewportSize, Vector3 at) =>
        TryMeasure(camera, viewportSize, at, out float radius, out _) ? radius : 0f;

    /// <summary>
    /// The icon's radius on screen at <paramref name="at"/>, in pixels. Zero
    /// when the point is outside the view.
    /// </summary>
    public static float PixelRadius(Camera camera, Vector2 viewportSize, Vector3 at) =>
        TryMeasure(camera, viewportSize, at, out float radius, out float pixel) ? radius / pixel : 0f;

    /// <summary>
    /// The icon's world radius at <paramref name="at"/> and what one screen
    /// pixel measures there. False when the point is outside the view.
    /// </summary>
    // The one place the icon's size is decided. Drawing, picking and the
    // marquee all ask it, so what is clicked is what is seen.
    public static bool TryMeasure(
        Camera camera, Vector2 viewportSize, Vector3 at, out float worldRadius, out float worldPerPixel)
    {
        worldRadius = 0f;
        worldPerPixel = 0f;

        float depth = GizmoMath.ViewDepth(camera, at);
        if (depth <= 0f || viewportSize.Y <= 0f)
            return false;

        worldPerPixel = GizmoMath.WorldPerPixel(camera, viewportSize.Y, depth);
        if (worldPerPixel <= 0f)
            return false;

        worldRadius = Math.Clamp(
            LightOverlay.IconWorldRadius,
            LightOverlay.MinIconPixels * worldPerPixel,
            LightOverlay.IconPixels * worldPerPixel);
        return true;
    }

    // Assumes a normalised direction. Near root, clamped to zero so an origin
    // inside the sphere hits at the origin.
    internal static bool TryRaySphere(in Ray3 ray, Vector3 centre, float radius, out float distance)
    {
        distance = 0f;

        Vector3 toCentre = centre - ray.Origin;
        float along = Vector3.Dot(toCentre, ray.Direction);

        // How far the ray passes from the centre, measured at its nearest
        // point. A flat view's ray starts a far plane back, and there the
        // usual difference of squares has no digits left for an icon.
        float missSquared = (toCentre - (ray.Direction * along)).LengthSquared();
        float halfChordSquared = (radius * radius) - missSquared;
        if (halfChordSquared < 0f)
            return false;

        float halfChord = MathF.Sqrt(halfChordSquared);

        // The whole sphere is behind the origin.
        if (along + halfChord < 0f)
            return false;

        distance = MathF.Max(along - halfChord, 0f);
        return true;
    }
}
