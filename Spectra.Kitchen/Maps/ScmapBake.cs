using System;
using System.Collections.Generic;
using Spectra.Kitchen.Diagnostics;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps;

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
        var assets = new ScmapBakeAssets(builder);

        try
        {
            if (!ScmapNodeBake.Write(document, scene, builder, assets, keepBrushSource, report, sourcePath))
                return null;

            WriteChunks(world, builder, assets);
        }
        catch (ScmapAssetKindException ex)
        {
            report(AssetOfTwoKinds(ex, sourcePath));
            return null;
        }

        return builder.Build(sourceMapDigest, (uint)document.FormatVersion);
    }

    private static CookDiagnostic AssetOfTwoKinds(ScmapAssetKindException clash, string sourcePath) =>
        CookDiagnostic.Error(
            CookDiagnosticCodes.MapAssetOfTwoKinds,
            $"'{sourcePath}' names '{clash.ContentPath}' as a {Describe(clash.First)} and as a " +
            $"{Describe(clash.Second)}. One file is one kind of asset. Give one of the two a file of its own.",
            sourcePath);

    private static string Describe(PackEntryKind kind) => kind switch
    {
        PackEntryKind.Material => "material",
        PackEntryKind.Model => "model",
        PackEntryKind.Script => "script",
        _ => kind.ToString(),
    };

    private static void WriteChunks(CsgWorld world, ScmapBuilder builder, ScmapBakeAssets assets)
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
    private static ScmapSubmeshSource[]? SubmeshesOf(ChunkMesh? mesh, ScmapBakeAssets assets)
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
}
