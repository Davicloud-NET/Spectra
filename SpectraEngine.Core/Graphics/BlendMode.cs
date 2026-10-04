namespace SpectraEngine.Core.Graphics;

/// <summary>
/// How a draw's output is combined with what is already in the render target.
/// </summary>
public enum BlendMode
{
    /// <summary>No blending. Source replaces destination.</summary>
    Opaque,

    /// <summary>Non-premultiplied source alpha over destination.</summary>
    AlphaBlend,
}
