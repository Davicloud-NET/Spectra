using System.Numerics;

namespace SpectraEngine.Core.Graphics;

/// <summary>One light, flattened into four <c>vec4</c>s for upload.</summary>
/// <param name="PositionRange">
/// xyz is the world position, or for a directional light the normalised
/// direction the light travels. w is the range, ignored for a directional light.
/// </param>
/// <param name="ColorIntensity">
/// Linear RGB in xyz, already multiplied by intensity. w is the
/// <see cref="RenderLightType"/> as a float.
/// </param>
/// <param name="Axis">
/// xyz is the direction the light faces: a spot's cone axis, an area light's
/// surface normal. w is cos(outer half-angle) for a spot, half-height for a
/// rect, radius for a disc.
/// </param>
/// <param name="Tangent">
/// xyz is an area light's first in-plane axis. w is cos(inner half-angle) for
/// a spot, half-width for a rect.
/// </param>
// Uploaded as four parallel vec4 arrays. A struct array compiles on OpenGL and
// then lights nothing, with no error.
public readonly record struct RenderLight(
    Vector4 PositionRange,
    Vector4 ColorIntensity,
    Vector4 Axis = default,
    Vector4 Tangent = default)
{
    /// <summary>What kind of light this is.</summary>
    public RenderLightType Type => (RenderLightType)(int)ColorIntensity.W;

    /// <summary>True when this light has no position, only a direction.</summary>
    public bool IsDirectional => Type == RenderLightType.Directional;
}

/// <summary>The light kinds, as the integers a shader compares against.</summary>
// Append only. DeferredLight and Lit hard-code these numbers, and they must
// match Scene.LightKind's order.
public enum RenderLightType
{
    /// <summary>Parallel rays from infinitely far away.</summary>
    Directional = 0,

    /// <summary>Radiates in every direction from a point.</summary>
    Point = 1,

    /// <summary>A cone from a point, with a soft edge between two half-angles.</summary>
    Spot = 2,

    /// <summary>A one-sided rectangle that emits from its whole surface.</summary>
    Rect = 3,

    /// <summary>A one-sided disc that emits from its whole surface.</summary>
    Disc = 4,
}
