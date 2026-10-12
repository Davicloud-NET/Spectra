using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Maps.Compiled;

// Reads and validates CHDR in place: the chunk directory, and that every
// blob a cell names lies inside CMSH or CBSP.
internal static class ScmapChunkTable
{
    public static ReadOnlySpan<ScmapChunkRecord> Read(
        string source,
        ReadOnlySpan<byte> section,
        int meshBlobLength,
        int bspBlobLength)
    {
        if (section.Length < ScmapFormat.ChunkPreambleSize)
        {
            throw new ScmapFormatException(
                $"'{source}' has a {section.Length}-byte CHDR section, short of the " +
                $"{ScmapFormat.ChunkPreambleSize}-byte preamble that carries its chunk count.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(section);
        long end = ScmapFormat.ChunkPreambleSize + ((long)count * ScmapFormat.ChunkRecordSize);
        if (end > section.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {count} chunks, whose {ScmapFormat.ChunkRecordSize}-byte records would " +
                $"end at byte {end} of a {section.Length}-byte CHDR section.");
        }

        ReadOnlySpan<ScmapChunkRecord> chunks = MemoryMarshal.Cast<byte, ScmapChunkRecord>(
            section.Slice(ScmapFormat.ChunkPreambleSize, (int)count * ScmapFormat.ChunkRecordSize));

        for (int i = 0; i < chunks.Length; i++)
        {
            ref readonly ScmapChunkRecord cell = ref chunks[i];

            if (i > 0 && Compare(in chunks[i - 1], in cell) >= 0)
            {
                // Cell lookup is a binary search, which misses cells in an
                // unsorted directory.
                throw new ScmapFormatException(
                    $"'{source}' chunk directory is not in ascending cell order at record {i}: " +
                    $"({chunks[i - 1].X}, {chunks[i - 1].Y}, {chunks[i - 1].Z}) is followed by " +
                    $"({cell.X}, {cell.Y}, {cell.Z}).");
            }

            RequireBlobRange(source, cell, "mesh", cell.MeshOffset, cell.MeshSize, meshBlobLength, "CMSH");
            RequireBlobRange(source, cell, "BSP", cell.BspOffset, cell.BspSize, bspBlobLength, "CBSP");
        }

        return chunks;
    }

    private static int Compare(in ScmapChunkRecord a, in ScmapChunkRecord b)
    {
        int c = a.X.CompareTo(b.X);
        if (c != 0) return c;
        c = a.Y.CompareTo(b.Y);
        return c != 0 ? c : a.Z.CompareTo(b.Z);
    }

    private static void RequireBlobRange(
        string source,
        in ScmapChunkRecord cell,
        string what,
        uint offset,
        uint size,
        int blobLength,
        string sectionName)
    {
        // Zero is legal: a cell can own no render geometry.
        if (size == 0) return;

        if ((long)offset + size > blobLength)
        {
            throw new ScmapFormatException(
                $"'{source}' chunk ({cell.X}, {cell.Y}, {cell.Z}) claims a {size}-byte {what} blob at offset " +
                $"{offset} of a {blobLength}-byte {sectionName} section.");
        }

        if ((offset % ScmapFormat.PayloadAlignment) != 0)
        {
            throw new ScmapFormatException(
                $"'{source}' chunk ({cell.X}, {cell.Y}, {cell.Z}) places its {what} blob at offset {offset}, " +
                $"which is not a multiple of {ScmapFormat.PayloadAlignment}.");
        }
    }
}
