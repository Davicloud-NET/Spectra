using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets.Sources;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// What a <c>.spack</c> reader does regardless of where the bytes come from:
/// mount-time validation, id lookup, the tombstone rule and enumeration.
/// Mounting throws; every lookup after it reports a miss instead.
/// </summary>
// All validation happens at mount. The per-entry bounds pass there is what lets
// a later read be a bare slice.
public abstract class PackSourceBase : IContentSource, IMountPathSource, IDisposable
{
    private readonly ILogger _logger;
    private bool _disposed;

    /// <summary>
    /// Creates the source over <paramref name="handle"/>, which it owns. The derived
    /// constructor must call <see cref="Mount"/>, and unmount the handle if that throws.
    /// </summary>
    protected PackSourceBase(ILogger logger, string packPath, int priority, PackHandle handle)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(packPath);
        ArgumentNullException.ThrowIfNull(handle);

        _logger = logger;
        PackPath = packPath;
        Priority = priority;
        Handle = handle;
    }

    /// <summary>Path of the mounted file, as it was given.</summary>
    public string PackPath { get; }

    /// <inheritdoc/>
    public int Priority { get; }

    /// <summary>
    /// The pack's refcounted lifetime. Anything holding a span into this pack
    /// must hold a reference.
    /// </summary>
    public PackHandle Handle { get; }

    /// <summary>The validated header, as it sits on disk.</summary>
    public PackHeader Header { get; private set; }

    /// <summary>Records in the entry table, tombstones included.</summary>
    // Stored, not read off Entries: an unmount may already have taken the view away.
    public int EntryCount { get; private set; }

    /// <summary>Number of tombstone records.</summary>
    public int TombstoneCount { get; private set; }

    /// <summary>Whether <see cref="Dispose"/> has been called.</summary>
    public bool IsUnmounted => _disposed;

    /// <summary>The logger for warnings.</summary>
    protected ILogger Logger => _logger;

    /// <summary>Bytes in the file.</summary>
    protected abstract long FileLength { get; }

    /// <summary>The entry table, in place. Empty until <see cref="LoadTables"/> has run.</summary>
    protected abstract ReadOnlySpan<PackEntry> Entries { get; }

    /// <summary>The name table, in place. Empty when the pack carries none.</summary>
    protected abstract ReadOnlySpan<byte> NameTable { get; }

    /// <summary>Copies <paramref name="destination"/>.Length bytes from <paramref name="offset"/>.</summary>
    protected abstract void ReadRaw(long offset, Span<byte> destination);

    /// <summary>
    /// Makes <see cref="Entries"/> and <see cref="NameTable"/> available. The
    /// regions are already validated to be inside the file.
    /// </summary>
    protected abstract void LoadTables(in PackHeader header);

    /// <summary>The digest of one region, which may be larger than a span can address.</summary>
    protected abstract UInt128 ComputeDigest(long offset, long length);

    /// <summary>Opens one entry's payload, decompressing it when the codec says to.</summary>
    protected abstract bool TryReadPayload(in PackEntry entry, [NotNullWhen(true)] out ContentBlob? blob);

    /// <summary>
    /// Validates the file and makes it ready for lookups.
    /// </summary>
    /// <exception cref="PackMountException">The pack is refused.</exception>
    // Order matters: header, then regions, then tables, then entries.
    // The digest is last because it reads the whole file.
    protected void Mount()
    {
        PackFormat.RequireLittleEndian();

        long length = FileLength;
        PackFormat.RequireMinimumFileSize(PackPath, length);

        Span<byte> headerBytes = stackalloc byte[PackFormat.HeaderSize];
        ReadRaw(0, headerBytes);
        PackHeader header = MemoryMarshal.Read<PackHeader>(headerBytes);

        ValidateHeader(in header, length);
        ValidateRegions(in header, length);

        LoadTables(in header);
        Header = header;

        TombstoneCount = ValidateEntries(in header, length);
        EntryCount = (int)header.EntryCount;
        ValidateDigest(in header, length);

        _logger.LogInformation(
            "Mounted pack {Pack} [priority {Priority}]: {Entries} entries ({Tombstones} tombstones), " +
            "format v{Format}, {Bytes} bytes, digest verified.",
            PackPath, Priority, EntryCount, TombstoneCount, header.FormatVersion, length);
    }

    /// <inheritdoc/>
    public bool TryOpen(string path, [NotNullWhen(true)] out ContentBlob? blob)
    {
        blob = null;
        if (!TryFindLive(path, out PackEntry entry) || entry.IsTombstone) return false;

        try
        {
            return TryReadPayload(in entry, out blob);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException)
        {
            // Unreadable is treated as a miss, like every other source. The log
            // line is the only record of the difference.
            _logger.LogWarning("Could not read content '{Path}' from {Source}: {Message}", path, this, ex.Message);
            blob = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public bool Exists(string path) => TryFindLive(path, out PackEntry entry) && !entry.IsTombstone;

    /// <summary>
    /// Whether the pack carries a tombstone for <paramref name="path"/>.
    /// </summary>
    public bool IsTombstone(string path) => TryFindLive(path, out PackEntry entry) && entry.IsTombstone;

    /// <inheritdoc/>
    // Always false: a pack has no file to watch.
    public bool TryGetWatchPath(string path, [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = null;
        return false;
    }

    /// <inheritdoc/>
    public void TryEnumerate(string prefix, string extension, List<string> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (_disposed) return;

        string? normalizedPrefix = NormalizePrefix(prefix);
        ReadOnlySpan<PackEntry> entries = Entries;
        ReadOnlySpan<byte> names = NameTable;

        for (int i = 0; i < entries.Length; i++)
        {
            ref readonly PackEntry entry = ref entries[i];
            if (entry.IsTombstone || !entry.HasName) continue;

            string name = PackEntryTable.ReadName(names, in entry);
            if (!Matches(name, normalizedPrefix, extension)) continue;

            results.Add(name);
        }
    }

    /// <inheritdoc/>
    public void EnumerateMountPaths(List<MountPath> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (_disposed) return;

        ReadOnlySpan<PackEntry> entries = Entries;
        ReadOnlySpan<byte> names = NameTable;

        for (int i = 0; i < entries.Length; i++)
        {
            ref readonly PackEntry entry = ref entries[i];
            if (!entry.HasName) continue;

            results.Add(new MountPath(PackEntryTable.ReadName(names, in entry), entry.IsTombstone));
        }
    }

    /// <summary>
    /// Unmounts the pack. The mapping survives until the last blob holding a
    /// reference is disposed, which may be after this returns.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        Handle.RequestUnmount();
    }

    /// <inheritdoc/>
    public override string ToString() => $"pack @ {PackPath}";

    /// <summary>
    /// Inflates <paramref name="stored"/> into <paramref name="destination"/>,
    /// which must be the entry's uncompressed size.
    /// </summary>
    protected static void Inflate(ReadOnlySpan<byte> stored, Span<byte> destination, string what)
    {
        // DeflateStream needs a Stream; there is no span-taking decoder in the box.
        byte[] rented = ArrayPool<byte>.Shared.Rent(stored.Length);
        try
        {
            stored.CopyTo(rented);

            using var source = new MemoryStream(rented, 0, stored.Length, writable: false);
            using var deflate = new DeflateStream(source, CompressionMode.Decompress);

            deflate.ReadExactly(destination);

            // More data than declared means a corrupt entry, not a short read.
            if (deflate.ReadByte() != -1)
            {
                throw new InvalidDataException(
                    $"Entry '{what}' inflates to more than the {destination.Length} bytes it declares.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    // A disposed source reports a miss, not a throw: an unmount can race a queued decode.
    private bool TryFindLive(string path, out PackEntry entry)
    {
        entry = default;
        if (_disposed || string.IsNullOrEmpty(path)) return false;

        string normalized;
        try
        {
            normalized = ContentRoot.NormalizeRelativePath(path);
        }
        catch (ArgumentException)
        {
            return false;
        }

        ReadOnlySpan<PackEntry> entries = Entries;
        if (!PackEntryTable.TryFind(entries, PackAssetId.FromNormalized(normalized), out int index)) return false;

        entry = entries[index];
        return true;
    }

    private void ValidateHeader(in PackHeader header, long length)
    {
        if (header.Magic != PackFormat.Magic)
        {
            throw new PackMountException(
                $"'{PackPath}' is not a .spack file: its first four bytes are 0x{header.Magic:X8}, " +
                $"not 0x{PackFormat.Magic:X8} ('SPAK').");
        }

        if (header.FormatVersion == 0)
        {
            throw new PackMountException(
                $"'{PackPath}' declares format version 0, which no writer emits.");
        }

        if (header.MinReaderVersion > header.FormatVersion)
        {
            throw new PackMountException(
                $"'{PackPath}' is self-inconsistent: it was written at format version {header.FormatVersion} " +
                $"but demands a reader implementing version {header.MinReaderVersion}.");
        }

        if (header.MinReaderVersion > EngineInfo.PackFormatVersion)
        {
            throw new PackMountException(
                $"'{PackPath}' demands a reader implementing pack format version {header.MinReaderVersion}; " +
                $"this engine implements version {EngineInfo.PackFormatVersion}. Recook the pack, or update.");
        }

        if (!header.EntriesSortedByAssetId)
        {
            throw new PackMountException(
                $"'{PackPath}' does not set {nameof(PackFlags.EntriesSortedByAssetId)}, which v1 requires. " +
                "Binary-searching an unsorted table misses entries silently, which presents as content that " +
                "is intermittently absent rather than as a corrupt file.");
        }

        if (header.TotalFileSize != (ulong)length)
        {
            throw new PackMountException(
                $"'{PackPath}' declares {header.TotalFileSize} bytes and is {length}: " +
                (header.TotalFileSize > (ulong)length ? "the file is truncated." : "the file has trailing bytes."));
        }
    }

    private void ValidateRegions(in PackHeader header, long length)
    {
        long tail = length - PackFormat.DigestSize;

        if (header.EntryTableOffset < PackFormat.HeaderSize || header.EntryTableOffset > (ulong)tail)
        {
            throw new PackMountException(
                $"'{PackPath}' puts its entry table at {header.EntryTableOffset}, outside the file's " +
                $"[{PackFormat.HeaderSize}, {tail}] body.");
        }

        // The table is cast in place, so its start must be aligned.
        if (header.EntryTableOffset % PackFormat.PayloadAlignment != 0)
        {
            throw new PackMountException(
                $"'{PackPath}' puts its entry table at {header.EntryTableOffset}, which is not a multiple of " +
                $"{PackFormat.PayloadAlignment}; the table is reinterpreted in place and every asset id must " +
                "stay aligned.");
        }

        // The table is addressed as one span, whose length is an int.
        long tableBytes = (long)header.EntryCount * PackFormat.EntrySize;
        if (tableBytes > int.MaxValue)
        {
            throw new PackMountException(
                $"'{PackPath}' declares {header.EntryCount} entries, whose table is {tableBytes} bytes, past the " +
                $"{int.MaxValue} a single span can address.");
        }

        if ((long)header.EntryTableOffset + tableBytes > tail)
        {
            throw new PackMountException(
                $"'{PackPath}' declares {header.EntryCount} entries ending at " +
                $"{(long)header.EntryTableOffset + tableBytes}, past the {tail} its body holds.");
        }

        if (header.HasNameTable)
        {
            if (header.NameTableLength > int.MaxValue)
            {
                throw new PackMountException(
                    $"'{PackPath}' declares a {header.NameTableLength}-byte name table, past the {int.MaxValue} " +
                    "a single span can address.");
            }

            if (header.NameTableOffset < PackFormat.HeaderSize ||
                header.NameTableOffset + header.NameTableLength > (ulong)tail)
            {
                throw new PackMountException(
                    $"'{PackPath}' puts a {header.NameTableLength}-byte name table at {header.NameTableOffset}, " +
                    $"outside the file's [{PackFormat.HeaderSize}, {tail}] body.");
            }
        }

        if (header.DataSectionOffset > (ulong)tail)
        {
            throw new PackMountException(
                $"'{PackPath}' puts its data section at {header.DataSectionOffset}, past the {tail} its body holds.");
        }

        if (header.DataSectionOffset % PackFormat.PayloadAlignment != 0)
        {
            throw new PackMountException(
                $"'{PackPath}' puts its data section at {header.DataSectionOffset}, which is not a multiple of " +
                $"{PackFormat.PayloadAlignment}; a payload is reinterpreted in place and may not straddle it.");
        }
    }

    private int ValidateEntries(in PackHeader header, long length)
    {
        ReadOnlySpan<PackEntry> entries = Entries;
        ReadOnlySpan<byte> names = NameTable;

        if (entries.Length != (int)header.EntryCount)
        {
            throw new PackMountException(
                $"'{PackPath}' declares {header.EntryCount} entries and its table holds {entries.Length}.");
        }

        ulong tail = (ulong)(length - PackFormat.DigestSize);
        int tombstones = 0;

        for (int i = 0; i < entries.Length; i++)
        {
            ref readonly PackEntry entry = ref entries[i];

            // Strictly ascending: verifies the sorted flag and rejects duplicate ids.
            if (i > 0 && entry.AssetId <= entries[i - 1].AssetId)
            {
                throw new PackMountException(
                    $"'{PackPath}' claims a sorted entry table, but entry {i} (id {entry.AssetId:X32}) does not " +
                    $"sit above entry {i - 1} (id {entries[i - 1].AssetId:X32}).");
            }

            if (entry.StoredSize > int.MaxValue || entry.UncompressedSize > int.MaxValue)
            {
                throw new PackMountException(
                    $"'{PackPath}' entry {i} is {entry.UncompressedSize} bytes uncompressed " +
                    $"({entry.StoredSize} stored), past the {int.MaxValue} one content blob can address.");
            }

            if (entry.PayloadOffset < header.DataSectionOffset ||
                entry.PayloadOffset > tail ||
                entry.StoredSize > tail - entry.PayloadOffset)
            {
                throw new PackMountException(
                    $"'{PackPath}' entry {i} claims {entry.StoredSize} bytes at {entry.PayloadOffset}, outside " +
                    $"the data section [{header.DataSectionOffset}, {tail}].");
            }

            if (entry.PayloadOffset % PackFormat.PayloadAlignment != 0)
            {
                throw new PackMountException(
                    $"'{PackPath}' entry {i} starts at {entry.PayloadOffset}, which is not a multiple of " +
                    $"{PackFormat.PayloadAlignment}.");
            }

            if (entry.EntryCodec == PackCodec.None && entry.StoredSize != entry.UncompressedSize)
            {
                throw new PackMountException(
                    $"'{PackPath}' entry {i} is stored uncompressed and declares {entry.StoredSize} stored " +
                    $"against {entry.UncompressedSize} uncompressed.");
            }

            if (entry.IsTombstone)
            {
                tombstones++;
                if (entry.StoredSize != 0)
                {
                    throw new PackMountException(
                        $"'{PackPath}' entry {i} is a tombstone carrying {entry.StoredSize} bytes; a deletion has " +
                        "no payload.");
                }
            }

            ValidateName(in header, names, in entry, i);
        }

        return tombstones;
    }

    private void ValidateName(in PackHeader header, ReadOnlySpan<byte> names, in PackEntry entry, int index)
    {
        if (!entry.HasName)
        {
            if (entry.NameLength != 0)
            {
                throw new PackMountException(
                    $"'{PackPath}' entry {index} has no name record and declares a {entry.NameLength}-byte name.");
            }

            return;
        }

        if (!header.HasNameTable)
        {
            throw new PackMountException(
                $"'{PackPath}' entry {index} points at name offset {entry.NameOffset} in a pack that carries no " +
                "name table.");
        }

        long record = (long)entry.NameOffset;
        long recordEnd = record + sizeof(ushort) + entry.NameLength;
        if (recordEnd > (long)header.NameTableLength)
        {
            throw new PackMountException(
                $"'{PackPath}' entry {index} names a record ending at {recordEnd} in a " +
                $"{header.NameTableLength}-byte name table.");
        }

        // The record's length prefix and the entry's copy must agree.
        ushort prefix = BinaryPrimitives.ReadUInt16LittleEndian(names[(int)record..]);
        if (prefix != entry.NameLength)
        {
            throw new PackMountException(
                $"'{PackPath}' entry {index} declares a {entry.NameLength}-byte name and its record says {prefix}.");
        }
    }

    private void ValidateDigest(in PackHeader header, long length)
    {
        Span<byte> stored = stackalloc byte[PackFormat.DigestSize];
        ReadRaw(length - PackFormat.DigestSize, stored);

        long from = (long)header.EntryTableOffset;
        UInt128 computed = ComputeDigest(from, length - PackFormat.DigestSize - from);
        UInt128 declared = PackDigest.Read(stored);

        if (computed == declared) return;

        throw new PackMountException(
            $"'{PackPath}' fails its content digest: the bytes from {from} to end of file hash to " +
            $"{computed:X32} and the file declares {declared:X32}. The pack is corrupt.");
    }

    private static string? NormalizePrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return null;

        try
        {
            return ContentRoot.NormalizeRelativePath(prefix);
        }
        catch (ArgumentException)
        {
            // Matches no normalised name.
            return " ";
        }
    }

    private static bool Matches(string name, string? prefix, string extension)
    {
        if (prefix is not null &&
            !(name.Length > prefix.Length &&
              name[prefix.Length] == '/' &&
              name.AsSpan(0, prefix.Length).Equals(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return string.IsNullOrEmpty(extension) || name.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }
}
