using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Maps.Compiled;

// Reads and validates COLL in place.
internal static class ScmapCollisionTable
{
    public static ReadOnlySpan<ScmapHullRecord> Read(
        string source,
        ReadOnlySpan<byte> section,
        ReadOnlySpan<ScmapNodeRecord> nodes,
        ScmapStringTable strings,
        out ReadOnlySpan<Plane> planes)
    {
        if (section.Length < ScmapFormat.CollisionPreambleSize)
        {
            throw new ScmapFormatException(
                $"'{source}' has a {section.Length}-byte COLL section, short of the " +
                $"{ScmapFormat.CollisionPreambleSize}-byte preamble that carries its counts.");
        }

        uint hullCount = BinaryPrimitives.ReadUInt32LittleEndian(section);
        uint planeCount = BinaryPrimitives.ReadUInt32LittleEndian(section[4..]);

        // 16-byte records after a 16-byte preamble: the planes need no padding.
        long hullBytes = (long)hullCount * ScmapFormat.HullRecordSize;
        long planeStart = ScmapFormat.CollisionPreambleSize + hullBytes;
        long planeBytes = (long)planeCount * ScmapFormat.PlaneSize;

        if (planeStart + planeBytes > section.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {hullCount} collision hulls over {planeCount} planes, whose records " +
                $"would end at byte {planeStart + planeBytes} of a {section.Length}-byte COLL section.");
        }

        ReadOnlySpan<ScmapHullRecord> hulls = MemoryMarshal.Cast<byte, ScmapHullRecord>(
            section.Slice(ScmapFormat.CollisionPreambleSize, (int)hullBytes));

        planes = MemoryMarshal.Cast<byte, Plane>(section.Slice((int)planeStart, (int)planeBytes));

        for (int i = 0; i < hulls.Length; i++)
        {
            ScmapHullRecord hull = hulls[i];

            if (hull.PlaneCount < ScmapFormat.MinimumHullPlanes)
            {
                throw new ScmapFormatException(
                    $"'{source}' collision hull {i} declares {hull.PlaneCount} planes, and fewer than " +
                    $"{ScmapFormat.MinimumHullPlanes} half-spaces bound no volume.");
            }

            if ((long)hull.PlaneStart + hull.PlaneCount > planeCount)
            {
                throw new ScmapFormatException(
                    $"'{source}' collision hull {i} claims planes [{hull.PlaneStart}, " +
                    $"{(long)hull.PlaneStart + hull.PlaneCount}) of a {planeCount}-plane table.");
            }
        }

        RequireOneHullPerBakedBrush(source, hulls, nodes, strings);
        return hulls;
    }

    // One cursor over both tables: hulls are in node order, like the nodes.
    private static void RequireOneHullPerBakedBrush(
        string source,
        ReadOnlySpan<ScmapHullRecord> hulls,
        ReadOnlySpan<ScmapNodeRecord> nodes,
        ScmapStringTable strings)
    {
        int next = 0;

        for (int i = 0; i < nodes.Length; i++)
        {
            if (!nodes[i].BakedIntoChunks) continue;

            if (next >= hulls.Length || hulls[next].NodeIndex > (uint)i)
            {
                throw new ScmapFormatException(
                    $"'{source}' node {i} ('{strings.GetStringOrEmpty((int)nodes[i].NameString)}') is baked " +
                    "into the level and has no collision hull. A character would walk through it. Recook " +
                    "the map.");
            }

            if (hulls[next].NodeIndex < (uint)i) break;

            next++;
        }

        if (next < hulls.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' collision hull {next} names node {hulls[next].NodeIndex}, which is not the next " +
                "baked world brush. There is one hull per baked brush, in node order.");
        }
    }
}
