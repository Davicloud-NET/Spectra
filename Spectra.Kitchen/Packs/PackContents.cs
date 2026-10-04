using SpectraEngine.Core.Assets.Packs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Spectra.Kitchen.Packs;

/// <summary>
/// A <c>.spack</c>'s header and tables read into arrays, for tools that list or
/// check a pack. Does not validate the pack; mounting does that.
/// </summary>
public sealed class PackContents
{
    private readonly PackEntry[] _entries;
    private readonly byte[] _nameTable;

    private PackContents(
        string path, long fileLength, in PackHeader header, PackEntry[] entries, byte[] nameTable, UInt128 digest)
    {
        Path = path;
        FileLength = fileLength;
        Header = header;
        StoredDigest = digest;
        _entries = entries;
        _nameTable = nameTable;
    }

    /// <summary>Full path of the file these tables came from.</summary>
    public string Path { get; }

    /// <summary>Bytes in the file.</summary>
    public long FileLength { get; }

    /// <summary>The header, as it sits on disk.</summary>
    public PackHeader Header { get; }

    /// <summary>The trailing digest the file declares, unverified.</summary>
    public UInt128 StoredDigest { get; }

    /// <summary>The entry table, in table order.</summary>
    public IReadOnlyList<PackEntry> Entries => _entries;

    /// <summary>Whether the pack carries a name table.</summary>
    public bool HasNameTable => _nameTable.Length > 0;

    /// <summary>The name of an entry, or the empty string when the pack carries none.</summary>
    public string NameOf(int index) => PackEntryTable.ReadName(_nameTable, in _entries[index]);

    /// <summary>
    /// Reads the tables of the pack at <paramref name="path"/>. Throws
    /// <see cref="PackMountException"/> when the file is not a pack or a table
    /// lies outside it.
    /// </summary>
    public static PackContents Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        PackFormat.RequireLittleEndian();

        path = System.IO.Path.GetFullPath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        long length = stream.Length;
        PackFormat.RequireMinimumFileSize(path, length);

        Span<byte> headerBytes = stackalloc byte[PackFormat.HeaderSize];
        stream.ReadExactly(headerBytes);
        PackHeader header = MemoryMarshal.Read<PackHeader>(headerBytes);

        if (header.Magic != PackFormat.Magic)
        {
            throw new PackMountException(
                $"'{path}' is not a .spack file: its first four bytes are 0x{header.Magic:X8}, " +
                $"not 0x{PackFormat.Magic:X8} ('SPAK').");
        }

        int tableBytes = RequireInside(
            path, header.EntryTableOffset, (long)header.EntryCount * PackFormat.EntrySize, length, "entry table");

        var entries = new PackEntry[tableBytes / PackFormat.EntrySize];
        stream.Position = (long)header.EntryTableOffset;
        stream.ReadExactly(MemoryMarshal.AsBytes(entries.AsSpan()));

        byte[] names = [];
        if (header.HasNameTable)
        {
            names = new byte[RequireInside(
                path, header.NameTableOffset, (long)header.NameTableLength, length, "name table")];

            stream.Position = (long)header.NameTableOffset;
            stream.ReadExactly(names);
        }

        Span<byte> digestBytes = stackalloc byte[PackFormat.DigestSize];
        stream.Position = length - PackFormat.DigestSize;
        stream.ReadExactly(digestBytes);

        return new PackContents(path, length, in header, entries, names, PackDigest.Read(digestBytes));
    }

    // Checked before allocating: a corrupt count would otherwise ask for gigabytes.
    private static int RequireInside(string path, ulong offset, long bytes, long fileLength, string what)
    {
        if (bytes is >= 0 and <= int.MaxValue &&
            offset >= (ulong)PackFormat.HeaderSize &&
            (long)offset + bytes <= fileLength)
        {
            return (int)bytes;
        }

        throw new PackMountException(
            $"'{path}' puts its {what} at {offset} for {bytes} bytes, which is not inside the " +
            $"{fileLength}-byte file.");
    }
}
