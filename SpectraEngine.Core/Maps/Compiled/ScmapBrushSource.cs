using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// One 16-byte <c>BRSH</c> record: an authored brush's planes, by node. The node
/// record does not link back; its <c>PayloadIndex</c> is zero for a brush.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapBrushRecord
{
    /// <summary>Index into <c>NODE</c> of the node this brush hangs on.</summary>
    public readonly uint NodeIndex;

    /// <summary>How many planes this brush has, which is also how many faces.</summary>
    public readonly uint PlaneCount;

    /// <summary>
    /// Index of this brush's first plane in the section's plane array, which is
    /// also the index of its first face.
    /// </summary>
    public readonly uint PlaneStart;

    /// <summary>Reserved; written zero.</summary>
    public readonly uint Reserved;

    /// <summary>Builds one brush record.</summary>
    public ScmapBrushRecord(uint nodeIndex, uint planeCount, uint planeStart)
    {
        NodeIndex = nodeIndex;
        PlaneCount = planeCount;
        PlaneStart = planeStart;
        Reserved = 0;
    }
}

/// <summary>
/// One 48-byte <c>BRSH</c> face record: the material and texture frame of one
/// authored brush plane. Zero axes mean world-aligned, as on <c>FaceSurface</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapFaceRecord
{
    /// <summary>
    /// Index into <c>ASTB</c> of this face's material, or
    /// <see cref="ScmapFormat.NoAssetIndex"/> when it names none.
    /// </summary>
    public readonly uint AssetIndex;

    /// <summary>Brush-local U axis, or zero for world-aligned.</summary>
    public readonly Vector3 UAxis;

    /// <summary>Brush-local V axis, or zero for world-aligned.</summary>
    public readonly Vector3 VAxis;

    /// <summary>U offset, in repeats.</summary>
    public readonly float UOffset;

    /// <summary>V offset, in repeats.</summary>
    public readonly float VOffset;

    /// <summary>World units per U repeat.</summary>
    public readonly float UScale;

    /// <summary>World units per V repeat.</summary>
    public readonly float VScale;

    /// <summary>Reserved; written zero.</summary>
    public readonly uint Reserved;

    /// <summary>Builds one face record.</summary>
    public ScmapFaceRecord(
        uint assetIndex,
        Vector3 uAxis,
        Vector3 vAxis,
        float uOffset,
        float vOffset,
        float uScale,
        float vScale)
    {
        AssetIndex = assetIndex;
        UAxis = uAxis;
        VAxis = vAxis;
        UOffset = uOffset;
        VOffset = vOffset;
        UScale = uScale;
        VScale = vScale;
        Reserved = 0;
    }
}

/// <summary>
/// The <c>BRSH</c> section, read in place: authored brush planes and their faces.
/// Part brushes are always here. World brushes only with <c>--keep-brush-source</c>,
/// and those must not be carved again: ask <see cref="IsReCarvable"/> first.
/// </summary>
public readonly ref struct ScmapBrushSource
{
    /// <summary>Parses and validates the section.</summary>
    /// <exception cref="ScmapFormatException">The section is not a well-formed brush table.</exception>
    public ScmapBrushSource(ReadOnlySpan<byte> section, string source, int nodeCount)
    {
        if (section.Length < ScmapFormat.BrushSourceHeaderSize)
        {
            throw new ScmapFormatException(
                $"'{source}' has a {section.Length}-byte BRSH section, short of the " +
                $"{ScmapFormat.BrushSourceHeaderSize}-byte preamble that carries its counts.");
        }

        uint brushCount = BinaryPrimitives.ReadUInt32LittleEndian(section);
        uint planeCount = BinaryPrimitives.ReadUInt32LittleEndian(section[4..]);

        long brushBytes = (long)brushCount * ScmapFormat.BrushSourceRecordSize;
        long planeStart = ScmapFormat.AlignUp(ScmapFormat.BrushSourceHeaderSize + brushBytes, ScmapFormat.PayloadAlignment);
        long planeBytes = (long)planeCount * ScmapFormat.PlaneSize;
        long faceStart = ScmapFormat.AlignUp(planeStart + planeBytes, ScmapFormat.PayloadAlignment);
        long faceBytes = (long)planeCount * ScmapFormat.BrushFaceRecordSize;

        if (faceStart + faceBytes > section.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {brushCount} brushes over {planeCount} planes, whose records would end " +
                $"at byte {faceStart + faceBytes} of a {section.Length}-byte BRSH section.");
        }

        Brushes = MemoryMarshal.Cast<byte, ScmapBrushRecord>(
            section.Slice(ScmapFormat.BrushSourceHeaderSize, (int)brushBytes));

        Planes = MemoryMarshal.Cast<byte, Plane>(section.Slice((int)planeStart, (int)planeBytes));
        Faces = MemoryMarshal.Cast<byte, ScmapFaceRecord>(section.Slice((int)faceStart, (int)faceBytes));

        for (int i = 0; i < Brushes.Length; i++)
        {
            ScmapBrushRecord brush = Brushes[i];

            if (brush.NodeIndex >= (uint)nodeCount)
            {
                throw new ScmapFormatException(
                    $"'{source}' brush {i} names node {brush.NodeIndex} of a {nodeCount}-node map. A brush " +
                    "that cannot name its node has no transform, so it would be carved at the origin.");
            }

            if (brush.PlaneCount < 4)
            {
                throw new ScmapFormatException(
                    $"'{source}' brush {i} declares {brush.PlaneCount} planes, and fewer than four half-spaces " +
                    "bound no volume at all.");
            }

            if ((long)brush.PlaneStart + brush.PlaneCount > planeCount)
            {
                throw new ScmapFormatException(
                    $"'{source}' brush {i} claims planes [{brush.PlaneStart}, " +
                    $"{brush.PlaneStart + brush.PlaneCount}) of a {planeCount}-plane table.");
            }
        }
    }

    /// <summary>One record per authored brush the cook kept, in node pre-order.</summary>
    public ReadOnlySpan<ScmapBrushRecord> Brushes { get; }

    /// <summary>Every brush's planes, concatenated. Brush-local, as authored.</summary>
    public ReadOnlySpan<Plane> Planes { get; }

    /// <summary>Every brush's faces, concatenated and index-aligned with <see cref="Planes"/>.</summary>
    public ReadOnlySpan<ScmapFaceRecord> Faces { get; }

    /// <summary>
    /// Whether a loader may carve this node's brush into the live world. False for
    /// a brush already baked into the chunks; carving it again draws it twice.
    /// </summary>
    public static bool IsReCarvable(in ScmapNodeRecord node) => !node.BakedIntoChunks;
}
