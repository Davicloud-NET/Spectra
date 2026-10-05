using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Assets.Packs;

namespace SpectraEngine.Core.Maps.Compiled;

// Reads and validates COLM in place: one asset index per plane of COLL.
internal static class ScmapHullMaterialTable
{
    public static ReadOnlySpan<uint> Read(
        string source,
        ReadOnlySpan<byte> section,
        int hullPlaneCount,
        ReadOnlySpan<ScmapAssetEntry> assets)
    {
        if (section.Length < ScmapFormat.HullMaterialPreambleSize)
        {
            throw new ScmapFormatException(
                $"'{source}' has a {section.Length}-byte COLM section, short of the " +
                $"{ScmapFormat.HullMaterialPreambleSize}-byte preamble that carries its face count.");
        }

        uint faceCount = BinaryPrimitives.ReadUInt32LittleEndian(section);

        // A face is found by its plane's index, so the two tables must line up.
        if (faceCount != (uint)hullPlaneCount)
        {
            throw new ScmapFormatException(
                $"'{source}' names materials for {faceCount} collision hull faces, and COLL has " +
                $"{hullPlaneCount} planes. There is one face per plane, in the same order.");
        }

        long end = ScmapFormat.HullMaterialPreambleSize + ((long)faceCount * ScmapFormat.HullMaterialRecordSize);
        if (end > section.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {faceCount} collision hull faces, whose records would end at byte " +
                $"{end} of a {section.Length}-byte COLM section.");
        }

        ReadOnlySpan<uint> faces = MemoryMarshal.Cast<byte, uint>(section.Slice(
            ScmapFormat.HullMaterialPreambleSize, (int)faceCount * ScmapFormat.HullMaterialRecordSize));

        for (int i = 0; i < faces.Length; i++)
        {
            uint row = faces[i];
            if (row == ScmapFormat.NoAssetIndex) continue;

            if (row >= (uint)assets.Length)
            {
                throw new ScmapFormatException(
                    $"'{source}' collision hull face {i} names asset {row} of a {assets.Length}-asset table.");
            }

            if (assets[(int)row].AssetKind != PackEntryKind.Material)
            {
                throw new ScmapFormatException(
                    $"'{source}' collision hull face {i} names asset {row}, which is a " +
                    $"{assets[(int)row].AssetKind} and not a material.");
            }
        }

        return faces;
    }
}
