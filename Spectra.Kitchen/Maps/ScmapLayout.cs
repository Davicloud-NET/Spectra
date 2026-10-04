using System;
using System.Collections.Generic;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Maps;

/// <summary>One section's four-character code and its declared body size.</summary>
/// <param name="BodySize">Bytes the body occupies, padding excluded.</param>
public readonly record struct ScmapSectionSize(uint Kind, long BodySize);

/// <summary>
/// Where every section of a <c>.scmap</c> lands, computed before a byte is written.
/// </summary>
// Layout and write passes must agree on padded sizes, or every later
// section sits somewhere other than where the table says and the file still
// parses. Both go through PaddedSectionSize; ScmapWriter also checks positions.
public sealed class ScmapLayout
{
    private readonly uint[] _kinds;
    private readonly long[] _bodySizes;
    private readonly long[] _offsets;

    private ScmapLayout(uint[] kinds, long[] bodySizes, long[] offsets, long totalSize)
    {
        _kinds = kinds;
        _bodySizes = bodySizes;
        _offsets = offsets;
        TotalSize = totalSize;
    }

    /// <summary>
    /// What one section occupies in the file: its body plus padding to the
    /// next 16-byte boundary. The only place this is computed.
    /// </summary>
    public static long PaddedSectionSize(long bodySize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bodySize);
        return ScmapFormat.AlignUp(bodySize, ScmapFormat.PayloadAlignment);
    }

    /// <summary>
    /// Places every section, in the order given, after the header and the section
    /// table. Throws if a section code appears twice.
    /// </summary>
    public static ScmapLayout Compute(IReadOnlyList<ScmapSectionSize> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);

        var kinds = new uint[sections.Count];
        var bodySizes = new long[sections.Count];
        var offsets = new long[sections.Count];

        long cursor = ScmapFormat.AlignUp(
            ScmapFormat.SectionTableOffset + ((long)sections.Count * ScmapFormat.SectionSize),
            ScmapFormat.PayloadAlignment);

        for (int i = 0; i < sections.Count; i++)
        {
            ScmapSectionSize section = sections[i];
            ArgumentOutOfRangeException.ThrowIfNegative(section.BodySize);

            for (int j = 0; j < i; j++)
            {
                if (kinds[j] != section.Kind) continue;

                throw new InvalidOperationException(
                    $"Section '{ScmapFormat.DescribeFourCc(section.Kind)}' was placed twice. A section names " +
                    "one region of the file, so a reader would have to choose, and choosing silently is how " +
                    "half a map comes from one copy and half from the other.");
            }

            kinds[i] = section.Kind;
            bodySizes[i] = section.BodySize;
            offsets[i] = cursor;
            cursor += PaddedSectionSize(section.BodySize);
        }

        return new ScmapLayout(kinds, bodySizes, offsets, cursor);
    }

    /// <summary>Number of sections.</summary>
    public int Count => _kinds.Length;

    /// <summary>Total bytes in the file, the last section's padding included.</summary>
    public long TotalSize { get; }

    /// <summary>The four-character code of a section.</summary>
    public uint KindAt(int index) => _kinds[index];

    /// <summary>The declared body size of a section, padding excluded.</summary>
    public long BodySizeAt(int index) => _bodySizes[index];

    /// <summary>The absolute offset of a section. Always 16-byte aligned.</summary>
    public long OffsetAt(int index) => _offsets[index];

    /// <summary>What a section occupies, padding included.</summary>
    public long PaddedSizeAt(int index) => PaddedSectionSize(_bodySizes[index]);
}
