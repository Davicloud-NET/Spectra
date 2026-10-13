using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Core.Maps.Compiled;

// Reads and validates NATR in place, and makes a node's attributes from it.
internal static class ScmapAttributeTable
{
    public static ReadOnlySpan<ScmapAttributeRecord> Read(
        string source, ReadOnlySpan<byte> section, int nodeCount, ScmapStringTable strings)
    {
        if (section.Length < ScmapFormat.NodeAttributePreambleSize)
        {
            throw new ScmapFormatException(
                $"'{source}' has a {section.Length}-byte NATR section, short of the " +
                $"{ScmapFormat.NodeAttributePreambleSize}-byte preamble that carries its attribute count.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(section);
        long end = ScmapFormat.NodeAttributePreambleSize + ((long)count * ScmapFormat.NodeAttributeRecordSize);
        if (end > section.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {count} attributes, whose {ScmapFormat.NodeAttributeRecordSize}-byte " +
                $"records would end at byte {end} of a {section.Length}-byte NATR section.");
        }

        ReadOnlySpan<ScmapAttributeRecord> attributes = MemoryMarshal.Cast<byte, ScmapAttributeRecord>(
            section.Slice(ScmapFormat.NodeAttributePreambleSize, (int)count * ScmapFormat.NodeAttributeRecordSize));

        for (int i = 0; i < attributes.Length; i++)
            RequireRecord(source, attributes, i, nodeCount, strings.Count);

        RequireSets(source, attributes, strings);
        return attributes;
    }

    // The attributes of one node, from the cursor on. The cursor is left on
    // the next node's first record. The reader has checked every set it makes.
    public static AttributeSet Take(
        ReadOnlySpan<ScmapAttributeRecord> attributes, ScmapStringTable strings, ref int next, int nodeIndex)
    {
        if (next >= attributes.Length || attributes[next].NodeIndex != (uint)nodeIndex)
            return AttributeSet.Empty;

        var own = new List<KeyValuePair<string, MemberValue>>();
        while (next < attributes.Length && attributes[next].NodeIndex == (uint)nodeIndex)
            own.Add(PairOf(in attributes[next++], strings));

        return AttributeSet.TryMake(own, out AttributeSet? made, out string? refusal)
            ? made
            : throw new InvalidOperationException(refusal);
    }

    private static void RequireRecord(
        string source, ReadOnlySpan<ScmapAttributeRecord> attributes, int index, int nodeCount, int stringCount)
    {
        ref readonly ScmapAttributeRecord attribute = ref attributes[index];

        if (attribute.NodeIndex >= (uint)nodeCount)
        {
            throw new ScmapFormatException(
                $"'{source}' attribute {index} names node {attribute.NodeIndex} of a {nodeCount}-node map.");
        }

        // The loader hands attributes out in one pass over the nodes.
        if (index > 0 && attributes[index - 1].NodeIndex > attribute.NodeIndex)
        {
            throw new ScmapFormatException(
                $"'{source}' attribute records are not in node order at record {index}: node " +
                $"{attributes[index - 1].NodeIndex} is followed by node {attribute.NodeIndex}.");
        }

        if (attribute.NameString >= (uint)stringCount || attribute.TextString >= (uint)stringCount)
        {
            throw new ScmapFormatException(
                $"'{source}' attribute {index} names string {attribute.NameString} and string " +
                $"{attribute.TextString} of a {stringCount}-string table.");
        }

        if (!attribute.IsKnownKind)
        {
            throw new ScmapFormatException(
                $"'{source}' attribute {index} declares kind {attribute.KindRaw}, which this engine has no " +
                $"value for at .scmap format version {EngineInfo.CompiledMapFormatVersion}. Recook the map.");
        }
    }

    // Refused here, a load never ends half way through its nodes.
    private static void RequireSets(
        string source, ReadOnlySpan<ScmapAttributeRecord> attributes, ScmapStringTable strings)
    {
        var own = new List<KeyValuePair<string, MemberValue>>();
        int start = 0;
        while (start < attributes.Length)
        {
            uint node = attributes[start].NodeIndex;
            own.Clear();
            while (start < attributes.Length && attributes[start].NodeIndex == node)
                own.Add(PairOf(in attributes[start++], strings));

            if (!AttributeSet.TryMake(own, out _, out string? refusal))
            {
                throw new ScmapFormatException(
                    $"'{source}' node {node} has attributes no node can hold. {refusal}");
            }
        }
    }

    private static KeyValuePair<string, MemberValue> PairOf(
        scoped in ScmapAttributeRecord attribute, ScmapStringTable strings) =>
        new(strings.GetString((int)attribute.NameString),
            attribute.ToValue(strings.GetString((int)attribute.TextString)));
}
