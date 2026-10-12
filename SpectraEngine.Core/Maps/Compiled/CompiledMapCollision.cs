using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Maps.Compiled;

// Builds a baked map's world collision: each baked brush as a hull, placed by
// its node. No node gets the brush, so no compile can pick it up.
internal static class CompiledMapCollision
{
    public static BrushPlacement[] Build(
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
}
