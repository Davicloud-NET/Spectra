using System;

namespace SpectraEngine.Core.Graphics;

/// <summary>A 2D texture. Owned by the renderer that created it.</summary>
public abstract class Texture : IDisposable
{
    internal virtual void WriteUploadRows(int level, TextureMipDesc mip, int firstRow, int rowCount, ReadOnlySpan<byte> bytes) =>
        throw new NotSupportedException("This texture has no resumable upload path.");
    internal virtual void FinishUpload(bool generateMips) { }
    internal virtual int UploadRowPitch(TextureMipDesc mip) => TextureFormatInfo.TightRowPitch(Format, mip.Width);
    public int Width { get; protected set; }
    public int Height { get; protected set; }
    public TextureFormat Format { get; protected set; }

    /// <summary>
    /// The colour space the sampler uses, after resolving. A format with no sRGB
    /// variant reports <see cref="TextureColorSpace.Linear"/> whatever was requested.
    /// </summary>
    public TextureColorSpace ColorSpace { get; protected set; }

    // Removes this texture from the renderer's tracking list. Render thread only.
    internal Action? Unregister { get; set; }

    public abstract void Dispose();
}
