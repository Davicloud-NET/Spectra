using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using System;

namespace SpectraEngine.Editing.Tests;

// Accepts static-world meshes and only counts them, so Scene.RebuildStaticWorld
// can run headless. Textures and shaders throw: an editing test should not need them.
internal sealed class CompilingRenderer : Renderer
{
    public CompilingRenderer()
        : base(NullLogger<Renderer>.Instance, new ThrowingShaderCompiler())
    {
    }

    public int CreatedMeshCount { get; private set; }

    public override GraphicsBackend Backend => GraphicsBackend.OpenGL;

    public override string CurrentPipelineName => "Compiling";

    public override string NextPipeline() => "Compiling";

    public override bool TrySelectPipeline(string name) =>
        string.Equals(name, "Compiling", StringComparison.OrdinalIgnoreCase);

    protected override void SetViewportCore(int x, int y, int width, int height) { }

    public override Mesh CreateMesh(
        ReadOnlySpan<float> vertices, ReadOnlySpan<uint> indices,
        ReadOnlySpan<VertexAttribute> attributes, MeshCpuAccess cpuAccess = MeshCpuAccess.Retained)
    {
        CreatedMeshCount++;
        return new EmptyMesh((uint)indices.Length);
    }

    public override InstanceBuffer CreateInstanceBuffer(
        int capacityInstances, ReadOnlySpan<VertexAttribute> attributes, ShaderProgram program)
        => throw new NotSupportedException("This renderer creates no GPU resources.");

    protected override Texture CreateTextureCore(in TextureUploadDesc desc)
        => throw new NotSupportedException("CompilingRenderer creates no textures.");

    // Passes are no-ops, not throws, so a test that opens one doesn't fail here.
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
        => throw new NotSupportedException("CompilingRenderer creates no shaders.");

    public override ShaderProgram CreateShader(PipelineBlob blob)
        => throw new NotSupportedException("CompilingRenderer creates no shaders.");

    private sealed class EmptyMesh : Mesh
    {
        public EmptyMesh(uint indexCount) => IndexCount = indexCount;

        public override void Draw()
        {
        }

        public override void DrawInstanced(InstanceBuffer instances, int instanceCount, int firstInstance = 0)
        {
        }

        public override void Dispose()
        {
        }
    }

    private sealed class ThrowingShaderCompiler : IShaderCompiler
    {
        public CompiledShaderFile Compile(string source, ReadOnlySpan<GraphicsBackend> targets)
            => throw new NotSupportedException("CompilingRenderer compiles no shaders.");
    }
}
