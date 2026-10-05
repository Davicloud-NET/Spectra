using System;
using SpectraEngine.Core.Assets.Models;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// The fixed byte geometry of a <c>.scmap</c> file, stated once for the cook that
/// writes one in <c>Spectra.Kitchen</c> and the reader here.
/// </summary>
public static class ScmapFormat
{
    /// <summary>
    /// File magic, <c>"SCMP"</c>. Stored as a little-endian <see cref="uint"/>,
    /// so the first four bytes on disk read <c>S C M P</c> in a hex dump.
    /// </summary>
    public const uint Magic = 'S' | ('C' << 8) | ('M' << 16) | ((uint)'P' << 24);

    /// <summary>
    /// The extension a compiled map is written and resolved under, including the
    /// dot.
    /// </summary>
    public const string FileExtension = ".scmap";

    /// <summary>Bytes in the header, which lives at offset 0.</summary>
    public const int HeaderSize = 64;

    /// <summary>Absolute offset of the first section-table record.</summary>
    public const int SectionTableOffset = HeaderSize;

    /// <summary>Bytes in one section-table record, fixed stride.</summary>
    public const int SectionSize = 32;

    /// <summary>
    /// The smallest file the length check accepts: a header and nothing else.
    /// The required-section check still refuses it.
    /// </summary>
    public const int MinimumFileSize = HeaderSize;

    /// <summary>
    /// Alignment every section starts on, checked at load. Payloads are cast in
    /// place from a mapped view, and a pack payload is 16-byte aligned too.
    /// </summary>
    public const int PayloadAlignment = 16;

    /// <summary>Bytes in one <see cref="ScmapAssetEntry"/> record.</summary>
    public const int AssetEntrySize = 16;

    /// <summary>Bytes in one <see cref="ScmapNodeRecord"/> record.</summary>
    public const int NodeRecordSize = 80;

    /// <summary>Bytes in one <see cref="ScmapChunkRecord"/> record.</summary>
    public const int ChunkRecordSize = 64;

    /// <summary>Bytes in one <see cref="ScmapSpawn"/> record.</summary>
    public const int SpawnRecordSize = 32;

    /// <summary>
    /// Bytes of fixed preamble in <c>META</c>, before the spawn array. Its last
    /// sixteen bytes are reserved zeros that keep the spawn array 16-byte aligned.
    /// </summary>
    public const int MetaPreambleSize = 48;

    /// <summary>
    /// Bytes of fixed preamble in <c>STRT</c>: the string count. The offset array
    /// and the blob size follow, then the blob.
    /// </summary>
    public const int StringCountSize = 4;

    /// <summary>Bytes of fixed preamble in <c>ASTB</c>: the entry count.</summary>
    public const int AssetCountSize = 4;

    /// <summary>
    /// Bytes of fixed preamble in <c>NODE</c>: the node count, padded to the
    /// payload alignment so the 80-byte records can be cast in place.
    /// </summary>
    public const int NodePreambleSize = 16;

    /// <summary>
    /// Bytes of fixed preamble in <c>CHDR</c>: the chunk count, padded to the
    /// payload alignment for the same reason as <see cref="NodePreambleSize"/>.
    /// </summary>
    public const int ChunkPreambleSize = 16;

    /// <summary>
    /// Bytes of fixed preamble in one <c>CMSH</c> blob: the submesh count, the
    /// vertex stride and a reserved word, sized so the submesh directory after it
    /// starts 16-byte aligned.
    /// </summary>
    public const int ChunkMeshHeaderSize = 16;

    /// <summary>Bytes in one <see cref="ScmapSubmeshEntry"/> directory record.</summary>
    public const int ChunkSubmeshEntrySize = 24;

    /// <summary>
    /// Bytes of fixed preamble in one <c>CBSP</c> blob: the node count, the root
    /// child code and a reserved word, sized so the node array is 16-byte aligned.
    /// </summary>
    public const int ChunkBspHeaderSize = 16;

    /// <summary>
    /// Bytes in one <see cref="Bsp.FlatBspNode"/>, which raw file bytes are cast
    /// into. Checked against the runtime by <see cref="ScmapChunkBsp.RequireNodeLayout"/>.
    /// </summary>
    public const int FlatBspNodeSize = 24;

    /// <summary>
    /// Bytes in one <c>System.Numerics.Plane</c>, which both <c>CBSP</c> and
    /// <c>BRSH</c> cast file bytes into.
    /// </summary>
    public const int PlaneSize = 16;

    /// <summary>
    /// Bytes of fixed preamble in <c>BRSH</c>: the brush count and the total plane
    /// count, padded so the brush records after it are 16-byte aligned.
    /// </summary>
    public const int BrushSourceHeaderSize = 16;

    /// <summary>Bytes in one <see cref="ScmapBrushRecord"/>.</summary>
    public const int BrushSourceRecordSize = 16;

    /// <summary>Bytes in one <see cref="ScmapFaceRecord"/>.</summary>
    public const int BrushFaceRecordSize = 48;

    /// <summary>
    /// Bytes of fixed preamble in <c>ENTT</c>: the entity count and the keyvalue
    /// count, padded to the payload alignment.
    /// </summary>
    public const int EntityPreambleSize = 16;

    /// <summary>Bytes in one <see cref="ScmapEntityRecord"/>.</summary>
    public const int EntityRecordSize = 24;

    /// <summary>Bytes in one <see cref="ScmapKeyvalueRecord"/>.</summary>
    public const int KeyvalueRecordSize = 8;

    /// <summary>
    /// Bytes of fixed preamble in <c>ECON</c>: the connection count, padded to
    /// the payload alignment.
    /// </summary>
    public const int ConnectionPreambleSize = 16;

    /// <summary>Bytes in one <see cref="ScmapConnectionRecord"/>.</summary>
    public const int ConnectionRecordSize = 24;

    /// <summary>
    /// Bytes of fixed preamble in <c>COLL</c>: the hull count and the plane
    /// count, padded to the payload alignment.
    /// </summary>
    public const int CollisionPreambleSize = 16;

    /// <summary>Bytes in one <see cref="ScmapHullRecord"/>.</summary>
    public const int HullRecordSize = 16;

    /// <summary>The fewest planes a collision hull may have. Fewer bound no volume.</summary>
    public const int MinimumHullPlanes = 4;

    /// <summary>
    /// Bytes of fixed preamble in <c>LGHT</c>: the light count, padded to the
    /// payload alignment.
    /// </summary>
    public const int LightPreambleSize = 16;

    /// <summary>Bytes in one <see cref="ScmapLightRecord"/>.</summary>
    public const int LightRecordSize = 48;

    /// <summary>
    /// The <c>assetIndex</c> a submesh or a face carries when it names no asset.
    /// Not 0: row 0 of <c>ASTB</c> is a real asset.
    /// </summary>
    public const uint NoAssetIndex = uint.MaxValue;

    /// <summary>Section <c>STRT</c>: the string blob every string index addresses.</summary>
    public const uint StringSection = 'S' | ('T' << 8) | ('R' << 16) | ((uint)'T' << 24);

    /// <summary>Section <c>ASTB</c>: the asset table every material and model reference resolves through.</summary>
    public const uint AssetSection = 'A' | ('S' << 8) | ('T' << 16) | ((uint)'B' << 24);

    /// <summary>Section <c>META</c>: scene metadata and the compile constants a load validates.</summary>
    public const uint MetaSection = 'M' | ('E' << 8) | ('T' << 16) | ((uint)'A' << 24);

    /// <summary>Section <c>NODE</c>: the node graph, pre-order, one 80-byte record each.</summary>
    public const uint NodeSection = 'N' | ('O' << 8) | ('D' << 16) | ((uint)'E' << 24);

    /// <summary>Section <c>CHDR</c>: the chunk directory, sorted by <c>ChunkCoord.CompareTo</c>.</summary>
    public const uint ChunkDirectorySection = 'C' | ('H' << 8) | ('D' << 16) | ((uint)'R' << 24);

    /// <summary>Section <c>CMSH</c>: the per-cell mesh blobs the directory points into.</summary>
    public const uint ChunkMeshSection = 'C' | ('M' << 8) | ('S' << 16) | ((uint)'H' << 24);

    /// <summary>Section <c>CBSP</c>: the per-cell flat BSP blobs the directory points into.</summary>
    public const uint ChunkBspSection = 'C' | ('B' << 8) | ('S' << 16) | ((uint)'P' << 24);

    /// <summary>
    /// Section <c>ENTT</c>: one entity record per node that carries an entity, in
    /// node order, then every entity's keyvalues.
    /// </summary>
    public const uint EntitySection = 'E' | ('N' << 8) | ('T' << 16) | ((uint)'T' << 24);

    /// <summary>Section <c>ECON</c>: every entity's output connections.</summary>
    public const uint EntityConnectionSection = 'E' | ('C' << 8) | ('O' << 16) | ((uint)'N' << 24);

    /// <summary>
    /// Section <c>COLL</c>: one convex hull per baked world brush, as the brush's
    /// authored planes.
    /// </summary>
    public const uint CollisionSection = 'C' | ('O' << 8) | ('L' << 16) | ((uint)'L' << 24);

    /// <summary>
    /// Section <c>LGHT</c>: one light record per node that carries a light, in
    /// node order.
    /// </summary>
    public const uint LightSection = 'L' | ('G' << 8) | ('H' << 16) | ((uint)'T' << 24);

    /// <summary>Section <c>SCPT</c>: script records.</summary>
    public const uint ScriptSection = 'S' | ('C' << 8) | ('P' << 16) | ((uint)'T' << 24);

    /// <summary>Section <c>LUAB</c>: compiled Luau bytecode, a cache of <c>LUAS</c>.</summary>
    public const uint ScriptBytecodeSection = 'L' | ('U' << 8) | ('A' << 16) | ((uint)'B' << 24);

    /// <summary>Section <c>LUAS</c>: Luau source.</summary>
    public const uint ScriptSourceSection = 'L' | ('U' << 8) | ('A' << 16) | ((uint)'S' << 24);

    /// <summary>Section <c>BRSH</c>: authored brush planes.</summary>
    public const uint BrushSourceSection = 'B' | ('R' << 8) | ('S' << 16) | ((uint)'H' << 24);

    /// <summary>Section <c>NBND</c>: optional per-node local bounds.</summary>
    public const uint NodeBoundsSection = 'N' | ('B' << 8) | ('N' << 16) | ((uint)'D' << 24);

    /// <summary>
    /// Section <c>RGNI</c>, reserved and never written: the region index
    /// streaming would need.
    /// </summary>
    public const uint RegionIndexSection = 'R' | ('G' << 8) | ('N' << 16) | ((uint)'I' << 24);

    /// <summary>
    /// Section <c>BMDL</c>, retired and never written. Do not reuse the code.
    /// </summary>
    public const uint BrushModelSection = 'B' | ('M' << 8) | ('D' << 16) | ((uint)'L' << 24);

    /// <summary>
    /// The vertex layout every cooked chunk mesh is in: the engine's standard
    /// interleaved position, normal, uv0, all float32.
    /// </summary>
    // SmodelVertexAttribute, so a model and a map with this layout hash to the
    // same layout id.
    public static ReadOnlySpan<SmodelVertexAttribute> StandardVertexLayout => _standardVertexLayout;

    private static readonly SmodelVertexAttribute[] _standardVertexLayout =
    [
        new(SmodelSemantic.Position, SmodelComponentType.Float32, componentCount: 3, byteOffset: 0),
        new(SmodelSemantic.Normal, SmodelComponentType.Float32, componentCount: 3, byteOffset: 12),
        new(SmodelSemantic.Uv0, SmodelComponentType.Float32, componentCount: 2, byteOffset: 24),
    ];

    /// <summary>Floats per vertex in <see cref="StandardVertexLayout"/>.</summary>
    public const uint StandardVertexStrideFloats = 8;

    /// <summary>
    /// The layout identity a header stamps: FNV-1a over each attribute's
    /// <c>(semantic, component count)</c> pair, in declaration order.
    /// </summary>
    public static uint StandardVertexLayoutId => SmodelFormat.ComputeVertexLayoutId(StandardVertexLayout);

    /// <summary>
    /// Rounds <paramref name="value"/> up to the next multiple of
    /// <paramref name="alignment"/>, which must be a power of two.
    /// </summary>
    public static long AlignUp(long value, int alignment) => PackFormat.AlignUp(value, alignment);

    /// <summary>
    /// Renders a FourCC as four characters for a message. Non-printable bytes become <c>?</c>.
    /// </summary>
    public static string DescribeFourCc(uint fourCc) => SmodelFormat.DescribeFourCc(fourCc);

    /// <summary>
    /// The cell size a compiled map must have been baked on: the one the running
    /// engine chunks with. A mismatch would mis-route every point and ray query.
    /// </summary>
    public static float EngineCellSize => ChunkCoord.CellSize;

    /// <summary>
    /// The vertex snap grid a compiled map must have been baked on. A map welded
    /// on another grid has hairline cracks.
    /// </summary>
    public static float EngineSnapGrid => VertexSnapper.GridSize;

    /// <summary>
    /// The cross-cell weld band a compiled map must have been baked on. A map
    /// welded across another band has seams where cells meet.
    /// </summary>
    public static float EngineWeldBand => ChunkGrid.WeldBand;

    /// <summary>
    /// Refuses to read or write a <c>.scmap</c> on a big-endian machine. Payloads
    /// are cast in place, so there is nowhere to byte-swap.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">The machine is big-endian.</exception>
    public static void RequireLittleEndian()
    {
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException(
                "The .scmap format is little-endian only: its node, chunk, mesh and BSP payloads are " +
                "reinterpreted in place, so a big-endian host would have to copy and byte-swap all of it.");
        }
    }
}
