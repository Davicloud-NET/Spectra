using System.Numerics;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// What a render pass clears before drawing. Clearing depth also clears stencil.
/// </summary>
/// <param name="Color">Linear colour to clear to, or null to keep what is there.</param>
/// <param name="Depth">Depth to clear to, usually 1, or null to keep what is there.</param>
public readonly record struct PassClear(Vector4? Color, float? Depth)
{
    /// <summary>Clears colour to <paramref name="color"/> and depth to the far plane.</summary>
    public static PassClear To(Vector4 color) => new(color, 1f);

    /// <summary>Clears depth only.</summary>
    public static PassClear DepthOnly => new(null, 1f);

    /// <summary>Clears nothing, for a pass drawing over a finished frame.</summary>
    public static PassClear Keep => new(null, null);
}
