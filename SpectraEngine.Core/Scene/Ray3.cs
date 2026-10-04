using System.Numerics;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// A ray in world space. <see cref="Direction"/> must be unit length, so the
/// parameter along the ray is a world-space distance.
/// </summary>
public readonly record struct Ray3(Vector3 Origin, Vector3 Direction)
{
    /// <summary>The point <paramref name="distance"/> world units along the ray.</summary>
    public Vector3 PointAt(float distance) => Origin + Direction * distance;
}
