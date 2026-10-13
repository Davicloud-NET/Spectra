using System;
using System.Collections.Generic;
using System.Numerics;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace Spectra.Kitchen.Maps;

// The bake's pass over a map's nodes: one NODE record each, and beside it
// the node's brush, hull, light, entity, script, tags and attributes.
internal static class ScmapNodeBake
{
    // Walks document and scene together in pre-order. The binder builds the
    // scene in document order, so the two sequences match.
    public static bool Write(
        MapDocument document,
        SpectraEngine.Core.Scene.Scene scene,
        ScmapBuilder builder,
        ScmapBakeAssets assets,
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

            if (DefectOf(mapped, seen, sourcePath) is { } defect)
            {
                report(defect);
                return false;
            }

            int index = builder.AddNode(new ScmapNodeSource(
                node.Id,
                node.Name,
                parent,
                node.LocalTransform,
                PayloadKindOf(node, mapped),
                PayloadFlagsOf(node),
                // The node's enums are numbered as the record's two fields are.
                (ScmapNodeRealm)node.DeclaredRealm,
                (ScmapNodeState)node.DeclaredState,
                PayloadIndexOf(mapped, assets)));

            WriteBrush(builder, assets, node, index, KeepsSource(node, mapped, keepBrushSource));
            WriteLightAndEntity(builder, node, index, report, sourcePath);

            if (node.Script is { } script && !WriteScript(builder, node, script, index, report, sourcePath))
                return false;

            if (node.Tags.Count > 0 || node.Attributes.Count > 0)
                builder.AddNodeSets(new ScmapNodeSetsSource(index, node.Tags, node.Attributes));

            PushChildren(pending, mapped.Children, node, index);
        }

        return true;
    }

    // What makes a node unbakeable, or null. Each stops the bake.
    private static CookDiagnostic? DefectOf(MapNode mapped, HashSet<Guid> seen, string sourcePath)
    {
        if (!seen.Add(mapped.Id))
        {
            return CookDiagnostic.Error(
                CookDiagnosticCodes.MapNodeIdDuplicate,
                $"Two nodes in '{sourcePath}' claim id {mapped.Id}. Every command, every wire and every " +
                "script reference resolves through that id, so a duplicate resolves by traversal order and " +
                "presents as an edit landing on the wrong object.",
                sourcePath);
        }

        if (mapped.Brush is { } authored && authored.Planes.Count != authored.Faces.Count)
        {
            return CookDiagnostic.Error(
                CookDiagnosticCodes.MapFaceCountMismatch,
                $"Node '{mapped.Name}' in '{sourcePath}' has {authored.Planes.Count} brush planes and " +
                $"{authored.Faces.Count} faces. One face per plane is the invariant the whole per-face " +
                "material path rests on, so a mismatch is an indexing bug rather than a wrong surface.",
                sourcePath);
        }

        return null;
    }

    private static void WriteBrush(
        ScmapBuilder builder, ScmapBakeAssets assets, SceneNode node, int index, bool keepsSource)
    {
        if (node.Brush is not { } brush) return;

        if (keepsSource)
            builder.AddBrushSource(BrushSourceOf(index, brush, assets));
        else
            ClaimFaceMaterials(brush, assets);

        // Not tied to the kept source: the chunks hold how a world brush
        // looks, this is what a character collides with and what a sound has
        // to get through.
        if (node.IsStaticWorldBrush)
        {
            builder.AddCollisionHull(new ScmapCollisionHullSource(
                index, [.. brush.LocalPlanes], FaceMaterialsOf(brush, assets)));
        }
    }

    private static void WriteLightAndEntity(
        ScmapBuilder builder, SceneNode node, int index, Action<CookDiagnostic> report, string sourcePath)
    {
        if (node.Light is { } light)
            builder.AddLight(new ScmapLightSource(index, light));

        if (node.Entity is { } entity)
        {
            if (node.IsStaticWorldBrush) report(EntityOnWorldBrush(node, entity, sourcePath));

            builder.AddEntity(new ScmapEntitySource(
                index, entity.ClassName, [.. entity.Keyvalues], [.. entity.Connections]));
        }
    }

    // The file's path only. Its text is an entry of the pack. False for a
    // file the script rule does not cook: it would be packed as it is, and
    // the game would compile at load what no cook checked.
    private static bool WriteScript(
        ScmapBuilder builder,
        SceneNode node,
        Script script,
        int index,
        Action<CookDiagnostic> report,
        string sourcePath)
    {
        if (!ScriptRule.Handles(script.Path))
        {
            report(CookDiagnostic.Error(
                CookDiagnosticCodes.MapScriptNotLuau,
                $"Node '{node.Name}' in '{sourcePath}' names '{script.Path}' as its script. A script is a " +
                $"{PackedScript.FileExtension} file: the cook checks and packs no other. Rename the file.",
                sourcePath));

            return false;
        }

        builder.AddScript(new ScmapScriptSource(index, script.Path, script.IsEnabled, script.IsModule));
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

    private static ScmapPayloadFlags PayloadFlagsOf(SceneNode node)
    {
        ScmapPayloadFlags flags = ScmapPayloadFlags.None;

        if (node.Brush is { Operation: BrushOperation.Subtractive }) flags |= ScmapPayloadFlags.SubtractiveBrush;
        if (node.Brush is not null && node.Entity is not null) flags |= ScmapPayloadFlags.IsEntityOwned;

        if (!node.CanCollide) flags |= ScmapPayloadFlags.NoCollide;
        if (!node.CanQuery) flags |= ScmapPayloadFlags.NoQuery;
        if (!node.CanTouch) flags |= ScmapPayloadFlags.NoTouch;
        if (!node.IsRendered) flags |= ScmapPayloadFlags.NoRender;

        return flags;
    }

    // Said here because a cooked world brush node has no brush left: at run
    // time nothing can tell it was world geometry.
    private static CookDiagnostic EntityOnWorldBrush(SceneNode node, EntityData entity, string sourcePath) =>
        CookDiagnostic.Warning(
            CookDiagnosticCodes.MapEntityOnWorldBrush,
            $"Node '{node.Name}' in '{sourcePath}' carries the entity '{entity.ClassName}' and a world " +
            "brush. A world brush is baked into the level, so the entity cannot move it, hide it or switch " +
            "its collision off. Make the brush a part.",
            sourcePath);

    // Zero for a brush: the BRSH record points at its node, not the other way round.
    private static uint PayloadIndexOf(MapNode mapped, ScmapBakeAssets assets) =>
        mapped is { Brush: null, Mesh: { } mesh } && mesh.Model.Length > 0
            ? assets.Model(mesh.Model)
            : 0;

    // Part brushes are always kept: their planes exist nowhere else in the file.
    private static bool KeepsSource(SceneNode node, MapNode mapped, bool keepBrushSource) =>
        !node.IsStaticWorldBrush || keepBrushSource || mapped.Brush is { KeepSource: true };

    private static ScmapBrushSourceEntry BrushSourceOf(int nodeIndex, Brush brush, ScmapBakeAssets assets)
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

    // One row per plane, in plane order. Every row is already claimed by the
    // brush source or by ClaimFaceMaterials, so this adds none.
    private static uint[] FaceMaterialsOf(Brush brush, ScmapBakeAssets assets)
    {
        var rows = new uint[brush.FaceSurfaces.Count];
        for (int i = 0; i < rows.Length; i++) rows[i] = assets.Material(brush.FaceSurfaces[i].Material);
        return rows;
    }

    // Claimed even when the source is dropped, so --keep-brush-source does
    // not renumber the asset table.
    private static void ClaimFaceMaterials(Brush brush, ScmapBakeAssets assets)
    {
        for (int i = 0; i < brush.FaceSurfaces.Count; i++) assets.Material(brush.FaceSurfaces[i].Material);
    }
}
