using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using SpectraEngine.Core.Assets.Sources;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// The same <c>.spack</c>, read through <see cref="RandomAccess"/> instead of a
/// mapping: the fallback for any platform where mapping misbehaves. Must answer
/// the same as <see cref="PackSource"/>.
/// </summary>
// Reads use RandomAccess on the handle, not the stream position: sources are
// read from several threads at once.
public sealed class StreamPackSource : PackSourceBase
{
    private readonly SafeFileHandle _file;
    private readonly long _length;

    private PackEntry[] _entries = [];
    private byte[] _nameTable = [];

    /// <summary>
    /// Mounts <paramref name="packPath"/>, validating it before it can answer
    /// anything.
    /// </summary>
    /// <exception cref="PackMountException">The pack is refused.</exception>
    public StreamPackSource(ILogger logger, string packPath, int priority = PackMountBand.Base)
        : this(logger, packPath, priority, new FileStream(packPath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
    }

    private StreamPackSource(ILogger logger, string packPath, int priority, FileStream stream)
        : base(logger, packPath, priority, PackHandle.OverStorage(packPath, stream, stream.Length))
    {
        _file = stream.SafeFileHandle;
        _length = stream.Length;

        try
        {
            Mount();
        }
        catch
        {
            // Nobody can dispose a half-constructed source, so close the file here.
            Handle.RequestUnmount();
            throw;
        }
    }

    /// <inheritdoc/>
    protected override long FileLength => _length;

    /// <inheritdoc/>
    protected override ReadOnlySpan<PackEntry> Entries => _entries;

    /// <inheritdoc/>
    protected override ReadOnlySpan<byte> NameTable => _nameTable;

    /// <inheritdoc/>
    protected override void ReadRaw(long offset, Span<byte> destination)
    {
        int filled = 0;
        while (filled < destination.Length)
        {
            int read = RandomAccess.Read(_file, destination[filled..], offset + filled);
            if (read == 0)
            {
                throw new EndOfStreamException(
                    $"'{PackPath}' ended after {offset + filled} bytes, with {destination.Length - filled} still " +
                    "to read.");
            }

            filled += read;
        }
    }

    /// <inheritdoc/>
    protected override void LoadTables(in PackHeader header)
    {
        _entries = new PackEntry[header.EntryCount];
        if (_entries.Length > 0)
        {
            ReadRaw((long)header.EntryTableOffset, MemoryMarshal.AsBytes(_entries.AsSpan()));
        }

        if (header.HasNameTable)
        {
            _nameTable = new byte[header.NameTableLength];
            ReadRaw((long)header.NameTableOffset, _nameTable);
        }
    }

    /// <inheritdoc/>
    protected override UInt128 ComputeDigest(long offset, long length)
    {
        const int ChunkSize = 1 << 20;

        var accumulator = new PackDigest.Accumulator();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            long remaining = length;
            long cursor = offset;

            while (remaining > 0)
            {
                int chunk = (int)Math.Min(remaining, ChunkSize);
                Span<byte> window = buffer.AsSpan(0, chunk);
                ReadRaw(cursor, window);
                accumulator.Append(window);
                cursor += chunk;
                remaining -= chunk;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return accumulator.Finish();
    }

    /// <inheritdoc/>
    protected override bool TryReadPayload(in PackEntry entry, [NotNullWhen(true)] out ContentBlob? blob)
    {
        blob = null;
        if (!Handle.TryAddRef()) return false;

        // Not inferred from blob == null: a failed read disposes a blob that
        // already owns the reference, and releasing again would over-release.
        bool referenceHandedOver = false;
        try
        {
            switch (entry.EntryCodec)
            {
                case PackCodec.None:
                {
                    ContentBlob stored = ContentBlob.RentUnderPack(
                        Handle, (int)entry.StoredSize, out Span<byte> destination);
                    referenceHandedOver = true;

                    try
                    {
                        ReadRaw((long)entry.PayloadOffset, destination);
                    }
                    catch
                    {
                        stored.Dispose();
                        throw;
                    }

                    blob = stored;
                    return true;
                }

                case PackCodec.Deflate:
                {
                    ContentBlob inflated = ContentBlob.RentUnderPack(
                        Handle, (int)entry.UncompressedSize, out Span<byte> destination);
                    referenceHandedOver = true;

                    byte[] compressed = ArrayPool<byte>.Shared.Rent((int)entry.StoredSize);
                    try
                    {
                        Span<byte> window = compressed.AsSpan(0, (int)entry.StoredSize);
                        ReadRaw((long)entry.PayloadOffset, window);
                        Inflate(window, destination, PackPath);
                    }
                    catch
                    {
                        inflated.Dispose();
                        throw;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(compressed);
                    }

                    blob = inflated;
                    return true;
                }

                default:
                    Logger.LogWarning(
                        "Entry in {Source} uses codec {Codec}, which this reader does not implement.",
                        this, entry.EntryCodec);
                    return false;
            }
        }
        finally
        {
            if (!referenceHandedOver) Handle.Release();
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"pack (streamed) @ {PackPath}";
}
