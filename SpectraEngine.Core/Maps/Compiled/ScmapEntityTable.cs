using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Maps.Compiled;

// Reads and validates ENTT with its keyvalues, and ECON, in place.
internal static class ScmapEntityTable
{
    public static ReadOnlySpan<ScmapConnectionRecord> ReadConnections(
        string source,
        ReadOnlySpan<byte> section,
        ScmapStringTable strings)
    {
        if (section.Length < ScmapFormat.ConnectionPreambleSize)
        {
            throw new ScmapFormatException(
                $"'{source}' has a {section.Length}-byte ECON section, short of the " +
                $"{ScmapFormat.ConnectionPreambleSize}-byte preamble that carries its connection count.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(section);
        long end = ScmapFormat.ConnectionPreambleSize + ((long)count * ScmapFormat.ConnectionRecordSize);
        if (end > section.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {count} connections, whose {ScmapFormat.ConnectionRecordSize}-byte " +
                $"records would end at byte {end} of a {section.Length}-byte ECON section.");
        }

        ReadOnlySpan<ScmapConnectionRecord> connections = MemoryMarshal.Cast<byte, ScmapConnectionRecord>(
            section.Slice(ScmapFormat.ConnectionPreambleSize, (int)count * ScmapFormat.ConnectionRecordSize));

        var stringCount = (uint)strings.Count;
        for (int i = 0; i < connections.Length; i++)
        {
            ref readonly ScmapConnectionRecord wire = ref connections[i];

            if (wire.OutputNameString >= stringCount || wire.TargetNameString >= stringCount ||
                wire.InputNameString >= stringCount || wire.ParameterString >= stringCount)
            {
                throw new ScmapFormatException(
                    $"'{source}' connection {i} names a string outside the {strings.Count}-string table.");
            }
        }

        return connections;
    }

    public static ReadOnlySpan<ScmapEntityRecord> Read(
        string source,
        ReadOnlySpan<byte> section,
        ScmapStringTable strings,
        int nodeCount,
        int connectionCount,
        out ReadOnlySpan<ScmapKeyvalueRecord> keyvalues)
    {
        if (section.Length < ScmapFormat.EntityPreambleSize)
        {
            throw new ScmapFormatException(
                $"'{source}' has a {section.Length}-byte ENTT section, short of the " +
                $"{ScmapFormat.EntityPreambleSize}-byte preamble that carries its counts.");
        }

        uint entityCount = BinaryPrimitives.ReadUInt32LittleEndian(section);
        uint keyvalueCount = BinaryPrimitives.ReadUInt32LittleEndian(section[4..]);

        long entityBytes = (long)entityCount * ScmapFormat.EntityRecordSize;
        long keyvalueStart = ScmapFormat.AlignUp(
            ScmapFormat.EntityPreambleSize + entityBytes, ScmapFormat.PayloadAlignment);
        long keyvalueBytes = (long)keyvalueCount * ScmapFormat.KeyvalueRecordSize;

        if (keyvalueStart + keyvalueBytes > section.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {entityCount} entities and {keyvalueCount} keyvalues, whose records " +
                $"would end at byte {keyvalueStart + keyvalueBytes} of a {section.Length}-byte ENTT section.");
        }

        ReadOnlySpan<ScmapEntityRecord> entities = MemoryMarshal.Cast<byte, ScmapEntityRecord>(
            section.Slice(ScmapFormat.EntityPreambleSize, (int)entityBytes));

        keyvalues = MemoryMarshal.Cast<byte, ScmapKeyvalueRecord>(
            section.Slice((int)keyvalueStart, (int)keyvalueBytes));

        var stringCount = (uint)strings.Count;
        for (int i = 0; i < keyvalues.Length; i++)
        {
            if (keyvalues[i].KeyString >= stringCount || keyvalues[i].ValueString >= stringCount)
            {
                throw new ScmapFormatException(
                    $"'{source}' keyvalue {i} names a string outside the {strings.Count}-string table.");
            }
        }

        RequireEntityRecords(source, entities, strings.Count, nodeCount, keyvalues.Length, connectionCount);
        return entities;
    }

    private static void RequireEntityRecords(
        string source,
        ReadOnlySpan<ScmapEntityRecord> entities,
        int stringCount,
        int nodeCount,
        int keyvalueCount,
        int connectionCount)
    {
        for (int i = 0; i < entities.Length; i++)
        {
            ref readonly ScmapEntityRecord entity = ref entities[i];

            if (entity.NodeIndex >= (uint)nodeCount)
            {
                throw new ScmapFormatException(
                    $"'{source}' entity {i} names node {entity.NodeIndex} of a {nodeCount}-node map.");
            }

            // The loader attaches entities in one pass over the nodes.
            if (i > 0 && entities[i - 1].NodeIndex >= entity.NodeIndex)
            {
                throw new ScmapFormatException(
                    $"'{source}' entity records are not in ascending node order at record {i}: node " +
                    $"{entities[i - 1].NodeIndex} is followed by node {entity.NodeIndex}.");
            }

            if (entity.ClassNameString >= (uint)stringCount)
            {
                throw new ScmapFormatException(
                    $"'{source}' entity {i} names string {entity.ClassNameString} of a {stringCount}-string " +
                    "table as its class.");
            }

            if ((ulong)entity.KeyvalueStart + entity.KeyvalueCount > (ulong)keyvalueCount)
            {
                throw new ScmapFormatException(
                    $"'{source}' entity {i} claims keyvalues [{entity.KeyvalueStart}, " +
                    $"{(ulong)entity.KeyvalueStart + entity.KeyvalueCount}) of a {keyvalueCount}-keyvalue " +
                    "table.");
            }

            if ((ulong)entity.ConnectionStart + entity.ConnectionCount > (ulong)connectionCount)
            {
                throw new ScmapFormatException(
                    $"'{source}' entity {i} claims connections [{entity.ConnectionStart}, " +
                    $"{(ulong)entity.ConnectionStart + entity.ConnectionCount}) of a {connectionCount}-connection " +
                    "table.");
            }
        }
    }
}
