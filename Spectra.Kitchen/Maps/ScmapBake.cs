using System;
using System.Collections.Generic;
using System.Numerics;
using Spectra.Kitchen.Diagnostics;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Maps;

/// <summary>Turns an authored map into a compiled one: bind, compile, bake.</summary>
public static class ScmapBake
{
    /// <summary>
    /// Bakes <paramref name="document"/>, or reports why it cannot be baked and
    /// returns null.
    /// </summary>
    /// <param name="sourceMapDigest">The bundle's digest, stamped into the header.</param>
    /// <param name="keepBrushSource">
    /// Keep world brushes' authored planes too. Part brushes are always kept.
    /// </param>
    /// <param name="report">Called on the cooking thread.</param>
    public static byte[]? Bake(
        MapDocument document,
        UInt128 sourceMapDigest,
        bool keepBrushSource,
        Action<CookDiagnostic> report,
        string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(report);

        var scene = new SpectraEngine.Core.Scene.Scene(document.Scene.Name);

        // No asset manager: a bake resolves no models. Mesh data is read from
        // the document below.
        MapSceneBinder.ApplyTo(document, scene);

        // The engine's own capture, so placement order (which breaks carve
        // ties) matches a runtime rebuild.
        IReadOnlyList<BrushPlacement>? placements =
            scene.CaptureStaticWorldPlacements(out string? defect);

        if (placements is null)
        {
            report(CookDiagnostic.Error(CookDiagnosticCodes.MapBrushNonRigid, defect!, sourcePath));
            return null;
        }

        // Cache-free overload: a bake must not depend on earlier compiles.
        CsgWorld world = CsgWorld.Build(placements);

        var builder = new ScmapBuilder(document.Scene.Name);
        var assets = new AssetTable(builder);

        if (!WriteNodes(document, scene, builder, assets, keepBrushSource, report, sourcePath)) return null;

        WriteChunks(world, builder, assets);

        return builder.Build(sourceMapDigest, (uint)document.FormatVersion);
    }

    // Walks document and scene together in pre-order. The binder builds the
    // scene in document order, so the two sequences match.
    private static bool WriteNodes(
        MapDocument document,
        SpectraEngine.Core.Scene.Scene scene,
        ScmapBuilder builder,
        AssetTable assets,
        bool keepBrushSource,
        Action<CookDiagnostic> report,
        string sourcePath)
    {
        var seen = new HashSet<Guid>();
        var pending = new Stack<(MapNode Mapped, SceneNode Node, int Parent)>();

        // Pre-order keeps ParentIndex < SelfIndex, which the one-pass loader needs.
        PushChildren(pending, document.Nodes, scene.Root, -1);

        while (pending.Count > 0)
        {
            (MapNode mapped, SceneNode node, int parent) = pending.Pop();

            if (mapped.Id != node.Id)
            {
                // Should not happen while the binder keeps document order.
                throw new InvalidOperationException(
                    $"'{sourcePath}' node '{mapped.Name}' bound to scene node '{node.Name}' with a different " +
                    "id. The compiled map's node records would carry one node's payload under another's " +
                    "transform.");
            }

            if (!seen.Add(mapped.Id))
            {
                report(CookDiagnostic.Error(
                    CookDiagnosticCodes.MapNodeIdDuplicate,
                    $"Two nodes in '{sourcePath}' claim id {mapped.Id}. Every command, every wire and every " +
                    "script reference resolves through that id, so a duplicate resolves by traversal order and " +
                    "presents as an edit landing on the wrong object.",
                    sourcePath));

                return false;
            }

            if (mapped.Brush is { } authored && authored.Planes.Count != authored.Faces.Count)
            {
                report(CookDiagnostic.Error(
                    CookDiagnosticCodes.MapFaceCountMismatch,
                    $"Node '{mapped.Name}' in '{sourcePath}' has {authored.Planes.Count} brush planes and " +
                    $"{authored.Faces.Count} faces. One face per plane is the invariant the whole per-face " +
                    "material path rests on, so a mismatch is an indexing bug rather than a wrong surface.",
                    sourcePath));

                return false;
            }

            int index = builder.AddNode(new ScmapNodeSource(
                node.Id,
                node.Name,
                parent,
                node.LocalTransform,
                PayloadKindOf(node, mapped),
                PayloadFlagsOf(node),
                PayloadIndex: PayloadIndexOf(mapped, assets)));

            if (node.Brush is { } brush && KeepsSource(node, mapped, keepBrushSource))
                builder.AddBrushSource(BrushSourceOf(index, brush, assets));
            else if (node.Brush is { } unkept)
                ClaimFaceMaterials(unkept, assets);

            PushChildren(pending, mapped.Children, node, index);
        }

        return true;
    }

    private static void PushChildren(
        Stack<(MapNode, SceneNode, int)> pending, List<MapNode> mapped, SceneNode node, int parent)
    {
        if (mapped.Count != node.Children.Count)
        {
            throw new InvalidOperationException(
                $"Node '{node.Name}' has {node.Children.Count} scene children and {mapped.Count} document " +
                "children. The bake walks both graphs in lockstep, and a shape difference would silently " +
                "misalign every record after it.");
        }

        for (int i = mapped.Count - 1; i >= 0; i--)
            pending.Push((mapped[i], node.Children[i], parent));
    }

    private static ScmapPayloadKind PayloadKindOf(SceneNode node, MapNode mapped)
    {
        if (node.Brush is not null)
        {
            // StaticWorldBrush means baked into chunks: the loader must not carve it again.
            return node.IsStaticWorldBrush ? ScmapPayloadKind.StaticWorldBrush : ScmapPayloadKind.PartBrush;
        }

        return mapped.Mesh is not null ? ScmapPayloadKind.MeshInstance : ScmapPayloadKind.None;
    }

    private static ScmapPayloadFlags PayloadFlagsOf(SceneNode node) =>
        node.Brush is { Operation: BrushOperation.Subtractive }
            ? ScmapPayloadFlags.SubtractiveBrush
            : ScmapPayloadFlags.None;

    // Zero for a brush: the BRSH record points at its node, not the other way round.
    private static uint PayloadIndexOf(MapNode mapped, AssetTable assets) =>
        mapped is { Brush: null, Mesh: { } mesh } && mesh.Model.Length > 0
            ? assets.Model(mesh.Model)
            : 0;

    // Part brushes are always kept: their planes exist nowhere else in the file.
    private static bool KeepsSource(SceneNode node, MapNode mapped, bool keepBrushSource) =>
        !node.IsStaticWorldBrush || keepBrushSource || mapped.Brush is { KeepSource: true };

    private static ScmapBrushSourceEntry BrushSourceOf(int nodeIndex, Brush brush, AssetTable assets)
    {
        var planes = new Plane[brush.LocalPlanes.Count];
        var faces = new ScmapFaceSource[brush.LocalPlanes.Count];

        for (int i = 0; i < planes.Length; i++)
        {
            planes[i] = brush.LocalPlanes[i];

            FaceSurface face = brush.FaceSurfaces[i];
            faces[i] = new ScmapFaceSource(
                assets.Material(face.Material),
                face.UAxis,
                face.VAxis,
                face.UOffset,
                face.VOffset,
                face.UScale,
                face.VScale);
        }

        return new ScmapBrushSourceEntry(nodeIndex, planes, faces);
    }

    // Claimed even when the source is dropped, so --keep-brush-source does
    // not renumber the asset table.
    private static void ClaimFaceMaterials(Brush brush, AssetTable assets)
    {
        for (int i = 0; i < brush.FaceSurfaces.Count; i++) assets.Material(brush.FaceSurfaces[i].Material);
    }

    private static void WriteChunks(CsgWorld world, ScmapBuilder builder, AssetTable assets)
    {
        var meshes = new Dictionary<ChunkCoord, ChunkMesh>(world.ChunkMeshes.Count);
        for (int i = 0; i < world.ChunkMeshes.Count; i++)
            meshes[world.ChunkMeshes[i].Coord] = world.ChunkMeshes[i];

        // Iterate the sorted chunk list, never the dictionary: its order is not stable.
        IReadOnlyList<WorldChunk> cells = world.Chunks.OrderedChunks;
        for (int i = 0; i < cells.Count; i++)
        {
            WorldChunk cell = cells[i];
            meshes.TryGetValue(cell.Coord, out ChunkMesh? mesh);

            FlatBspNode[]? nodes = null;
            int root = FlatBspNode.EmptyLeaf;
            if (cell.Bsp is { } tree) nodes = BspFlattener.Flatten(tree, out root);

            builder.AddChunk(new ScmapChunkSource(
                cell.Coord,

                // A cell with no mesh is never culled; the cell cube is a stable placeholder.
                mesh?.RenderBounds ?? cell.Coord.Bounds,
                SubmeshesOf(mesh, assets),
                nodes,
                root));
        }
    }

    // Sorted by asset index. The compile's order is by material id, which
    // depends on interning order in this process.
    private static ScmapSubmeshSource[]? SubmeshesOf(ChunkMesh? mesh, AssetTable assets)
    {
        if (mesh is null || mesh.Submeshes.Count == 0) return null;

        var submeshes = new ScmapSubmeshSource[mesh.Submeshes.Count];
        for (int i = 0; i < submeshes.Length; i++)
        {
            ChunkSubmesh submesh = mesh.Submeshes[i];
            submeshes[i] = new ScmapSubmeshSource(
                assets.Material(submesh.Material), submesh.Vertices, submesh.Indices);
        }

        Array.Sort(submeshes, static (a, b) => a.AssetIndex.CompareTo(b.AssetIndex));
        return submeshes;
    }

    // The only route from a MaterialRef to an ASTB row. MaterialRef.Id is
    // per-process and must never reach the file.
    private sealed class AssetTable(ScmapBuilder builder)
    {
        private readonly Dictionary<int, uint> _materials = [];

        public uint Material(MaterialRef material)
        {
            // Sentinel, not row 0: row 0 is a real asset.
            if (material.IsDefault) return ScmapFormat.NoAssetIndex;

            if (_materials.TryGetValue(material.Id, out uint existing)) return existing;

            uint index = builder.AddMaterial(material);
            _materials[material.Id] = index;
            return index;
        }

        public uint Model(string path) =>
            builder.AddAsset(new ScmapAssetSource(PackEntryKind.Model, path));
    }
}
