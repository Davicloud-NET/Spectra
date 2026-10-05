using System;
using System.Numerics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// What a gameplay ray hit: where, what surface, and which node if any.
/// <see cref="Node"/> is null for static world geometry, which is fused from
/// many brushes and has no single owner.
/// </summary>
public readonly record struct GameplayRayHit(
    SceneNode? Node,
    Vector3 Point,
    Vector3 Normal,
    float Distance,
    MaterialRef Material,
    bool StaticWorld);

public sealed partial class Scene
{
    /// <summary>
    /// The ray a shot, a footstep probe or a line-of-sight check should use.
    /// Reports the first solid surface of the world as drawn, so it passes
    /// through openings a subtractive brush cut. Dynamic bodies are not covered.
    /// </summary>
    // Unlike Raycast, world geometry comes from the compiled static world, live
    // or baked (the only thing that knows what the carve removed). Parts and
    // meshes come from the spatial index, with world brushes skipped so nothing
    // answers twice.
    public bool RaycastGameplay(
        in Ray3 ray,
        out GameplayRayHit hit,
        float maxDistance = 1000f)
        => RaycastGameplay(in ray, out hit, default, maxDistance);

    /// <inheritdoc cref="RaycastGameplay(in Ray3, out GameplayRayHit, float)"/>
    public bool RaycastGameplay(
        in Ray3 ray,
        out GameplayRayHit hit,
        in SceneQueryFilter filter,
        float maxDistance = 1000f)
    {
        hit = default;

        if (ray.Direction == Vector3.Zero || !(maxDistance > 0f))
            return false;

        Vector3 direction = Vector3.Normalize(ray.Direction);
        bool found = false;
        float best = maxDistance;

        if (StaticWorld is { } world &&
            world.Raycast(ray.Origin, direction, maxDistance, out BspRaycastHit worldHit))
        {
            found = true;
            best = worldHit.Distance;

            // The BSP reports a plane, not a polygon, so the material is looked
            // up from the owning chunk's surfaces.
            MaterialRef material = world.TryResolveSurface(worldHit.Point, worldHit.Normal, out FaceSurface face)
                ? face.Material
                : default;

            hit = new GameplayRayHit(null, worldHit.Point, worldHit.Normal, worldHit.Distance, material, true);
        }
        else if (_compiledStaticWorld is { } baked &&
            baked.Raycast(ray.Origin, direction, maxDistance, out BspRaycastHit bakedHit))
        {
            found = true;
            best = bakedHit.Distance;

            // A baked world keeps no surfaces on the CPU, so the hit names no
            // material.
            hit = new GameplayRayHit(null, bakedHit.Point, bakedHit.Normal, bakedHit.Distance, default, true);
        }

        // Parts and meshes, bounded by the world hit.
        SceneQueryFilter sceneFilter = filter with { ExcludeStaticWorldBrushes = true };
        if (Raycast(in ray, out SceneRaycastHit nodeHit, in sceneFilter, best) && nodeHit.Distance <= best)
        {
            MaterialRef material = ResolveNodeMaterial(nodeHit.Node, nodeHit.Normal);
            hit = new GameplayRayHit(
                nodeHit.Node, nodeHit.Point, nodeHit.Normal, nodeHit.Distance, material, false);
            found = true;
        }

        return found;
    }

    // Brush: the face whose plane matches the hit normal. Mesh nodes report
    // the default; the caller reads Node instead.
    private static MaterialRef ResolveNodeMaterial(SceneNode node, Vector3 normal)
    {
        if (node.Brush is not { } brush)
            return default;

        Matrix4x4 world = node.WorldMatrix;
        int best = -1;
        float bestAgreement = 0.9f;

        for (int i = 0; i < brush.LocalPlanes.Count; i++)
        {
            Vector3 worldNormal = Vector3.Normalize(
                Vector3.TransformNormal(brush.LocalPlanes[i].Normal, world));

            float agreement = Vector3.Dot(worldNormal, normal);
            if (agreement > bestAgreement)
            {
                bestAgreement = agreement;
                best = i;
            }
        }

        return best >= 0 ? brush.FaceSurfaces[best].Material : default;
    }
}
