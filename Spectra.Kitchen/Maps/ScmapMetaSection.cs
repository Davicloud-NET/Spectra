using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Maps;

// Builds META: the scene's name, then one record per spawn point.
internal static class ScmapMetaSection
{
    public static byte[] Build(uint sceneNameString, IReadOnlyList<ScmapSpawnSource> spawns)
    {
        var body = new byte[ScmapFormat.MetaPreambleSize + (spawns.Count * ScmapFormat.SpawnRecordSize)];
        Span<byte> span = body;

        var meta = new ScmapMeta(sceneNameString, (uint)spawns.Count);
        MemoryMarshal.Write(span, in meta);

        for (int i = 0; i < spawns.Count; i++)
        {
            var record = new ScmapSpawn(spawns[i].Position, spawns[i].Rotation);
            MemoryMarshal.Write(span[(ScmapFormat.MetaPreambleSize + (i * ScmapFormat.SpawnRecordSize))..], in record);
        }

        return body;
    }
}
