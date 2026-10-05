using SpectraEngine.Core.Assets;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// One stretch of solid on a traced segment: where it starts and ends, in
/// world units from the segment's first point, and what it is made of.
/// </summary>
/// <param name="Material">
/// The material of the face the segment enters the solid by. Behind a cut that
/// is the cut's own face, the one drawn there. Where two brushes overlap, the
/// later one's share names the face of its own the segment entered by. A
/// segment that starts inside a brush names the face it leaves that brush by.
/// A baked map that kept no face materials names the default one throughout.
/// </param>
public readonly record struct SolidSpan(float Start, float End, MaterialRef Material)
{
    /// <summary>
    /// Solid thinner than this is left out, and two solids closer together
    /// than this count as touching. The carve treats planes this close as one.
    /// </summary>
    public const float Tolerance = 1e-3f;

    /// <summary>How much solid the segment passes through here, in world units.</summary>
    public float Thickness => End - Start;
}
