using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scripting;

namespace Spectra.Kitchen.Maps;

// Builds NTAG and NATR: what nodes were built with of tags and attributes,
// in node order. Interns per node: its tags, then each attribute's name and
// its text. That order is part of the file's bytes.
internal static class ScmapNodeSetsSection
{
    // Each body is null when no node has any, so the section is left out
    // and a map without them keeps the bytes it had.
    public static void Build(
        IReadOnlyList<ScmapNodeSetsSource> nodes,
        int nodeCount,
        ScmapStringTableBuilder strings,
        out byte[]? tagBody,
        out byte[]? attributeBody)
    {
        int tags = 0;
        int attributes = 0;
        foreach (ScmapNodeSetsSource node in nodes)
        {
            if (node.NodeIndex < 0 || node.NodeIndex >= nodeCount)
            {
                throw new InvalidOperationException(
                    $"Tags and attributes name node {node.NodeIndex} of a {nodeCount}-node map.");
            }

            tags += node.Tags.Count;
            attributes += node.Attributes.Count;
        }

        tagBody = tags == 0
            ? null
            : new byte[ScmapFormat.NodeTagPreambleSize + ((long)tags * ScmapFormat.NodeTagRecordSize)];
        attributeBody = attributes == 0
            ? null
            : new byte[ScmapFormat.NodeAttributePreambleSize + ((long)attributes * ScmapFormat.NodeAttributeRecordSize)];

        if (tagBody is not null) BinaryPrimitives.WriteUInt32LittleEndian(tagBody, (uint)tags);
        if (attributeBody is not null) BinaryPrimitives.WriteUInt32LittleEndian(attributeBody, (uint)attributes);

        int tag = 0;
        int attribute = 0;
        foreach (ScmapNodeSetsSource node in nodes)
        {
            foreach (string tagName in node.Tags)
            {
                var record = new ScmapTagRecord((uint)node.NodeIndex, strings.Intern(tagName));
                MemoryMarshal.Write(
                    tagBody.AsSpan(ScmapFormat.NodeTagPreambleSize + (tag++ * ScmapFormat.NodeTagRecordSize)),
                    in record);
            }

            foreach (KeyValuePair<string, MemberValue> pair in node.Attributes)
            {
                MemberValue value = pair.Value;
                uint name = strings.Intern(pair.Key);
                uint text = value.Kind == MemberValueKind.Text ? strings.Intern(value.Text) : 0;
                var record = new ScmapAttributeRecord((uint)node.NodeIndex, name, in value, text);
                MemoryMarshal.Write(
                    attributeBody.AsSpan(
                        ScmapFormat.NodeAttributePreambleSize + (attribute++ * ScmapFormat.NodeAttributeRecordSize)),
                    in record);
            }
        }
    }
}
