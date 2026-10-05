using SpectraEngine.Core.Assets;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// One stretch of solid on a traced segment: where it starts and ends, in
/// world units from the segment's first point, and what it is made of.
/// </summary>
/// <param name="Material">
/// What the stretch is made of: the material of the face through which the
/// segment entered the brush it belongs to, even where a cut or another brush
/// took the first part of that brush. For a segment that starts inside the
/// brush, the face it leaves by.
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
