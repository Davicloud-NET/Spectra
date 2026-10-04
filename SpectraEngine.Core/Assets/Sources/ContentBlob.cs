using SpectraEngine.Core.Assets.Packs;
using System;
using System.Buffers;
using System.Threading;

namespace SpectraEngine.Core.Assets.Sources;

/// <summary>
/// A block of content bytes handed out by an <see cref="IContentSource"/>, owned
/// by the caller and released with <see cref="Dispose"/>. Single owner: one blob
/// belongs to one thread at a time.
/// </summary>
// The bytes are a pooled array or a window into a mapped pack. A blob from a pack
// holds a PackHandle reference, because unmapping under a live span is an access
// violation and the blob can outlive the call that opened it.
public sealed class ContentBlob : IDisposable
{
    private readonly ulong _offset;
    private byte[]? _buffer;
    private PackHandle? _handle;
    private int _disposed;

    private ContentBlob(byte[]? buffer, PackHandle? handle, ulong offset, int length)
    {
        _buffer = buffer;
        _handle = handle;
        _offset = offset;
        Length = length;
    }

    /// <summary>Number of content bytes. A pooled backing array may be longer.</summary>
    public int Length { get; }

    /// <summary>The content bytes. Valid until <see cref="Dispose"/>.</summary>
    /// <exception cref="ObjectDisposedException">The blob was already disposed.</exception>
    public ReadOnlySpan<byte> Span
    {
        get
        {
            // Checked first: a mapped blob's bytes may already be unmapped.
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(ContentBlob));

            byte[]? buffer = _buffer;
            if (buffer is not null) return buffer.AsSpan(0, Length);

            PackHandle handle = _handle ?? throw new ObjectDisposedException(nameof(ContentBlob));
            return handle.Slice(_offset, Length);
        }
    }

    /// <summary>
    /// Rents a blob of <paramref name="length"/> bytes. The caller must fill
    /// <paramref name="destination"/> completely: a pooled buffer is not cleared.
    /// </summary>
    public static ContentBlob Rent(int length, out Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        destination = buffer.AsSpan(0, length);
        return new ContentBlob(buffer, handle: null, offset: 0, length);
    }

    /// <summary>
    /// Rents a blob holding a copy of <paramref name="bytes"/>.
    /// </summary>
    public static ContentBlob CopyOf(ReadOnlySpan<byte> bytes)
    {
        ContentBlob blob = Rent(bytes.Length, out Span<byte> destination);
        bytes.CopyTo(destination);
        return blob;
    }

    // A window into a mounted pack's mapped view, no copy.
    // The caller must already hold the reference; this blob takes it over and releases it.
    internal static ContentBlob OverPack(PackHandle handle, ulong offset, int length)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        return new ContentBlob(buffer: null, handle, offset, length);
    }

    // A pooled blob a pack reads or inflates into. Takes over the caller's pack
    // reference, which keeps the compressed bytes mapped while they are read.
    internal static ContentBlob RentUnderPack(PackHandle handle, int length, out Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        destination = buffer.AsSpan(0, length);
        return new ContentBlob(buffer, handle, offset: 0, length);
    }

    /// <summary>Releases the backing storage and any pack reference. Idempotent.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        byte[]? buffer = _buffer;
        _buffer = null;
        if (buffer is not null) ArrayPool<byte>.Shared.Return(buffer);

        PackHandle? handle = _handle;
        _handle = null;
        handle?.Release();
    }
}
