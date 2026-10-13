using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// Reads a compiled <c>.scmap</c> in place: tables come back as spans into the
/// input. Unknown sections are skipped; every version and compile constant must
/// match exactly.
/// </summary>
// The bytes are usually a memory-mapped pack view, where a read out of range is
// an access violation with no managed stack. Bounds-check every offset and
// length before use.
public static class ScmapReader
{
    /// <summary>
    /// Validates <paramref name="file"/> and returns its tables as spans into it.
    /// </summary>
    /// <param name="file">The whole file, header included.</param>
    /// <param name="source">The file's name in error messages: a logical asset path, not a machine path.</param>
    /// <exception cref="ScmapFormatException">The file is not a readable <c>.scmap</c>.</exception>
    public static ScmapDocument Read(ReadOnlySpan<byte> file, string source)
    {
        ScmapHeader header = ReadHeader(file, source);
        ScmapSectionTable sections = ScmapSectionTable.Read(file, in header, source);

        var strings = new ScmapStringTable(sections.Body(file, ScmapSectionTable.Strings), source);

        ReadOnlySpan<ScmapAssetEntry> assets = ReadAssets(
            source, sections.Body(file, ScmapSectionTable.Asset), strings);

        ScmapMeta meta = ReadMeta(
            source, sections.Body(file, ScmapSectionTable.Meta), strings, out ReadOnlySpan<ScmapSpawn> spawns);

        ReadOnlySpan<ScmapNodeRecord> nodes = ReadNodes(
            source, sections.Body(file, ScmapSectionTable.Node), strings, out int invalidDeclaredStates);

        ReadOnlySpan<ScmapConnectionRecord> connections = ScmapEntityTable.ReadConnections(
            source, sections.Body(file, ScmapSectionTable.Connection), strings);

        ReadOnlySpan<ScmapEntityRecord> entities = ScmapEntityTable.Read(
            source, sections.Body(file, ScmapSectionTable.Entity), strings, nodes.Length, connections.Length,
            out ReadOnlySpan<ScmapKeyvalueRecord> keyvalues);

        ReadOnlySpan<ScmapHullRecord> hulls = ScmapCollisionTable.Read(
            source, sections.Body(file, ScmapSectionTable.Collision), nodes, strings,
            out ReadOnlySpan<Plane> hullPlanes);

        // Optional: a map cooked without it loads, and its hulls name no material.
        ReadOnlySpan<uint> hullFaceAssets = sections.Has(ScmapSectionTable.HullMaterial)
            ? ScmapHullMaterialTable.Read(
                source, sections.Body(file, ScmapSectionTable.HullMaterial), hullPlanes.Length, assets)
            : default;

        ReadOnlySpan<ScmapLightRecord> lights = ScmapLightTable.Read(
            source, sections.Body(file, ScmapSectionTable.Light), nodes.Length);

        ReadOnlySpan<ScmapScriptRecord> scripts = ScmapScriptTable.Read(
            source, sections.Body(file, ScmapSectionTable.Script), nodes.Length, assets, strings);

        // Optional: a map cooked before them, or with none, has no node with a
        // tag or an attribute.
        ReadOnlySpan<ScmapTagRecord> nodeTags = sections.Has(ScmapSectionTable.NodeTag)
            ? ScmapTagTable.Read(source, sections.Body(file, ScmapSectionTable.NodeTag), nodes.Length, strings)
            : default;

        ReadOnlySpan<ScmapAttributeRecord> nodeAttributes = sections.Has(ScmapSectionTable.NodeAttribute)
            ? ScmapAttributeTable.Read(
                source, sections.Body(file, ScmapSectionTable.NodeAttribute), nodes.Length, strings)
            : default;

        // An absent blob section is an empty span.
        ReadOnlySpan<byte> meshBlob = sections.Body(file, ScmapSectionTable.ChunkMesh);
        ReadOnlySpan<byte> bspBlob = sections.Body(file, ScmapSectionTable.ChunkBsp);

        ReadOnlySpan<ScmapChunkRecord> chunks = ScmapChunkTable.Read(
            source, sections.Body(file, ScmapSectionTable.Chunk), meshBlob.Length, bspBlob.Length);

        return new ScmapDocument(
            source, header, strings, assets, meta, spawns, nodes,
            entities, keyvalues, connections,
            hulls, hullPlanes, hullFaceAssets, sections.Has(ScmapSectionTable.HullMaterial),
            lights, scripts, nodeTags, nodeAttributes,
            chunks, meshBlob, bspBlob, sections.OffsetOf(ScmapSectionTable.ChunkBsp),
            sections.Body(file, ScmapSectionTable.BrushSource), sections.Has(ScmapSectionTable.BrushSource),
            sections.SkippedCount, invalidDeclaredStates);
    }

    // The header, with every version and layout it states checked against
    // this engine's. The section table is not read here.
    private static ScmapHeader ReadHeader(ReadOnlySpan<byte> file, string source)
    {
        ScmapFormat.RequireLittleEndian();

        // BSP blobs are cast at this struct's stride, so check the layout first.
        ScmapChunkBsp.RequireNodeLayout();

        if (file.Length < ScmapFormat.MinimumFileSize)
        {
            throw new ScmapFormatException(
                $"'{source}' is {file.Length} bytes, too short to hold a " +
                $"{ScmapFormat.HeaderSize}-byte .scmap header.");
        }

        ScmapHeader header = MemoryMarshal.Read<ScmapHeader>(file);

        if (header.Magic != ScmapFormat.Magic)
        {
            throw new ScmapFormatException(
                $"'{source}' is not a .scmap file: its first four bytes read " +
                $"'{ScmapFormat.DescribeFourCc(header.Magic)}', not 'SCMP'.");
        }

        if (header.FormatVersion != EngineInfo.CompiledMapFormatVersion)
        {
            // Exact match, not a floor: a compiled map is a build output, recook it.
            throw new ScmapFormatException(
                $"'{source}' is .scmap format version {header.FormatVersion}, and this engine reads version " +
                $"{EngineInfo.CompiledMapFormatVersion}. Recook the map.");
        }

        if (header.HeaderSize != ScmapFormat.HeaderSize)
        {
            throw new ScmapFormatException(
                $"'{source}' declares a {header.HeaderSize}-byte header at format version " +
                $"{header.FormatVersion}, which this engine writes as {ScmapFormat.HeaderSize} bytes. " +
                "A wrong header size reads the section table out of the middle of the header.");
        }

        if (header.GeometryFormatVersion != EngineInfo.GeometryFormatVersion)
        {
            // The container can be unchanged while the vertex buffer's meaning moved.
            throw new ScmapFormatException(
                $"'{source}' was cooked at geometry format version {header.GeometryFormatVersion}, and this " +
                $"engine reads version {EngineInfo.GeometryFormatVersion}. Recook the map.");
        }

        if (header.VertexLayoutId != ScmapFormat.StandardVertexLayoutId)
        {
            throw new ScmapFormatException(
                $"'{source}' was cooked for vertex layout {header.VertexLayoutId:X8}, and this engine's " +
                $"standard layout is {ScmapFormat.StandardVertexLayoutId:X8}. Recook the map.");
        }

        return header;
    }

    private static ReadOnlySpan<ScmapAssetEntry> ReadAssets(
        string source,
        ReadOnlySpan<byte> section,
        ScmapStringTable strings)
    {
        if (section.Length < ScmapFormat.AssetCountSize)
        {
            throw new ScmapFormatException(
                $"'{source}' has a {section.Length}-byte ASTB section, too short to hold its own entry count.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(section);
        long end = ScmapFormat.AssetCountSize + ((long)count * ScmapFormat.AssetEntrySize);
        if (end > section.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {count} assets, whose {ScmapFormat.AssetEntrySize}-byte records would " +
                $"end at byte {end} of a {section.Length}-byte ASTB section.");
        }

        ReadOnlySpan<ScmapAssetEntry> entries = MemoryMarshal.Cast<byte, ScmapAssetEntry>(
            section.Slice(ScmapFormat.AssetCountSize, (int)count * ScmapFormat.AssetEntrySize));

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].PathString >= (uint)strings.Count)
            {
                throw new ScmapFormatException(
                    $"'{source}' asset {i} names string {entries[i].PathString} of a {strings.Count}-string " +
                    "table. An asset that cannot name its own path is one every material referencing it " +
                    "would resolve to the placeholder, which renders as magenta rather than as an error.");
            }
        }

        return entries;
    }

    private static ScmapMeta ReadMeta(
        string source,
        ReadOnlySpan<byte> section,
        ScmapStringTable strings,
        out ReadOnlySpan<ScmapSpawn> spawns)
    {
        if (section.Length < ScmapFormat.MetaPreambleSize)
        {
            throw new ScmapFormatException(
                $"'{source}' has a {section.Length}-byte META section, short of the " +
                $"{ScmapFormat.MetaPreambleSize}-byte preamble every compiled map carries.");
        }

        ScmapMeta meta = MemoryMarshal.Read<ScmapMeta>(section);

        RequireCompileConstant(source, "cell size", meta.CellSize, ScmapFormat.EngineCellSize,
            "every point and ray query would be routed against a directory built for another lattice, " +
            "which reads as sporadic collision bugs rather than as a version problem");

        RequireCompileConstant(source, "weld band", meta.WeldBand, ScmapFormat.EngineWeldBand,
            "cell borders would be welded across a different band, which reads as seams exactly where two " +
            "cells meet");

        RequireCompileConstant(source, "snap grid", meta.SnapGrid, ScmapFormat.EngineSnapGrid,
            "vertices were quantised to another lattice, which reads as hairline cracks rather than as a " +
            "version problem");

        if (meta.SceneNameString >= (uint)strings.Count)
        {
            throw new ScmapFormatException(
                $"'{source}' names its scene with string {meta.SceneNameString} of a {strings.Count}-string " +
                "table.");
        }

        long spawnEnd = ScmapFormat.MetaPreambleSize + ((long)meta.SpawnCount * ScmapFormat.SpawnRecordSize);
        if (spawnEnd > section.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {meta.SpawnCount} spawns, whose records would end at byte {spawnEnd} " +
                $"of a {section.Length}-byte META section.");
        }

        spawns = MemoryMarshal.Cast<byte, ScmapSpawn>(
            section.Slice(ScmapFormat.MetaPreambleSize, (int)meta.SpawnCount * ScmapFormat.SpawnRecordSize));

        return meta;
    }

    private static ReadOnlySpan<ScmapNodeRecord> ReadNodes(
        string source,
        ReadOnlySpan<byte> section,
        ScmapStringTable strings,
        out int invalidDeclaredStates)
    {
        if (section.Length < ScmapFormat.NodePreambleSize)
        {
            throw new ScmapFormatException(
                $"'{source}' has a {section.Length}-byte NODE section, short of the " +
                $"{ScmapFormat.NodePreambleSize}-byte preamble that carries its node count.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(section);
        long end = ScmapFormat.NodePreambleSize + ((long)count * ScmapFormat.NodeRecordSize);
        if (end > section.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {count} nodes, whose {ScmapFormat.NodeRecordSize}-byte records would " +
                $"end at byte {end} of a {section.Length}-byte NODE section.");
        }

        ReadOnlySpan<ScmapNodeRecord> nodes = MemoryMarshal.Cast<byte, ScmapNodeRecord>(
            section.Slice(ScmapFormat.NodePreambleSize, (int)count * ScmapFormat.NodeRecordSize));

        invalidDeclaredStates = 0;

        for (int i = 0; i < nodes.Length; i++)
        {
            ref readonly ScmapNodeRecord node = ref nodes[i];

            if (node.NameString >= (uint)strings.Count)
            {
                throw new ScmapFormatException(
                    $"'{source}' node {i} names string {node.NameString} of a {strings.Count}-string table. " +
                    "A node's name is its target name, so a name that cannot be read is entity wiring that " +
                    "silently resolves to nothing.");
            }

            if (node.ParentIndex < -1 || node.ParentIndex >= i)
            {
                throw new ScmapFormatException(
                    $"'{source}' node {i} ('{strings.GetStringOrEmpty((int)node.NameString)}') names parent " +
                    $"{node.ParentIndex}. Records are pre-order, so a parent index is -1 or strictly less " +
                    "than the child's own; anything else is a cycle a forward-pass loader walks forever.");
            }

            if (node.PayloadKind == ScmapPayloadKind.RetiredBrushModel)
            {
                throw new ScmapFormatException(
                    $"'{source}' node {i} ('{strings.GetStringOrEmpty((int)node.NameString)}') declares " +
                    "payload kind 3, which is retired and carries no meaning. An entity-owned brush is a " +
                    "part brush wearing the entity-owned flag. Recook the map.");
            }

            if (!Enum.IsDefined(node.PayloadKind))
            {
                throw new ScmapFormatException(
                    $"'{source}' node {i} ('{strings.GetStringOrEmpty((int)node.NameString)}') declares " +
                    $"payload kind {node.PayloadKindRaw}, which this engine has no meaning for at .scmap " +
                    $"format version {EngineInfo.CompiledMapFormatVersion}. Recook the map.");
            }

            if (node.DeclaredState == ScmapNodeState.Invalid) invalidDeclaredStates++;
        }

        return nodes;
    }

    private static void RequireCompileConstant(
        string source,
        string what,
        float stored,
        float engine,
        string consequence)
    {
        // No tolerance: both sides are compile-time constants. Also refuses NaN.
        if (stored == engine) return;

        throw new ScmapFormatException(
            $"'{source}' was compiled with a {what} of {stored} and this engine's is {engine}. " +
            $"Loaded anyway, {consequence}. Recook the map.");
    }
}
