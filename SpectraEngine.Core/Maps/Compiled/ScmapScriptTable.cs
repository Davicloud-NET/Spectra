using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Packs;

namespace SpectraEngine.Core.Maps.Compiled;

// Reads and validates SCPT in place.
internal static class ScmapScriptTable
{
    public static ReadOnlySpan<ScmapScriptRecord> Read(
        string source,
        ReadOnlySpan<byte> section,
        int nodeCount,
        ReadOnlySpan<ScmapAssetEntry> assets,
        ScmapStringTable strings)
    {
        if (section.Length < ScmapFormat.ScriptPreambleSize)
        {
            throw new ScmapFormatException(
                $"'{source}' has a {section.Length}-byte SCPT section, short of the " +
                $"{ScmapFormat.ScriptPreambleSize}-byte preamble that carries its script count.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(section);
        long end = ScmapFormat.ScriptPreambleSize + ((long)count * ScmapFormat.ScriptRecordSize);
        if (end > section.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {count} scripts, whose {ScmapFormat.ScriptRecordSize}-byte records would " +
                $"end at byte {end} of a {section.Length}-byte SCPT section.");
        }

        ReadOnlySpan<ScmapScriptRecord> scripts = MemoryMarshal.Cast<byte, ScmapScriptRecord>(
            section.Slice(ScmapFormat.ScriptPreambleSize, (int)count * ScmapFormat.ScriptRecordSize));

        for (int i = 0; i < scripts.Length; i++)
        {
            ref readonly ScmapScriptRecord script = ref scripts[i];

            if (script.NodeIndex >= (uint)nodeCount)
            {
                throw new ScmapFormatException(
                    $"'{source}' script {i} names node {script.NodeIndex} of a {nodeCount}-node map.");
            }

            // The loader attaches scripts in one pass over the nodes.
            if (i > 0 && scripts[i - 1].NodeIndex >= script.NodeIndex)
            {
                throw new ScmapFormatException(
                    $"'{source}' script records are not in ascending node order at record {i}: node " +
                    $"{scripts[i - 1].NodeIndex} is followed by node {script.NodeIndex}.");
            }

            if (script.KindRaw is not (ScmapScriptRecord.ScriptKind or ScmapScriptRecord.ModuleKind))
            {
                throw new ScmapFormatException(
                    $"'{source}' script {i} declares kind {script.KindRaw}, which this engine has no meaning " +
                    $"for at .scmap format version {EngineInfo.CompiledMapFormatVersion}. Recook the map.");
            }

            RequireFile(source, i, in script, assets, strings);
        }

        return scripts;
    }

    // The loader makes a Script from the path, and that throws on one that
    // is no content path. Refused here, a load never ends half way.
    private static void RequireFile(
        string source,
        int index,
        scoped in ScmapScriptRecord script,
        ReadOnlySpan<ScmapAssetEntry> assets,
        ScmapStringTable strings)
    {
        if (script.AssetIndex >= (uint)assets.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' script {index} names asset {script.AssetIndex} of a {assets.Length}-asset table.");
        }

        ScmapAssetEntry file = assets[(int)script.AssetIndex];
        if (file.AssetKind != PackEntryKind.Script)
        {
            throw new ScmapFormatException(
                $"'{source}' script {index} names asset {script.AssetIndex}, which is a {file.AssetKind} and " +
                "not a script.");
        }

        string path = strings.GetStringOrEmpty((int)file.PathString);
        try
        {
            _ = ContentRoot.NormalizeRelativePath(path);
        }
        catch (ArgumentException)
        {
            throw new ScmapFormatException(
                $"'{source}' script {index} names the file '{path}', which is no path inside the content root.");
        }
    }
}
