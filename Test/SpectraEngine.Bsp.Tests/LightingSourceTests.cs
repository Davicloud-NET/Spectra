using SpectraEngine.Core.Graphics.Shaders;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The forward shader and the deferred light pass carry the same lighting
/// text. SpectraShade has no include, so nothing else keeps them in step.
/// </summary>
// Text, not pixels: this runs where there is no GPU. PipelineParityGlTests
// compares the pictures where there is one.
public sealed class LightingSourceTests
{
    private const string Begin = "// Shared lighting begins.";
    private const string End = "// Shared lighting ends.";

    [Fact]
    public void Lit_and_DeferredLight_share_one_lighting_block()
    {
        string forward = SharedBlock(BaseShaders.Lit, "Lit");
        string deferred = SharedBlock(BaseShaders.DeferredLight, "DeferredLight");

        forward.ShouldContain("vec3 Lighting(");
        forward.ShouldBe(deferred, "a change to the lighting goes into both files");
    }

    [Theory]
    [InlineData("Lit")]
    [InlineData("DeferredLight")]
    public void Nothing_that_lights_a_surface_sits_outside_the_block(string shader)
    {
        // A light loop or a shadow lookup outside the markers would escape
        // the comparison above.
        string source = Normalize(shader == "Lit" ? BaseShaders.Lit : BaseShaders.DeferredLight);
        string outside = source.Replace(SharedBlock(source, shader), string.Empty);

        outside.ShouldNotContain("uLightPositions[i]");
        outside.ShouldNotContain("uShadowMap.Sample");
        outside.ShouldNotContain("uAmbientSky,");
    }

    private static string SharedBlock(string source, string name)
    {
        source = Normalize(source);

        int begin = source.IndexOf(Begin, StringComparison.Ordinal);
        int end = source.IndexOf(End, StringComparison.Ordinal);
        begin.ShouldBeGreaterThanOrEqualTo(0, $"{name} has no '{Begin}' marker");
        end.ShouldBeGreaterThan(begin, $"{name} has no '{End}' marker after the first");

        return source[begin..(end + End.Length)];
    }

    // The check must not depend on how the file was checked out.
    private static string Normalize(string source) => source.Replace("\r\n", "\n");
}
