namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Facts about the asset manager that several suites count against.
/// </summary>
internal static class AssetTestFacts
{
    /// <summary>
    /// GPU textures <c>AttachRenderer</c> creates before any content is loaded:
    /// the magenta placeholder checker and the white texel the neutral surface
    /// is tinted from.
    /// </summary>
    /// <remarks>
    /// <b>Named once because it is arithmetic in seven assertions.</b> Every
    /// upload count in these suites is "the built-ins plus what this test
    /// loaded", and spelling that as a literal meant adding one built-in turned
    /// seven tests red with numbers that say nothing about what changed.
    /// </remarks>
    public const int BuiltInTextures = 2;
}
