namespace SpectraEngine.Core.Graphics;

/// <summary>
/// What a draw does with the depth buffer. Chosen per draw, because one shader
/// (the debug-line one) draws both depth-tested lines and always-on-top overlays.
/// </summary>
public enum DepthMode
{
    /// <summary>Test and write. Ordinary opaque geometry.</summary>
    TestWrite,

    /// <summary>
    /// Test and write, accepting an exact depth tie. Unused today, kept for
    /// opaque decals that lie exactly on a surface.
    /// </summary>
    TestWriteEqual,

    /// <summary>Test, no write. Sorted transparency.</summary>
    TestNoWrite,

    /// <summary>
    /// Test accepting a tie, no write. Blended lines lying on a surface, like
    /// the grid on a floor: strict Less would reject the coplanar case, and a
    /// depth write would let translucent pixels occlude later draws.
    /// </summary>
    TestNoWriteEqual,

    /// <summary>No test, no write: always on top. Editor overlays.</summary>
    None,
}
