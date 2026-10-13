using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace SpectraEngine.Core.Maps;

/// <summary>
/// Projects a live <see cref="Scene.Scene"/> to a <see cref="MapDocument"/> and
/// builds one back. Lossy: a mesh built in code has no file to name, and brush
/// planes come back normalised. Render thread only.
/// </summary>
// Every node gets its own Brush instance. CsgCompileCache and PartBrushMeshCache
// key on reference identity, so a shared one re-carves on every compile.
public static class MapSceneBinder
{
    /// <summary>Projects <paramref name="scene"/>'s graph to a document.</summary>
    public static MapDocument FromScene(Scene.Scene scene) => FromScene(scene, null);

    /// <summary>
    /// Projects <paramref name="scene"/>'s graph to a document, recording what
    /// could not be written into <paramref name="report"/>.
    /// </summary>
    public static MapDocument FromScene(Scene.Scene scene, MapSaveReport? report)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var document = new MapDocument();
        document.Scene.Name = scene.Name;

        foreach (SceneNode child in scene.Root.Children)
            document.Nodes.Add(NodeToMap(child, report));

        document.MinimumReadableVersion = RequiredReaderVersion(scene);
        return document;
    }

    // The oldest reader that can open and re-save this scene without losing data.
    // Per document: only a shaped light (spot, rect, disc), an entity, a node
    // flag that is off, a script, a declared realm or state, or a tag or an
    // attribute raises the floor, since an older editor would drop those on
    // save. Light kind is tested, not its numbers. Takes the max of what applies.
    private static int RequiredReaderVersion(Scene.Scene scene)
    {
        int floor = EngineInfo.MinimumReadableMapVersion;

        foreach (SceneNode node in scene.LightNodes)
        {
            if (node.Light is { Kind: not (LightKind.Directional or LightKind.Point) })
            {
                floor = Math.Max(floor, EngineInfo.LightShapeMapVersion);
                break;
            }
        }

        // Descendants only: the root's own payload is never written.
        SceneNode root = scene.Root;

        if (AnyBelow(root, static node => node.Entity is not null))
            floor = Math.Max(floor, EngineInfo.EntityMapVersion);

        // What would write collide, query, touch or render.
        if (AnyBelow(root, static node =>
                !node.CanCollide || !node.CanQuery || !node.CanTouch || !node.IsRendered))
        {
            floor = Math.Max(floor, EngineInfo.NodeFlagsMapVersion);
        }

        if (AnyBelow(root, static node => node.Script is not null))
            floor = Math.Max(floor, EngineInfo.ScriptMapVersion);

        if (AnyBelow(root, static node =>
                node.DeclaredRealm != NodeRealm.Inherit || node.DeclaredState != NodeState.Inherit))
        {
            floor = Math.Max(floor, EngineInfo.NodeRealmMapVersion);
        }

        if (AnyBelow(root, static node => node.Tags.Count > 0 || node.Attributes.Count > 0))
            floor = Math.Max(floor, EngineInfo.NodeTagsMapVersion);

        return floor;
    }

    private static bool AnyBelow(SceneNode node, Func<SceneNode, bool> carries)
    {
        foreach (SceneNode child in node.Children)
        {
            if (carries(child) || AnyBelow(child, carries))
                return true;
        }

        return false;
    }

    private static MapNode NodeToMap(SceneNode node, MapSaveReport? report)
    {
        var mapped = new MapNode
        {
            Id = node.Id,
            Name = node.Name,
            // The node's own declaration, never what its parents make of it.
            Realm = node.DeclaredRealm,
            State = node.DeclaredState,
            // World is the default and is omitted.
            Kind = node.BrushKind == BrushKind.Part ? BrushKind.Part : null,
            Collide = node.CanCollide,
            Query = node.CanQuery,
            Touch = node.CanTouch,
            Render = node.IsRendered,
            Transform = ToMap(node.LocalTransform),
        };

        if (node.Brush is { } brush)
            mapped.Brush = BrushToMap(brush);

        if (node.Light is { } light)
            mapped.Light = LightToMap(light);

        if (node.Entity is { } entity)
            mapped.Entity = EntityToMap(entity);

        if (node.Script is { } script)
            mapped.Script = ScriptToMap(script);

        mapped.Tags.AddRange(node.Tags);
        mapped.Attributes.AddRange(node.Attributes);

        mapped.Mesh = MeshToMap(node, report);

        foreach (SceneNode child in node.Children)
            mapped.Children.Add(NodeToMap(child, report));

        return mapped;
    }

    // Copy every field. This is a fresh MapLight, so a member left out here is
    // deleted from the file on the next save. The same goes for the entity and
    // the script below.
    private static MapLight LightToMap(Light light) => new()
    {
        Kind = light.Kind,
        Color = light.Color,
        Intensity = light.Intensity,
        Range = light.Range,
        Enabled = light.Enabled,
        InnerAngle = light.InnerAngle,
        OuterAngle = light.OuterAngle,
        Width = light.Width,
        Height = light.Height,
        Radius = light.Radius,
    };

    private static MapEntity EntityToMap(EntityData entity)
    {
        var mapped = new MapEntity { Class = entity.ClassName };

        // Authored order, duplicates included.
        mapped.Keys.AddRange(entity.Keyvalues);

        foreach (EntityConnection wire in entity.Connections)
        {
            mapped.Outputs.Add(new MapConnection
            {
                Output = wire.Output,
                Target = wire.TargetName,
                Input = wire.Input,
                Param = wire.Parameter,
                Delay = wire.Delay,
                Times = wire.TimesToFire,
            });
        }

        return mapped;
    }

    private static MapScript ScriptToMap(Script script) => new()
    {
        Path = script.Path,
        Enabled = script.IsEnabled,
        Module = script.IsModule,
    };

    // A mesh is saved as a reference to its model file, never as geometry.
    private static MapMeshSource? MeshToMap(SceneNode node, MapSaveReport? report)
    {
        if (node.MeshRenderer is null)
            return null;

        if (node.MeshSource is { } source)
            return new MapMeshSource { Model = source.ModelPath, Submesh = source.MeshIndex };

        // Built in code: no file to name, so report it.
        report?.RecordUnsourcedMesh(node);
        return null;
    }

    private static MapBrush BrushToMap(Brush brush)
    {
        var mapped = new MapBrush
        {
            Operation = brush.Operation,
            Transform = brush.Transform,
        };

        foreach (Plane plane in brush.LocalPlanes)
            mapped.Planes.Add(new Vector4(plane.Normal.X, plane.Normal.Y, plane.Normal.Z, plane.D));

        foreach (FaceSurface face in brush.FaceSurfaces)
            mapped.Faces.Add(FaceToMap(face));

        return mapped;
    }

    private static MapFace FaceToMap(FaceSurface face)
    {
        var mapped = new MapFace
        {
            // The path, never the id: ids depend on interning order. TryGetPath
            // is false for the default material, which writes nothing.
            Material = MaterialRegistry.TryGetPath(face.Material, out string path) ? path : null,
            // World-aligned (zero axes) is written as an absent member.
            UAxis = face.IsWorldAligned ? null : face.UAxis,
            VAxis = face.IsWorldAligned ? null : face.VAxis,
            UOffset = face.UOffset,
            VOffset = face.VOffset,
            // FaceSurface treats a stored zero scale as 1, but its constructor
            // refuses a zero. Write the effective value.
            UScale = face.UScale != 0f ? face.UScale : 1f,
            VScale = face.VScale != 0f ? face.VScale : 1f,
        };

        return mapped;
    }

    private static MapTransform ToMap(Transform transform) => new()
    {
        Position = transform.Position,
        Rotation = transform.Rotation,
        Scale = transform.Scale,
    };

    /// <summary>
    /// Replaces <paramref name="scene"/>'s graph with the document's. The root node itself is kept.
    /// </summary>
    /// <exception cref="MapFormatException">A node's brush cannot be built.</exception>
    public static void ApplyTo(MapDocument document, Scene.Scene scene) =>
        ApplyTo(document, scene, null);

    /// <summary>
    /// Replaces the scene's graph with the document's, recording anything that
    /// could not be resolved into <paramref name="report"/>.
    /// </summary>
    /// <exception cref="MapFormatException">A node's brush cannot be built.</exception>
    public static void ApplyTo(MapDocument document, Scene.Scene scene, MapLoadReport? report)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scene);

        for (int i = scene.Root.Children.Count - 1; i >= 0; i--)
            scene.Root.RemoveChild(scene.Root.Children[i]);

        scene.Name = document.Scene.Name;

        foreach (MapNode node in document.Nodes)
            scene.Root.AddChild(ToSceneNode(node, scene.Assets, report));

        // After the whole graph: a wire may name a node later in the document.
        if (report is not null)
            ReportUnresolvedTargets(document, report);
    }

    // Reports wires whose target is not in this map. The wires are kept: a
    // dangling target is usually a rename in progress.
    private static void ReportUnresolvedTargets(MapDocument document, MapLoadReport report)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var wired = new List<MapNode>();
        CollectTargets(document.Nodes, names, wired);

        foreach (MapNode node in wired)
        {
            foreach (MapConnection wire in node.Entity!.Outputs)
            {
                if (!Resolves(wire.Target, names))
                    report.RecordUnresolvedTarget(node.Name, wire.Output, wire.Target);
            }
        }
    }

    private static void CollectTargets(List<MapNode> nodes, HashSet<string> names, List<MapNode> wired)
    {
        foreach (MapNode node in nodes)
        {
            names.Add(node.Name);
            if (node.Entity is { Outputs.Count: > 0 })
                wired.Add(node);

            CollectTargets(node.Children, names, wired);
        }
    }

    private static bool Resolves(string target, HashSet<string> names)
    {
        // An empty target is a half-wired entity, not a dangling wire.
        if (target.Length == 0)
            return true;

        // Runtime tokens resolve when the output fires.
        if (Array.IndexOf(MapFormat.RuntimeTargets, target) >= 0)
            return true;

        if (target[^1] == MapFormat.TargetWildcard)
        {
            string prefix = target[..^1];
            foreach (string name in names)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        return names.Contains(target);
    }

    private static SceneNode ToSceneNode(MapNode mapped, AssetManager? assets, MapLoadReport? report)
    {
        // Keep the saved id: commands and undo address nodes by it.
        var node = new SceneNode(mapped.Name, mapped.Id)
        {
            LocalTransform = new Transform
            {
                Position = mapped.Transform.Position,
                Rotation = mapped.Transform.Rotation,
                Scale = mapped.Transform.Scale,
            },
        };

        // Kind before brush: the brush setter reads the kind, so the other
        // order briefly admits a part brush to the static world.
        if (mapped.Kind is { } kind)
            node.BrushKind = kind;

        node.CanCollide = mapped.Collide;
        node.CanQuery = mapped.Query;
        node.CanTouch = mapped.Touch;
        node.IsRendered = mapped.Render;
        node.DeclaredRealm = mapped.Realm;
        node.DeclaredState = mapped.State;

        if (mapped.Brush is { } brush)
            node.Brush = ToBrush(brush, mapped);

        if (mapped.Mesh is { } mesh)
            AttachMesh(node, mesh, assets, report);

        if (mapped.Light is { } light)
            node.Light = ToLight(light);

        if (mapped.Entity is { } entity)
            node.Entity = ToEntity(entity);

        if (mapped.Script is { } script)
            node.Script = ToScript(script, mapped);

        node.Tags = MapNodeSets.TagsOf(mapped);
        node.Attributes = MapNodeSets.AttributesOf(mapped);

        // In document order: child order is placement order, which breaks ties
        // in the carve.
        foreach (MapNode child in mapped.Children)
            node.AddChild(ToSceneNode(child, assets, report));

        return node;
    }

    // Inner angle before outer: OuterAngle clamps against the inner one.
    private static Light ToLight(MapLight mapped) => new()
    {
        Kind = mapped.Kind,
        Color = mapped.Color,
        Intensity = mapped.Intensity,
        Range = mapped.Range,
        Enabled = mapped.Enabled,
        InnerAngle = mapped.InnerAngle,
        OuterAngle = mapped.OuterAngle,
        Width = mapped.Width,
        Height = mapped.Height,
        Radius = mapped.Radius,
    };

    // No catalogue lookup, so a class this build lacks round-trips.
    private static EntityData ToEntity(MapEntity mapped)
    {
        var data = new EntityData(mapped.Class);

        // Add, not SetValue, which would collapse a duplicate key.
        data.Keyvalues.AddRange(mapped.Keys);

        foreach (MapConnection wire in mapped.Outputs)
        {
            data.Connections.Add(new EntityConnection(
                wire.Output, wire.Target, wire.Input, wire.Param, wire.Delay, wire.Times));
        }

        return data;
    }

    // A model that cannot be resolved leaves a node with no renderer and a line
    // in the report. Unlike a bad brush, it never throws.
    private static void AttachMesh(
        SceneNode node, MapMeshSource mesh, AssetManager? assets, MapLoadReport? report)
    {
        if (assets is null)
        {
            report?.RecordUnresolved(node.Name, mesh.Model, "the scene has no asset manager attached");
            return;
        }

        try
        {
            ModelAsset model = assets.LoadModel(mesh.Model);
            if (model.Metadata is not { } data)
            {
                report?.RecordUnresolved(node.Name, mesh.Model, model.Error ?? "the model is not loaded");
                return;
            }

            // A re-exported model can have fewer submeshes.
            if (mesh.Submesh >= model.Meshes.Count)
            {
                report?.RecordUnresolved(node.Name, mesh.Model,
                    $"submesh {mesh.Submesh} does not exist; the model has {model.Meshes.Count}");
                return;
            }

            node.MeshRenderer = new MeshRenderer(
                model.Meshes[mesh.Submesh], model.MaterialFor(data.Meshes[mesh.Submesh].MaterialIndex));
            node.MeshSource = new MeshSource(mesh.Model, mesh.Submesh);
        }
        catch (Exception ex) when (
            ex is FileNotFoundException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            report?.RecordUnresolved(node.Name, mesh.Model, ex.Message);
        }
    }

    // The reader has checked a path it read. A document built in code has not
    // been through it.
    private static Script ToScript(MapScript mapped, MapNode owner)
    {
        try
        {
            return new Script(mapped.Path) { IsEnabled = mapped.Enabled, IsModule = mapped.Module };
        }
        catch (ArgumentException ex)
        {
            throw new MapFormatException(
                $"This script names no file under the content root: {ex.Message}",
                owner.Name, owner.SourceOffset, ex);
        }
    }

    private static Brush ToBrush(MapBrush mapped, MapNode owner)
    {
        var planes = new Plane[mapped.Planes.Count];
        for (int i = 0; i < planes.Length; i++)
        {
            Vector4 p = mapped.Planes[i];
            planes[i] = new Plane(p.X, p.Y, p.Z, p.W);
        }

        var faces = new FaceSurface[mapped.Faces.Count];
        for (int i = 0; i < faces.Length; i++)
        {
            MapFace face = mapped.Faces[i];
            MaterialRef material = string.IsNullOrEmpty(face.Material)
                ? MaterialRef.Default
                : MaterialRegistry.Intern(face.Material);

            try
            {
                faces[i] = new FaceSurface(
                    material,
                    face.UAxis ?? Vector3.Zero,
                    face.VAxis ?? Vector3.Zero,
                    face.UOffset, face.VOffset, face.UScale, face.VScale);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new MapFormatException(
                    $"Face {i}: {ex.Message}", owner.Name, owner.SourceOffset, ex);
            }
        }

        try
        {
            return new Brush(planes, mapped.Transform, faces, mapped.Operation);
        }
        catch (ArgumentException ex)
        {
            // Brush's own error names plane indices only. Add the node and offset.
            throw new MapFormatException(
                $"This brush cannot be built: {ex.Message}", owner.Name, owner.SourceOffset, ex);
        }
    }
}

/// <summary>
/// What a save could not write down. A mesh built in code names no model file,
/// so its node is saved without geometry.
/// </summary>
public sealed class MapSaveReport
{
    private readonly List<string> _unsourced = [];

    /// <summary>Nodes whose mesh had no model behind it to name.</summary>
    public IReadOnlyList<string> UnsourcedMeshNodes => _unsourced;

    /// <summary>Whether anything was lost.</summary>
    public bool IsComplete => _unsourced.Count == 0;

    internal void RecordUnsourcedMesh(SceneNode node) => _unsourced.Add(node.Name);

    /// <summary>A one-line summary for a log, or null when nothing was lost.</summary>
    public string? Describe() => _unsourced.Count == 0
        ? null
        : $"{_unsourced.Count} mesh node(s) saved without geometry, because their meshes were built in "
          + $"code and name no model file: {Join(_unsourced)}";

    internal static string Join(List<string> names) =>
        string.Join(", ", names.Count > 8 ? names.GetRange(0, 8) : names)
        + (names.Count > 8 ? ", ..." : string.Empty);
}

/// <summary>
/// What a load could not resolve. A map naming a missing model still loads; the
/// node arrives without a renderer and the reason is recorded here.
/// </summary>
public sealed class MapLoadReport
{
    private readonly List<string> _unresolved = [];
    private readonly List<string> _danglingWires = [];

    /// <summary>One line per node whose model reference could not be resolved.</summary>
    public IReadOnlyList<string> UnresolvedMeshes => _unresolved;

    /// <summary>
    /// One line per wire naming a target nothing in this map answers to. The
    /// wires themselves are kept.
    /// </summary>
    public IReadOnlyList<string> UnresolvedTargets => _danglingWires;

    /// <summary>Whether everything the map named was found.</summary>
    public bool IsComplete => _unresolved.Count == 0 && _danglingWires.Count == 0;

    internal void RecordUnresolved(string nodeName, string modelPath, string reason) =>
        _unresolved.Add($"{nodeName} -> {modelPath} ({reason})");

    internal void RecordUnresolvedTarget(string nodeName, string output, string target) =>
        _danglingWires.Add($"{nodeName}.{output} -> {target}");

    /// <summary>A one-line summary for a log, or null when nothing was missing.</summary>
    public string? Describe()
    {
        string? meshes = _unresolved.Count == 0
            ? null
            : $"{_unresolved.Count} mesh node(s) loaded without geometry: {MapSaveReport.Join(_unresolved)}";

        string? wires = _danglingWires.Count == 0
            ? null
            : $"{_danglingWires.Count} connection(s) name a target this map does not have, and were "
              + $"kept: {MapSaveReport.Join(_danglingWires)}";

        if (meshes is null) return wires;
        return wires is null ? meshes : $"{meshes}; {wires}";
    }
}
