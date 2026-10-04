using System;
using System.Numerics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// Loads a baked <c>.scmap</c> into a live scene without running any CSG.
/// </summary>
// Pass order: reader gates, ASTB interned in table order, nodes rebuilt in one
// forward pass, chunks adopted.
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
            RebuildGraph(scene, in document, materials, report);

            scene.AdoptCompiledStaticWorld(renderer, in document, materials, file, report);
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
    private static void RebuildGraph(
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

        for (int i = 0; i < nodes.Length; i++)
        {
            ScmapNodeRecord record = document.Nodes[i];
            string name = document.NodeName(i);

            // Keeps the stored id. Commands and entity wires address nodes by it.
            var node = new SceneNode(name, record.NodeId)
            {
                LocalTransform = new Transform
                {
                    Position = record.LocalPosition,
                    Rotation = record.LocalRotation,
                    Scale = record.LocalScale,
                },
            };

            AttachPayload(node, in record, i, name, brushes, brushOfNode[i], materials, report);

            if (record.ParentIndex < 0) scene.Root.AddChild(node);
            else nodes[record.ParentIndex].AddChild(node);

            nodes[i] = node;
        }

        report.NodesLoaded = nodes.Length;
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
