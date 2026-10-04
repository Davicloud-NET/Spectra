using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Maps;

/// <summary>
/// Accumulates the <c>STRT</c> section: one blob of UTF-8, addressed by index.
/// Index 0 is always the empty string.
/// </summary>
public sealed class ScmapStringTableBuilder
{
    private readonly List<string> _strings = [string.Empty];

    // Lookup only, never enumerated: dictionary order varies per process.
    // Ordinal, because node names are matched case-sensitively at runtime.
    private readonly Dictionary<string, uint> _lookup = new(StringComparer.Ordinal) { [string.Empty] = 0 };

    /// <summary>Number of strings, the empty one at index 0 included.</summary>
    public int Count => _strings.Count;

    /// <summary>
    /// Returns the index of <paramref name="value"/>, appending it on first
    /// reference. A null or empty string is index 0.
    /// </summary>
    public uint Intern(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;

        if (_lookup.TryGetValue(value, out uint existing)) return existing;

        var index = (uint)_strings.Count;
        _strings.Add(value);
        _lookup[value] = index;
        return index;
    }

    /// <summary>The string at <paramref name="index"/>, for a diagnostic.</summary>
    public string At(int index) => _strings[index];

    /// <summary>
    /// Builds the section body: the count, count+1 offsets, the blob length and
    /// the blob. The extra offset is the end of the last string.
    /// </summary>
    public byte[] Build()
    {
        int count = _strings.Count;
        var lengths = new int[count];

        long blobSize = 0;
        for (int i = 0; i < count; i++)
        {
            lengths[i] = Encoding.UTF8.GetByteCount(_strings[i]);
            blobSize += lengths[i];
        }

        if (blobSize > uint.MaxValue)
        {
            throw new InvalidOperationException(
                $"The compiled map's string blob would be {blobSize} bytes, past what a 32-bit offset can " +
                "address.");
        }

        long total = ScmapFormat.StringCountSize + ((long)(count + 1) * sizeof(uint)) + sizeof(uint) + blobSize;
        var body = new byte[total];
        Span<byte> span = body;

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)count);

        int cursor = ScmapFormat.StringCountSize;
        uint offset = 0;
        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[cursor..], offset);
            cursor += sizeof(uint);
            offset += (uint)lengths[i];
        }

        BinaryPrimitives.WriteUInt32LittleEndian(span[cursor..], offset);
        cursor += sizeof(uint);

        BinaryPrimitives.WriteUInt32LittleEndian(span[cursor..], (uint)blobSize);
        cursor += sizeof(uint);

        for (int i = 0; i < count; i++)
        {
            cursor += Encoding.UTF8.GetBytes(_strings[i], span[cursor..]);
        }

        return body;
    }
}
