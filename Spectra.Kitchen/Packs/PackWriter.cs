using SpectraEngine.Core;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Packs;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Spectra.Kitchen.Packs;

/// <summary>
/// Writes a <c>.spack</c> container. Two writes of the same entries give
/// byte-identical files, except that <see cref="PackCodec.Deflate"/> output is
/// only stable within one runtime version.
/// </summary>
public sealed class PackWriter : IDisposable
{
    // Insertion order. Write sorts a copy so it can be called twice.
    private readonly List<PendingEntry> _entries = [];
    private readonly List<string> _temporaryPayloads = [];

    private readonly bool _includeNameTable;
    private readonly uint _packSequence;
    private readonly PackFlags _bandFlags;

    /// <summary>Creates a writer.</summary>
    /// <param name="packSequence">Ordering key among patch packs, which decides mount order.</param>
    /// <param name="includeNameTable">Emit the name table, so tools can show paths instead of ids.</param>
    /// <param name="bandFlags">
    /// <see cref="PackFlags.IsPatchPack"/> or <see cref="PackFlags.IsModPack"/>,
    /// or none for a base pack.
    /// </param>
    public PackWriter(
        uint packSequence = 0,
        bool includeNameTable = true,
        PackFlags bandFlags = PackFlags.None)
    {
        const PackFlags Allowed = PackFlags.IsPatchPack | PackFlags.IsModPack;
        if ((bandFlags & ~Allowed) != 0)
        {
            throw new ArgumentException(
                $"Only {nameof(PackFlags.IsPatchPack)} and {nameof(PackFlags.IsModPack)} may be set by a caller; " +
                $"'{bandFlags}' also names a flag the writer derives from what it wrote.", nameof(bandFlags));
        }

        if (bandFlags == Allowed)
        {
            throw new ArgumentException(
                "A pack is a patch or a mod, not both: the two name different mount bands.", nameof(bandFlags));
        }

        _packSequence = packSequence;
        _includeNameTable = includeNameTable;
        _bandFlags = bandFlags;
    }

    /// <summary>Number of entries added so far, tombstones included.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Adds one asset. <paramref name="contentPath"/> is normalised and becomes the
    /// entry's name and, hashed, its id. The payload is copied and compressed here.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The path is empty, rooted or escapes the content root, or the kind is
    /// <see cref="PackEntryKind.Tombstone"/>.
    /// </exception>
    public void Add(
        string contentPath,
        PackEntryKind kind,
        ReadOnlySpan<byte> payload,
        PackCodec codec = PackCodec.None)
    {
        if (kind == PackEntryKind.Tombstone)
        {
            throw new ArgumentException(
                $"A tombstone carries no payload; use {nameof(AddTombstone)}.", nameof(kind));
        }

        string normalized = ContentRoot.NormalizeRelativePath(contentPath);
        byte[] stored = Compress(payload, codec, normalized);

        _entries.Add(new PendingEntry(
            normalized,
            PackAssetId.FromNormalized(normalized),
            kind,
            codec,
            PackPayload.FromBytes(stored),
            (ulong)payload.Length));
    }

    /// <summary>Adds an immutable file payload without loading it into an array.</summary>
    public void AddFile(string contentPath, PackEntryKind kind, string path, PackCodec codec = PackCodec.None) =>
        Add(contentPath, kind, PackPayload.FromFile(path), codec);

    public void Add(string contentPath, PackEntryKind kind, PackPayload payload, PackCodec codec = PackCodec.None)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (kind == PackEntryKind.Tombstone) throw new ArgumentException("Use AddTombstone for a deletion.", nameof(kind));
        string normalized = ContentRoot.NormalizeRelativePath(contentPath);
        PackPayload stored = payload;
        if (codec == PackCodec.Deflate)
        {
            string temporary = Path.Combine(Path.GetTempPath(), "spectra-deflate-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var output = File.Create(temporary))
                using (var compressor = new DeflateStream(output, CompressionLevel.Optimal)) payload.CopyTo(compressor);
                stored = PackPayload.FromFile(temporary);
                _temporaryPayloads.Add(temporary);
            }
            catch { File.Delete(temporary); throw; }
        }
        else if (codec != PackCodec.None)
            throw new NotSupportedException($"Pack codec {codec} is not implemented for '{normalized}'.");
        _entries.Add(new(normalized, PackAssetId.FromNormalized(normalized), kind, codec, stored, (ulong)payload.Length));
    }

    /// <summary>
    /// Adds a deletion, so a higher-priority pack can remove content a
    /// lower-priority one shipped.
    /// </summary>
    public void AddTombstone(string contentPath)
    {
        string normalized = ContentRoot.NormalizeRelativePath(contentPath);

        _entries.Add(new PendingEntry(
            normalized,
            PackAssetId.FromNormalized(normalized),
            PackEntryKind.Tombstone,
            PackCodec.None,
            PackPayload.FromBytes([]),
            0));
    }

    /// <summary>Writes the pack to <paramref name="path"/>.</summary>
    public void WriteToFile(string path)
    {
        WriteToFile(path, CancellationToken.None);
    }

    public void WriteToFile(string path, CancellationToken cancellationToken)
    {
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (FileStream stream = File.Create(temporary))
            { Write(stream, cancellationToken); stream.Flush(flushToDisk: true); }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    /// <summary>Writes the pack. A second call produces byte-identical output.</summary>
    /// <exception cref="PlatformNotSupportedException">The machine is big-endian.</exception>
    /// <exception cref="InvalidOperationException">
    /// Two entries share an asset id, or the pack exceeds a field's range.
    /// </exception>
    public void Write(Stream stream) => Write(stream, CancellationToken.None);

    public void Write(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        PackFormat.RequireLittleEndian();

        PendingEntry[] sorted = SortAndCheckForCollisions();
        byte[] nameTable = _includeNameTable ? BuildNameTable(sorted) : [];

        // Layout first: the entry table holds payload offsets and is written before them.
        long entryTableOffset = PackFormat.HeaderSize;
        long entryTableEnd = entryTableOffset + ((long)sorted.Length * PackFormat.EntrySize);

        bool hasNameTable = nameTable.Length > 0;
        long nameTableOffset = hasNameTable ? entryTableEnd : 0;
        long tablesEnd = hasNameTable ? nameTableOffset + nameTable.Length : entryTableEnd;

        long dataSectionOffset = PackFormat.AlignUp(tablesEnd, PackFormat.DataSectionAlignment);

        var payloadOffsets = new long[sorted.Length];
        long cursor = dataSectionOffset;
        for (int i = 0; i < sorted.Length; i++)
        {
            payloadOffsets[i] = cursor;
            cursor = PackFormat.AlignUp(cursor + sorted[i].Stored.Length, PackFormat.PayloadAlignment);
        }

        long totalFileSize = cursor + PackFormat.DigestSize;

        PackFlags flags = PackFlags.EntriesSortedByAssetId | _bandFlags;
        if (hasNameTable) flags |= PackFlags.NameTablePresent;

        var header = new PackHeader(
            PackFormat.Magic,
            EngineInfo.PackFormatVersion,
            EngineInfo.MinimumReadablePackVersion,
            flags,
            (uint)sorted.Length,
            (ulong)entryTableOffset,
            (ulong)nameTableOffset,
            (ulong)nameTable.Length,
            _packSequence,
            EngineVersionWord,
            (ulong)dataSectionOffset,
            (ulong)totalFileSize);

        // The header is not covered by the digest.
        Span<byte> headerBytes = stackalloc byte[PackFormat.HeaderSize];
        MemoryMarshal.Write(headerBytes, in header);
        stream.Write(headerBytes);

        var region = new RegionWriter(stream, entryTableOffset);

        Span<byte> entryBytes = stackalloc byte[PackFormat.EntrySize];
        uint nameCursor = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            PendingEntry pending = sorted[i];
            uint nameOffset = PackFormat.NameOffsetAbsent;
            ushort nameLength = 0;

            if (hasNameTable)
            {
                nameOffset = nameCursor;
                nameLength = (ushort)Encoding.UTF8.GetByteCount(pending.Path);

                // Cannot overflow: BuildNameTable already checked the total.
                nameCursor += (uint)(sizeof(ushort) + nameLength);
            }

            var entry = new PackEntry(
                pending.AssetId,
                (ulong)payloadOffsets[i],
                (ulong)pending.Stored.Length,
                pending.UncompressedSize,
                nameOffset,
                nameLength,
                pending.Kind,
                pending.Codec);

            MemoryMarshal.Write(entryBytes, in entry);
            region.Write(entryBytes);
        }

        if (hasNameTable) region.Write(nameTable);

        // Written, not seeked over: the digest covers the gap, and a seek does
        // not guarantee zeros.
        region.WriteZeros(dataSectionOffset - region.Position);

        for (int i = 0; i < sorted.Length; i++)
        {
            if (region.Position != payloadOffsets[i])
            {
                throw new InvalidOperationException(
                    $"Pack layout disagrees with what was written: entry {i} was placed at " +
                    $"{payloadOffsets[i]} but the writer is at {region.Position}.");
            }

            sorted[i].Stored.CopyTo(region, cancellationToken);
            region.WriteZeros(PackFormat.AlignUp(region.Position, PackFormat.PayloadAlignment) - region.Position);
        }

        if (region.Position + PackFormat.DigestSize != totalFileSize)
        {
            throw new InvalidOperationException(
                $"Pack layout disagrees with what was written: the header declares {totalFileSize} bytes " +
                $"but the writer ended at {region.Position + PackFormat.DigestSize}.");
        }

        Span<byte> digest = stackalloc byte[PackFormat.DigestSize];
        PackDigest.Write(digest, region.Digest());
        stream.Write(digest);
    }

    private static uint EngineVersionWord =>
        ((uint)EngineInfo.MajorVersion << 20) | ((uint)EngineInfo.MinorVersion << 10) | EngineInfo.RevisionVersion;

    // Ids are unique (a tie throws below), so the unstable sort cannot affect the output.
    private PendingEntry[] SortAndCheckForCollisions()
    {
        PendingEntry[] sorted = [.. _entries];
        Array.Sort(sorted, static (a, b) => a.AssetId.CompareTo(b.AssetId));

        for (int i = 1; i < sorted.Length; i++)
        {
            if (sorted[i].AssetId != sorted[i - 1].AssetId) continue;

            string first = sorted[i - 1].Path;
            string second = sorted[i].Path;

            // Ids are case-insensitive, so two spellings of one path collide here.
            throw new InvalidOperationException(string.Equals(first, second, StringComparison.Ordinal)
                ? $"Asset '{first}' was added to the pack twice (id {sorted[i].AssetId:X32})."
                : $"Asset id collision: '{first}' and '{second}' both resolve to id {sorted[i].AssetId:X32}. " +
                  "Pack identity is case-insensitive, matching the engine's asset caches, so two paths " +
                  "differing only in case are one asset; rename one of them.");
        }

        return sorted;
    }

    // Records in entry-table order: u16 UTF-8 byte count, then the bytes, no
    // terminator. An entry's NameOffset points at the count.
    private static byte[] BuildNameTable(PendingEntry[] sorted)
    {
        if (sorted.Length == 0) return [];

        long length = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            int nameBytes = Encoding.UTF8.GetByteCount(sorted[i].Path);
            if (nameBytes > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Asset path '{sorted[i].Path}' is {nameBytes} UTF-8 bytes, past the {ushort.MaxValue} " +
                    "an entry's name length can hold.");
            }

            length += sizeof(ushort) + nameBytes;
        }

        // NameOffsetAbsent is the largest u32, so it is not available as an offset.
        if (length > PackFormat.NameOffsetAbsent - 1)
        {
            throw new InvalidOperationException(
                $"The name table would be {length} bytes, past what a 32-bit name offset can address.");
        }

        var table = new byte[length];
        Span<byte> remaining = table;
        for (int i = 0; i < sorted.Length; i++)
        {
            int written = Encoding.UTF8.GetBytes(sorted[i].Path, remaining[sizeof(ushort)..]);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(remaining, (ushort)written);
            remaining = remaining[(sizeof(ushort) + written)..];
        }

        return table;
    }

    private static byte[] Compress(ReadOnlySpan<byte> payload, PackCodec codec, string path)
    {
        switch (codec)
        {
            case PackCodec.None:
                return payload.ToArray();

            case PackCodec.Deflate:
            {
                var buffer = new MemoryStream();
                using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
                {
                    deflate.Write(payload);
                }

                // No store-raw fallback when deflate grows the data: the caller chose the codec.
                return buffer.ToArray();
            }

            case PackCodec.Zstandard:
                throw new NotSupportedException(
                    $"Codec {codec} is reserved and not implemented: Zstandard ships in-box in .NET 11 and this " +
                    $"solution targets .NET 10. Entry '{path}' must use None or Deflate.");

            default:
                throw new ArgumentOutOfRangeException(nameof(codec), codec, $"Unknown pack codec for entry '{path}'.");
        }
    }

    private readonly record struct PendingEntry(
        string Path,
        UInt128 AssetId,
        PackEntryKind Kind,
        PackCodec Codec,
        PackPayload Stored,
        ulong UncompressedSize);

    // Everything past the header goes through here, so it is always hashed.
    private sealed class RegionWriter(Stream stream, long startOffset) : Stream
    {
        private readonly PackDigest.Accumulator _digest = new();

        private long _position = startOffset;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _position;
        public override void Flush() => stream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> bytes)
        {
            stream.Write(bytes);
            _digest.Append(bytes);
            _position += bytes.Length;
        }

        public void WriteZeros(long count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            Span<byte> zeros = stackalloc byte[PackFormat.DataSectionAlignment];
            zeros.Clear();

            while (count > 0)
            {
                int chunk = (int)Math.Min(count, zeros.Length);
                Write(zeros[..chunk]);
                count -= chunk;
            }
        }

        public UInt128 Digest() => _digest.Finish();
    }

    public void Dispose()
    {
        foreach (string path in _temporaryPayloads) File.Delete(path);
        _temporaryPayloads.Clear();
    }
}
