using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// One 24-byte <c>CMSH</c> submesh directory record: where one cell's geometry
/// for one material sits inside that cell's blob. Its indices are zero-based at
/// this submesh's own first vertex.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapSubmeshEntry
{
    /// <summary>
    /// Index into <c>ASTB</c> of the material every triangle here wears, or
    /// <see cref="ScmapFormat.NoAssetIndex"/> when the surfaces name none.
    /// </summary>
    // Never a MaterialRef.Id: ids are per-process interning order.
    public readonly uint AssetIndex;

    /// <summary>Vertices in this submesh.</summary>
    public readonly uint VertexCount;

    /// <summary>Indices in this submesh, always a multiple of three.</summary>
    public readonly uint IndexCount;

    /// <summary>Reserved; written zero.</summary>
    public readonly uint Reserved;

    /// <summary>Byte offset of the vertex array from the start of this cell's blob. 16-byte aligned.</summary>
    public readonly uint VertexOffset;

    /// <summary>Byte offset of the index array from the start of this cell's blob. 16-byte aligned.</summary>
    public readonly uint IndexOffset;

    /// <summary>Builds one directory record.</summary>
    public ScmapSubmeshEntry(
        uint assetIndex,
        uint vertexCount,
        uint indexCount,
        uint vertexOffset,
        uint indexOffset)
    {
        AssetIndex = assetIndex;
        VertexCount = vertexCount;
        IndexCount = indexCount;
        Reserved = 0;
        VertexOffset = vertexOffset;
        IndexOffset = indexOffset;
    }

    /// <summary>Whether this submesh names a row of the asset table.</summary>
    public bool NamesAsset => AssetIndex != ScmapFormat.NoAssetIndex;
}

/// <summary>
/// One cell's <c>CMSH</c> blob, read in place: a directory plus the vertex and
/// index arrays it addresses. Every array is 16-byte aligned within the blob
/// and submeshes are in ascending asset index; both are checked on parse.
/// </summary>
public readonly ref struct ScmapChunkMesh
{
    private readonly ReadOnlySpan<byte> _blob;

    /// <summary>Parses and validates one cell's blob.</summary>
    /// <param name="blob">The cell's slice of <c>CMSH</c>, at its declared length.</param>
    /// <exception cref="ScmapFormatException">The blob is not a well-formed chunk mesh.</exception>
    public ScmapChunkMesh(ReadOnlySpan<byte> blob, string source, scoped in ScmapChunkRecord cell)
    {
        string where = $"'{source}' chunk ({cell.X}, {cell.Y}, {cell.Z})";
        _blob = blob;

        if (blob.Length < ScmapFormat.ChunkMeshHeaderSize)
        {
            throw new ScmapFormatException(
                $"{where} has a {blob.Length}-byte mesh blob, short of the " +
                $"{ScmapFormat.ChunkMeshHeaderSize}-byte header every one carries.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(blob);
        VertexStrideFloats = BinaryPrimitives.ReadUInt32LittleEndian(blob[4..]);

        if (VertexStrideFloats == 0)
        {
            throw new ScmapFormatException(
                $"{where} declares a zero-float vertex stride, which makes every vertex count meaningless.");
        }

        long directoryEnd =
            ScmapFormat.ChunkMeshHeaderSize + ((long)count * ScmapFormat.ChunkSubmeshEntrySize);

        if (directoryEnd > blob.Length)
        {
            throw new ScmapFormatException(
                $"{where} declares {count} submeshes, whose {ScmapFormat.ChunkSubmeshEntrySize}-byte records " +
                $"would end at byte {directoryEnd} of a {blob.Length}-byte mesh blob.");
        }

        Submeshes = MemoryMarshal.Cast<byte, ScmapSubmeshEntry>(
            blob.Slice(ScmapFormat.ChunkMeshHeaderSize, (int)count * ScmapFormat.ChunkSubmeshEntrySize));

        for (int i = 0; i < Submeshes.Length; i++)
        {
            ScmapSubmeshEntry entry = Submeshes[i];

            if (i > 0 && Submeshes[i - 1].AssetIndex >= entry.AssetIndex)
            {
                // Strictly ascending: the order keeps cooks deterministic, and a
                // duplicate would draw one material's surfaces twice.
                throw new ScmapFormatException(
                    $"{where} has submeshes out of ascending asset order at record {i}: asset " +
                    $"{Submeshes[i - 1].AssetIndex} is followed by asset {entry.AssetIndex}.");
            }

            if (entry.IndexCount % 3 != 0)
            {
                throw new ScmapFormatException(
                    $"{where} submesh {i} declares {entry.IndexCount} indices, which is not a whole number of " +
                    "triangles.");
            }

            RequireArray(where, i, "vertex", entry.VertexOffset, (long)entry.VertexCount * VertexStrideFloats * sizeof(float), blob.Length);
            RequireArray(where, i, "index", entry.IndexOffset, (long)entry.IndexCount * sizeof(uint), blob.Length);
        }
    }

    /// <summary>Floats per vertex. This engine writes eight.</summary>
    public uint VertexStrideFloats { get; }

    /// <summary>The submesh directory, in ascending asset index.</summary>
    public ReadOnlySpan<ScmapSubmeshEntry> Submeshes { get; }

    /// <summary>
    /// Submesh <paramref name="index"/>'s interleaved vertex data, cast in place.
    /// </summary>
    public ReadOnlySpan<float> Vertices(int index)
    {
        ScmapSubmeshEntry entry = Submeshes[index];
        return MemoryMarshal.Cast<byte, float>(
            _blob.Slice((int)entry.VertexOffset, (int)entry.VertexCount * (int)VertexStrideFloats * sizeof(float)));
    }

    /// <summary>
    /// Submesh <paramref name="index"/>'s index data, cast in place. Zero-based at
    /// this submesh's own first vertex.
    /// </summary>
    public ReadOnlySpan<uint> Indices(int index)
    {
        ScmapSubmeshEntry entry = Submeshes[index];
        return MemoryMarshal.Cast<byte, uint>(
            _blob.Slice((int)entry.IndexOffset, (int)entry.IndexCount * sizeof(uint)));
    }

    /// <summary>Triangles across every submesh of this cell.</summary>
    public int TriangleCount
    {
        get
        {
            int triangles = 0;
            for (int i = 0; i < Submeshes.Length; i++) triangles += (int)(Submeshes[i].IndexCount / 3);
            return triangles;
        }
    }

    private static void RequireArray(
        string where, int index, string what, uint offset, long bytes, int blobLength)
    {
        // Subtract, don't add: offset + length can wrap on a corrupt file.
        if (offset > (ulong)blobLength || bytes > blobLength - offset)
        {
            throw new ScmapFormatException(
                $"{where} submesh {index} claims a {bytes}-byte {what} array at offset {offset} of a " +
                $"{blobLength}-byte mesh blob.");
        }

        if ((offset % ScmapFormat.PayloadAlignment) != 0)
        {
            throw new ScmapFormatException(
                $"{where} submesh {index} places its {what} array at offset {offset}, which is not a multiple " +
                $"of {ScmapFormat.PayloadAlignment}. The array is reinterpreted in place, so an unaligned " +
                "start is a read out of the middle of the array before it.");
        }
    }
}
