using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Maps;

// Builds NODE: one record per node, in the caller's pre-order.
internal static class ScmapNodeSection
{
    public static byte[] Build(IReadOnlyList<ScmapNodeSource> nodes, uint[] nameStrings)
    {
        var body = new byte[ScmapFormat.NodePreambleSize + (nodes.Count * ScmapFormat.NodeRecordSize)];
        Span<byte> span = body;

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)nodes.Count);

        for (int i = 0; i < nodes.Count; i++)
        {
            ScmapNodeSource source = nodes[i];
            var record = new ScmapNodeRecord(
                source.Id,
                nameStrings[i],
                source.ParentIndex,
                source.LocalTransform.Position,
                source.LocalTransform.Rotation,
                source.LocalTransform.Scale,
                source.PayloadKind,
                source.PayloadFlags,
                source.DeclaredRealm,
                source.DeclaredState,
                source.PayloadIndex);

            MemoryMarshal.Write(span[(ScmapFormat.NodePreambleSize + (i * ScmapFormat.NodeRecordSize))..], in record);
        }

        return body;
    }
}
