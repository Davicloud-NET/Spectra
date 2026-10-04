using System;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// One 48-byte entry-table record, as it sits on disk. The table is sorted
/// ascending by <see cref="AssetId"/>, compared unsigned.
/// </summary>
// The mapped table is cast straight to a span of these. Field order and Pack = 1
// are the file layout; 48 keeps every AssetId 16-byte aligned.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct PackEntry
{
    /// <summary>
    /// <c>XxHash128</c> of the normalized content-relative source path. See
    /// <see cref="PackAssetId"/>.
    /// </summary>
    public readonly UInt128 AssetId;

    /// <summary>
    /// Absolute file offset of the payload, aligned to
    /// <see cref="PackFormat.PayloadAlignment"/>.
    /// </summary>
    public readonly ulong PayloadOffset;

    /// <summary>Bytes on disk, i.e. after <see cref="Codec"/> was applied.</summary>
    public readonly ulong StoredSize;

    /// <summary>
    /// Bytes after decompression, equal to <see cref="StoredSize"/> when
    /// <see cref="Codec"/> is <see cref="PackCodec.None"/>.
    /// </summary>
    public readonly ulong UncompressedSize;

    /// <summary>
    /// Byte offset of this entry's record within the name table, or
    /// <see cref="PackFormat.NameOffsetAbsent"/>. Points at the record's
    /// <c>u16</c> length prefix, not the text after it.
    /// </summary>
    public readonly uint NameOffset;

    /// <summary>
    /// Length of the name in UTF-8 bytes, which must equal the <c>u16</c> prefix
    /// the record itself carries.
    /// </summary>
    public readonly ushort NameLength;

    /// <summary>See <see cref="PackEntryKind"/>.</summary>
    public readonly byte Kind;

    /// <summary>See <see cref="PackCodec"/>.</summary>
    public readonly byte Codec;

    /// <summary>Builds an entry.</summary>
    public PackEntry(
        UInt128 assetId,
        ulong payloadOffset,
        ulong storedSize,
        ulong uncompressedSize,
        uint nameOffset,
        ushort nameLength,
        PackEntryKind kind,
        PackCodec codec)
    {
        AssetId = assetId;
        PayloadOffset = payloadOffset;
        StoredSize = storedSize;
        UncompressedSize = uncompressedSize;
        NameOffset = nameOffset;
        NameLength = nameLength;
        Kind = (byte)kind;
        Codec = (byte)codec;
    }

    /// <summary><see cref="Kind"/> as its enum.</summary>
    public PackEntryKind EntryKind => (PackEntryKind)Kind;

    /// <summary><see cref="Codec"/> as its enum.</summary>
    public PackCodec EntryCodec => (PackCodec)Codec;

    /// <summary>Whether this entry deletes the path it names rather than serving it.</summary>
    public bool IsTombstone => Kind == (byte)PackEntryKind.Tombstone;

    /// <summary>Whether this entry has a name-table record.</summary>
    public bool HasName => NameOffset != PackFormat.NameOffsetAbsent;
}
