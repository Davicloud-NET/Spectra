using System;
using System.Collections.Generic;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Tests;

// A compiled map copied into ordinary objects. ScmapDocument is a ref struct
// and cannot go in a lambda, field or collection.
internal sealed class ScmapProbe
{
    public required ScmapHeader Header { get; init; }
    public required ScmapMeta Meta { get; init; }
    public required string SceneName { get; init; }
    public required List<string> Strings { get; init; }
    public required List<AssetRow> Assets { get; init; }
    public required List<ScmapNodeRecord> Nodes { get; init; }
    public required List<string> NodeNames { get; init; }
    public required List<ScmapChunkRecord> Chunks { get; init; }
    public required List<ScmapSpawn> Spawns { get; init; }
    public required int SkippedSections { get; init; }
    public required int InvalidDeclaredStates { get; init; }
    public required List<CellGeometry> Geometry { get; init; }
    public required bool HasBrushSource { get; init; }
    public required List<BrushCopy> Brushes { get; init; }
    public required List<EntityCopy> Entities { get; init; }
    public required List<HullCopy> Hulls { get; init; }
    public required bool HasHullMaterials { get; init; }
    public required List<ScmapLightRecord> Lights { get; init; }

    // The document's own count, not recomputed here: tests grade the reader on it.
    public required int TriangleCount { get; init; }

    public readonly record struct AssetRow(PackEntryKind Kind, string Path, ulong ContentHash);

    public sealed record CellGeometry(
        int X,
        int Y,
        int Z,
        List<SubmeshCopy> Submeshes,
        int BspNodeCount,
        int BspRootIndex,
        bool HasBsp);

    public sealed record SubmeshCopy(uint AssetIndex, float[] Vertices, uint[] Indices);

    public sealed record BrushCopy(
        uint NodeIndex,
        System.Numerics.Plane[] Planes,
        ScmapFaceRecord[] Faces);

    // FaceAssets is empty when the map has no COLM section.
    public sealed record HullCopy(uint NodeIndex, System.Numerics.Plane[] Planes, uint[] FaceAssets);

    public sealed record EntityCopy(
        uint NodeIndex,
        string ClassName,
        List<KeyValuePair<string, string>> Keyvalues,
        List<EntityConnection> Connections);

    public static ScmapProbe Read(ReadOnlySpan<byte> file, string source = "fixture.scmap")
    {
        ScmapDocument document = ScmapReader.Read(file, source);

        var strings = new List<string>(document.Strings.Count);
        for (int i = 0; i < document.Strings.Count; i++) strings.Add(document.Strings.GetString(i));

        var assets = new List<AssetRow>(document.Assets.Length);
        for (int i = 0; i < document.Assets.Length; i++)
        {
            assets.Add(new AssetRow(
                document.Assets[i].AssetKind,
                document.AssetPath(i),
                document.Assets[i].ContentHash));
        }

        var nodes = new List<ScmapNodeRecord>(document.Nodes.Length);
        var nodeNames = new List<string>(document.Nodes.Length);
        for (int i = 0; i < document.Nodes.Length; i++)
        {
            nodes.Add(document.Nodes[i]);
            nodeNames.Add(document.NodeName(i));
        }

        var chunks = new List<ScmapChunkRecord>(document.Chunks.Length);
        var geometry = new List<CellGeometry>(document.Chunks.Length);
        for (int i = 0; i < document.Chunks.Length; i++)
        {
            ScmapChunkRecord cell = document.Chunks[i];
            chunks.Add(cell);

            var submeshes = new List<SubmeshCopy>();
            if (cell.MeshSize != 0)
            {
                ScmapChunkMesh mesh = document.ChunkMesh(i);
                for (int s = 0; s < mesh.Submeshes.Length; s++)
                {
                    submeshes.Add(new SubmeshCopy(
                        mesh.Submeshes[s].AssetIndex,
                        mesh.Vertices(s).ToArray(),
                        mesh.Indices(s).ToArray()));
                }
            }

            int bspNodes = 0;
            int bspRoot = 0;
            if (cell.BspSize != 0)
            {
                ScmapChunkBsp bsp = document.ChunkBsp(i);
                bspNodes = bsp.Nodes.Length;
                bspRoot = bsp.RootIndex;
            }

            geometry.Add(new CellGeometry(
                cell.X, cell.Y, cell.Z, submeshes, bspNodes, bspRoot, cell.BspSize != 0));
        }

        var brushes = new List<BrushCopy>();
        if (document.HasBrushSource)
        {
            ScmapBrushSource kept = document.BrushSource();
            for (int i = 0; i < kept.Brushes.Length; i++)
            {
                ScmapBrushRecord record = kept.Brushes[i];
                brushes.Add(new BrushCopy(
                    record.NodeIndex,
                    kept.Planes.Slice((int)record.PlaneStart, (int)record.PlaneCount).ToArray(),
                    kept.Faces.Slice((int)record.PlaneStart, (int)record.PlaneCount).ToArray()));
            }
        }

        var entities = new List<EntityCopy>(document.Entities.Length);
        foreach (ScmapEntityRecord record in document.Entities)
        {
            var keyvalues = new List<KeyValuePair<string, string>>();
            foreach (ScmapKeyvalueRecord pair in
                document.Keyvalues.Slice((int)record.KeyvalueStart, (int)record.KeyvalueCount))
            {
                keyvalues.Add(new(document.StringAt(pair.KeyString), document.StringAt(pair.ValueString)));
            }

            var connections = new List<EntityConnection>();
            foreach (ScmapConnectionRecord wire in
                document.Connections.Slice((int)record.ConnectionStart, (int)record.ConnectionCount))
            {
                connections.Add(new EntityConnection(
                    document.StringAt(wire.OutputNameString),
                    document.StringAt(wire.TargetNameString),
                    document.StringAt(wire.InputNameString),
                    document.StringAt(wire.ParameterString),
                    wire.Delay,
                    wire.TimesToFire));
            }

            entities.Add(new EntityCopy(
                record.NodeIndex, document.StringAt(record.ClassNameString), keyvalues, connections));
        }

        var hulls = new List<HullCopy>(document.CollisionHulls.Length);
        foreach (ScmapHullRecord hull in document.CollisionHulls)
        {
            hulls.Add(new HullCopy(
                hull.NodeIndex,
                document.CollisionPlanes.Slice((int)hull.PlaneStart, (int)hull.PlaneCount).ToArray(),
                document.HasCollisionFaceMaterials
                    ? document.CollisionFaceAssets.Slice((int)hull.PlaneStart, (int)hull.PlaneCount).ToArray()
                    : []));
        }

        var spawns = new List<ScmapSpawn>(document.Spawns.Length);
        for (int i = 0; i < document.Spawns.Length; i++) spawns.Add(document.Spawns[i]);

        return new ScmapProbe
        {
            Header = document.Header,
            Meta = document.Meta,
            SceneName = document.SceneName,
            Strings = strings,
            Assets = assets,
            Nodes = nodes,
            NodeNames = nodeNames,
            Chunks = chunks,
            Spawns = spawns,
            SkippedSections = document.SkippedSectionCount,
            InvalidDeclaredStates = document.InvalidDeclaredStateCount,
            Geometry = geometry,
            HasBrushSource = document.HasBrushSource,
            Brushes = brushes,
            Entities = entities,
            Hulls = hulls,
            HasHullMaterials = document.HasCollisionFaceMaterials,
            Lights = [.. document.Lights],
            TriangleCount = document.TriangleCount,
        };
    }
}
