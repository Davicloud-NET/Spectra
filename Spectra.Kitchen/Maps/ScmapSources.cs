using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Maps;

/// <summary>One node on its way into a <c>NODE</c> record.</summary>
/// <param name="Name">Interned into <c>STRT</c>. May be empty.</param>
/// <param name="ParentIndex">
/// The index the parent was added at, or -1 for a root. Must be less than this
/// node's own index.
/// </param>
/// <param name="LocalTransform">
/// The authored local transform, not a composed world matrix: the loader
/// recomposes, which reproduces the same bits the compile used.
/// </param>
/// <param name="PayloadFlags">Flag bits only. Realm and state are passed separately.</param>
/// <param name="DeclaredRealm">The declared realm, not the effective one.</param>
/// <param name="DeclaredState">The declared state, not the effective one.</param>
/// <param name="PayloadIndex">
/// Index into the table the kind names. Unused for a baked brush.
/// </param>
public readonly record struct ScmapNodeSource(
    Guid Id,
    string Name,
    int ParentIndex,
    Transform LocalTransform,
    ScmapPayloadKind PayloadKind,
    ScmapPayloadFlags PayloadFlags = ScmapPayloadFlags.None,
    ScmapNodeRealm DeclaredRealm = ScmapNodeRealm.Inherit,
    ScmapNodeState DeclaredState = ScmapNodeState.Inherit,
    uint PayloadIndex = 0);

/// <summary>
/// One cell on its way into a <c>CHDR</c> record, with its geometry.
/// <see cref="ScmapBuilder"/> computes the blob offsets.
/// </summary>
/// <param name="RenderBounds">
/// The bounds of what the cell draws, not the cell cube. Owned surfaces can
/// overhang the cell.
/// </param>
/// <param name="Submeshes">
/// One entry per material. Null or empty when the cell owns no render geometry.
/// </param>
/// <param name="BspNodes">
/// The cell's flat solid-leaf tree. Null means no tree; an empty array is a tree
/// that is one bare leaf and still gets a blob.
/// </param>
/// <param name="BspRootIndex">
/// An index into <paramref name="BspNodes"/>, or one of <c>FlatBspNode</c>'s two
/// leaf codes.
/// </param>
public readonly record struct ScmapChunkSource(
    ChunkCoord Coord,
    Aabb RenderBounds,
    ScmapSubmeshSource[]? Submeshes = null,
    FlatBspNode[]? BspNodes = null,
    int BspRootIndex = FlatBspNode.EmptyLeaf);

/// <summary>
/// One cell's geometry for one material, on its way into a <c>CMSH</c> submesh.
/// </summary>
/// <param name="AssetIndex">
/// The material's <c>ASTB</c> row, or <c>ScmapFormat.NoAssetIndex</c>. Not a
/// <c>MaterialRef.Id</c>.
/// </param>
/// <param name="Vertices">Interleaved, 8 floats per vertex.</param>
/// <param name="Indices">Zero-based at this submesh's first vertex, not the cell's.</param>
public readonly record struct ScmapSubmeshSource(uint AssetIndex, float[] Vertices, uint[] Indices);

/// <summary>
/// One authored brush plane's surface, on its way into a <c>BRSH</c> face record.
/// </summary>
/// <param name="AssetIndex">
/// The material's <c>ASTB</c> row, or <c>ScmapFormat.NoAssetIndex</c>.
/// </param>
/// <param name="UAxis">Brush-local U axis, or zero for world-aligned.</param>
/// <param name="VAxis">Brush-local V axis, or zero for world-aligned.</param>
/// <param name="UOffset">In repeats.</param>
/// <param name="VOffset">In repeats.</param>
/// <param name="UScale">World units per repeat.</param>
/// <param name="VScale">World units per repeat.</param>
public readonly record struct ScmapFaceSource(
    uint AssetIndex,
    Vector3 UAxis,
    Vector3 VAxis,
    float UOffset,
    float VOffset,
    float UScale,
    float VScale);

/// <summary>
/// One authored brush kept in <c>BRSH</c>. Part brushes are always kept; world
/// brushes only under <c>--keep-brush-source</c>.
/// </summary>
/// <param name="NodeIndex">The <c>NODE</c> record this brush hangs on.</param>
/// <param name="Planes">Brush-local planes.</param>
/// <param name="Faces">One per plane, index-aligned. A different count is refused.</param>
public readonly record struct ScmapBrushSourceEntry(
    int NodeIndex,
    Plane[] Planes,
    ScmapFaceSource[] Faces);

/// <summary>
/// One entity on its way into an <c>ENTT</c> record, with its keyvalues and wires.
/// </summary>
/// <param name="NodeIndex">The <c>NODE</c> record this entity sits on.</param>
/// <param name="Keyvalues">In authored order. A key may repeat.</param>
/// <param name="Connections">In authored order.</param>
public readonly record struct ScmapEntitySource(
    int NodeIndex,
    string ClassName,
    KeyValuePair<string, string>[] Keyvalues,
    EntityConnection[] Connections);

/// <summary>
/// One baked world brush on its way into a <c>COLL</c> hull. Every node whose
/// payload kind is <c>StaticWorldBrush</c> needs one.
/// </summary>
/// <param name="NodeIndex">The <c>NODE</c> record of the brush.</param>
/// <param name="Planes">Brush-local planes, as authored. At least four.</param>
/// <param name="FaceAssets">
/// Each plane's face material as an <c>ASTB</c> row, or
/// <c>ScmapFormat.NoAssetIndex</c>, on its way into <c>COLM</c>. Null when the
/// hull does not say. Either every hull of a map says or none does.
/// </param>
public readonly record struct ScmapCollisionHullSource(int NodeIndex, Plane[] Planes, uint[]? FaceAssets = null);

/// <summary>One light on its way into an <c>LGHT</c> record.</summary>
/// <param name="NodeIndex">The <c>NODE</c> record this light sits on.</param>
public readonly record struct ScmapLightSource(int NodeIndex, Light Light);

/// <summary>One asset on its way into an <c>ASTB</c> record.</summary>
/// <param name="ContentPath">The normalised content-relative path.</param>
/// <param name="ContentHash">
/// Low 64 bits of the cooked payload's hash, or zero when unknown. Advisory: a
/// mismatch against the pack only warns.
/// </param>
public readonly record struct ScmapAssetSource(
    PackEntryKind Kind,
    string ContentPath,
    ulong ContentHash = 0);

/// <summary>One spawn point on its way into a <c>META</c> spawn record.</summary>
public readonly record struct ScmapSpawnSource(Vector3 Position, Quaternion Rotation);
