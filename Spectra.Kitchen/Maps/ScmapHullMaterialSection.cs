using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Maps;

// Builds COLM: what each collision hull face is made of, one asset index per
// plane of COLL and in COLL's order.
internal static class ScmapHullMaterialSection
{
    // Null when no hull names its faces, so the section is left out.
    public static byte[]? Build(
        IReadOnlyList<ScmapCollisionHullSource> hulls, IReadOnlyList<ScmapAssetSource> assets)
    {
        int faces = 0;
        int named = 0;
        foreach (ScmapCollisionHullSource hull in hulls)
        {
            faces += hull.Planes.Length;
            if (hull.FaceAssets is not null) named++;
        }

        if (named == 0) return null;

        if (named != hulls.Count)
        {
            throw new InvalidOperationException(
                $"{named} of {hulls.Count} collision hulls name their faces' materials. A map says what " +
                "every hull is made of or says it for none, because one record stands for one plane.");
        }

        var body = new byte[
            ScmapFormat.HullMaterialPreambleSize + ((long)faces * ScmapFormat.HullMaterialRecordSize)];

        BinaryPrimitives.WriteUInt32LittleEndian(body, (uint)faces);

        int cursor = ScmapFormat.HullMaterialPreambleSize;
        foreach (ScmapCollisionHullSource hull in hulls)
        {
            // Never null here: the count above saw every hull name its faces.
            if (hull.FaceAssets is not { } rows) continue;

            foreach (uint row in rows)
            {
                RequireMaterial(row, hull.NodeIndex, assets);
                BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(cursor), row);
                cursor += ScmapFormat.HullMaterialRecordSize;
            }
        }

        return body;
    }

    private static void RequireMaterial(uint row, int nodeIndex, IReadOnlyList<ScmapAssetSource> assets)
    {
        if (row == ScmapFormat.NoAssetIndex) return;

        if (row >= (uint)assets.Count || assets[(int)row].Kind != PackEntryKind.Material)
        {
            throw new InvalidOperationException(
                $"The collision hull on node {nodeIndex} names asset {row} as a face material, which is " +
                $"not a material row of this map's {assets.Count}-row asset table.");
        }
    }
}
