using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using System;

namespace SpectraEngine.Editing.Tests;

// Only the framebuffer size is needed here. Every GPU entry point throws.
internal sealed class StubRenderer : Renderer
{
    public StubRenderer()
        : base(NullLogger<Renderer>.Instance, new ThrowingShaderCompiler())
    {
    }

    // Arbitrary.
    public override GraphicsBackend Backend => GraphicsBackend.OpenGL;

    public override string CurrentPipelineName => "Stub";

    public override string NextPipeline() => "Stub";

    public override bool TrySelectPipeline(string name) =>
        string.Equals(name, "Stub", StringComparison.OrdinalIgnoreCase);

    // The shadow atlas calls this.
    protected override void SetViewportCore(int x, int y, int width, int height) { }

    public override Mesh CreateMesh(ReadOnlySpan<float> vertices, ReadOnlySpan<uint> indices,
        ReadOnlySpan<VertexAttribute> attributes, MeshCpuAccess cpuAccess = MeshCpuAccess.Retained)
        => throw new NotSupportedException("StubRenderer creates no GPU resources.");

    public override InstanceBuffer CreateInstanceBuffer(
        int capacityInstances, ReadOnlySpan<VertexAttribute> attributes, ShaderProgram program)
        => throw new NotSupportedException("StubRenderer creates no GPU resources.");

    protected override Texture CreateTextureCore(in TextureUploadDesc desc)
        => throw new NotSupportedException("StubRenderer creates no GPU resources.");

    // Passes are no-ops, not throws: a test that opens one should not fail here.
    protected override void BeginPassCore(
        RenderTarget? target, ReadOnlySpan<RenderTarget> targets, in PassClear clear)
    {
    }

    protected override void EndPassCore(RenderTarget? target, ReadOnlySpan<RenderTarget> targets)
    {
    }

    protected override void FlushDebugDrawCore(SpectraEngine.Core.Scene.Camera camera)

    {
    }


    protected override void FlushWorldLinesCore(
        SpectraEngine.Core.Scene.Camera camera, ShaderProgram program, float nudge, GBuffer? gbuffer)
    {
    }

    protected override void DrawFullscreen(PostPass pass, Mesh geometry)
        => throw new NotSupportedException($"{GetType().Name} draws nothing.");

    internal override (byte R, byte G, byte B, byte A) ReadTargetPixel(RenderTarget target, int x, int y)
        => throw new NotSupportedException($"{GetType().Name} has no pixels to read.");

    public override RenderTarget CreateRenderTarget(in RenderTargetDesc desc)
        => throw new NotSupportedException($"{GetType().Name} creates no GPU resources.");

    public override ShaderProgram CreateShader(string vertexSource, string fragmentSource)
        => throw new NotSupportedException("StubRenderer creates no shaders.");

    public override ShaderProgram CreateShader(PipelineBlob blob)
        => throw new NotSupportedException("StubRenderer creates no shaders.");

    private sealed class ThrowingShaderCompiler : IShaderCompiler
    {
        public CompiledShaderFile Compile(string source, ReadOnlySpan<GraphicsBackend> targets)
            => throw new NotSupportedException("StubRenderer compiles no shaders.");
    }
}
