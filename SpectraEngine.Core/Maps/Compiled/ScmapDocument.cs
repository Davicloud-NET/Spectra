using System;
using System.Numerics;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// A validated <c>.scmap</c>, as spans into the bytes it sits in.
/// </summary>
// A ref struct so it cannot outlive the bytes, which are usually a mapped view.
public readonly ref struct ScmapDocument
{
    internal ScmapDocument(
        string source,
        ScmapHeader header,
        ScmapStringTable strings,
        ReadOnlySpan<ScmapAssetEntry> assets,
        ScmapMeta meta,
        ReadOnlySpan<ScmapSpawn> spawns,
        ReadOnlySpan<ScmapNodeRecord> nodes,
        ReadOnlySpan<ScmapEntityRecord> entities,
        ReadOnlySpan<ScmapKeyvalueRecord> keyvalues,
        ReadOnlySpan<ScmapConnectionRecord> connections,
        ReadOnlySpan<ScmapHullRecord> collisionHulls,
        ReadOnlySpan<Plane> collisionPlanes,
        ReadOnlySpan<uint> collisionFaceAssets,
        bool hasCollisionFaceMaterials,
        ReadOnlySpan<ScmapLightRecord> lights,
        ReadOnlySpan<ScmapChunkRecord> chunks,
        ReadOnlySpan<byte> chunkMeshBlob,
        ReadOnlySpan<byte> chunkBspBlob,
        int chunkBspBlobFileOffset,
        ReadOnlySpan<byte> brushSourceSection,
        bool hasBrushSource,
        int skippedSectionCount,
        int invalidDeclaredStateCount)
    {
        BrushSourceSection = brushSourceSection;
        HasBrushSource = hasBrushSource;
        Source = source;
        Header = header;
        Strings = strings;
        Assets = assets;
        Meta = meta;
        Spawns = spawns;
        Nodes = nodes;
        Entities = entities;
        Keyvalues = keyvalues;
        Connections = connections;
        CollisionHulls = collisionHulls;
        CollisionPlanes = collisionPlanes;
        CollisionFaceAssets = collisionFaceAssets;
        HasCollisionFaceMaterials = hasCollisionFaceMaterials;
        Lights = lights;
        Chunks = chunks;
        ChunkMeshBlob = chunkMeshBlob;
        ChunkBspBlob = chunkBspBlob;
        ChunkBspBlobFileOffset = chunkBspBlobFileOffset;
        SkippedSectionCount = skippedSectionCount;
        InvalidDeclaredStateCount = invalidDeclaredStateCount;
    }

    /// <summary>The map's logical asset path, used in messages.</summary>
    public string Source { get; }

    /// <summary>The file header, already validated.</summary>
    public ScmapHeader Header { get; }

    /// <summary>The <c>STRT</c> section.</summary>
    public ScmapStringTable Strings { get; }

    /// <summary>The <c>ASTB</c> section, in the order a load must intern it.</summary>
    public ReadOnlySpan<ScmapAssetEntry> Assets { get; }

    /// <summary>The <c>META</c> preamble.</summary>
    public ScmapMeta Meta { get; }

    /// <summary>The spawn records following the <c>META</c> preamble.</summary>
    public ReadOnlySpan<ScmapSpawn> Spawns { get; }

    /// <summary>The <c>NODE</c> section, in pre-order.</summary>
    public ReadOnlySpan<ScmapNodeRecord> Nodes { get; }

    /// <summary>
    /// The entity records of <c>ENTT</c>, in ascending node index. At most one
    /// per node.
    /// </summary>
    public ReadOnlySpan<ScmapEntityRecord> Entities { get; }

    /// <summary>The keyvalue records of <c>ENTT</c>, which entity records index into.</summary>
    public ReadOnlySpan<ScmapKeyvalueRecord> Keyvalues { get; }

    /// <summary>The <c>ECON</c> section, which entity records index into.</summary>
    public ReadOnlySpan<ScmapConnectionRecord> Connections { get; }

    /// <summary>
    /// The hull records of <c>COLL</c>: one per baked world brush, in ascending
    /// node index.
    /// </summary>
    public ReadOnlySpan<ScmapHullRecord> CollisionHulls { get; }

    /// <summary>
    /// The planes of <c>COLL</c>, which hull records index into. Brush-local, as
    /// authored.
    /// </summary>
    public ReadOnlySpan<Plane> CollisionPlanes { get; }

    /// <summary>
    /// The face records of <c>COLM</c>: for each plane of
    /// <see cref="CollisionPlanes"/>, its face's material as an index into
    /// <see cref="Assets"/>, or <see cref="ScmapFormat.NoAssetIndex"/>. Empty
    /// when the map has no such section.
    /// </summary>
    public ReadOnlySpan<uint> CollisionFaceAssets { get; }

    /// <summary>
    /// Whether the map carries <c>COLM</c>. Without it a collision hull knows
    /// its shape and not what it is made of.
    /// </summary>
    public bool HasCollisionFaceMaterials { get; }

    /// <summary>
    /// The <c>LGHT</c> section, in ascending node index. At most one record per
    /// node.
    /// </summary>
    public ReadOnlySpan<ScmapLightRecord> Lights { get; }

    /// <summary>The <c>CHDR</c> section, sorted by cell coordinate.</summary>
    public ReadOnlySpan<ScmapChunkRecord> Chunks { get; }

    /// <summary>
    /// The whole <c>CMSH</c> section, which every chunk record's mesh offset is
    /// relative to. Empty when no cell owns render geometry.
    /// </summary>
    public ReadOnlySpan<byte> ChunkMeshBlob { get; }

    /// <summary>
    /// The whole <c>CBSP</c> section, which every chunk record's BSP offset is
    /// relative to. Empty when no cell has a tree.
    /// </summary>
    public ReadOnlySpan<byte> ChunkBspBlob { get; }

    /// <summary>
    /// Where <see cref="ChunkBspBlob"/> starts, counted from the first byte of
    /// the file. Zero when there is no <c>CBSP</c> section.
    /// </summary>
    // The loader keeps querying BSP nodes off the blob after this document is
    // gone, so it needs a file offset rather than a span.
    public int ChunkBspBlobFileOffset { get; }

    /// <summary>
    /// How many section records this reader stepped over because it did not know
    /// the code.
    /// </summary>
    public int SkippedSectionCount { get; }

    /// <summary>
    /// How many node records declared the unused encoding of the two-bit state
    /// field. Such a node reads as <c>Inherit</c>; the load is not refused.
    /// </summary>
    public int InvalidDeclaredStateCount { get; }

    /// <summary>
    /// The whole <c>BRSH</c> section, or empty when the cook kept no brush source.
    /// </summary>
    public ReadOnlySpan<byte> BrushSourceSection { get; }

    /// <summary>
    /// Whether this map carries authored brush planes. Says the section is present, not that
    /// any brush may be carved: ask <c>ScmapBrushSource.IsReCarvable</c> for that.
    /// </summary>
    public bool HasBrushSource { get; }

    /// <summary>
    /// Chunk <paramref name="index"/>'s mesh blob, parsed in place. Call only when the
    /// record's <c>MeshSize</c> is non-zero; a cell with no render geometry has no blob.
    /// </summary>
    /// <exception cref="ScmapFormatException">The blob is not a well-formed chunk mesh.</exception>
    public ScmapChunkMesh ChunkMesh(int index)
    {
        ScmapChunkRecord cell = Chunks[index];
        return new ScmapChunkMesh(
            ChunkMeshBlob.Slice((int)cell.MeshOffset, (int)cell.MeshSize), Source, in cell);
    }

    /// <summary>Chunk <paramref name="index"/>'s flat BSP blob, parsed in place.</summary>
    /// <exception cref="ScmapFormatException">The blob is not a well-formed flat tree.</exception>
    public ScmapChunkBsp ChunkBsp(int index)
    {
        ScmapChunkRecord cell = Chunks[index];
        return new ScmapChunkBsp(
            ChunkBspBlob.Slice((int)cell.BspOffset, (int)cell.BspSize), Source, in cell);
    }

    /// <summary>The <c>BRSH</c> section, parsed in place.</summary>
    /// <exception cref="ScmapFormatException">The section is not a well-formed brush table.</exception>
    public ScmapBrushSource BrushSource() => new(BrushSourceSection, Source, Nodes.Length);

    /// <summary>Triangles across every cell that owns render geometry.</summary>
    public int TriangleCount
    {
        get
        {
            int triangles = 0;
            for (int i = 0; i < Chunks.Length; i++)
            {
                if (Chunks[i].MeshSize == 0) continue;
                triangles += ChunkMesh(i).TriangleCount;
            }

            return triangles;
        }
    }

    /// <summary>The name of node <paramref name="index"/>, decoded.</summary>
    public string NodeName(int index) => Strings.GetStringOrEmpty((int)Nodes[index].NameString);

    /// <summary>The content path of asset <paramref name="index"/>, decoded.</summary>
    public string AssetPath(int index) => Strings.GetStringOrEmpty((int)Assets[index].PathString);

    /// <summary>The string at <paramref name="index"/> of <c>STRT</c>, decoded.</summary>
    public string StringAt(uint index) => Strings.GetStringOrEmpty((int)index);

    /// <summary>The scene's name, decoded.</summary>
    public string SceneName => Strings.GetStringOrEmpty((int)Meta.SceneNameString);
}
