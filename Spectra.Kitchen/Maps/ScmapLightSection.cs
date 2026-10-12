using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Maps;

// Builds LGHT: one record per light, in node order.
internal static class ScmapLightSection
{
    public static byte[] Build(IReadOnlyList<ScmapLightSource> lights, int nodeCount)
    {
        var body = new byte[ScmapFormat.LightPreambleSize + (lights.Count * ScmapFormat.LightRecordSize)];
        Span<byte> span = body;

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)lights.Count);

        for (int i = 0; i < lights.Count; i++)
        {
            ScmapLightSource light = lights[i];

            if (light.NodeIndex < 0 || light.NodeIndex >= nodeCount)
            {
                throw new InvalidOperationException(
                    $"A light names node {light.NodeIndex} of a {nodeCount}-node map. A light that cannot " +
                    "name its node has no position and no direction.");
            }

            var record = new ScmapLightRecord((uint)light.NodeIndex, light.Light);
            MemoryMarshal.Write(
                span[(ScmapFormat.LightPreambleSize + (i * ScmapFormat.LightRecordSize))..], in record);
        }

        return body;
    }
}
