namespace SpectraEngine.Core.Graphics;

/// <summary>
/// How the rasterizer treats depth for the next draw. Used by the shadow pass:
/// the offset is against acne, and it moves stored depth, so the shadow's
/// outline stays put.
/// </summary>
/// <param name="Constant">In depth-buffer units, as glPolygonOffset and D3D DepthBias take them.</param>
/// <param name="SlopeScaled">Multiplier on the primitive's maximum depth slope.</param>
/// <param name="ClampDepth">
/// Depth outside the volume is clamped, not clipped. A shadow caster nearer
/// the light than the map's volume is drawn flat on its front and still casts.
/// </param>
public readonly record struct DepthBias(int Constant, float SlopeScaled, bool ClampDepth = false)
{
    /// <summary>No bias and no clamp.</summary>
    public static DepthBias None => default;

    public bool IsZero => Constant == 0 && SlopeScaled == 0f && !ClampDepth;
}
