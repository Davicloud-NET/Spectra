using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// Reading the entry and name tables in place: the cast, the binary search and
/// the name record. Shared by the mapped and the stream source.
/// </summary>
public static class PackEntryTable
{
    /// <summary>
    /// The entry table's bytes reinterpreted as records, with no parse and no
    /// allocation.
    /// </summary>
    public static ReadOnlySpan<PackEntry> Cast(ReadOnlySpan<byte> tableBytes) =>
        MemoryMarshal.Cast<byte, PackEntry>(tableBytes);

    /// <summary>
    /// Finds <paramref name="assetId"/> in a table sorted ascending as an
    /// unsigned 128-bit value. Allocation-free.
    /// </summary>
    public static bool TryFind(ReadOnlySpan<PackEntry> entries, UInt128 assetId, out int index)
    {
        int low = 0;
        int high = entries.Length - 1;

        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            UInt128 candidate = entries[middle].AssetId;

            if (candidate == assetId)
            {
                index = middle;
                return true;
            }

            if (candidate < assetId) low = middle + 1;
            else high = middle - 1;
        }

        index = -1;
        return false;
    }

    /// <summary>
    /// The name of <paramref name="entry"/>, or the empty string when the pack
    /// carries no name table or the entry has no record in it.
    /// </summary>
    public static string ReadName(ReadOnlySpan<byte> nameTable, in PackEntry entry)
    {
        if (!entry.HasName || nameTable.IsEmpty) return string.Empty;

        ReadOnlySpan<byte> record = nameTable[(int)entry.NameOffset..];
        ushort length = BinaryPrimitives.ReadUInt16LittleEndian(record);
        return Encoding.UTF8.GetString(record.Slice(sizeof(ushort), length));
    }
}
