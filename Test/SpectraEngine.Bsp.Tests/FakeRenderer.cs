using Microsoft.Extensions.Logging;
using Silk.NET.Maths;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

// GPU-free Renderer for headless static-world and asset tests. Meshes and
// textures are recorded CPU-side; shader creation throws.
internal sealed class FakeRenderer : Renderer
{
    // Creation order, destroyed ones included.
    public List<FakeMesh> CreatedMeshes { get; } = [];

    // Still registered: shows DestroyMesh deregistered as well as disposed.
    public HashSet<FakeMesh> LiveMeshes { get; } = new(ReferenceEqualityComparer.Instance);

    // Creation order, destroyed ones included.
    public List<FakeTexture> CreatedTextures { get; } = [];

    public HashSet<FakeTexture> LiveTextures { get; } = new(ReferenceEqualityComparer.Instance);

    // Whether this stands in for a backend whose device can be lost.
    public bool HasDevice { get; set; }

    public override bool CanLoseDevice => HasDevice;

    // What a backend's Present asks: was a loss asked for since the last one?
    public bool TakeDeviceLoss() => TakeSimulatedDeviceLoss();

    // CreateMesh calls left before it starts throwing. int.MaxValue never fails.
    public int CreateMeshBudget { get; set; } = int.MaxValue;

    // One entry per BeginPass: the clear and the target size when it opened.
    public List<(PassClear Clear, Vector2D<int> Size, RenderTarget? Target)> Passes { get; } = [];

    // Zero at the end of a well-formed frame.
    public int OpenPasses { get; private set; }

    public List<FakeRenderTarget> CreatedRenderTargets { get; } = [];

    public List<FakeRenderTarget> LiveRenderTargets { get; } = [];

    // Attachment count of each pass.
    public List<int> PassTargetCounts { get; } = [];

    protected override void BeginPassCore(
        RenderTarget? target, ReadOnlySpan<RenderTarget> targets, in PassClear clear)
    {
        Passes.Add((clear, PassSize, target));
        PassTargetCounts.Add(targets.Length);
        OpenPasses++;
    }

    public int OverlayFlushes { get; private set; }

    protected override void FlushDebugDrawCore(SpectraEngine.Core.Scene.Camera camera) => OverlayFlushes++;


    protected override void FlushWorldLinesCore(
        SpectraEngine.Core.Scene.Camera camera, ShaderProgram program, float nudge, GBuffer? gbuffer) { }

    public int FullscreenDraws { get; private set; }

    protected override void DrawFullscreen(PostPass pass, Mesh geometry) => FullscreenDraws++;

    internal override (byte R, byte G, byte B, byte A) ReadTargetPixel(RenderTarget target, int x, int y)
        => throw new NotSupportedException($"{GetType().Name} has no pixels to read.");

    protected override void EndPassCore(RenderTarget? target, ReadOnlySpan<RenderTarget> targets) => OpenPasses--;

    public override RenderTarget CreateRenderTarget(in RenderTargetDesc desc)
    {
        desc.Validate();

        var target = new FakeRenderTarget(desc);
        target.Unregister = () => LiveRenderTargets.Remove(target);
        CreatedRenderTargets.Add(target);
        LiveRenderTargets.Add(target);
        return target;
    }

    public FakeRenderer()
        : base(NullLogger<Renderer>.Instance, new ThrowingShaderCompiler())
    {
        // Material loading reads this as the fallback program.
        DefaultShader = new NoopShaderProgram();
    }

    // Stands in for a backend whose shader compilation failed.
    public void ClearDefaultShader() => DefaultShader = null;

    // Arbitrary: nothing headless branches on the backend.
    public override GraphicsBackend Backend => GraphicsBackend.OpenGL;

    public override string CurrentPipelineName => "Fake";

    public override string NextPipeline() => "Fake";

    public override bool TrySelectPipeline(string name) =>
        string.Equals(name, "Fake", StringComparison.OrdinalIgnoreCase);

    protected override void SetViewportCore(int x, int y, int width, int height) { }

    public override Mesh CreateMesh(ReadOnlySpan<float> vertices, ReadOnlySpan<uint> indices,
        ReadOnlySpan<VertexAttribute> attributes, MeshCpuAccess cpuAccess = MeshCpuAccess.Retained)
    {
        if (CreateMeshBudget <= 0)
            throw new InvalidOperationException("Simulated CreateMesh failure (CreateMeshBudget exhausted).");
        if (CreateMeshBudget != int.MaxValue)
            CreateMeshBudget--;

        var mesh = new FakeMesh(vertices.ToArray(), indices.ToArray(), attributes, cpuAccess);
        mesh.Unregister = () => LiveMeshes.Remove(mesh);
        CreatedMeshes.Add(mesh);
        LiveMeshes.Add(mesh);
        return mesh;
    }

    public override InstanceBuffer CreateInstanceBuffer(
        int capacityInstances, ReadOnlySpan<VertexAttribute> attributes, ShaderProgram program)
        => throw new NotSupportedException("This renderer creates no GPU resources.");

    protected override Texture CreateTextureCore(in TextureUploadDesc desc)
    {
        // Level 0 only: nothing here samples a mip.
        var texture = new FakeTexture(
            desc.Payload.Slice(desc.Mips[0].Offset).ToArray(),
            desc.Width, desc.Height, desc.Format, desc.ColorSpace, desc.Filter, desc.Wrap);
        texture.Unregister = () => LiveTextures.Remove(texture);
        CreatedTextures.Add(texture);
        LiveTextures.Add(texture);
        return texture;
    }

    public override ShaderProgram CreateShader(string vertexSource, string fragmentSource)
        => throw new NotSupportedException("FakeRenderer does not create shaders.");

    public override ShaderProgram CreateShader(PipelineBlob blob)
        => throw new NotSupportedException("FakeRenderer does not create shaders.");

    // Headless tests never compile shaders, so any call is a test defect.
    private sealed class ThrowingShaderCompiler : IShaderCompiler
    {
        public CompiledShaderFile Compile(string source, ReadOnlySpan<GraphicsBackend> targets)
            => throw new NotSupportedException("FakeRenderer does not compile shaders.");
    }
}

// CPU-only Mesh: keeps the raw arrays it was created from and records disposal.
// Positions, normals and LocalBounds are filled like a real backend's mesh.
internal sealed class FakeMesh : Mesh
{
    public float[] VertexData { get; }
    public uint[] IndexData { get; }

    public bool Disposed { get; private set; }

    public FakeMesh(float[] vertices, uint[] indices)
        : this(vertices, indices, VertexAttribute.StandardLayout, MeshCpuAccess.Retained)
    {
    }

    public FakeMesh(
        float[] vertices, uint[] indices, ReadOnlySpan<VertexAttribute> attributes, MeshCpuAccess cpuAccess)
    {
        VertexData = vertices;
        IndexData = indices;
        IndexCount = (uint)indices.Length;

        // Shared base helper, so a MeshCpuAccess.None mesh has no CPU arrays
        // here either. VertexData/IndexData above always stay populated.
        InitializeCpuData(vertices, indices, attributes, cpuAccess);
    }

    public override void Draw()
    {
    }

    public override void DrawInstanced(InstanceBuffer instances, int instanceCount, int firstInstance = 0)
    {
    }

    public override void Dispose() => Disposed = true;
}

// GPU-free RenderTarget. The colour attachment keeps its identity across a
// resize, like the real ones.
internal sealed class FakeRenderTarget : RenderTarget
{
    private readonly FakeTexture _color;
    private readonly FakeTexture? _depth;

    public FakeRenderTarget(in RenderTargetDesc desc)
    {
        Desc = desc;
        Width = desc.Width;
        Height = desc.Height;
        _color = new FakeTexture(
            [], desc.Width, desc.Height, desc.ColorFormat, desc.ColorSpace, desc.Filter, desc.Wrap);
        if (desc.Depth)
        {
            _depth = new FakeTexture(
                [], desc.Width, desc.Height, TextureFormat.Depth32Float, TextureColorSpace.Linear,
                TextureFilter.Nearest, TextureWrap.Clamp);
        }
    }

    public bool Disposed { get; private set; }

    public List<(int Width, int Height)> Resizes { get; } = [];

    public override Texture ColorTexture => _color;

    public override Texture? DepthTexture => _depth;

    public override void Resize(int width, int height)
    {
        if (width == Width && height == Height) return;

        Width = width;
        Height = height;
        _color.ResizeInPlace(width, height);
        _depth?.ResizeInPlace(width, height);
        Resizes.Add((width, height));
    }

    public override void Dispose() => Disposed = true;
}

// CPU-only Texture: keeps the uploaded pixels and sampling state, records disposal.
internal sealed class FakeTexture : Texture
{
    public byte[] Pixels { get; }
    public TextureFilter Filter { get; }
    public TextureWrap Wrap { get; }

    public bool Disposed { get; private set; }

    public FakeTexture(byte[] pixels, int width, int height, TextureFormat format,
        TextureColorSpace colorSpace, TextureFilter filter, TextureWrap wrap)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        Format = format;
        // Same resolve the real backends use.
        ColorSpace = TextureFormatInfo.Resolve(format, colorSpace);
        Filter = filter;
        Wrap = wrap;
    }

    public void ResizeInPlace(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public override void Dispose() => Disposed = true;
}
