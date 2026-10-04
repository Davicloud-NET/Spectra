using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Maps.Compiled;

namespace SpectraEngine.Core.Scene;

public sealed partial class Scene
{
    private CompiledStaticWorld? _compiledStaticWorld;

    // The dirty marks run from property setters with no logger, so refusals
    // are counted here and logged by ProcessStaticWorldCompilation.
    private int _reportedRebuildRefusals;
    private int _reportedDirtyRefusals;

    /// <summary>
    /// The static world this scene adopted from a compiled map, or null when the
    /// world is compiled live or absent. While set, <see cref="StaticWorld"/> is
    /// null and every rebuild and dirty mark is refused and counted.
    /// </summary>
    // Carving on top of baked chunks would draw every wall twice (z-fighting).
    public CompiledStaticWorld? CompiledStaticWorld => _compiledStaticWorld;

    /// <summary>True when this scene's static world arrived baked.</summary>
    public bool HasCompiledStaticWorld => _compiledStaticWorld is not null;

    /// <summary>
    /// How many times a synchronous rebuild was refused because the world arrived
    /// baked. Zero after a correct load.
    /// </summary>
    public int RefusedStaticWorldRebuilds { get; private set; }

    /// <summary>
    /// How many automatic dirty marks were refused because the world arrived
    /// baked.
    /// </summary>
    public int RefusedStaticWorldDirtyMarks { get; private set; }

    /// <summary>The most recent refusal, in words, or null when nothing has been refused.</summary>
    public string? StaticWorldGuardMessage { get; private set; }

    /// <summary>
    /// Installs a compiled map's baked chunks as this scene's static world:
    /// one GPU mesh per (cell, material) and one flat BSP tree per cell, both
    /// straight from the mapped bytes. Runs no CSG. Render thread only.
    /// </summary>
    /// <param name="assetMaterials">
    /// One entry per <c>ASTB</c> row, in table order: the material this process
    /// interned for that row. A row that is not a material carries
    /// <see cref="MaterialRef.Default"/>.
    /// </param>
    /// <param name="file">
    /// The map's bytes. The adopted world owns them from here and holds them
    /// until <see cref="ReleaseCompiledStaticWorld"/>, because the BSP nodes
    /// read from this blob.
    /// </param>
    // Every mesh is created before anything is destroyed, so a CreateMesh throw
    // leaves what was rendering intact.
    public CompiledStaticWorld AdoptCompiledStaticWorld(
        Renderer renderer,
        scoped in Maps.Compiled.ScmapDocument document,
        ReadOnlySpan<MaterialRef> assetMaterials,
        ContentBlob file,
        CompiledMapLoadReport report)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(report);

        // Wait out a background compile and drop its result, or a later frame
        // would swap it in over the baked chunks. Its dirty cells are not
        // folded back: no compile will ever handle them.
        if (_inFlightCompile is not null)
        {
            try { _inFlightCompile.Wait(); }
            catch (AggregateException) { /* superseded - its failure is irrelevant now */ }
            _inFlightCompile = null;
            _inFlightDirtyCells = null;
        }

        ReadOnlySpan<Maps.Compiled.ScmapChunkRecord> cells = document.Chunks;

        var replacement = new List<StaticWorldChunkMesh>(cells.Length);
        var compiled = new CompiledStaticWorldChunk[cells.Length];
        var created = new List<StaticWorldSubmesh[]>(cells.Length);
        int submeshCount = 0;
        int triangles = 0;
        int trees = 0;

        try
        {
            for (int i = 0; i < cells.Length; i++)
            {
                Maps.Compiled.ScmapChunkRecord cell = cells[i];
                var coord = new ChunkCoord(cell.X, cell.Y, cell.Z);
                var bounds = new Aabb(cell.BoundsMin, cell.BoundsMax);

                int cellTriangles = 0;
                if (cell.MeshSize != 0)
                {
                    Maps.Compiled.ScmapChunkMesh mesh = document.ChunkMesh(i);
                    var submeshes = new StaticWorldSubmesh[mesh.Submeshes.Length];

                    for (int s = 0; s < submeshes.Length; s++)
                    {
                        Maps.Compiled.ScmapSubmeshEntry entry = mesh.Submeshes[s];

                        // AssetIndex is a row in this file, not a MaterialRef.Id.
                        MaterialRef material = entry.NamesAsset && entry.AssetIndex < (uint)assetMaterials.Length
                            ? assetMaterials[(int)entry.AssetIndex]
                            : MaterialRef.Default;

                        // No CPU copy: chunks cull by bounds and query the tree.
                        Mesh gpuMesh = renderer.CreateMesh(
                            mesh.Vertices(s), mesh.Indices(s), VertexAttribute.StandardLayout,
                            MeshCpuAccess.None);

                        submeshes[s] = new StaticWorldSubmesh(
                            material, gpuMesh, ResolveWorldMaterial(material));

                        cellTriangles += (int)(entry.IndexCount / 3);
                    }

                    created.Add(submeshes);
                    submeshCount += submeshes.Length;
                    replacement.Add(new StaticWorldChunkMesh(coord, bounds, Artifact: null, submeshes));
                }

                triangles += cellTriangles;

                FlatBspTree? tree = null;
                if (cell.BspSize != 0)
                {
                    Maps.Compiled.ScmapChunkBsp bsp = document.ChunkBsp(i);

                    // Reads the mapped bytes in place; no node objects.
                    var nodes = new MappedBspNodes(
                        file,
                        document.ChunkBspBlobFileOffset + (int)cell.BspOffset
                            + Maps.Compiled.ScmapFormat.ChunkBspHeaderSize,
                        bsp.Nodes.Length);

                    tree = new FlatBspTree(nodes.Memory, bsp.RootIndex);
                    trees++;
                }

                compiled[i] = new CompiledStaticWorldChunk(coord, bounds, tree, cellTriangles);
            }
        }
        catch
        {
            foreach (StaticWorldSubmesh[] submeshes in created)
                DestroyChunkSubmeshes(renderer, submeshes);
            throw;
        }

        // Commit: the compiled world replaces whatever was drawn before.
        foreach (KeyValuePair<ChunkCoord, StaticWorldChunkMesh> stale in _staticWorldChunkMeshes)
            DestroyChunkSubmeshes(renderer, stale.Value.Submeshes);

        _staticWorldChunkMeshes.Clear();
        _staticWorldChunkList.Clear();
        foreach (StaticWorldChunkMesh chunk in replacement)
        {
            _staticWorldChunkMeshes.Add(chunk.Coord, chunk);
            _staticWorldChunkList.Add(chunk);
        }

        // Z-order, so a run of consecutive chunks is a compact block and the
        // cluster boxes can reject it.
        _staticWorldChunkList.Sort(static (a, b) => a.Coord.MortonKey.CompareTo(b.Coord.MortonKey));
        RebuildChunkClusters();

        StaticWorld = null;
        _staticWorldCarry = null;

        // Mark handled: the graph replacement before a load dirtied the world,
        // and no compile will run to clear it.
        _handledStaticWorldVersion = _staticWorldVersion;
        _compiledStaticWorld?.Dispose();
        _compiledStaticWorld = new CompiledStaticWorld(document.Source, compiled, file);

        report.ChunksLoaded = replacement.Count;
        report.SubmeshesUploaded = submeshCount;
        report.TriangleCount = triangles;
        report.BspChunksLoaded = trees;

        return _compiledStaticWorld;
    }

    /// <summary>
    /// Drops an adopted compiled world: destroys its GPU meshes and releases the
    /// map's bytes, leaving the scene with no static world. Call this before
    /// loading an authored map over it, or every rebuild stays refused.
    /// Render thread only.
    /// </summary>
    public void ReleaseCompiledStaticWorld(Renderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        if (_compiledStaticWorld is null) return;

        foreach (KeyValuePair<ChunkCoord, StaticWorldChunkMesh> chunk in _staticWorldChunkMeshes)
            DestroyChunkSubmeshes(renderer, chunk.Value.Submeshes);

        _staticWorldChunkMeshes.Clear();
        _staticWorldChunkList.Clear();
        RebuildChunkClusters();

        _compiledStaticWorld.Dispose();
        _compiledStaticWorld = null;
    }

    // True when the caller must stand down.
    private bool RefuseForCompiledWorld(string what, bool isRebuild)
    {
        if (_compiledStaticWorld is null) return false;

        if (isRebuild) RefusedStaticWorldRebuilds++;
        else RefusedStaticWorldDirtyMarks++;

        StaticWorldGuardMessage =
            $"{what} was refused: '{_compiledStaticWorld.Source}' arrived baked, and its chunks already hold " +
            "every world brush's surfaces. Carving them again draws every wall twice, which reads as depth " +
            "precision rather than as a map loader. Call ReleaseCompiledStaticWorld first if this scene is " +
            "meant to be authored.";

        return true;
    }

    // Logs only when the counts have moved, not once per frame.
    private void ReportCompiledWorldGuard(ILogger logger)
    {
        if (RefusedStaticWorldRebuilds == _reportedRebuildRefusals
            && RefusedStaticWorldDirtyMarks == _reportedDirtyRefusals)
        {
            return;
        }

        _reportedRebuildRefusals = RefusedStaticWorldRebuilds;
        _reportedDirtyRefusals = RefusedStaticWorldDirtyMarks;

        logger.LogWarning(
            "Static world guard: {Rebuilds} rebuild(s) and {Marks} dirty mark(s) refused on the compiled map " +
            "'{Source}'. {Why}",
            RefusedStaticWorldRebuilds, RefusedStaticWorldDirtyMarks,
            _compiledStaticWorld?.Source ?? "(released)", StaticWorldGuardMessage);
    }
}
