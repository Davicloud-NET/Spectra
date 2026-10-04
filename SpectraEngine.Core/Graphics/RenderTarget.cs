using System;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// How a render target's colour attachment is shared with something outside
/// this renderer.
/// </summary>
public enum RenderTargetSharing
{
    /// <summary>Not shared.</summary>
    None,

    /// <summary>
    /// Shared with a keyed mutex: the producer acquires key 0 and releases key
    /// 1, the consumer acquires key 1 and releases key 0. See
    /// <see cref="Renderer.BeginSharedWrite"/>.
    /// </summary>
    KeyedMutex,
}

/// <summary>
/// What an offscreen render target is made of. Depth, when attached, can be
/// sampled with a plain <c>sampler2D</c>, which returns it in <c>.r</c>.
/// </summary>
/// <param name="ColorFormat">
/// <see cref="TextureFormat.Rgba8"/> for a display-ready image,
/// <see cref="TextureFormat.Rgba16Float"/> for linear light between passes.
/// </param>
/// <param name="ColorSpace">Whether the colour attachment encodes on write and decodes on read.</param>
/// <param name="Filter">How the colour attachment samples. Targets get no mipmaps.</param>
/// <param name="Color">False makes a depth-only target; see <see cref="DepthOnly"/>.</param>
public readonly record struct RenderTargetDesc(
    int Width,
    int Height,
    TextureFormat ColorFormat = TextureFormat.Rgba8,
    TextureColorSpace ColorSpace = TextureColorSpace.Linear,
    bool Depth = true,
    TextureFilter Filter = TextureFilter.Linear,
    TextureWrap Wrap = TextureWrap.Clamp,
    bool Color = true,
    RenderTargetSharing Sharing = RenderTargetSharing.None)
{
    /// <summary>
    /// A target with depth and no colour, such as a shadow map. The depth
    /// attachment samples nearest.
    /// </summary>
    public static RenderTargetDesc DepthOnly(int size) => DepthOnly(size, size);

    /// <inheritdoc cref="DepthOnly(int)"/>
    public static RenderTargetDesc DepthOnly(int width, int height) => new(
        width, height, TextureFormat.Rgba8, TextureColorSpace.Linear,
        Depth: true, TextureFilter.Linear, TextureWrap.Clamp, Color: false);

    /// <summary>Throws if this description cannot be built.</summary>
    public void Validate()
    {
        if (Width <= 0 || Height <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(Width), $"A render target needs a positive size; got {Width}x{Height}.");

        if (!Color && !Depth)
            throw new ArgumentException(
                "A render target needs at least one attachment; both Color and Depth are false.",
                nameof(Color));

        // D3D has no 24-bit render target format, and nothing needs R8.
        if (Color && ColorFormat is not (TextureFormat.Rgba8 or TextureFormat.Rgba16Float))
            throw new ArgumentOutOfRangeException(
                nameof(ColorFormat),
                $"Render targets support {nameof(TextureFormat.Rgba8)} and " +
                $"{nameof(TextureFormat.Rgba16Float)}; got {ColorFormat}.");

        if (Sharing == RenderTargetSharing.None)
            return;

        if (!Color)
            throw new ArgumentException(
                "A shared render target needs a colour attachment; a depth-only target has nothing to share.",
                nameof(Sharing));

        // Refuse here; the driver's HRESULT would name neither format nor target.
        if (ColorFormat != TextureFormat.Rgba8)
            throw new ArgumentOutOfRangeException(
                nameof(ColorFormat),
                $"A shared render target must be {nameof(TextureFormat.Rgba8)}; got {ColorFormat}. " +
                "The import that consumes a shared handle has no half-float format.");
    }
}

/// <summary>
/// An offscreen surface a pass can draw into. Its colour attachment is an
/// ordinary <see cref="Texture"/> that materials can sample. Owned by the
/// renderer that created it; render thread only.
/// </summary>
// A shared target (RenderTargetSharing) is never resized in place. It is
// recreated under a new SharedTargetHandle.Generation, so the consumer never
// samples a destroyed resource.
public abstract class RenderTarget : IDisposable
{
    /// <summary>Current pixel width.</summary>
    public int Width { get; protected set; }

    /// <summary>Current pixel height.</summary>
    public int Height { get; protected set; }

    /// <summary>What this target was created from. Its size is the original one, not the current.</summary>
    public RenderTargetDesc Desc { get; protected set; }

    /// <summary>
    /// The colour attachment, or null on a depth-only target. The same object
    /// before and after <see cref="Resize"/>.
    /// </summary>
    public abstract Texture? ColorTexture { get; }

    /// <summary>
    /// The depth attachment as a sampleable texture, or null without depth.
    /// The same object before and after <see cref="Resize"/>.
    /// </summary>
    public abstract Texture? DepthTexture { get; }

    /// <summary>Resizes in place. The attachment textures stay the same objects.</summary>
    public abstract void Resize(int width, int height);

    // Removes this target from the creating renderer's tracking list.
    internal Action? Unregister { get; set; }

    public abstract void Dispose();
}
