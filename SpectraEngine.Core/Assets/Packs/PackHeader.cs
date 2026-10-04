using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// The 64 bytes at offset 0 of a <c>.spack</c> file, as they sit on disk.
/// </summary>
// Written and read as raw bytes. Field order and Pack = 1 are the file layout,
// and all 64 bytes are declared fields so nothing is left unwritten.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct PackHeader
{
    /// <summary>Always <see cref="PackFormat.Magic"/>; four bytes reading <c>SPAK</c>.</summary>
    public readonly uint Magic;

    /// <summary>The version this pack was written at: <c>EngineInfo.PackFormatVersion</c>.</summary>
    public readonly ushort FormatVersion;

    /// <summary>
    /// The oldest reader that can still make sense of this pack. A reader refuses
    /// the file when this exceeds the version it implements.
    /// </summary>
    public readonly ushort MinReaderVersion;

    /// <summary>Whole-pack properties. See <see cref="PackFlags"/>.</summary>
    public readonly uint Flags;

    /// <summary>Number of records in the entry table.</summary>
    public readonly uint EntryCount;

    /// <summary>
    /// Absolute offset of the entry table. 64 in v1.
    /// </summary>
    public readonly ulong EntryTableOffset;

    /// <summary>Absolute offset of the name table; 0 when there is none.</summary>
    public readonly ulong NameTableOffset;

    /// <summary>Bytes in the name table; 0 when there is none.</summary>
    public readonly ulong NameTableLength;

    /// <summary>
    /// Monotonic ordering key among patch packs.
    /// </summary>
    public readonly uint PackSequence;

    /// <summary>
    /// <c>(Major &lt;&lt; 20) | (Minor &lt;&lt; 10) | Revision</c> of the engine that
    /// wrote this pack. Informational only; <see cref="MinReaderVersion"/> gates a load.
    /// </summary>
    public readonly uint EngineVersion;

    /// <summary>
    /// Absolute offset of the first payload, aligned to
    /// <see cref="PackFormat.DataSectionAlignment"/>.
    /// </summary>
    public readonly ulong DataSectionOffset;

    /// <summary>
    /// Size of the whole file including the trailing digest. Detects truncation.
    /// </summary>
    public readonly ulong TotalFileSize;

    /// <summary>Builds a header.</summary>
    public PackHeader(
        uint magic,
        ushort formatVersion,
        ushort minReaderVersion,
        PackFlags flags,
        uint entryCount,
        ulong entryTableOffset,
        ulong nameTableOffset,
        ulong nameTableLength,
        uint packSequence,
        uint engineVersion,
        ulong dataSectionOffset,
        ulong totalFileSize)
    {
        Magic = magic;
        FormatVersion = formatVersion;
        MinReaderVersion = minReaderVersion;
        Flags = (uint)flags;
        EntryCount = entryCount;
        EntryTableOffset = entryTableOffset;
        NameTableOffset = nameTableOffset;
        NameTableLength = nameTableLength;
        PackSequence = packSequence;
        EngineVersion = engineVersion;
        DataSectionOffset = dataSectionOffset;
        TotalFileSize = totalFileSize;
    }

    /// <summary><see cref="Flags"/> as its enum.</summary>
    public PackFlags PackFlags => (PackFlags)Flags;

    /// <summary>Whether the entry table may be binary-searched.</summary>
    public bool EntriesSortedByAssetId => (PackFlags & PackFlags.EntriesSortedByAssetId) != 0;

    /// <summary>Whether a name table is present and non-empty.</summary>
    public bool HasNameTable =>
        (PackFlags & PackFlags.NameTablePresent) != 0 && NameTableOffset != 0 && NameTableLength != 0;
}
