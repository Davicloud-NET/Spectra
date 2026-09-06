using System;

namespace SpectraEngine.Core.Graphics;

/// <summary>Unpublished texture storage populated by aligned row/mip steps.</summary>
public sealed class TextureUpload : IDisposable
{
    private readonly Renderer _renderer;
    private Texture? _texture;
    private readonly TextureMipDesc[] _mips;
    private readonly TextureFormat _format;
    private readonly bool _generateMips;
    private byte[]? _prepared;
    private int _level, _row;
    public bool IsComplete => _level == _mips.Length;
    internal TextureUpload(Renderer renderer, Texture texture, in TextureUploadDesc desc, bool uploaded = false, byte[]? prepared = null)
    {
        _renderer = renderer; _texture = texture; _mips = desc.Mips.ToArray(); _format = desc.Format;
        _generateMips = desc.Filter == TextureFilter.LinearMipmap && !desc.HasSuppliedMipChain && !TextureFormatInfo.IsBlockCompressed(desc.Format);
        _prepared = prepared;
        if (uploaded) _level = _mips.Length;
    }
    /// <summary>Copies at most maxBytes, except when one required row exceeds it. Payload ownership stays with the caller.</summary>
    public int Step(ReadOnlySpan<byte> payload, int maxBytes = 256 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        maxBytes = Math.Min(maxBytes, 256 * 1024);
        ObjectDisposedException.ThrowIf(_texture is null, this);
        if (IsComplete) return 0;
        if (_prepared is not null) payload = _prepared;
        var mip = _mips[_level];
        int pitch = TextureFormatInfo.TightRowPitch(_format, mip.Width);
        int uploadPitch = _texture.UploadRowPitch(mip);
        int remainingRows = TextureFormatInfo.RowCount(_format, mip.Height) - _row;
        int count = Math.Min(remainingRows, Math.Max(1, maxBytes / uploadPitch));
        var rows = payload.Slice(checked(mip.Offset + _row * mip.RowPitch), checked((count - 1) * mip.RowPitch + pitch));
        _texture.WriteUploadRows(_level, mip, _row, count, rows);
        _row += count;
        if (_row == TextureFormatInfo.RowCount(_format, mip.Height)) { _level++; _row = 0; }
        if (IsComplete) _texture.FinishUpload(_generateMips);
        return checked(count * uploadPitch);
    }
    public Texture Complete()
    {
        if (!IsComplete) throw new InvalidOperationException("The texture upload is incomplete.");
        ObjectDisposedException.ThrowIf(_texture is null, this);
        Texture texture = _texture; _texture = null; _prepared = null;
        return texture;
    }
    public void Dispose()
    {
        if (_texture is { } texture) _renderer.DestroyTexture(texture);
        _texture = null; _prepared = null;
    }
}
