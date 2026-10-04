using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// One cell's <c>CBSP</c> blob, read in place: a flat solid-leaf BSP tree.
/// Leaves take no slots; the two negative child codes are the leaves.
/// </summary>
// Only the root is range-checked. Checking every child index would touch the
// whole block at load; this assumes a pack our own cook produced.
public readonly ref struct ScmapChunkBsp
{
    /// <summary>Parses one cell's blob, validating its extent and its root.</summary>
    /// <param name="blob">The cell's slice of <c>CBSP</c>, at its declared length.</param>
    /// <exception cref="ScmapFormatException">The blob is not a well-formed flat tree.</exception>
    public ScmapChunkBsp(ReadOnlySpan<byte> blob, string source, scoped in ScmapChunkRecord cell)
    {
        string where = $"'{source}' chunk ({cell.X}, {cell.Y}, {cell.Z})";

        if (blob.Length < ScmapFormat.ChunkBspHeaderSize)
        {
            throw new ScmapFormatException(
                $"{where} has a {blob.Length}-byte BSP blob, short of the " +
                $"{ScmapFormat.ChunkBspHeaderSize}-byte header every one carries.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(blob);
        RootIndex = BinaryPrimitives.ReadInt32LittleEndian(blob[4..]);

        long end = ScmapFormat.ChunkBspHeaderSize + ((long)count * ScmapFormat.FlatBspNodeSize);
        if (end > blob.Length)
        {
            throw new ScmapFormatException(
                $"{where} declares {count} BSP nodes, whose {ScmapFormat.FlatBspNodeSize}-byte records would " +
                $"end at byte {end} of a {blob.Length}-byte BSP blob.");
        }

        if (RootIndex < FlatBspNode.SolidLeaf || RootIndex >= count)
        {
            throw new ScmapFormatException(
                $"{where} names root {RootIndex}, which is neither a node index in [0, {count}) nor one of " +
                "the two leaf codes. A root out of range is a query that walks off the end of the block.");
        }

        Nodes = MemoryMarshal.Cast<byte, FlatBspNode>(
            blob.Slice(ScmapFormat.ChunkBspHeaderSize, (int)count * ScmapFormat.FlatBspNodeSize));
    }

    /// <summary>The internal nodes, in <see cref="BspFlattener"/> order.</summary>
    public ReadOnlySpan<FlatBspNode> Nodes { get; }

    /// <summary>The root's child code: a node index, or a leaf code for a bare-leaf tree.</summary>
    public int RootIndex { get; }

    /// <summary>True when <paramref name="point"/> lies inside solid space.</summary>
    // Same walk as FlatBspTree.ContainsPoint, which needs a ReadOnlyMemory and
    // cannot take this span. Keep the two identical.
    public bool ContainsPoint(System.Numerics.Vector3 point)
    {
        int i = RootIndex;
        while (i >= 0)
        {
            ref readonly FlatBspNode node = ref Nodes[i];
            i = System.Numerics.Plane.DotCoordinate(node.Plane, point) >= 0f ? node.Front : node.Back;
        }

        return i == FlatBspNode.SolidLeaf;
    }

    /// <summary>
    /// Checks that this runtime lays out <c>Plane</c> and <see cref="FlatBspNode"/> at the
    /// sizes the format casts file bytes into. Plane's layout is not a documented contract.
    /// </summary>
    /// <exception cref="ScmapFormatException">This runtime lays either struct out differently.</exception>
    public static void RequireNodeLayout()
    {
        int plane = Unsafe.SizeOf<System.Numerics.Plane>();
        int node = Unsafe.SizeOf<FlatBspNode>();

        if (plane == 16 && node == ScmapFormat.FlatBspNodeSize) return;

        throw new ScmapFormatException(
            $"This runtime lays out Plane as {plane} bytes and FlatBspNode as {node}, and the .scmap format " +
            $"casts file bytes into both at 16 and {ScmapFormat.FlatBspNodeSize}. Every compiled map's BSP " +
            "blob would be read at the wrong stride.");
    }
}
