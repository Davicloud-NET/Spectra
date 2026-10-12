using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Maps;

// Builds COLL: one hull per baked world brush, in node order, and their
// planes after the records.
internal static class ScmapCollisionSection
{
    // A baked brush with no hull is refused here: the reader would refuse the file.
    public static byte[] Build(
        IReadOnlyList<ScmapNodeSource> nodes, IReadOnlyList<ScmapCollisionHullSource> hulls)
    {
        int planes = 0;
        int next = 0;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].PayloadKind != ScmapPayloadKind.StaticWorldBrush) continue;

            if (next >= hulls.Count || hulls[next].NodeIndex != i)
            {
                throw new InvalidOperationException(
                    $"Node {i} ('{nodes[i].Name}') is a baked world brush with no collision hull. Every " +
                    "baked brush needs one, or a character walks through a wall that is drawn.");
            }

            planes += hulls[next++].Planes.Length;
        }

        if (next < hulls.Count)
        {
            throw new InvalidOperationException(
                $"A collision hull names node {hulls[next].NodeIndex}, which is not a baked world brush of " +
                $"this {nodes.Count}-node map. Only a brush baked into the chunks carries a hull.");
        }

        long planeStart = ScmapFormat.CollisionPreambleSize + ((long)hulls.Count * ScmapFormat.HullRecordSize);
        var body = new byte[planeStart + ((long)planes * ScmapFormat.PlaneSize)];
        Span<byte> span = body;

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)hulls.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)planes);

        int plane = 0;
        for (int i = 0; i < hulls.Count; i++)
        {
            ScmapCollisionHullSource hull = hulls[i];
            var record = new ScmapHullRecord((uint)hull.NodeIndex, (uint)hull.Planes.Length, (uint)plane);
            MemoryMarshal.Write(
                span[(ScmapFormat.CollisionPreambleSize + (i * ScmapFormat.HullRecordSize))..], in record);

            for (int p = 0; p < hull.Planes.Length; p++, plane++)
            {
                MemoryMarshal.Write(
                    span[(int)(planeStart + ((long)plane * ScmapFormat.PlaneSize))..], in hull.Planes[p]);
            }
        }

        return body;
    }
}
