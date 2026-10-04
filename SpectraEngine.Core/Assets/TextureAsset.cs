using SpectraEngine.Core.Graphics;
using System.Threading;

namespace SpectraEngine.Core.Assets;

/// <summary>
/// A stable handle to a texture asset. The <see cref="Texture"/> behind it
/// is swapped when an async load lands or a hot-reload runs, so bind the
/// handle into a <see cref="Material"/>, not the texture.
/// </summary>
public sealed class TextureAsset
{
    // Ticket per decode request. The pump drops an upload older than the one
    // already applied, so a reload wins over a slower earlier decode.
    private long _requestSequence;

    internal TextureAsset(
        string relativePath,
        string sourcePath,
        TextureFilter filter,
        TextureWrap wrap,
        TextureColorSpace colorSpace,
        Texture initialTexture,
        bool isPlaceholder)
    {
        RelativePath = relativePath;
        SourcePath = sourcePath;
        Filter = filter;
        Wrap = wrap;
        ColorSpace = colorSpace;
        Texture = initialTexture;
        IsPlaceholder = isPlaceholder;
    }

    /// <summary>Normalised content-root-relative path this asset was loaded from. Any thread.</summary>
    public string RelativePath { get; }

    /// <summary>
    /// Absolute path under the content root. Any thread. In a packed build no
    /// file need exist there.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>Sampling filter requested at load time. Any thread.</summary>
    public TextureFilter Filter { get; }

    /// <summary>Wrap mode requested at load time. Any thread.</summary>
    public TextureWrap Wrap { get; }

    /// <summary>
    /// Colour space requested at load time, part of the cache key. Any thread.
    /// The bound texture's <see cref="Texture.ColorSpace"/> is what it resolved to.
    /// </summary>
    public TextureColorSpace ColorSpace { get; }

    /// <summary>
    /// The texture to bind now: the placeholder while a load is in flight,
    /// the real one afterwards. Render thread only.
    /// </summary>
    public Texture Texture { get; internal set; }

    /// <summary>
    /// True while <see cref="Texture"/> is still the placeholder (load pending,
    /// or failed). Render thread only.
    /// </summary>
    public bool IsPlaceholder { get; internal set; }

    /// <summary>
    /// Increments on every swap, so cached per-texture state can notice a
    /// hot-reload. Render thread only.
    /// </summary>
    public int Version { get; internal set; }

    /// <summary>
    /// True when the last decode failed. A later request or load retries
    /// into this same handle. Render thread only.
    /// </summary>
    public bool LoadFailed { get; internal set; }

    internal long AppliedSequence { get; set; }

    // Decodes queued but not yet pumped. Guarded by AssetManager's texture lock.
    internal int PendingDecodes { get; set; }

    // Any thread.
    internal long NextRequestSequence() => Interlocked.Increment(ref _requestSequence);
    internal long RequestSequence => Interlocked.Read(ref _requestSequence);
}
