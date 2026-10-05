using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Assets.Packs;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// One 16-byte <c>ASTB</c> record: an asset this map references, by path.
/// </summary>
// Never write a MaterialRef.Id here. Ids are per-process interning order, so a
// file holding one mis-textures the world as soon as another map interns first.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapAssetEntry
{
    /// <summary>The asset kind: a <see cref="PackEntryKind"/> value widened to a word.</summary>
    public readonly uint Kind;

    /// <summary>Index into <c>STRT</c> of this asset's content-relative path.</summary>
    public readonly uint PathString;

    /// <summary>
    /// Low 64 bits of the cooked payload's content hash, or zero when unknown.
    /// Advisory: a mismatch against the mounted pack warns and never fails.
    /// </summary>
    public readonly ulong ContentHash;

    /// <summary>Builds one asset-table record.</summary>
    public ScmapAssetEntry(PackEntryKind kind, uint pathString, ulong contentHash)
    {
        Kind = (uint)kind;
        PathString = pathString;
        ContentHash = contentHash;
    }

    /// <summary><see cref="Kind"/> as the enum.</summary>
    public PackEntryKind AssetKind => (PackEntryKind)Kind;
}

/// <summary>
/// One 80-byte <c>NODE</c> record: an authored node, in pre-order. Every authored
/// node gets one, baked brushes included, so ids and target names still resolve.
/// </summary>
// The transform is the authored ten floats, not a world matrix. Replaying the
// composition gives bit-identical matrices, which the compile cache and the
// bake oracle compare exactly.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapNodeRecord
{
    /// <summary>Bit 3 of <c>PayloadFlags</c>: the low bit of the declared realm.</summary>
    public const int RealmShift = 3;

    /// <summary>Bit 5 of <c>PayloadFlags</c>: the low bit of the declared state.</summary>
    public const int StateShift = 5;

    /// <summary>Mask for the two-bit realm and state fields.</summary>
    public const int TwoBitMask = 0x3;

    /// <summary>
    /// The node's id as sixteen RFC 4122 bytes. Convert with <see cref="EncodeId"/>
    /// and <see cref="DecodeId"/>.
    /// </summary>
    // Not a Guid field: Guid's layout byte-swaps its first three components on
    // little-endian, and the bytes on disk should match the hex in map.json.
    public readonly UInt128 Id;

    /// <summary>Index into <c>STRT</c> of the node's name, which is also its target name.</summary>
    public readonly uint NameString;

    /// <summary>Index of this node's parent record, or -1 for a root. Always less than this record's index.</summary>
    public readonly int ParentIndex;

    /// <summary>Authored local position.</summary>
    public readonly Vector3 LocalPosition;

    /// <summary>Authored local rotation, stored x, y, z, w.</summary>
    public readonly Quaternion LocalRotation;

    /// <summary>Authored local scale.</summary>
    public readonly Vector3 LocalScale;

    /// <summary>What the payload is. See <see cref="ScmapPayloadKind"/>.</summary>
    public readonly ushort PayloadKindRaw;

    /// <summary>Flags plus the two-bit realm and state fields. See <see cref="ScmapPayloadFlags"/>.</summary>
    public readonly ushort PayloadFlagsRaw;

    /// <summary>
    /// Index into whatever table <see cref="PayloadKindRaw"/> names, or zero when
    /// the kind has no table. Unused for a baked brush.
    /// </summary>
    public readonly uint PayloadIndex;

    /// <summary>Reserved; written zero.</summary>
    public readonly ulong Reserved;

    /// <summary>Builds one node record.</summary>
    public ScmapNodeRecord(
        Guid id,
        uint nameString,
        int parentIndex,
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        ScmapPayloadKind payloadKind,
        ScmapPayloadFlags payloadFlags,
        ScmapNodeRealm declaredRealm = ScmapNodeRealm.Inherit,
        ScmapNodeState declaredState = ScmapNodeState.Inherit,
        uint payloadIndex = 0)
    {
        Id = EncodeId(id);
        NameString = nameString;
        ParentIndex = parentIndex;
        LocalPosition = localPosition;
        LocalRotation = localRotation;
        LocalScale = localScale;
        PayloadKindRaw = (ushort)payloadKind;
        PayloadFlagsRaw = ComposeFlags(payloadFlags, declaredRealm, declaredState);
        PayloadIndex = payloadIndex;
        Reserved = 0;
    }

    /// <summary>The node's id as a <see cref="Guid"/>.</summary>
    public Guid NodeId => DecodeId(Id);

    /// <summary>What the payload is.</summary>
    public ScmapPayloadKind PayloadKind => (ScmapPayloadKind)PayloadKindRaw;

    /// <summary>The flag bits, with the realm and state fields masked out.</summary>
    public ScmapPayloadFlags PayloadFlags =>
        (ScmapPayloadFlags)(PayloadFlagsRaw & ~((TwoBitMask << RealmShift) | (TwoBitMask << StateShift)));

    /// <summary>The declared realm, never the effective one.</summary>
    public ScmapNodeRealm DeclaredRealm => (ScmapNodeRealm)((PayloadFlagsRaw >> RealmShift) & TwoBitMask);

    /// <summary>The declared state, never the effective one. May read <see cref="ScmapNodeState.Invalid"/>.</summary>
    public ScmapNodeState DeclaredState => (ScmapNodeState)((PayloadFlagsRaw >> StateShift) & TwoBitMask);

    /// <summary>
    /// Whether this node's geometry is already in the compiled chunks. A load must
    /// not carve it again, even when <c>BRSH</c> holds its planes.
    /// </summary>
    // Not the same question as SceneNode.IsStaticWorldBrush, which admits a brush
    // to the carve. Re-carving a baked brush draws every wall twice (z-fighting,
    // no error). Loaders ask ScmapBrushSource.IsReCarvable.
    public bool BakedIntoChunks => PayloadKind == ScmapPayloadKind.StaticWorldBrush;

    /// <summary>Whether the brush subtracts. False for any payload kind that is not a brush.</summary>
    public bool IsSubtractiveBrush =>
        PayloadKind is ScmapPayloadKind.StaticWorldBrush or ScmapPayloadKind.PartBrush
        && (PayloadFlagsRaw & (ushort)ScmapPayloadFlags.SubtractiveBrush) != 0;

    /// <summary>Packs the flag bits and the realm and state fields into one half-word.</summary>
    public static ushort ComposeFlags(ScmapPayloadFlags flags, ScmapNodeRealm realm, ScmapNodeState state)
    {
        int reserved = (TwoBitMask << RealmShift) | (TwoBitMask << StateShift);
        if (((ushort)flags & reserved) != 0)
        {
            throw new ArgumentException(
                $"Payload flags '{flags}' set a bit inside the realm or state field. Those four bits are a " +
                "pair of two-bit enums, not flags; pass them as the realm and state arguments.", nameof(flags));
        }

        return (ushort)((ushort)flags
            | (((int)realm & TwoBitMask) << RealmShift)
            | (((int)state & TwoBitMask) << StateShift));
    }

    /// <summary>Turns a <see cref="Guid"/> into the integer its RFC 4122 bytes read as.</summary>
    public static UInt128 EncodeId(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        return BinaryPrimitives.ReadUInt128LittleEndian(bytes);
    }

    /// <summary>The inverse of <see cref="EncodeId"/>.</summary>
    public static Guid DecodeId(UInt128 value)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt128LittleEndian(bytes, value);
        return new Guid(bytes, bigEndian: true);
    }
}

/// <summary>
/// One 64-byte <c>CHDR</c> record: where one chunk cell's baked geometry lives.
/// Records are sorted by <c>ChunkCoord.CompareTo</c>, so a cell lookup is a binary search.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapChunkRecord
{
    /// <summary>Cell coordinate on the X axis.</summary>
    public readonly int X;

    /// <summary>Cell coordinate on the Y axis.</summary>
    public readonly int Y;

    /// <summary>Cell coordinate on the Z axis.</summary>
    public readonly int Z;

    /// <summary>
    /// Minimum corner of the cell's render bounds. Not the cell cube: owned
    /// surfaces can overhang the cell.
    /// </summary>
    public readonly Vector3 BoundsMin;

    /// <summary>Maximum corner of the cell's render bounds.</summary>
    public readonly Vector3 BoundsMax;

    /// <summary>Offset of this cell's mesh blob from the start of <c>CMSH</c>.</summary>
    public readonly uint MeshOffset;

    /// <summary>Bytes in this cell's mesh blob; zero when the cell owns no render geometry.</summary>
    public readonly uint MeshSize;

    /// <summary>Offset of this cell's BSP blob from the start of <c>CBSP</c>.</summary>
    public readonly uint BspOffset;

    /// <summary>Bytes in this cell's BSP blob; zero when the cell has no tree.</summary>
    public readonly uint BspSize;

    /// <summary>Index into <c>RGNI</c>, which is reserved and never written. Zero.</summary>
    public readonly uint RegionIndex;

    /// <summary>Per-cell flags. None defined in v1; written zero.</summary>
    public readonly uint Flags;

    /// <summary>Reserved; written zero.</summary>
    public readonly uint Reserved;

    /// <summary>Builds one chunk-directory record.</summary>
    public ScmapChunkRecord(
        int x,
        int y,
        int z,
        Vector3 boundsMin,
        Vector3 boundsMax,
        uint meshOffset,
        uint meshSize,
        uint bspOffset,
        uint bspSize)
    {
        X = x;
        Y = y;
        Z = z;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        MeshOffset = meshOffset;
        MeshSize = meshSize;
        BspOffset = bspOffset;
        BspSize = bspSize;
        RegionIndex = 0;
        Flags = 0;
        Reserved = 0;
    }
}

/// <summary>
/// One 32-byte <c>META</c> spawn record.
/// </summary>
// 28 bytes of content padded to 32 so the array casts in place. The padding is
// a declared field so it is always written zero.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapSpawn
{
    /// <summary>Where a player enters.</summary>
    public readonly Vector3 Position;

    /// <summary>Which way they face, stored x, y, z, w.</summary>
    public readonly Quaternion Rotation;

    /// <summary>Reserved; written zero.</summary>
    public readonly uint Reserved;

    /// <summary>Builds one spawn record.</summary>
    public ScmapSpawn(Vector3 position, Quaternion rotation)
    {
        Position = position;
        Rotation = rotation;
        Reserved = 0;
    }
}

/// <summary>
/// One 24-byte <c>ENTT</c> record: the entity a node carries. Records are in
/// node order, at most one per node.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapEntityRecord
{
    /// <summary>Index into <c>NODE</c> of the node this entity sits on.</summary>
    public readonly uint NodeIndex;

    /// <summary>Index into <c>STRT</c> of the class name.</summary>
    public readonly uint ClassNameString;

    /// <summary>Index of this entity's first keyvalue in the keyvalue array.</summary>
    public readonly uint KeyvalueStart;

    /// <summary>How many keyvalues this entity has, in authored order.</summary>
    public readonly uint KeyvalueCount;

    /// <summary>Index of this entity's first connection in <c>ECON</c>.</summary>
    public readonly uint ConnectionStart;

    /// <summary>How many connections this entity has, in authored order.</summary>
    public readonly uint ConnectionCount;

    /// <summary>Builds one entity record.</summary>
    public ScmapEntityRecord(
        uint nodeIndex,
        uint classNameString,
        uint keyvalueStart,
        uint keyvalueCount,
        uint connectionStart,
        uint connectionCount)
    {
        NodeIndex = nodeIndex;
        ClassNameString = classNameString;
        KeyvalueStart = keyvalueStart;
        KeyvalueCount = keyvalueCount;
        ConnectionStart = connectionStart;
        ConnectionCount = connectionCount;
    }
}

/// <summary>
/// One 8-byte <c>ENTT</c> keyvalue record. Both halves are strings; a key may
/// repeat within one entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapKeyvalueRecord
{
    /// <summary>Index into <c>STRT</c> of the key.</summary>
    public readonly uint KeyString;

    /// <summary>Index into <c>STRT</c> of the value.</summary>
    public readonly uint ValueString;

    /// <summary>Builds one keyvalue record.</summary>
    public ScmapKeyvalueRecord(uint keyString, uint valueString)
    {
        KeyString = keyString;
        ValueString = valueString;
    }
}

/// <summary>
/// One 24-byte <c>ECON</c> record: a wire from an entity's output to an input.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapConnectionRecord
{
    /// <summary>Index into <c>STRT</c> of the output that fires this wire.</summary>
    public readonly uint OutputNameString;

    /// <summary>Index into <c>STRT</c> of the target name.</summary>
    public readonly uint TargetNameString;

    /// <summary>Index into <c>STRT</c> of the input to send.</summary>
    public readonly uint InputNameString;

    /// <summary>Index into <c>STRT</c> of the parameter. Zero, the empty string, for none.</summary>
    public readonly uint ParameterString;

    /// <summary>Seconds to wait before sending.</summary>
    public readonly float Delay;

    /// <summary>How many times the wire may fire. Negative for no limit.</summary>
    public readonly int TimesToFire;

    /// <summary>Builds one connection record.</summary>
    public ScmapConnectionRecord(
        uint outputNameString,
        uint targetNameString,
        uint inputNameString,
        uint parameterString,
        float delay,
        int timesToFire)
    {
        OutputNameString = outputNameString;
        TargetNameString = targetNameString;
        InputNameString = inputNameString;
        ParameterString = parameterString;
        Delay = delay;
        TimesToFire = timesToFire;
    }
}
