using System;

namespace SpectraEngine.Core.Graphics;

public enum GBufferLayout { Standard, Extended }

/// <summary>
/// The surfaces a deferred geometry pass writes and the light pass reads.
/// The extended layout adds a fifth, custom attachment.
/// </summary>
// The layout is a contract with the geometry and light shaders. Change a
// channel and both must change.
//
// RT0  RGBA8 sRGB   albedo.rgb        ambient occlusion.a
// RT1  RGBA16F      normal.rgb        roughness.a
// RT2  RGBA8        metallic.r        shadingModel.g       (spare .ba)
// RT3  RGBA16F      emissive.rgb      (spare .a)
// RT4  RGBA16F      custom.rgba       per shading model
// depth R32 typeless, sampled         world position, by reconstruction
public sealed class GBuffer : IDisposable
{
    /// <summary>Colour attachments in the extended layout.</summary>
    public const int AttachmentCount = 5;

    private readonly Renderer _renderer;
    private readonly RenderTarget[] _targets;
    private bool _disposed;

    /// <summary>Creates the whole set at one size. Render thread.</summary>
    public GBuffer(Renderer renderer, int width, int height)
        : this(renderer, width, height, GBufferLayout.Extended) { }

    public GBuffer(Renderer renderer, int width, int height, GBufferLayout layout)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (layout is not (GBufferLayout.Standard or GBufferLayout.Extended)) throw new ArgumentOutOfRangeException(nameof(layout));
        Layout = layout;
        _targets = new RenderTarget[layout == GBufferLayout.Standard ? 4 : AttachmentCount];
        _renderer = renderer;

        // Only the first carries depth; the pass shares it.
        _targets[0] = renderer.CreateRenderTarget(new RenderTargetDesc(
            width, height, TextureFormat.Rgba8, TextureColorSpace.Srgb, Depth: true));
        _targets[1] = renderer.CreateRenderTarget(new RenderTargetDesc(
            width, height, TextureFormat.Rgba16Float, TextureColorSpace.Linear, Depth: false));
        _targets[2] = renderer.CreateRenderTarget(new RenderTargetDesc(
            width, height, TextureFormat.Rgba8, TextureColorSpace.Linear, Depth: false));
        _targets[3] = renderer.CreateRenderTarget(new RenderTargetDesc(
            width, height, TextureFormat.Rgba16Float, TextureColorSpace.Linear, Depth: false));
        if (layout == GBufferLayout.Extended)
            _targets[4] = renderer.CreateRenderTarget(new RenderTargetDesc(
                width, height, TextureFormat.Rgba16Float, TextureColorSpace.Linear, Depth: false));

        Width = width;
        Height = height;
    }

    public int Width { get; private set; }

    public int Height { get; private set; }
    public GBufferLayout Layout { get; }

    /// <summary>The attachments, in binding order. Pass this to <c>BeginPass</c>.</summary>
    public ReadOnlySpan<RenderTarget> Targets => _targets;

    /// <summary>Albedo in rgb, ambient occlusion in a.</summary>
    public Texture Albedo => _targets[0].ColorTexture!;

    /// <summary>World normal in rgb, roughness in a.</summary>
    public Texture NormalRoughness => _targets[1].ColorTexture!;

    /// <summary>Metallic in r, shading-model id in g.</summary>
    public Texture MaterialData => _targets[2].ColorTexture!;

    /// <summary>Emissive radiance in rgb.</summary>
    public Texture Emissive => _targets[3].ColorTexture!;

    /// <summary>Per shading model data. Extended layout only.</summary>
    public Texture Custom => Layout == GBufferLayout.Extended ? _targets[4].ColorTexture!
        : throw new InvalidOperationException("The standard G-buffer has no custom attachment. Create an extended layout to use it.");

    /// <summary>Depth, for reconstructing world position.</summary>
    public Texture Depth => _targets[0].DepthTexture!;

    /// <summary>Resizes every attachment together. Free when the size is unchanged.</summary>
    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width == Width && height == Height) return;

        foreach (RenderTarget target in _targets)
            target.Resize(width, height);

        Width = width;
        Height = height;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (RenderTarget target in _targets)
            _renderer.DestroyRenderTarget(target);
    }
}
