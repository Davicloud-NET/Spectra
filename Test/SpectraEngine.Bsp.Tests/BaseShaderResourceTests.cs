using SpectraEngine.Core.Graphics.Shaders;

namespace SpectraEngine.Bsp.Tests;

// Catches a shader dropped from the EmbeddedResource glob or moved to a folder
// that changes its resource name. The compiler cannot see either.
public sealed class BaseShaderResourceTests
{
    [Fact]
    public void Every_base_shader_resolves_by_its_constant_resource_name()
    {
        BaseShaders.FileNames.Count.ShouldBeGreaterThan(0);

        foreach (string fileName in BaseShaders.FileNames)
        {
            using Stream stream = BaseShaders.OpenEmbedded(fileName);
            stream.ShouldNotBeNull($"'{fileName}' should be embedded in SpectraEngine.Core");
            stream.Length.ShouldBeGreaterThan(0, $"'{fileName}' should not be empty");
        }
    }

    [Fact]
    public void An_unknown_shader_name_throws_and_names_it()
    {
        var thrown = Should.Throw<InvalidOperationException>(
            () => BaseShaders.OpenEmbedded("NoSuchShader.spectrashade"));

        thrown.Message.ShouldContain("NoSuchShader.spectrashade");
    }

    [Fact]
    public void A_bare_suffix_of_a_real_shader_name_does_not_resolve()
    {
        // A suffix match would return DebugLine or WorldLine for "Line.spectrashade".
        Should.Throw<InvalidOperationException>(
            () => BaseShaders.OpenEmbedded("Line.spectrashade"));
    }

    [Fact]
    public void Every_declared_file_name_reads_as_source()
    {
        // The count ties the accessors below to FileNames: add one, add both.
        BaseShaders.FileNames.Count.ShouldBe(9);

        foreach (string source in new[]
                 {
                     BaseShaders.Lit,
                     BaseShaders.DebugLine,
                     BaseShaders.PostResolve,
                     BaseShaders.GBufferFill,
                     BaseShaders.GBufferFillCompact,
                     BaseShaders.DeferredLight,
                     BaseShaders.ShadowDepth,
                     BaseShaders.WorldLine,
                     BaseShaders.WorldLineBlend,
                 })
        {
            source.ShouldNotBeNullOrWhiteSpace();
        }
    }
}
