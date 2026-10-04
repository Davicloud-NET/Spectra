using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// The <c>STRT</c> section, read in place: count+1 offsets over one UTF-8 blob.
/// Index 0 is the empty string, so "no name" needs no sentinel.
/// </summary>
public readonly ref struct ScmapStringTable
{
    private readonly ReadOnlySpan<uint> _offsets;
    private readonly ReadOnlySpan<byte> _blob;

    /// <summary>Parses the section and validates every offset up front.</summary>
    // The section may be a mapped view, where a read past the end is an access
    // violation and not an exception. So nothing indexes before the checks pass.
    public ScmapStringTable(ReadOnlySpan<byte> section, string source)
    {
        if (section.Length < ScmapFormat.StringCountSize)
        {
            throw new ScmapFormatException(
                $"'{source}' has a {section.Length}-byte STRT section, too short to hold its own string count.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(section);
        if (count == 0)
        {
            throw new ScmapFormatException(
                $"'{source}' declares an empty STRT section. Index 0 is always the empty string, so a " +
                "well-formed table carries at least one entry.");
        }

        long offsetBytes = (long)(count + 1) * sizeof(uint);
        long declared = ScmapFormat.StringCountSize + offsetBytes + sizeof(uint);
        if (declared > section.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {count} strings, whose offset array and blob length would end at byte " +
                $"{declared} of a {section.Length}-byte STRT section.");
        }

        _offsets = MemoryMarshal.Cast<byte, uint>(
            section.Slice(ScmapFormat.StringCountSize, (int)offsetBytes));

        uint blobSize = BinaryPrimitives.ReadUInt32LittleEndian(section[(int)(ScmapFormat.StringCountSize + offsetBytes)..]);
        long blobEnd = declared + blobSize;
        if (blobEnd > section.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares a {blobSize}-byte string blob ending at byte {blobEnd} of a " +
                $"{section.Length}-byte STRT section.");
        }

        _blob = section.Slice((int)declared, (int)blobSize);

        if (_offsets[0] != 0)
        {
            throw new ScmapFormatException(
                $"'{source}' starts its STRT offset array at {_offsets[0]} rather than 0.");
        }

        for (int i = 1; i <= (int)count; i++)
        {
            if (_offsets[i] < _offsets[i - 1])
            {
                throw new ScmapFormatException(
                    $"'{source}' has a STRT offset array that goes backwards at index {i}: " +
                    $"{_offsets[i]} after {_offsets[i - 1]}. Every string's length is the difference " +
                    "between two neighbours, so a decreasing pair is a negative length.");
            }
        }

        if (_offsets[(int)count] != blobSize)
        {
            throw new ScmapFormatException(
                $"'{source}' has a STRT offset array ending at {_offsets[(int)count]} over a {blobSize}-byte " +
                "blob. The last offset is the blob length, which is what makes every string's extent a " +
                "subtraction with no special case for the final one.");
        }

        Count = (int)count;
    }

    /// <summary>How many strings the table holds, the empty string at index 0 included.</summary>
    public int Count { get; }

    /// <summary>The UTF-8 bytes of one string, without decoding them.</summary>
    public ReadOnlySpan<byte> GetUtf8(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

        int start = (int)_offsets[index];
        return _blob.Slice(start, (int)_offsets[index + 1] - start);
    }

    /// <summary>One string, decoded. Allocates.</summary>
    public string GetString(int index) => Encoding.UTF8.GetString(GetUtf8(index));

    /// <summary>
    /// One string, decoded, or the empty string when the index is out of range.
    /// For error messages about a record whose name index may be bad too.
    /// </summary>
    public string GetStringOrEmpty(int index) =>
        index >= 0 && index < Count ? GetString(index) : string.Empty;
}
