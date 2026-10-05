using System;
using System.Buffers.Binary;
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
    private const int StringSlot = 0;
    private const int AssetSlot = 1;
    private const int MetaSlot = 2;
    private const int NodeSlot = 3;
    private const int ChunkSlot = 4;
    private const int ChunkMeshSlot = 5;
    private const int ChunkBspSlot = 6;
    private const int BrushSourceSlot = 7;
    private const int EntitySlot = 8;
    private const int ConnectionSlot = 9;
    private const int KnownSectionCount = 10;

    /// <summary>
    /// Validates <paramref name="file"/> and returns its tables as spans into it.
    /// </summary>
    /// <param name="file">The whole file, header included.</param>
    /// <param name="source">The file's name in error messages: a logical asset path, not a machine path.</param>
    /// <exception cref="ScmapFormatException">The file is not a readable <c>.scmap</c>.</exception>
    public static ScmapDocument Read(ReadOnlySpan<byte> file, string source)
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

        long tableEnd = ScmapFormat.SectionTableOffset + ((long)header.SectionCount * ScmapFormat.SectionSize);
        if (tableEnd > file.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {header.SectionCount} sections, whose {ScmapFormat.SectionSize}-byte " +
                $"table would end at byte {tableEnd} of a {file.Length}-byte file.");
        }

        Span<int> sectionOffset = stackalloc int[KnownSectionCount];
        Span<int> sectionLength = stackalloc int[KnownSectionCount];
        Span<bool> sectionPresent = stackalloc bool[KnownSectionCount];
        sectionPresent.Clear();

        int skipped = 0;
        for (uint i = 0; i < header.SectionCount; i++)
        {
            ScmapSection record = MemoryMarshal.Read<ScmapSection>(
                file[(ScmapFormat.SectionTableOffset + ((int)i * ScmapFormat.SectionSize))..]);

            // Checked for unknown sections too, so the skip rule cannot let a
            // malformed file through.
            RequireSectionInFile(source, record, file.Length);

            if ((record.Offset % ScmapFormat.PayloadAlignment) != 0)
            {
                throw new ScmapFormatException(
                    $"'{source}' section '{ScmapFormat.DescribeFourCc(record.Kind)}' starts at byte " +
                    $"{record.Offset}, which is not a multiple of {ScmapFormat.PayloadAlignment}. Payloads " +
                    "are reinterpreted in place, so an unaligned section start is a plane straddling a " +
                    "boundary.");
            }

            if ((record.SectionFlags & ScmapSectionFlags.Compressed) != 0)
            {
                throw new ScmapFormatException(
                    $"'{source}' section '{ScmapFormat.DescribeFourCc(record.Kind)}' is marked compressed. " +
                    "That flag is reserved and no cook sets it: compression and a mapped zero-copy read are " +
                    "mutually exclusive.");
            }

            int slot = KnownSlot(record.Kind);
            if (slot < 0)
            {
                skipped++;
                continue;
            }

            if (sectionPresent[slot])
            {
                throw new ScmapFormatException(
                    $"'{source}' carries section '{ScmapFormat.DescribeFourCc(record.Kind)}' more than once. " +
                    "A section names one region of the file, so a reader would have to choose, and choosing " +
                    "silently is how half a map comes from one copy and half from the other.");
            }

            sectionPresent[slot] = true;
            sectionOffset[slot] = (int)record.Offset;
            sectionLength[slot] = (int)record.Size;
        }

        RequireSection(source, sectionPresent, StringSlot, ScmapFormat.StringSection);
        RequireSection(source, sectionPresent, AssetSlot, ScmapFormat.AssetSection);
        RequireSection(source, sectionPresent, MetaSlot, ScmapFormat.MetaSection);
        RequireSection(source, sectionPresent, NodeSlot, ScmapFormat.NodeSection);
        RequireSection(source, sectionPresent, ChunkSlot, ScmapFormat.ChunkDirectorySection);
        RequireSection(source, sectionPresent, EntitySlot, ScmapFormat.EntitySection);
        RequireSection(source, sectionPresent, ConnectionSlot, ScmapFormat.EntityConnectionSection);

        // The header flag and the section table must agree about BRSH.
        if (((header.FileFlags & ScmapFlags.HasBrushSource) != 0) != sectionPresent[BrushSourceSlot])
        {
            throw new ScmapFormatException(
                $"'{source}' has its HasBrushSource header flag " +
                $"{((header.FileFlags & ScmapFlags.HasBrushSource) != 0 ? "set" : "clear")} and a BRSH section " +
                $"{(sectionPresent[BrushSourceSlot] ? "present" : "absent")}. The flag says the section is " +
                "there and nothing else, so the two can only disagree in a file nothing this engine wrote.");
        }

        var strings = new ScmapStringTable(
            file.Slice(sectionOffset[StringSlot], sectionLength[StringSlot]),
            source);

        ReadOnlySpan<ScmapAssetEntry> assets = ReadAssets(
            source,
            file.Slice(sectionOffset[AssetSlot], sectionLength[AssetSlot]),
            strings);

        ScmapMeta meta = ReadMeta(
            source,
            file.Slice(sectionOffset[MetaSlot], sectionLength[MetaSlot]),
            strings,
            out ReadOnlySpan<ScmapSpawn> spawns);

        ReadOnlySpan<ScmapNodeRecord> nodes = ReadNodes(
            source,
            file.Slice(sectionOffset[NodeSlot], sectionLength[NodeSlot]),
            strings,
            out int invalidDeclaredStates);

        ReadOnlySpan<ScmapConnectionRecord> connections = ReadConnections(
            source,
            file.Slice(sectionOffset[ConnectionSlot], sectionLength[ConnectionSlot]),
            strings);

        ReadOnlySpan<ScmapEntityRecord> entities = ReadEntities(
            source,
            file.Slice(sectionOffset[EntitySlot], sectionLength[EntitySlot]),
            strings,
            nodes.Length,
            connections.Length,
            out ReadOnlySpan<ScmapKeyvalueRecord> keyvalues);

        ReadOnlySpan<byte> meshBlob = sectionPresent[ChunkMeshSlot]
            ? file.Slice(sectionOffset[ChunkMeshSlot], sectionLength[ChunkMeshSlot])
            : default;

        ReadOnlySpan<byte> bspBlob = sectionPresent[ChunkBspSlot]
            ? file.Slice(sectionOffset[ChunkBspSlot], sectionLength[ChunkBspSlot])
            : default;

        ReadOnlySpan<ScmapChunkRecord> chunks = ReadChunks(
            source,
            file.Slice(sectionOffset[ChunkSlot], sectionLength[ChunkSlot]),
            meshBlob.Length,
            bspBlob.Length);

        ReadOnlySpan<byte> brushSource = sectionPresent[BrushSourceSlot]
            ? file.Slice(sectionOffset[BrushSourceSlot], sectionLength[BrushSourceSlot])
            : default;

        return new ScmapDocument(
            source,
            header,
            strings,
            assets,
            meta,
            spawns,
            nodes,
            entities,
            keyvalues,
            connections,
            chunks,
            meshBlob,
            bspBlob,
            sectionPresent[ChunkBspSlot] ? sectionOffset[ChunkBspSlot] : 0,
            brushSource,
            sectionPresent[BrushSourceSlot],
            skipped,
            invalidDeclaredStates);
    }

    private static int KnownSlot(uint fourCc) => fourCc switch
    {
        ScmapFormat.StringSection => StringSlot,
        ScmapFormat.AssetSection => AssetSlot,
        ScmapFormat.MetaSection => MetaSlot,
        ScmapFormat.NodeSection => NodeSlot,
        ScmapFormat.ChunkDirectorySection => ChunkSlot,
        ScmapFormat.ChunkMeshSection => ChunkMeshSlot,
        ScmapFormat.ChunkBspSection => ChunkBspSlot,
        ScmapFormat.BrushSourceSection => BrushSourceSlot,
        ScmapFormat.EntitySection => EntitySlot,
        ScmapFormat.EntityConnectionSection => ConnectionSlot,

        // Reserved codes with no consumer yet (SCPT, LUAB, LUAS, NBND, RGNI,
        // BMDL) are skipped like any unknown one.
        _ => -1,
    };

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

    private static ReadOnlySpan<ScmapConnectionRecord> ReadConnections(
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

    private static ReadOnlySpan<ScmapEntityRecord> ReadEntities(
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

    private static ReadOnlySpan<ScmapChunkRecord> ReadChunks(
        string source,
        ReadOnlySpan<byte> section,
        int meshBlobLength,
        int bspBlobLength)
    {
        if (section.Length < ScmapFormat.ChunkPreambleSize)
        {
            throw new ScmapFormatException(
                $"'{source}' has a {section.Length}-byte CHDR section, short of the " +
                $"{ScmapFormat.ChunkPreambleSize}-byte preamble that carries its chunk count.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(section);
        long end = ScmapFormat.ChunkPreambleSize + ((long)count * ScmapFormat.ChunkRecordSize);
        if (end > section.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {count} chunks, whose {ScmapFormat.ChunkRecordSize}-byte records would " +
                $"end at byte {end} of a {section.Length}-byte CHDR section.");
        }

        ReadOnlySpan<ScmapChunkRecord> chunks = MemoryMarshal.Cast<byte, ScmapChunkRecord>(
            section.Slice(ScmapFormat.ChunkPreambleSize, (int)count * ScmapFormat.ChunkRecordSize));

        for (int i = 0; i < chunks.Length; i++)
        {
            ref readonly ScmapChunkRecord cell = ref chunks[i];

            if (i > 0 && Compare(in chunks[i - 1], in cell) >= 0)
            {
                // Cell lookup is a binary search, which misses cells in an
                // unsorted directory.
                throw new ScmapFormatException(
                    $"'{source}' chunk directory is not in ascending cell order at record {i}: " +
                    $"({chunks[i - 1].X}, {chunks[i - 1].Y}, {chunks[i - 1].Z}) is followed by " +
                    $"({cell.X}, {cell.Y}, {cell.Z}).");
            }

            RequireBlobRange(source, cell, "mesh", cell.MeshOffset, cell.MeshSize, meshBlobLength, "CMSH");
            RequireBlobRange(source, cell, "BSP", cell.BspOffset, cell.BspSize, bspBlobLength, "CBSP");
        }

        return chunks;
    }

    private static int Compare(in ScmapChunkRecord a, in ScmapChunkRecord b)
    {
        int c = a.X.CompareTo(b.X);
        if (c != 0) return c;
        c = a.Y.CompareTo(b.Y);
        return c != 0 ? c : a.Z.CompareTo(b.Z);
    }

    private static void RequireBlobRange(
        string source,
        in ScmapChunkRecord cell,
        string what,
        uint offset,
        uint size,
        int blobLength,
        string sectionName)
    {
        // Zero is legal: a cell can own no render geometry.
        if (size == 0) return;

        if ((long)offset + size > blobLength)
        {
            throw new ScmapFormatException(
                $"'{source}' chunk ({cell.X}, {cell.Y}, {cell.Z}) claims a {size}-byte {what} blob at offset " +
                $"{offset} of a {blobLength}-byte {sectionName} section.");
        }

        if ((offset % ScmapFormat.PayloadAlignment) != 0)
        {
            throw new ScmapFormatException(
                $"'{source}' chunk ({cell.X}, {cell.Y}, {cell.Z}) places its {what} blob at offset {offset}, " +
                $"which is not a multiple of {ScmapFormat.PayloadAlignment}.");
        }
    }

    private static void RequireSectionInFile(string source, in ScmapSection record, int fileLength)
    {
        // Subtract, don't add: offset + size can wrap in a corrupt file.
        if (record.Offset > (ulong)fileLength || record.Size > (ulong)fileLength - record.Offset)
        {
            throw new ScmapFormatException(
                $"'{source}' section '{ScmapFormat.DescribeFourCc(record.Kind)}' claims {record.Size} bytes " +
                $"at offset {record.Offset}, which runs past the {fileLength}-byte file.");
        }

        if (record.UncompressedSize != record.Size)
        {
            throw new ScmapFormatException(
                $"'{source}' section '{ScmapFormat.DescribeFourCc(record.Kind)}' declares {record.Size} " +
                $"stored bytes and {record.UncompressedSize} decoded ones while claiming no codec.");
        }
    }

    private static void RequireSection(string source, ReadOnlySpan<bool> present, int slot, uint fourCc)
    {
        if (present[slot]) return;

        throw new ScmapFormatException(
            $"'{source}' has no '{ScmapFormat.DescribeFourCc(fourCc)}' section, which every .scmap must " +
            "carry.");
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
