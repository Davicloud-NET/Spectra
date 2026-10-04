using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets.Sources;
using System;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// A mounted <c>.spack</c> read out of a memory-mapped view. An uncompressed
/// entry is a span into the file, with no copy. Every blob it hands out holds a
/// <see cref="PackHandle"/> reference.
/// </summary>
public sealed class PackSource : PackSourceBase
{
    private ulong _entryTableOffset;
    private int _entryTableBytes;
    private ulong _nameTableOffset;
    private int _nameTableBytes;
    private bool _tablesLoaded;

    /// <summary>
    /// Mounts <paramref name="packPath"/>, validating it before it can answer
    /// anything.
    /// </summary>
    /// <exception cref="PackMountException">The pack is refused.</exception>
    public PackSource(ILogger logger, string packPath, int priority = PackMountBand.Base)
        : base(logger, packPath, priority, PackHandle.MapWholeFile(packPath))
    {
        try
        {
            Mount();
        }
        catch
        {
            // Nobody can dispose a half-constructed source, so unmap here.
            Handle.RequestUnmount();
            throw;
        }
    }

    /// <inheritdoc/>
    protected override long FileLength => Handle.RegionLength;

    /// <inheritdoc/>
    protected override ReadOnlySpan<PackEntry> Entries =>
        _tablesLoaded ? PackEntryTable.Cast(Handle.Slice(_entryTableOffset, _entryTableBytes)) : default;

    /// <inheritdoc/>
    protected override ReadOnlySpan<byte> NameTable =>
        _tablesLoaded && _nameTableBytes > 0 ? Handle.Slice(_nameTableOffset, _nameTableBytes) : default;

    /// <inheritdoc/>
    protected override void ReadRaw(long offset, Span<byte> destination) =>
        Handle.Slice((ulong)offset, destination.Length).CopyTo(destination);

    /// <inheritdoc/>
    protected override void LoadTables(in PackHeader header)
    {
        _entryTableOffset = header.EntryTableOffset;
        _entryTableBytes = (int)((long)header.EntryCount * PackFormat.EntrySize);

        if (header.HasNameTable)
        {
            _nameTableOffset = header.NameTableOffset;
            _nameTableBytes = (int)header.NameTableLength;
        }

        _tablesLoaded = true;
    }

    /// <inheritdoc/>
    protected override UInt128 ComputeDigest(long offset, long length)
    {
        // Chunked: a pack can be larger than one span can address.
        const int ChunkSize = 1 << 20;

        var accumulator = new PackDigest.Accumulator();
        long remaining = length;
        long cursor = offset;

        while (remaining > 0)
        {
            int chunk = (int)Math.Min(remaining, ChunkSize);
            accumulator.Append(Handle.Slice((ulong)cursor, chunk));
            cursor += chunk;
            remaining -= chunk;
        }

        return accumulator.Finish();
    }

    /// <inheritdoc/>
    protected override bool TryReadPayload(in PackEntry entry, [NotNullWhen(true)] out ContentBlob? blob)
    {
        blob = null;

        // Taken before any read; the blob owns it afterwards.
        if (!Handle.TryAddRef()) return false;

        // Not inferred from blob == null: a failed inflate disposes a blob that
        // already owns the reference, and releasing again would over-release.
        bool referenceHandedOver = false;
        try
        {
            switch (entry.EntryCodec)
            {
                case PackCodec.None:
                    blob = ContentBlob.OverPack(Handle, entry.PayloadOffset, (int)entry.StoredSize);
                    referenceHandedOver = true;
                    return true;

                case PackCodec.Deflate:
                {
                    ContentBlob inflated = ContentBlob.RentUnderPack(
                        Handle, (int)entry.UncompressedSize, out Span<byte> destination);
                    referenceHandedOver = true;

                    try
                    {
                        Inflate(Handle.Slice(entry.PayloadOffset, (int)entry.StoredSize), destination, PackPath);
                    }
                    catch
                    {
                        inflated.Dispose();
                        throw;
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
}
