using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Maps;

// Builds CHDR and the two blob sections its records point into, CMSH and
// CBSP. One pass writes all three, so offsets and blobs share one order.
// Every blob is padded to the payload alignment through
// ScmapLayout.PaddedSectionSize.
internal static class ScmapChunkSection
{
    // Throws for a cell a reader would refuse: submeshes that are unsorted or
    // not whole triangles, or a BSP root that is neither an index nor a leaf.
    public static void Validate(in ScmapChunkSource chunk)
    {
        ValidateSubmeshes(chunk);

        if (chunk.BspNodes is { Length: > 0 } &&
            (chunk.BspRootIndex < FlatBspNode.SolidLeaf || chunk.BspRootIndex >= chunk.BspNodes.Length))
        {
            throw new InvalidOperationException(
                $"Cell ({chunk.Coord.X}, {chunk.Coord.Y}, {chunk.Coord.Z}) names BSP root " +
                $"{chunk.BspRootIndex} over {chunk.BspNodes.Length} nodes, which is neither an index into them " +
                "nor a leaf code. A root out of range is a query that walks off the end of the block.");
        }
    }

    // Sorts the directory by cell. The caller's order is not kept.
    public static byte[] Build(IReadOnlyList<ScmapChunkSource> chunks, out byte[] meshBody, out byte[] bspBody)
    {
        ScmapChunkSource[] sorted = [.. chunks];
        Array.Sort(sorted, static (a, b) => a.Coord.CompareTo(b.Coord));

        using var meshes = new MemoryStream();
        using var bsps = new MemoryStream();

        var body = new byte[ScmapFormat.ChunkPreambleSize + (sorted.Length * ScmapFormat.ChunkRecordSize)];
        Span<byte> span = body;

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)sorted.Length);

        for (int i = 0; i < sorted.Length; i++)
        {
            ScmapChunkSource source = sorted[i];

            var meshOffset = (uint)meshes.Length;
            uint meshSize = WriteChunkMesh(meshes, source);

            var bspOffset = (uint)bsps.Length;
            uint bspSize = WriteChunkBsp(bsps, source);

            var record = new ScmapChunkRecord(
                source.Coord.X,
                source.Coord.Y,
                source.Coord.Z,
                source.RenderBounds.Min,
                source.RenderBounds.Max,
                meshSize == 0 ? 0 : meshOffset,
                meshSize,
                bspSize == 0 ? 0 : bspOffset,
                bspSize);

            MemoryMarshal.Write(span[(ScmapFormat.ChunkPreambleSize + (i * ScmapFormat.ChunkRecordSize))..], in record);
        }

        meshBody = meshes.ToArray();
        bspBody = bsps.ToArray();
        return body;
    }

    private static void ValidateSubmeshes(in ScmapChunkSource chunk)
    {
        if (chunk.Submeshes is not { Length: > 0 }) return;

        for (int i = 0; i < chunk.Submeshes.Length; i++)
        {
            ScmapSubmeshSource submesh = chunk.Submeshes[i];
            ArgumentNullException.ThrowIfNull(submesh.Vertices);
            ArgumentNullException.ThrowIfNull(submesh.Indices);

            if (i > 0 && chunk.Submeshes[i - 1].AssetIndex >= submesh.AssetIndex)
            {
                throw new InvalidOperationException(
                    $"Cell ({chunk.Coord.X}, {chunk.Coord.Y}, {chunk.Coord.Z}) has submeshes out of ascending " +
                    $"asset order at {i}: asset {chunk.Submeshes[i - 1].AssetIndex} is followed by asset " +
                    $"{submesh.AssetIndex}.");
            }

            if (submesh.Vertices.Length % ScmapFormat.StandardVertexStrideFloats != 0)
            {
                throw new InvalidOperationException(
                    $"Cell ({chunk.Coord.X}, {chunk.Coord.Y}, {chunk.Coord.Z}) submesh {i} carries " +
                    $"{submesh.Vertices.Length} floats, which is not a whole number of " +
                    $"{ScmapFormat.StandardVertexStrideFloats}-float vertices.");
            }

            if (submesh.Indices.Length % 3 != 0)
            {
                throw new InvalidOperationException(
                    $"Cell ({chunk.Coord.X}, {chunk.Coord.Y}, {chunk.Coord.Z}) submesh {i} carries " +
                    $"{submesh.Indices.Length} indices, which is not a whole number of triangles.");
            }
        }
    }

    // One cell's CMSH blob. Writes nothing for a cell with no render geometry.
    private static uint WriteChunkMesh(MemoryStream blobs, in ScmapChunkSource cell)
    {
        if (cell.Submeshes is not { Length: > 0 }) return 0;

        ScmapSubmeshSource[] submeshes = cell.Submeshes;
        long start = blobs.Length;

        long directory = ScmapLayout.PaddedSectionSize(
            ScmapFormat.ChunkMeshHeaderSize + ((long)submeshes.Length * ScmapFormat.ChunkSubmeshEntrySize));

        // Place the arrays first: the directory needs their offsets before they are written.
        var entries = new ScmapSubmeshEntry[submeshes.Length];
        long cursor = directory;
        for (int i = 0; i < submeshes.Length; i++)
        {
            ScmapSubmeshSource submesh = submeshes[i];

            long vertexOffset = cursor;
            cursor += ScmapLayout.PaddedSectionSize((long)submesh.Vertices.Length * sizeof(float));

            long indexOffset = cursor;
            cursor += ScmapLayout.PaddedSectionSize((long)submesh.Indices.Length * sizeof(uint));

            entries[i] = new ScmapSubmeshEntry(
                submesh.AssetIndex,
                (uint)(submesh.Vertices.Length / ScmapFormat.StandardVertexStrideFloats),
                (uint)submesh.Indices.Length,
                (uint)vertexOffset,
                (uint)indexOffset);
        }

        Span<byte> header = stackalloc byte[ScmapFormat.ChunkMeshHeaderSize];
        header.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)submeshes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], ScmapFormat.StandardVertexStrideFloats);
        blobs.Write(header);

        Span<byte> entry = stackalloc byte[ScmapFormat.ChunkSubmeshEntrySize];
        for (int i = 0; i < entries.Length; i++)
        {
            MemoryMarshal.Write(entry, in entries[i]);
            blobs.Write(entry);
        }

        WriteZeros(blobs, start + directory - blobs.Length);

        for (int i = 0; i < submeshes.Length; i++)
        {
            ScmapSubmeshSource submesh = submeshes[i];

            blobs.Write(MemoryMarshal.AsBytes<float>(submesh.Vertices));
            WriteZeros(blobs, start + entries[i].IndexOffset - blobs.Length);

            blobs.Write(MemoryMarshal.AsBytes<uint>(submesh.Indices));
            WriteZeros(blobs, ScmapLayout.PaddedSectionSize(blobs.Length - start) - (blobs.Length - start));
        }

        return (uint)(blobs.Length - start);
    }

    // One cell's CBSP blob. Null nodes means no tree. An empty array is a
    // single leaf and still gets a blob, so the root's solid/empty code survives.
    private static uint WriteChunkBsp(MemoryStream blobs, in ScmapChunkSource cell)
    {
        if (cell.BspNodes is null) return 0;

        long start = blobs.Length;

        Span<byte> header = stackalloc byte[ScmapFormat.ChunkBspHeaderSize];
        header.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)cell.BspNodes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], cell.BspRootIndex);
        blobs.Write(header);

        blobs.Write(MemoryMarshal.AsBytes<FlatBspNode>(cell.BspNodes));
        WriteZeros(blobs, ScmapLayout.PaddedSectionSize(blobs.Length - start) - (blobs.Length - start));

        return (uint)(blobs.Length - start);
    }

    // Padding is written, not seeked over: a gap left by a seek is not guaranteed zero.
    private static void WriteZeros(MemoryStream blobs, long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        Span<byte> zeros = stackalloc byte[ScmapFormat.PayloadAlignment];
        zeros.Clear();

        while (count > 0)
        {
            int chunk = (int)Math.Min(count, zeros.Length);
            blobs.Write(zeros[..chunk]);
            count -= chunk;
        }
    }
}
