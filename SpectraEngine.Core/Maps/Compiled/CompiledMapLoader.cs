using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// Loads a baked <c>.scmap</c> into a live scene without running any CSG.
/// </summary>
// Pass order: reader gates, ASTB interned in table order, nodes rebuilt in one
// forward pass with their flags, lights and entities, collision hulls built,
// chunks adopted.
// A baked world brush gets no Brush even when BRSH holds its planes. Rebuilding
// it would carve the level again and draw every wall twice.
public static class CompiledMapLoader
{
    /// <summary>
    /// Replaces <paramref name="scene"/>'s graph and static world with the
    /// compiled map in <paramref name="file"/>. Render thread only.
    /// </summary>
    /// <param name="file">
    /// The map's bytes. Ownership passes to this call: the scene keeps them for the
    /// world's lifetime on success, and they are disposed here on failure.
    /// </param>
    /// <param name="source">The map's logical asset path, used in messages.</param>
    /// <exception cref="ScmapFormatException">The file is not a readable, loadable <c>.scmap</c>.</exception>
    public static CompiledMapLoadReport Load(
        Scene.Scene scene, Renderer renderer, ContentBlob file, string source)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(source);

        var report = new CompiledMapLoadReport();

        try
        {
            ScmapDocument document = ScmapReader.Read(file.Span, source);
            report.SkippedSections = document.SkippedSectionCount;

            // Before the graph is replaced: the old adoption holds the rebuild
            // guard, which removing nodes would trip.
            scene.ReleaseCompiledStaticWorld(renderer);

            MaterialRef[] materials = InternAssets(in document, report);
            SceneNode[] nodes = RebuildGraph(scene, in document, materials, report);
            BrushPlacement[] collision = BuildCollision(in document, nodes, materials, report);

            scene.AdoptCompiledStaticWorld(renderer, in document, materials, collision, file, report);
            return report;
        }
        catch
        {
            // Still ours. An undisposed blob pins its pack for the process's life.
            file.Dispose();
            throw;
        }
    }

    // Result is indexed by ASTB row. Material ids are per-process interning
    // order and must not be confused with row numbers.
    private static MaterialRef[] InternAssets(scoped in ScmapDocument document, CompiledMapLoadReport report)
    {
        var materials = new MaterialRef[document.Assets.Length];
        int interned = 0;

        for (int i = 0; i < materials.Length; i++)
        {
            // Model rows and reserved kinds must not be interned as materials.
            if (document.Assets[i].AssetKind != PackEntryKind.Material)
            {
                materials[i] = MaterialRef.Default;
                continue;
            }

            materials[i] = MaterialRegistry.Intern(document.AssetPath(i));
            interned++;
        }

        report.MaterialsInterned = interned;
        return materials;
    }

    // One forward pass: the reader has checked ParentIndex < own index, so a
    // parent exists before its child is read.
    private static SceneNode[] RebuildGraph(
        Scene.Scene scene,
        scoped in ScmapDocument document,
        ReadOnlySpan<MaterialRef> materials,
        CompiledMapLoadReport report)
    {
        for (int i = scene.Root.Children.Count - 1; i >= 0; i--)
            scene.Root.RemoveChild(scene.Root.Children[i]);

        scene.Name = document.SceneName;

        ScmapBrushSource brushes = document.HasBrushSource
            ? document.BrushSource()
            : default;

        // node index -> BRSH record. The file only links brush -> node.
        var brushOfNode = new int[document.Nodes.Length];
        brushOfNode.AsSpan().Fill(-1);
        for (int b = 0; b < brushes.Brushes.Length; b++)
            brushOfNode[(int)brushes.Brushes[b].NodeIndex] = b;

        var nodes = new SceneNode[document.Nodes.Length];
        int nextEntity = 0;
        int nextLight = 0;

        for (int i = 0; i < nodes.Length; i++)
        {
            ScmapNodeRecord record = document.Nodes[i];
            string name = document.NodeName(i);
            ScmapPayloadFlags flags = record.PayloadFlags;

            // Keeps the stored id. Commands and entity wires address nodes by it.
            var node = new SceneNode(name, record.NodeId)
            {
                LocalTransform = new Transform
                {
                    Position = record.LocalPosition,
                    Rotation = record.LocalRotation,
                    Scale = record.LocalScale,
                },
                CanCollide = (flags & ScmapPayloadFlags.NoCollide) == 0,
                CanQuery = (flags & ScmapPayloadFlags.NoQuery) == 0,
                CanTouch = (flags & ScmapPayloadFlags.NoTouch) == 0,
                IsRendered = (flags & ScmapPayloadFlags.NoRender) == 0,
            };

            AttachPayload(node, in record, i, name, brushes, brushOfNode[i], materials, report);

            // Light and entity records are in node order, so one cursor each
            // finds them all.
            if (nextLight < document.Lights.Length && document.Lights[nextLight].NodeIndex == (uint)i)
                node.Light = document.Lights[nextLight++].ToLight();

            if (nextEntity < document.Entities.Length && document.Entities[nextEntity].NodeIndex == (uint)i)
                node.Entity = BuildEntity(in document, document.Entities[nextEntity++]);

            if (record.ParentIndex < 0) scene.Root.AddChild(node);
            else nodes[record.ParentIndex].AddChild(node);

            nodes[i] = node;
        }

        report.NodesLoaded = nodes.Length;
        report.EntitiesLoaded = nextEntity;
        report.LightsLoaded = nextLight;

        return nodes;
    }

    // The world brushes come back as hulls placed by their nodes, for collision
    // only. No node gets the brush, so no compile can pick it up.
    private static BrushPlacement[] BuildCollision(
        scoped in ScmapDocument document,
        SceneNode[] nodes,
        ReadOnlySpan<MaterialRef> materials,
        CompiledMapLoadReport report)
    {
        var placements = new List<BrushPlacement>(document.CollisionHulls.Length);

        foreach (ScmapHullRecord hull in document.CollisionHulls)
        {
            SceneNode node = nodes[(int)hull.NodeIndex];
            Matrix4x4 world = node.WorldMatrix;

            // A scaled placement gives planes that are not unit length, which
            // collides in the wrong place. The cook refuses one.
            if (Scene.Scene.DescribeNonRigidDefect(world) is not null ||
                !TryBuildHull(in document, hull, materials, out Brush? brush))
            {
                report.CollisionHullRefused(node.Name);
                continue;
            }

            placements.Add(new BrushPlacement(brush, world));
        }

        report.CollisionHullsLoaded = placements.Count;
        report.CollisionFaceMaterialsMissing =
            document.CollisionHulls.Length > 0 && !document.HasCollisionFaceMaterials;

        return [.. placements];
    }

    private static bool TryBuildHull(
        scoped in ScmapDocument document,
        ScmapHullRecord hull,
        ReadOnlySpan<MaterialRef> materials,
        [NotNullWhen(true)] out Brush? brush)
    {
        Plane[] planes = document.CollisionPlanes.Slice((int)hull.PlaneStart, (int)hull.PlaneCount).ToArray();

        BrushOperation operation = document.Nodes[(int)hull.NodeIndex].IsSubtractiveBrush
            ? BrushOperation.Subtractive
            : BrushOperation.Additive;

        try
        {
            brush = new Brush(planes, Matrix4x4.Identity, HullFaces(in document, hull, materials), operation);
            return true;
        }
        catch (ArgumentException)
        {
            // Planes that bound no solid. One wall without collision is
            // reported; it should not fail the whole load.
            brush = null;
            return false;
        }
    }

    // What each face of a hull is made of, so a query can name it. Null when
    // the map carries no face materials: every face is then the default one.
    // Only the material is kept. A hull is never drawn, so it has no texture axes.
    private static FaceSurface[]? HullFaces(
        scoped in ScmapDocument document, ScmapHullRecord hull, ReadOnlySpan<MaterialRef> materials)
    {
        if (!document.HasCollisionFaceMaterials) return null;

        ReadOnlySpan<uint> rows = document.CollisionFaceAssets.Slice((int)hull.PlaneStart, (int)hull.PlaneCount);
        var faces = new FaceSurface[rows.Length];

        for (int i = 0; i < faces.Length; i++)
        {
            // The reader has checked that a row is a material of this table.
            faces[i] = new FaceSurface(
                rows[i] == ScmapFormat.NoAssetIndex ? MaterialRef.Default : materials[(int)rows[i]]);
        }

        return faces;
    }

    // No catalogue lookup: the class is a name, and a class this build lacks
    // must still load.
    private static EntityData BuildEntity(scoped in ScmapDocument document, ScmapEntityRecord record)
    {
        var data = new EntityData(document.StringAt(record.ClassNameString));

        ReadOnlySpan<ScmapKeyvalueRecord> keyvalues =
            document.Keyvalues.Slice((int)record.KeyvalueStart, (int)record.KeyvalueCount);

        // Add, not SetValue, which would collapse a duplicate key.
        foreach (ScmapKeyvalueRecord pair in keyvalues)
        {
            data.Keyvalues.Add(new KeyValuePair<string, string>(
                document.StringAt(pair.KeyString), document.StringAt(pair.ValueString)));
        }

        ReadOnlySpan<ScmapConnectionRecord> wires =
            document.Connections.Slice((int)record.ConnectionStart, (int)record.ConnectionCount);

        foreach (ScmapConnectionRecord wire in wires)
        {
            data.Connections.Add(new EntityConnection(
                document.StringAt(wire.OutputNameString),
                document.StringAt(wire.TargetNameString),
                document.StringAt(wire.InputNameString),
                document.StringAt(wire.ParameterString),
                wire.Delay,
                wire.TimesToFire));
        }

        return data;
    }

    private static void AttachPayload(
        SceneNode node,
        scoped in ScmapNodeRecord record,
        int index,
        string name,
        scoped in ScmapBrushSource brushes,
        int brushRecord,
        ReadOnlySpan<MaterialRef> materials,
        CompiledMapLoadReport report)
    {
        switch (record.PayloadKind)
        {
            case ScmapPayloadKind.StaticWorldBrush:
                // No brush, even if BRSH has its planes: its surfaces are already
                // in the chunk meshes and a brush would be carved again.
                if (brushRecord >= 0) report.BakedBrushSourceSkipped();
                break;

            case ScmapPayloadKind.PartBrush:
                if (brushRecord < 0)
                {
                    // A part is never baked into a chunk, so without its planes
                    // it draws nothing.
                    report.PartBrushWithoutSource(name);
                    break;
                }

                // Kind before brush: the brush setter reads the kind, so the other
                // order admits the part to the static world.
                node.BrushKind = BrushKind.Part;

                if (TryBuildBrush(in brushes, brushRecord, in record, materials, out Brush? brush))
                    node.Brush = brush;
                else
                    report.BrushRefused(name);

                break;

            case ScmapPayloadKind.MeshInstance:
                // Not bound: the format stores no submesh index, and guessing 0
                // is wrong for any multi-submesh model.
                report.MeshInstanceUnbound(name);
                break;

            default:
                // None and PrefabRoot. The reader already refused unknown kinds.
                _ = index;
                break;
        }
    }

    private static bool TryBuildBrush(
        scoped in ScmapBrushSource brushes,
        int brushRecord,
        scoped in ScmapNodeRecord node,
        ReadOnlySpan<MaterialRef> materials,
        out Brush? brush)
    {
        ScmapBrushRecord record = brushes.Brushes[brushRecord];
        int start = (int)record.PlaneStart;
        int count = (int)record.PlaneCount;

        var planes = new Plane[count];
        var faces = new FaceSurface[count];

        try
        {
            for (int k = 0; k < count; k++)
            {
                planes[k] = brushes.Planes[start + k];

                ScmapFaceRecord face = brushes.Faces[start + k];
                MaterialRef material =
                    face.AssetIndex != ScmapFormat.NoAssetIndex && face.AssetIndex < (uint)materials.Length
                        ? materials[(int)face.AssetIndex]
                        : MaterialRef.Default;

                faces[k] = new FaceSurface(
                    material, face.UAxis, face.VAxis,
                    face.UOffset, face.VOffset, face.UScale, face.VScale);
            }

            // Identity: a node-attached brush ignores Brush.Transform, and the
            // file does not store one.
            brush = new Brush(
                planes,
                Matrix4x4.Identity,
                faces,
                node.IsSubtractiveBrush ? BrushOperation.Subtractive : BrushOperation.Additive);

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            // Brush and FaceSurface throw on bad geometry. One missing brush is
            // reported; it should not fail the whole load.
            brush = null;
            return false;
        }
    }
}
