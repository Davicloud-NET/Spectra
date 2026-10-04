namespace SpectraEngine.Core.Graphics;

/// <summary>
/// A depth offset the rasterizer applies to the next draw. Used by the shadow
/// pass against acne; it moves stored depth, so the shadow's outline stays put.
/// </summary>
/// <param name="Constant">In depth-buffer units, as glPolygonOffset and D3D DepthBias take them.</param>
/// <param name="SlopeScaled">Multiplier on the primitive's maximum depth slope.</param>
public readonly record struct DepthBias(int Constant, float SlopeScaled)
{
    /// <summary>No bias.</summary>
    public static DepthBias None => default;

    public bool IsZero => Constant == 0 && SlopeScaled == 0f;
}
