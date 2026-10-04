namespace SpectraEngine.Bsp.Tests;

internal static class AssetTestFacts
{
    // Textures AttachRenderer creates before any content loads: the magenta
    // placeholder and the white texel. Upload counts are this plus what a test loaded.
    public const int BuiltInTextures = 2;
}
