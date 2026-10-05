using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Maps.Compiled;

// Reads and validates LGHT in place.
internal static class ScmapLightTable
{
    public static ReadOnlySpan<ScmapLightRecord> Read(string source, ReadOnlySpan<byte> section, int nodeCount)
    {
        if (section.Length < ScmapFormat.LightPreambleSize)
        {
            throw new ScmapFormatException(
                $"'{source}' has a {section.Length}-byte LGHT section, short of the " +
                $"{ScmapFormat.LightPreambleSize}-byte preamble that carries its light count.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(section);
        long end = ScmapFormat.LightPreambleSize + ((long)count * ScmapFormat.LightRecordSize);
        if (end > section.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {count} lights, whose {ScmapFormat.LightRecordSize}-byte records would " +
                $"end at byte {end} of a {section.Length}-byte LGHT section.");
        }

        ReadOnlySpan<ScmapLightRecord> lights = MemoryMarshal.Cast<byte, ScmapLightRecord>(
            section.Slice(ScmapFormat.LightPreambleSize, (int)count * ScmapFormat.LightRecordSize));

        for (int i = 0; i < lights.Length; i++)
        {
            ref readonly ScmapLightRecord light = ref lights[i];

            if (light.NodeIndex >= (uint)nodeCount)
            {
                throw new ScmapFormatException(
                    $"'{source}' light {i} names node {light.NodeIndex} of a {nodeCount}-node map.");
            }

            // The loader attaches lights in one pass over the nodes.
            if (i > 0 && lights[i - 1].NodeIndex >= light.NodeIndex)
            {
                throw new ScmapFormatException(
                    $"'{source}' light records are not in ascending node order at record {i}: node " +
                    $"{lights[i - 1].NodeIndex} is followed by node {light.NodeIndex}.");
            }

            if (!ScmapLightRecord.TryDecodeKind(light.KindRaw, out _))
            {
                throw new ScmapFormatException(
                    $"'{source}' light {i} declares kind {light.KindRaw}, which this engine has no meaning " +
                    $"for at .scmap format version {EngineInfo.CompiledMapFormatVersion}. Recook the map.");
            }

            // Written as negations so a NaN is refused too. Light throws on both.
            if (!(light.Intensity >= 0f))
            {
                throw new ScmapFormatException(
                    $"'{source}' light {i} has an intensity of {light.Intensity}. A light's intensity cannot " +
                    "be negative.");
            }

            if (!(light.Range > 0f))
            {
                throw new ScmapFormatException(
                    $"'{source}' light {i} has a range of {light.Range}. A light's range must be positive.");
            }
        }

        return lights;
    }
}
