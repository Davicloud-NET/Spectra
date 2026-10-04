using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Assets.Models;

/// <summary>
/// One convex collision hull, as a range into the <c>COLL</c> section's plane
/// array. Eight bytes, as on disk. A model may have several, and they may overlap.
/// </summary>
// Plane sets because that is what Brush's constructor takes.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmodelCollisionHull
{
    /// <summary>Index of this hull's first plane in the section's plane array.</summary>
    public readonly uint PlaneStart;

    /// <summary>
    /// How many planes bound this hull. At least
    /// <see cref="SmodelFormat.MinimumHullPlanes"/>.
    /// </summary>
    public readonly uint PlaneCount;

    /// <summary>Builds one hull record.</summary>
    public SmodelCollisionHull(uint planeStart, uint planeCount)
    {
        PlaneStart = planeStart;
        PlaneCount = planeCount;
    }
}
