using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Maps.Compiled;

// Reads and validates NTAG in place, and makes a node's tags from it.
internal static class ScmapTagTable
{
    public static ReadOnlySpan<ScmapTagRecord> Read(
        string source, ReadOnlySpan<byte> section, int nodeCount, ScmapStringTable strings)
    {
        if (section.Length < ScmapFormat.NodeTagPreambleSize)
        {
            throw new ScmapFormatException(
                $"'{source}' has a {section.Length}-byte NTAG section, short of the " +
                $"{ScmapFormat.NodeTagPreambleSize}-byte preamble that carries its tag count.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(section);
        long end = ScmapFormat.NodeTagPreambleSize + ((long)count * ScmapFormat.NodeTagRecordSize);
        if (end > section.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {count} tags, whose {ScmapFormat.NodeTagRecordSize}-byte records would " +
                $"end at byte {end} of a {section.Length}-byte NTAG section.");
        }

        ReadOnlySpan<ScmapTagRecord> tags = MemoryMarshal.Cast<byte, ScmapTagRecord>(
            section.Slice(ScmapFormat.NodeTagPreambleSize, (int)count * ScmapFormat.NodeTagRecordSize));

        for (int i = 0; i < tags.Length; i++)
        {
            ref readonly ScmapTagRecord tag = ref tags[i];

            if (tag.NodeIndex >= (uint)nodeCount)
            {
                throw new ScmapFormatException(
                    $"'{source}' tag {i} names node {tag.NodeIndex} of a {nodeCount}-node map.");
            }

            // The loader hands tags out in one pass over the nodes.
            if (i > 0 && tags[i - 1].NodeIndex > tag.NodeIndex)
            {
                throw new ScmapFormatException(
                    $"'{source}' tag records are not in node order at record {i}: node " +
                    $"{tags[i - 1].NodeIndex} is followed by node {tag.NodeIndex}.");
            }

            if (tag.TagString >= (uint)strings.Count)
            {
                throw new ScmapFormatException(
                    $"'{source}' tag {i} names string {tag.TagString} of a {strings.Count}-string table.");
            }
        }

        RequireSets(source, tags, strings);
        return tags;
    }

    // The tags of one node, from the cursor on. The cursor is left on the
    // next node's first record. The reader has checked every set it makes.
    public static TagSet Take(
        ReadOnlySpan<ScmapTagRecord> tags, ScmapStringTable strings, ref int next, int nodeIndex)
    {
        if (next >= tags.Length || tags[next].NodeIndex != (uint)nodeIndex)
            return TagSet.Empty;

        var own = new List<string>();
        while (next < tags.Length && tags[next].NodeIndex == (uint)nodeIndex)
            own.Add(strings.GetString((int)tags[next++].TagString));

        return TagSet.Of(own);
    }

    // Refused here, a load never ends half way through its nodes.
    private static void RequireSets(string source, ReadOnlySpan<ScmapTagRecord> tags, ScmapStringTable strings)
    {
        var own = new List<string>();
        int start = 0;
        while (start < tags.Length)
        {
            uint node = tags[start].NodeIndex;
            own.Clear();
            while (start < tags.Length && tags[start].NodeIndex == node)
                own.Add(strings.GetString((int)tags[start++].TagString));

            if (!TagSet.TryMake(own, out _, out string? refusal))
                throw new ScmapFormatException($"'{source}' node {node} has tags no node can hold. {refusal}");
        }
    }
}
