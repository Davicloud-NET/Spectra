using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using SpectraShade.Compiler;

namespace SpectraEngine.Graphics.Tests;

/// <summary>Compiles the built-in shaders with a real OpenGL driver.</summary>
[Collection(GlRendererCollection.Name)]
public sealed class BaseShaderGlCompilationTests
{
    private readonly GlRendererFixture _fixture;

    public BaseShaderGlCompilationTests(GlRendererFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Lit_compiles_in_opengl()
    {
        // OpenGLRenderer.Initialize compiles Lit; a rejection throws in the fixture.
        _fixture.Renderer.DefaultShader.ShouldNotBeNull();
    }

    [Fact]
    public void The_deferred_shaders_compile_in_opengl()
    {
        // The analyser does no name or type checking, so the driver is the
        // first thing to reject a misspelled builtin.
        _fixture.Renderer.CreateShaderFromSource(BaseShaders.GBufferFill).ShouldNotBeNull();
        _fixture.Renderer.CreateShaderFromSource(BaseShaders.DeferredLight).ShouldNotBeNull();
    }

    [Fact]
    public void The_generated_instanced_stages_compile_in_opengl()
    {
        // The instanced vertex stage is generated from [PerInstance].
        foreach (string source in new[] { BaseShaders.ShadowDepth, BaseShaders.GBufferFill })
        {
            CompiledShaderFile compiled = new SpectraShadeCompiler()
                .Compile(source, [GraphicsBackend.OpenGL]);
            PipelineBlob blob = compiled.GetPipeline(GraphicsBackend.OpenGL).ShouldNotBeNull();

            blob.InstancedVertexData.ShouldNotBeNull();
            _fixture.Renderer.CreateShaderFromSource(source).ShouldNotBeNull();
            _fixture.Renderer.TryCreateInstancedShaderFromSource(source).ShouldNotBeNull();
        }
    }

    [Fact]
    public void Lit_recompiles_explicitly()
    {
        var shader = _fixture.Renderer.CreateShaderFromSource(BaseShaders.Lit);
        shader.ShouldNotBeNull();
    }
}
