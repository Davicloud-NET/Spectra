using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Maps;

// Builds BRSH: the authored planes and faces of every brush that was kept.
internal static class ScmapBrushSourceSection
{
    // Null when no brush source was kept, so the section is left out.
    public static byte[]? Build(IReadOnlyList<ScmapBrushSourceEntry> brushes, int nodeCount)
    {
        if (brushes.Count == 0) return null;

        int planes = 0;
        foreach (ScmapBrushSourceEntry brush in brushes)
        {
            if (brush.NodeIndex < 0 || brush.NodeIndex >= nodeCount)
            {
                throw new InvalidOperationException(
                    $"A kept brush names node {brush.NodeIndex} of a {nodeCount}-node map. A brush that " +
                    "cannot name its node has no transform, so a load would carve it at the origin.");
            }

            planes += brush.Planes.Length;
        }

        long records = (long)brushes.Count * ScmapFormat.BrushSourceRecordSize;
        long planeStart = ScmapLayout.PaddedSectionSize(ScmapFormat.BrushSourceHeaderSize + records);
        long faceStart = ScmapLayout.PaddedSectionSize(planeStart + ((long)planes * ScmapFormat.PlaneSize));
        long total = faceStart + ((long)planes * ScmapFormat.BrushFaceRecordSize);

        var body = new byte[total];
        Span<byte> span = body;

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)brushes.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)planes);

        int plane = 0;
        for (int i = 0; i < brushes.Count; i++)
        {
            ScmapBrushSourceEntry brush = brushes[i];
            var record = new ScmapBrushRecord((uint)brush.NodeIndex, (uint)brush.Planes.Length, (uint)plane);
            MemoryMarshal.Write(
                span[(ScmapFormat.BrushSourceHeaderSize + (i * ScmapFormat.BrushSourceRecordSize))..], in record);

            for (int f = 0; f < brush.Planes.Length; f++, plane++)
            {
                MemoryMarshal.Write(
                    span[(int)(planeStart + ((long)plane * ScmapFormat.PlaneSize))..], in brush.Planes[f]);

                ScmapFaceSource face = brush.Faces[f];
                var faceRecord = new ScmapFaceRecord(
                    face.AssetIndex,
                    face.UAxis,
                    face.VAxis,
                    face.UOffset,
                    face.VOffset,
                    face.UScale,
                    face.VScale);

                MemoryMarshal.Write(
                    span[(int)(faceStart + ((long)plane * ScmapFormat.BrushFaceRecordSize))..], in faceRecord);
            }
        }

        return body;
    }
}
