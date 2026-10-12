using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Maps;

// Builds SCPT: one record per script node, in node order. A map with no
// script still gets the section, with a count of zero.
internal static class ScmapScriptSection
{
    // Each script comes with the ASTB row of its file.
    public static byte[] Build(IReadOnlyList<(ScmapScriptSource Script, uint AssetIndex)> scripts, int nodeCount)
    {
        var body = new byte[ScmapFormat.ScriptPreambleSize + (scripts.Count * ScmapFormat.ScriptRecordSize)];
        BinaryPrimitives.WriteUInt32LittleEndian(body, (uint)scripts.Count);

        for (int i = 0; i < scripts.Count; i++)
        {
            (ScmapScriptSource script, uint assetIndex) = scripts[i];

            if (script.NodeIndex < 0 || script.NodeIndex >= nodeCount)
            {
                throw new InvalidOperationException(
                    $"A script names node {script.NodeIndex} of a {nodeCount}-node map. A script that cannot " +
                    "name its node has no parent to act on.");
            }

            var record = new ScmapScriptRecord(
                (uint)script.NodeIndex, assetIndex, script.IsEnabled, script.IsModule);

            MemoryMarshal.Write(
                body.AsSpan(ScmapFormat.ScriptPreambleSize + (i * ScmapFormat.ScriptRecordSize)), in record);
        }

        return body;
    }
}
