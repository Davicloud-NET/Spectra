using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// What a compiled map load could not bring across: per-node misses in this
/// file, plus <see cref="FormatGaps"/>, which the format cannot carry at all.
/// </summary>
public sealed class CompiledMapLoadReport
{
    private readonly List<string> _unboundMeshInstances = [];
    private readonly List<string> _partBrushesWithoutSource = [];
    private readonly List<string> _brushesRefused = [];
    private readonly List<string> _collisionHullsRefused = [];

    /// <summary>
    /// What a <c>.scmap</c> at this format version cannot carry, whatever is in
    /// the file. Logged on every load.
    /// </summary>
    public static IReadOnlyList<string> FormatGaps { get; } =
    [
        "spawns (scene.spawn is a preserved .smap member, so META always writes a spawn count of zero)",
        "scripts (SCPT/LUAB/LUAS are claimed and empty)",
        "mesh-instance submesh indices (MeshSource.SubmeshIndex has no table to name)",
        "standalone brush transforms (BRSH carries planes and faces; a node-attached brush ignores it)",
        "world surface materials for ray hits (COLL carries planes and no faces, so a ray that hits baked " +
            "geometry names no material)",
    ];

    /// <summary>Nodes that named a mesh this loader did not attach.</summary>
    public IReadOnlyList<string> UnboundMeshInstances => _unboundMeshInstances;

    /// <summary>Part brushes whose planes were not in <c>BRSH</c>, so they draw nothing.</summary>
    public IReadOnlyList<string> PartBrushesWithoutSource => _partBrushesWithoutSource;

    /// <summary>Brushes whose planes this engine's <c>Brush</c> refused to build.</summary>
    public IReadOnlyList<string> BrushesRefused => _brushesRefused;

    /// <summary>
    /// Baked world brushes that got no collision hull, because their planes
    /// bound no solid or their node's transform is not rigid. They are drawn
    /// and can be walked through.
    /// </summary>
    public IReadOnlyList<string> CollisionHullsRefused => _collisionHullsRefused;

    /// <summary>Nodes rebuilt from the file.</summary>
    public int NodesLoaded { get; internal set; }

    /// <summary>Nodes that got an entity from the file.</summary>
    public int EntitiesLoaded { get; internal set; }

    /// <summary>Nodes that got a light from the file.</summary>
    public int LightsLoaded { get; internal set; }

    /// <summary>Baked world brushes that became collision hulls.</summary>
    public int CollisionHullsLoaded { get; internal set; }

    /// <summary>
    /// True when the map has collision hulls and no <c>COLM</c> section to say
    /// what their faces are made of. The hulls still collide. A solid span
    /// through the world then names the default material.
    /// </summary>
    public bool CollisionFaceMaterialsMissing { get; internal set; }

    /// <summary>Asset-table rows interned into this process's material registry.</summary>
    public int MaterialsInterned { get; internal set; }

    /// <summary>Cells whose baked geometry became GPU meshes.</summary>
    public int ChunksLoaded { get; internal set; }

    /// <summary>GPU meshes created, one per (cell, material).</summary>
    public int SubmeshesUploaded { get; internal set; }

    /// <summary>Triangles the load put on the GPU.</summary>
    public int TriangleCount { get; internal set; }

    /// <summary>Cells that carry a queryable flat BSP tree.</summary>
    public int BspChunksLoaded { get; internal set; }

    /// <summary>Section records this build stepped over because it did not know the code.</summary>
    public int SkippedSections { get; internal set; }

    /// <summary>
    /// Baked world brushes whose planes were in <c>BRSH</c> and were not rebuilt,
    /// because their surfaces are already in the chunks.
    /// </summary>
    public int BakedBrushSourcesSkipped { get; private set; }

    /// <summary>Whether nothing in this file was lost. Says nothing about <see cref="FormatGaps"/>.</summary>
    public bool IsComplete =>
        _unboundMeshInstances.Count == 0 && _partBrushesWithoutSource.Count == 0 && _brushesRefused.Count == 0
        && _collisionHullsRefused.Count == 0 && !CollisionFaceMaterialsMissing;

    /// <summary>One sentence naming what this file lost, or null when nothing was.</summary>
    public string? Describe()
    {
        if (IsComplete) return null;

        var parts = new List<string>(4);
        if (_unboundMeshInstances.Count > 0)
            parts.Add($"{_unboundMeshInstances.Count} mesh instance(s) unbound ({Join(_unboundMeshInstances)})");
        if (_partBrushesWithoutSource.Count > 0)
            parts.Add($"{_partBrushesWithoutSource.Count} part brush(es) with no planes ({Join(_partBrushesWithoutSource)})");
        if (_brushesRefused.Count > 0)
            parts.Add($"{_brushesRefused.Count} brush(es) refused ({Join(_brushesRefused)})");
        if (_collisionHullsRefused.Count > 0)
        {
            parts.Add(
                $"{_collisionHullsRefused.Count} world brush(es) with no collision " +
                $"({Join(_collisionHullsRefused)})");
        }

        if (CollisionFaceMaterialsMissing)
        {
            parts.Add(
                "no materials on the world's collision hulls (the map has no COLM section, so a solid span " +
                "through the world names the default material; recook the map)");
        }

        return string.Join("; ", parts) + ".";
    }

    /// <summary>One sentence naming what this build cannot carry, whatever the file holds.</summary>
    public static string DescribeFormatGaps() =>
        $".scmap v{EngineInfo.CompiledMapFormatVersion} carries no " + string.Join("; no ", FormatGaps) + ".";

    internal void BakedBrushSourceSkipped() => BakedBrushSourcesSkipped++;

    internal void MeshInstanceUnbound(string node) => _unboundMeshInstances.Add(node);

    internal void PartBrushWithoutSource(string node) => _partBrushesWithoutSource.Add(node);

    internal void BrushRefused(string node) => _brushesRefused.Add(node);

    internal void CollisionHullRefused(string node) => _collisionHullsRefused.Add(node);

    private static string Join(List<string> names) =>
        names.Count <= 5
            ? string.Join(", ", names)
            : string.Join(", ", names.GetRange(0, 5)) + $", and {names.Count - 5} more";
}
