using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Maps;

// Builds ENTT, and ECON beside it. Interns per entity: its class, its keys
// and values, then each wire's output, target, input and parameter. That
// order is part of the file's bytes.
internal static class ScmapEntitySection
{
    public static byte[] Build(
        IReadOnlyList<ScmapEntitySource> entities,
        int nodeCount,
        ScmapStringTableBuilder strings,
        out byte[] connectionBody)
    {
        CountRecords(entities, nodeCount, out int keyvalueCount, out int connectionCount);

        long keyvalueStart = ScmapLayout.PaddedSectionSize(
            ScmapFormat.EntityPreambleSize + ((long)entities.Count * ScmapFormat.EntityRecordSize));

        var body = new byte[keyvalueStart + ((long)keyvalueCount * ScmapFormat.KeyvalueRecordSize)];
        connectionBody = new byte[
            ScmapFormat.ConnectionPreambleSize + ((long)connectionCount * ScmapFormat.ConnectionRecordSize)];

        BinaryPrimitives.WriteUInt32LittleEndian(body, (uint)entities.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), (uint)keyvalueCount);
        BinaryPrimitives.WriteUInt32LittleEndian(connectionBody, (uint)connectionCount);

        int keyvalue = 0;
        int connection = 0;
        for (int i = 0; i < entities.Count; i++)
        {
            ScmapEntitySource entity = entities[i];

            var record = new ScmapEntityRecord(
                (uint)entity.NodeIndex,
                strings.Intern(entity.ClassName),
                (uint)keyvalue,
                (uint)entity.Keyvalues.Length,
                (uint)connection,
                (uint)entity.Connections.Length);

            MemoryMarshal.Write(
                body.AsSpan(ScmapFormat.EntityPreambleSize + (i * ScmapFormat.EntityRecordSize)), in record);

            foreach (KeyValuePair<string, string> pair in entity.Keyvalues)
            {
                var pairRecord = new ScmapKeyvalueRecord(strings.Intern(pair.Key), strings.Intern(pair.Value));
                MemoryMarshal.Write(
                    body.AsSpan((int)keyvalueStart + (keyvalue++ * ScmapFormat.KeyvalueRecordSize)), in pairRecord);
            }

            foreach (EntityConnection wire in entity.Connections)
            {
                var wireRecord = new ScmapConnectionRecord(
                    strings.Intern(wire.Output),
                    strings.Intern(wire.TargetName),
                    strings.Intern(wire.Input),
                    strings.Intern(wire.Parameter),
                    wire.Delay,
                    wire.TimesToFire);

                MemoryMarshal.Write(
                    connectionBody.AsSpan(
                        ScmapFormat.ConnectionPreambleSize + (connection++ * ScmapFormat.ConnectionRecordSize)),
                    in wireRecord);
            }
        }

        return body;
    }

    private static void CountRecords(
        IReadOnlyList<ScmapEntitySource> entities, int nodeCount, out int keyvalues, out int connections)
    {
        keyvalues = 0;
        connections = 0;

        foreach (ScmapEntitySource entity in entities)
        {
            if (entity.NodeIndex < 0 || entity.NodeIndex >= nodeCount)
            {
                throw new InvalidOperationException(
                    $"An entity names node {entity.NodeIndex} of a {nodeCount}-node map. An entity that " +
                    "cannot name its node has no target name and no place in the level.");
            }

            keyvalues += entity.Keyvalues.Length;
            connections += entity.Connections.Length;
        }
    }
}
