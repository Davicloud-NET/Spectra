using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading.Tasks;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// The scene graph: a tree of <see cref="SceneNode"/> under <see cref="Root"/>,
/// viewed through <see cref="Camera"/>. Brush nodes are the authoring primitive
/// for static geometry; <see cref="StaticWorld"/> is derived from them and
/// recompiled in the background when they change.
/// Every member is render thread only.
/// </summary>
public sealed partial class Scene
{
    public Scene(string name = "Scene")
    {
        Name = name;
        _drawableComparison = _drawableNodes.Compare;
        // Selection and both indexes subscribe to the change events, so they
        // have to exist before the root is claimed.
        Selection = new SelectionSet(this);
        Bvh = new SceneBvh(this);
        DrawableBvh = new SceneBvh(this, drawableOnly: true);
        Root.SetOwner(this);
    }

    public string Name { get; set; }

    /// <summary>The implicit root node; never has a parent.</summary>
    public SceneNode Root { get; } = new("Root");

    public Camera Camera { get; } = new();

    /// <summary>The editor-facing selection over this scene's nodes.</summary>
    public SelectionSet Selection { get; }

    internal SceneBvh Bvh { get; }
    internal SceneBvh DrawableBvh { get; }

    // Event handlers must not add, remove or reparent nodes: membership events
    // fire in the middle of the ownership walk. Not enforced.

    /// <summary>
    /// Raised for every node that enters this scene's graph, parents before
    /// children. A reparent within the scene does not raise it.
    /// Handlers must not mutate the graph.
    /// </summary>
    public event Action<SceneNode>? NodeAdded;

    /// <summary>
    /// Raised for every node that leaves this scene's graph, whether detached
    /// or moved to another scene. Handlers must not mutate the graph.
    /// </summary>
    public event Action<SceneNode>? NodeRemoved;

    /// <summary>
    /// Raised when an owned node's local transform changes. An equal-value
    /// write raises nothing. Handlers must not mutate the graph.
    /// </summary>
    public event Action<SceneNode>? NodeTransformChanged;

    /// <summary>
    /// Raised when an owned subtree moves to a different parent, or to a
    /// different index under the same one, without leaving this scene.
    /// Raised once, for the subtree's root. Handlers must not mutate the graph.
    /// </summary>
    public event Action<SceneNode>? NodeReparented;

    /// <summary>
    /// Raised when an owned node's <see cref="SceneNode.Name"/> changes.
    /// Handlers must not mutate the graph.
    /// </summary>
    public event Action<SceneNode>? NodeRenamed;

    // Membership and reparent notifications bump _graphStructureVersion:
    // traversal order may have changed.
    internal void OnNodeAdded(SceneNode node)
    {
        _graphStructureVersion++;
        // Indexed before the event so handlers can resolve the node by id.
        _nodesById[node.Id] = node;
        UpdatePartBrushMembership(node);
        // OnNodeRemoved drops the node from _lightNodes and the Light setter
        // only registers an owned node, so a light that leaves and comes back
        // (undo of a delete) has to be relisted here.
        UpdateLightMembership(node);
        UpdateEntityMembership(node);
        NodeAdded?.Invoke(node);
    }

    internal void OnNodeRemoved(SceneNode node)
    {
        ForgetWorldPlacement(node);
        _graphStructureVersion++;
        // De-indexed before the event. The identity check keeps a stale
        // duplicate id from unmapping the node that owns the key now.
        if (_nodesById.TryGetValue(node.Id, out SceneNode? indexed) && ReferenceEquals(indexed, node))
            _nodesById.Remove(node.Id);
        _partBrushNodes.Remove(node);
        _partBrushMeshes.SetReference(node, null);
        _subtractiveBrushNodes.Remove(node);
        _inertPartBrushNodes.Remove(node);
        _hiddenBrushNodes.Remove(node);
        _drawableNodes.Remove(node);
        _lightNodes.Remove(node);
        _entityNodes.Remove(node);
        NodeRemoved?.Invoke(node);
    }

    internal void OnNodeTransformChanged(SceneNode node) => NodeTransformChanged?.Invoke(node);

    // A mesh or brush changed on an owned node.
    internal void OnNodeSpatialComponentChanged(SceneNode node)
    {
        Bvh.OnSpatialComponentChanged(node);
        DrawableBvh.OnSpatialComponentChanged(node);
        UpdatePartBrushMembership(node);
    }

    internal void UpdateLightMembership(SceneNode node)
    {
        bool shouldBeListed = node.Light is not null;
        if (shouldBeListed) _lightNodes.Add(node);
        else _lightNodes.Remove(node);
    }

    /// <summary>Nodes currently carrying a <see cref="Scene.Light"/>, in attachment order.</summary>
    public IReadOnlyList<SceneNode> LightNodes => _lightNodes;

    internal void UpdateEntityMembership(SceneNode node)
    {
        if (node.Entity is not null) _entityNodes.Add(node);
        else _entityNodes.Remove(node);
    }

    /// <summary>
    /// Nodes currently carrying <see cref="SceneNode.Entity"/> data, in
    /// attachment order. Not the order entities run in.
    /// </summary>
    public IReadOnlyList<SceneNode> EntityNodes => _entityNodes;

    // The render bit changes what draws and nothing else, so world placements
    // are left alone.
    internal void OnNodeRenderFlagChanged(SceneNode node) => UpdateRenderMembership(node);

    internal void UpdatePartBrushMembership(SceneNode node)
    {
        TrackWorldPlacement(node);
        // Additive parts only. A mesh built from a subtractive brush's own
        // faces would draw a solid block where the author asked for a hole.
        if (node.Brush is { Operation: BrushOperation.Additive } &&
            node.BrushKind == BrushKind.Part)
        {
            _partBrushNodes.Add(node);
        }
        else
        {
            _partBrushNodes.Remove(node);
        }

        if (node.Brush is { Operation: BrushOperation.Subtractive })
            _subtractiveBrushNodes.Add(node);
        else
            _subtractiveBrushNodes.Remove(node);

        // A subtractive part carves nothing and draws nothing. Counted so the
        // mistake can be reported.
        if (node.Brush is { Operation: BrushOperation.Subtractive } &&
            node.BrushKind == BrushKind.Part)
        {
            _inertPartBrushNodes.Add(node);
        }
        else
        {
            _inertPartBrushNodes.Remove(node);
        }

        UpdateRenderMembership(node);
    }

    // The mesh reference and the drawable list move together. A reference
    // without a list entry holds a GPU mesh nothing draws, and the reverse
    // hides a part that should draw.
    private void UpdateRenderMembership(SceneNode node)
    {
        bool additivePart = node.BrushKind == BrushKind.Part &&
                            node.Brush is { Operation: BrushOperation.Additive };

        _partBrushMeshes.SetReference(node, additivePart && node.IsRendered ? node.Brush : null);

        if (additivePart && !node.IsRendered)
            _hiddenBrushNodes.Add(node);
        else
            _hiddenBrushNodes.Remove(node);

        UpdateDrawableMembership(node);
    }

    // Kept apart from Bvh, which also indexes world brushes for picking. Those
    // never draw on their own, and walking them to build a draw list is slow.
    // A list, not a set: emission order feeds the draw list.
    private readonly OrderedIdentityList<SceneNode> _drawableNodes = new();
    private readonly Comparison<SceneNode> _drawableComparison;

    private void UpdateDrawableMembership(SceneNode node)
    {
        DrawableBvh.OnSpatialComponentChanged(node);
        // World brushes draw through the static world, not here.
        bool drawable = node.IsRendered &&
                        (node.MeshRenderer is not null ||
                         (node.BrushKind == BrushKind.Part && node.Brush is not null));

        if (drawable) _drawableNodes.Add(node);
        else _drawableNodes.Remove(node);
    }

    /// <summary>
    /// Nodes that can produce a draw of their own: mesh renderers and part
    /// brushes with <see cref="SceneNode.IsRendered"/> on.
    /// </summary>
    public IReadOnlyList<SceneNode> DrawableNodes => _drawableNodes;

    private readonly HashSet<SceneNode> _inertPartBrushNodes = [];

    /// <summary>
    /// Nodes carrying a brush that is both <see cref="BrushKind.Part"/> and
    /// subtractive, which carves nothing and draws nothing. Always a mistake.
    /// </summary>
    public int InertPartBrushCount => _inertPartBrushNodes.Count;

    internal void OnNodeSubtreeMoved(SceneNode node)
    {
        ReorderWorldPlacements(node);
        _graphStructureVersion++;
        Bvh.OnSubtreeMoved(node);
        DrawableBvh.OnSubtreeMoved(node);
        NodeReparented?.Invoke(node);
    }

    internal void OnNodeRenamed(SceneNode node) => NodeRenamed?.Invoke(node);

    // Written with the indexer, not Add: this runs inside the ownership walk,
    // where a throw would leave the graph half-owned. On a duplicate id the
    // latest node wins.
    private readonly Dictionary<Guid, SceneNode> _nodesById = [];

    /// <summary>
    /// Resolves a <see cref="SceneNode.Id"/> to the live node in this scene.
    /// Editor commands address nodes this way, because an undo can recreate a
    /// node under the same id.
    /// </summary>
    public bool TryFindById(Guid id, [MaybeNullWhen(false)] out SceneNode node) =>
        _nodesById.TryGetValue(id, out node);

    /// <summary>The number of nodes in this scene's graph, root included.</summary>
    public int NodeCount => _nodesById.Count;

    /// <summary>
    /// Casts a world-space ray against nodes with a mesh or a brush and
    /// reports the nearest hit. Brushes are tested against their authored
    /// planes, meshes per triangle (or by bounding box when the mesh keeps no
    /// CPU geometry). A ray starting inside a solid does not hit that solid.
    /// Honours <see cref="SceneNode.CanQuery"/>; editor picking should pass
    /// <see cref="SceneQueryFilter.EditorPicking"/>.
    /// </summary>
    public bool Raycast(in Ray3 ray, out SceneRaycastHit hit, float maxDistance = float.PositiveInfinity) =>
        Bvh.Raycast(in ray, out hit, default, maxDistance);

    /// <summary>
    /// As <see cref="Raycast(in Ray3, out SceneRaycastHit, float)"/>, reporting
    /// only nodes <paramref name="filter"/> accepts.
    /// <see cref="SceneNode.CanQuery"/> applies to every node, world brushes
    /// included. This tests authored planes, so it hits inside a doorway a
    /// subtractive brush cut: right for picking, wrong for gameplay. Use
    /// <see cref="RaycastGameplay(in Ray3, out GameplayRayHit, float)"/> there.
    /// </summary>
    public bool Raycast(
        in Ray3 ray, out SceneRaycastHit hit, in SceneQueryFilter filter,
        float maxDistance = float.PositiveInfinity)
    {
        ValidateQueryGroup(in filter);
        return Bvh.Raycast(in ray, out hit, in filter, maxDistance);
    }

    // Checked before the walk. Inside it, the throw would depend on what
    // overlapped and leave the results list half-filled.
    private static void ValidateQueryGroup(in SceneQueryFilter filter)
    {
        if (filter.Groups is { } groups && (uint)filter.CollisionGroup >= (uint)groups.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(filter), filter.CollisionGroup,
                $"SceneQueryFilter.CollisionGroup must name a registered group " +
                $"(0..{groups.Count - 1}).");
        }
    }

    /// <summary>
    /// The scene's collision groups: up to 64 named groups and which pairs
    /// interact. Every node starts in <see cref="CollisionGroups.DefaultGroup"/>
    /// and everything collides.
    /// </summary>
    public CollisionGroups CollisionGroups { get; } = new();

    /// <summary>
    /// Appends every spatial node whose world AABB intersects
    /// <paramref name="box"/> to <paramref name="results"/>. The list is not
    /// cleared. A broad phase: bounds are tested, not geometry.
    /// </summary>
    public void GetPartBoundsInBox(in Aabb box, List<SceneNode> results) =>
        Bvh.QueryBox(in box, results, default);

    /// <summary>
    /// As <see cref="GetPartBoundsInBox(in Aabb, List{SceneNode})"/>, reporting
    /// only nodes <paramref name="filter"/> accepts.
    /// </summary>
    public void GetPartBoundsInBox(in Aabb box, List<SceneNode> results, in SceneQueryFilter filter)
    {
        ValidateQueryGroup(in filter);
        Bvh.QueryBox(in box, results, in filter);
    }

    /// <summary>
    /// Appends every spatial node whose world AABB overlaps the sphere to
    /// <paramref name="results"/>. The list is not cleared. Bounds only, like
    /// <see cref="GetPartBoundsInBox(in Aabb, List{SceneNode})"/>.
    /// </summary>
    public void GetPartBoundsInRadius(Vector3 center, float radius, List<SceneNode> results) =>
        Bvh.QuerySphere(center, radius, results, default);

    /// <summary>
    /// As <see cref="GetPartBoundsInRadius(Vector3, float, List{SceneNode})"/>,
    /// reporting only nodes <paramref name="filter"/> accepts.
    /// </summary>
    public void GetPartBoundsInRadius(
        Vector3 center, float radius, List<SceneNode> results, in SceneQueryFilter filter)
    {
        ValidateQueryGroup(in filter);
        Bvh.QuerySphere(center, radius, results, in filter);
    }

    /// <summary>
    /// Appends every spatial node whose world AABB intersects
    /// <paramref name="frustum"/> to <paramref name="results"/>. The list is
    /// not cleared. Conservative: false positives are possible, false
    /// negatives are not. Ignores <see cref="SceneNode.CanQuery"/>, because
    /// visibility is not a gameplay query.
    /// </summary>
    public void QueryFrustum(in Frustum frustum, List<SceneNode> results) =>
        Bvh.QueryFrustum(in frustum, results);

    /// <summary>
    /// The tight world AABB the spatial index keeps for
    /// <paramref name="node"/>. False when the node is not spatial or does not
    /// belong to this scene.
    /// </summary>
    public bool TryGetWorldBounds(SceneNode node, out Aabb bounds)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Bvh.TryGetWorldBounds(node, out bounds);
    }

    private readonly List<SceneNode> _renderViewScratch = [];

    private static RenderLightType ToRenderType(LightKind kind) => kind switch
    {
        LightKind.Directional => RenderLightType.Directional,
        LightKind.Point => RenderLightType.Point,
        LightKind.Spot => RenderLightType.Spot,
        LightKind.Rect => RenderLightType.Rect,
        LightKind.Disc => RenderLightType.Disc,

        // Throw: a default would light a new kind as the wrong shape.
        _ => throw new NotSupportedException($"No render type for light kind '{kind}'."),
    };

    // Not frustum-culled: a light behind the camera still lights what is in
    // front of it. The view keeps the nearest few.
    private void CollectLights(Vector3 viewer, RenderView view)
    {
        for (int i = 0; i < _lightNodes.Count; i++)
        {
            SceneNode node = _lightNodes[i];
            if (node.Light is not { Enabled: true } light) continue;

            Vector3 radiance = light.Color * light.Intensity;

            // The light type rides in the colour's w.
            var color = new Vector4(radiance, (float)(int)ToRenderType(light.Kind));

            // Basis from the world matrix rows, the same ones the overlay and
            // the gizmo read, so the three cannot disagree.
            Matrix4x4 world = node.WorldMatrix;
            var forward = Vector3.Normalize(new Vector3(world.M31, world.M32, world.M33));

            if (light.Kind == LightKind.Directional)
            {
                // Negative key: a sun has no position and must never lose its
                // slot to a nearer lamp.
                view.OfferLight(new RenderLight(new Vector4(forward, 0f), color), -1f);
                continue;
            }

            Vector3 position = node.WorldPosition;
            float distance = Vector3.Distance(position, viewer);

            var right = Vector3.Normalize(new Vector3(world.M11, world.M12, world.M13));

            // The shader wants cosines of the half-angles. The w slots hold
            // cone cosines for a spot and extents for an area light; the type
            // says which.
            const float ToRadians = MathF.PI / 180f;

            Vector4 axis = light.Kind switch
            {
                LightKind.Spot => new Vector4(forward, MathF.Cos(light.OuterAngle * ToRadians)),
                LightKind.Rect => new Vector4(forward, light.Height * 0.5f),
                LightKind.Disc => new Vector4(forward, light.Radius),
                _ => new Vector4(forward, 0f),
            };

            Vector4 tangent = light.Kind switch
            {
                LightKind.Spot => new Vector4(right, MathF.Cos(light.InnerAngle * ToRadians)),
                LightKind.Rect => new Vector4(right, light.Width * 0.5f),
                _ => new Vector4(right, 0f),
            };

            view.OfferLight(
                new RenderLight(new Vector4(position, light.Range), color, axis, tangent),
                distance);
        }
    }

    /// <summary>
    /// Fills <paramref name="view"/> with this frame's draw list for
    /// <paramref name="camera"/>: visible mesh nodes and part brushes, the
    /// static world's visible chunks (one item per material), and the lights.
    /// Item order is stable while the scene is unchanged.
    /// </summary>
    public void BuildRenderView(Camera camera, RenderView view)
    {
        view.Clear();

        Frustum frustum = camera.GetFrustum();
        CollectLights(camera.Position, view);
        CollectVisible(in frustum, view);

        view.BuildBatches();
    }

    /// <summary>
    /// Builds the draw list for a shadow map: everything inside
    /// <paramref name="lightViewProjection"/>'s volume, with no lights.
    /// </summary>
    // Culled against the light, not reused from the camera's list: a wall just
    // off screen still has to shade the floor.
    public void BuildShadowView(in Matrix4x4 lightViewProjection, RenderView view)
    {
        view.Clear();

        Frustum frustum = Frustum.FromViewProjection(lightViewProjection);
        CollectVisible(in frustum, view);

        // Per cascade: each one culls against its own volume.
        view.BuildBatches();
    }

    private void CollectVisible(in Frustum frustum, RenderView view)
    {
        _renderViewScratch.Clear();
        IReadOnlyList<SceneNode> visible = _drawableNodes;
        if (!DrawableBvh.EntirelyInside(in frustum))
        {
            DrawableBvh.QueryFrustum(in frustum, _renderViewScratch);
            if (_renderViewScratch.Count != _drawableNodes.Count)
            {
                if (_renderViewScratch.Count > 1) _renderViewScratch.Sort(_drawableComparison);
                visible = _renderViewScratch;
            }
        }

        // Mesh nodes and part brushes are counted apart: view.Items holds both.
        int meshItems = 0;
        int partBrushes = 0;
        for (int i = 0; i < visible.Count; i++)
        {
            SceneNode node = visible[i];
            if (node.MeshRenderer is { } meshRenderer)
            {
                meshItems++;
                view.Add(new RenderItem(meshRenderer.Mesh, meshRenderer.Material, node.WorldMatrix));
            }

            if (node.BrushKind == BrushKind.Part &&
                node.Brush is { } partBrush &&
                _partBrushMeshes.TryGet(partBrush, out BrushSubmesh[] submeshes))
            {
                partBrushes++;
                Matrix4x4 world = node.WorldMatrix;
                for (int s = 0; s < submeshes.Length; s++)
                    view.Add(new RenderItem(submeshes[s].Mesh, submeshes[s].Material, world));
            }
        }

        // Static world chunks, a cluster at a time. Culled against render
        // bounds, not cell bounds: owned surfaces can overhang the cell.
        // Vertices are already in world space, so the model matrix is identity.
        int visibleChunks = 0;
        for (int cluster = 0; cluster < _chunkClusterBounds.Count; cluster++)
        {
            if (!frustum.Intersects(_chunkClusterBounds[cluster]))
                continue;

            int start = cluster * ChunkClusterSize;
            int end = Math.Min(start + ChunkClusterSize, _staticWorldChunkList.Count);
            for (int i = start; i < end; i++)
            {
                StaticWorldChunkMesh chunk = _staticWorldChunkList[i];
                if (!frustum.Intersects(chunk.RenderBounds))
                    continue;

                visibleChunks++;
                StaticWorldSubmesh[] submeshes = chunk.Submeshes;
                for (int s = 0; s < submeshes.Length; s++)
                {
                    StaticWorldSubmesh submesh = submeshes[s];
                    view.AddWorldChunk(new RenderItem(submesh.Mesh, submesh.Material, Matrix4x4.Identity));
                }
            }
        }

        // The totals leave out nodes with the render bit off, or one would
        // read as culled on every frame.
        view.VisibleCount = meshItems;
        view.TotalCount = DrawableBvh.MeshLeafCount;
        view.PartBrushesVisible = partBrushes;
        view.PartBrushesTotal = _partBrushNodes.Count - _hiddenBrushNodes.Count;
        view.WorldChunksVisible = visibleChunks;
        view.WorldChunksTotal = _staticWorldChunkList.Count;
        view.WorldMaterialBatchesVisible = view.WorldItems.Count;
        // Kept by the swap. Counting here would touch every chunk.
        view.WorldMaterialBatchesTotal = _staticWorldBatchTotal;
    }

    /// <summary>
    /// The compiled static world, derived from the brush nodes in the graph.
    /// Null until the first compile completes or when the scene has no brush
    /// nodes. To change it, edit the brush nodes.
    /// </summary>
    public CsgWorld? StaticWorld { get; private set; }

    // Same entries in both. The list is the ordered copy the cull pass walks,
    // rebuilt at swap time.
    private readonly Dictionary<ChunkCoord, StaticWorldChunkMesh> _staticWorldChunkMeshes = [];
    private readonly List<StaticWorldChunkMesh> _staticWorldChunkList = [];

    // One box per run of consecutive chunks, so the cull rejects a run with a
    // single test. Relies on the chunk list's order keeping a run compact.
    private const int ChunkClusterSize = 64;
    private readonly List<Aabb> _chunkClusterBounds = [];
    private readonly HashSet<int> _dirtyChunkClusters = [];
    private int _staticWorldBatchTotal;

    // O(chunks) per shape change. An insert shifts every cluster after it, so
    // tracking which ones moved would not be cheaper.
    private void RebuildChunkClusters()
    {
        _chunkClusterBounds.Clear();
        _staticWorldBatchTotal = 0;

        int count = _staticWorldChunkList.Count;
        for (int start = 0; start < count; start += ChunkClusterSize)
        {
            int end = Math.Min(start + ChunkClusterSize, count);
            Aabb bounds = _staticWorldChunkList[start].RenderBounds;
            _staticWorldBatchTotal += _staticWorldChunkList[start].Submeshes.Length;

            for (int i = start + 1; i < end; i++)
            {
                StaticWorldChunkMesh chunk = _staticWorldChunkList[i];
                Aabb box = chunk.RenderBounds;
                bounds = new Aabb(Vector3.Min(bounds.Min, box.Min), Vector3.Max(bounds.Max, box.Max));
                _staticWorldBatchTotal += chunk.Submeshes.Length;
            }

            _chunkClusterBounds.Add(bounds);
        }
    }

    private void RefitChunkCluster(int cluster)
    {
        int start = cluster * ChunkClusterSize;
        int end = Math.Min(start + ChunkClusterSize, _staticWorldChunkList.Count);
        Aabb bounds = _staticWorldChunkList[start].RenderBounds;
        for (int i = start + 1; i < end; i++)
        {
            Aabb box = _staticWorldChunkList[i].RenderBounds;
            bounds = new Aabb(Vector3.Min(bounds.Min, box.Min), Vector3.Max(bounds.Max, box.Max));
        }
        _chunkClusterBounds[cluster] = bounds;
    }

    // A list, not a set: light selection is a nearest-N and ties need a stable
    // order.
    private readonly OrderedIdentityList<SceneNode> _lightNodes = new();

    // A list too: a pick between two markers at one depth takes the first.
    private readonly OrderedIdentityList<SceneNode> _entityNodes = new();

    private readonly HashSet<SceneNode> _partBrushNodes = [];
    private readonly PartBrushMeshCache _partBrushMeshes = new();

    // Both kinds. A subtractive brush draws nothing either way, so the editor
    // overlay has to see all of them.
    private readonly HashSet<SceneNode> _subtractiveBrushNodes = [];

    // Additive parts with the render bit off.
    private readonly HashSet<SceneNode> _hiddenBrushNodes = [];

    // Cached so the upload path does not allocate a delegate per call.
    private Func<MaterialRef, Material?>? _resolveWorldMaterial;

    /// <summary>
    /// The static world's GPU meshes, one entry per chunk with render
    /// geometry, each holding one submesh per material. Vertices are in world
    /// space. Empty until the first compile lands.
    /// </summary>
    public IReadOnlyList<StaticWorldChunkMesh> StaticWorldChunkMeshes => _staticWorldChunkList;

    /// <summary>
    /// Looks up the static world's GPU mesh entry for the chunk cell at
    /// <paramref name="coord"/>, if that cell has render geometry.
    /// </summary>
    public bool TryGetStaticWorldChunkMesh(ChunkCoord coord, out StaticWorldChunkMesh chunk) =>
        _staticWorldChunkMeshes.TryGetValue(coord, out chunk);

    /// <summary>
    /// Material for static-world faces that name none. Null means
    /// <see cref="AssetManager.NeutralMaterial"/>. Changing it only affects
    /// chunks uploaded afterwards; call
    /// <see cref="RefreshStaticWorldMaterials"/> for the rest.
    /// </summary>
    public Material? StaticWorldMaterial { get; set; }

    /// <summary>
    /// Asset manager that resolves the material ids compiled into the static
    /// world's faces. Without one every face falls back to
    /// <see cref="StaticWorldMaterial"/>. Assign before the first rebuild.
    /// </summary>
    public AssetManager? Assets { get; set; }

    /// <summary>
    /// The schemas of the entity classes placed in this scene, parsed from a
    /// <c>.sentdef</c> image, or null. An entity whose class is missing here
    /// still loads and saves; it only loses its property editor.
    /// </summary>
    public Entities.EntitySchemaCatalog? EntitySchemas { get; set; }

    /// <summary>
    /// Re-resolves every uploaded chunk's materials without touching a GPU
    /// mesh. Call after assigning <see cref="StaticWorldMaterial"/> on a
    /// compiled world, or after an <see cref="Assets"/> reload replaced a
    /// material instance.
    /// </summary>
    public void RefreshStaticWorldMaterials()
    {
        _resolveWorldMaterial ??= ResolveWorldMaterial;
        _partBrushMeshes.RefreshMaterials(_resolveWorldMaterial);
        for (int i = 0; i < _staticWorldChunkList.Count; i++)
        {
            StaticWorldChunkMesh chunk = _staticWorldChunkList[i];
            StaticWorldSubmesh[] submeshes = chunk.Submeshes;
            for (int s = 0; s < submeshes.Length; s++)
            {
                StaticWorldSubmesh submesh = submeshes[s];
                submeshes[s] = submesh with { Material = ResolveWorldMaterial(submesh.SourceMaterial) };
            }
        }
    }

    /// <summary>
    /// Builds GPU meshes for part brushes that lack one and destroys those
    /// nothing references any more. Call once per frame. A part that only
    /// moves costs nothing here: its mesh is brush-local.
    /// </summary>
    public void ProcessPartBrushMeshes(Renderer renderer)
    {
        if (_partBrushMeshes.PendingCount == 0)
            return;

        _resolveWorldMaterial ??= ResolveWorldMaterial;

        _partBrushMeshes.Pump(renderer, _resolveWorldMaterial);
    }

    /// <summary>How many distinct part brushes currently hold GPU meshes.</summary>
    public int PartBrushMeshCount => _partBrushMeshes.Count;

    /// <summary>How many nodes in this scene carry a <see cref="BrushKind.Part"/> brush.</summary>
    public int PartBrushNodeCount => _partBrushNodes.Count;

    /// <summary>
    /// Every node in this scene carrying an additive
    /// <see cref="BrushKind.Part"/> brush. Order is unspecified.
    /// </summary>
    public IReadOnlyCollection<SceneNode> PartBrushNodes => _partBrushNodes;

    /// <summary>
    /// Every node in this scene carrying a subtractive brush, of either
    /// <see cref="BrushKind"/>. These draw nothing, so the editor has to.
    /// Order is unspecified.
    /// </summary>
    public IReadOnlyCollection<SceneNode> SubtractiveBrushNodes => _subtractiveBrushNodes;

    /// <summary>How many nodes in this scene carry a subtractive brush.</summary>
    public int SubtractiveBrushNodeCount => _subtractiveBrushNodes.Count;

    /// <summary>
    /// Every node in this scene carrying an additive
    /// <see cref="BrushKind.Part"/> brush with
    /// <see cref="SceneNode.IsRendered"/> off, such as a trigger volume or a
    /// clip brush. These draw nothing, so the editor has to. Order is unspecified.
    /// </summary>
    public IReadOnlyCollection<SceneNode> HiddenBrushNodes => _hiddenBrushNodes;

    /// <summary>Destroys every GPU mesh the part-brush cache owns. Call before renderer shutdown.</summary>
    public void ReleasePartBrushMeshes(Renderer renderer) => _partBrushMeshes.ReleaseGraphicsResources(renderer);

    private Material? ResolveWorldMaterial(MaterialRef reference)
    {
        if (!reference.IsDefault && Assets is { } assets)
            return assets.ResolveMaterial(reference);

        // A face naming no material gets the neutral surface, not the asset
        // manager's magenta fallback. That one is for references that failed.
        return StaticWorldMaterial ?? Assets?.NeutralMaterial;
    }

    // Compile state below is render thread only. The background compile gets
    // an immutable snapshot and returns a result; it never touches these.

    // Bumped on every dirtying edit.
    private int _staticWorldVersion;

    // Highest version launched, handled synchronously or rejected. Marking a
    // defective snapshot handled stops it retrying every frame.
    private int _handledStaticWorldVersion;

    // One compile at a time, so a landing result is always the newest.
    private Task<StaticWorldCompilation>? _inFlightCompile;
    private int _inFlightVersion;

    // Suppresses per-frame repeats of the same defect line.
    private string? _lastLoggedSnapshotDefect;

    /// <summary>
    /// Why the last static-world compile was refused, or null when the world is
    /// current. While set, the world stops following brush edits.
    /// </summary>
    public string? StaticWorldDefect { get; private set; }

    // An animating brush lands a compile nearly every frame, so harvests are
    // summed and logged at this rate. A deadline of 0 logs the first at once.
    private const long CompileStatsLogIntervalMs = 5000;
    private long _nextCompileStatsLogTicks;

    private int _compileStatsLanded;
    private int _compileStatsCacheHits;
    private int _compileStatsCacheMisses;

    // Drained at every launch. A faulted compile merges its cells back in.
    private readonly HashSet<ChunkCoord> _pendingDirtyCells = [];

    // Kept so a fault can restore it. Null when nothing is in flight.
    private ChunkCoord[]? _inFlightDirtyCells;

    // The edited nodes, not their descendants: a group move lists the group.
    // Kept across rejected snapshots so one node's defect does not lose
    // another node's change.
    private readonly HashSet<SceneNode> _dirtyBrushSubtrees = [];

    // Set by dirty marks that name no node: the next snapshot validates
    // everything.
    private bool _snapshotForceFull = true;


    /// <summary>
    /// The sorted set of chunk cells the most recent static-world compile was
    /// given as dirty. The first compile reports every covered cell; a
    /// recompile of an unchanged scene reports none.
    /// </summary>
    public IReadOnlyList<ChunkCoord> LastCompileDirtyCells { get; private set; } = [];

    private ChunkCoord[] DrainPendingDirtyCells()
    {
        var dirty = new ChunkCoord[_pendingDirtyCells.Count];
        _pendingDirtyCells.CopyTo(dirty);
        _pendingDirtyCells.Clear();
        // Sorted so equal edit histories give the same set.
        Array.Sort(dirty);
        return dirty;
    }

    private void MarkCellsDirty(ChunkCoord[] cells)
    {
        foreach (ChunkCoord cell in cells)
            _pendingDirtyCells.Add(cell);
    }

    // The last compiled world, input to the next incremental compile. Handed
    // to the background task at launch (and nulled here); the task only reads
    // it. A faulted compile restores it from StaticWorld.
    private CsgWorld? _staticWorldCarry;

    // Bumped when traversal order may have changed. The incremental compile
    // needs placement i to be the same slot as last time, so a launch whose
    // version differs from the carry's takes the fully validated path.
    private int _graphStructureVersion;

    private int _carryStructureVersion;
    private int _inFlightStructureVersion;

    // What a faulted compile restores _carryStructureVersion from.
    private int _staticWorldStructureVersion;

    /// <summary>
    /// Number of times a recompiled static world has been swapped in,
    /// synchronous rebuilds included.
    /// </summary>
    public int StaticWorldCompileCount { get; private set; }

    /// <summary>
    /// True when brush nodes changed since the last compile was launched or
    /// handled synchronously. Edits a compile in flight already covers do not
    /// count.
    /// </summary>
    public bool StaticWorldDirty => _staticWorldVersion != _handledStaticWorldVersion;

    /// <summary>
    /// Flags the static world as stale so it recompiles in the background.
    /// Forces the next snapshot to validate the whole graph; node edits use
    /// <see cref="MarkBrushSubtreeDirty"/> instead.
    /// </summary>
    public void MarkStaticWorldDirty()
    {
        if (RefuseForCompiledWorld("A static-world dirty mark", isRebuild: false)) return;

        _staticWorldVersion++;
        _snapshotForceFull = true;
    }

    // A brush-kind change shifts placement slots without adding or removing
    // nodes, so it bumps the structure version itself.
    internal void MarkAdmissionChanged(SceneNode node)
    {
        // Under a baked world only the dirtying is refused. The part set still
        // has to follow the node's kind.
        if (!RefuseForCompiledWorld("A brush-kind change", isRebuild: false))
        {
            _graphStructureVersion++;
            _staticWorldVersion++;
        }

        UpdatePartBrushMembership(node);
    }

    // For edits that keep placement order (transforms, brush swaps): the next
    // snapshot patches only this subtree's slots.
    internal void MarkBrushSubtreeDirty(SceneNode node)
    {
        if (RefuseForCompiledWorld("A brush subtree dirty mark", isRebuild: false)) return;

        _staticWorldVersion++;
        _dirtyBrushSubtrees.Add(node);
    }

    /// <summary>Synchronously rebuilds the static world only if it has been marked dirty.</summary>
    public void RebuildStaticWorldIfDirty(Renderer renderer)
    {
        if (RefuseForCompiledWorld("A conditional static-world rebuild", isRebuild: true)) return;

        if (StaticWorldDirty)
            RebuildStaticWorld(renderer);
    }

    /// <summary>
    /// Captures the admitted brush placements as a compile would, with no
    /// renderer and no compile. The map bake uses this so it carves the same
    /// list in the same order as the engine. Returns null and fills
    /// <paramref name="defectMessage"/> when a brush node's world transform is
    /// not rigid.
    /// </summary>
    // Read-only: does not consume the live compiler's change journal.
    public IReadOnlyList<BrushPlacement>? CaptureStaticWorldPlacements(out string? defectMessage)
    {
        defectMessage = null;
        var placements = new List<BrushPlacement>();
        foreach (var node in Nodes)
        {
            if (!node.IsStaticWorldBrush) continue;
            var world = node.WorldMatrix;
            if (DescribeNonRigidDefect(world) is { } defect)
            { defectMessage = DescribeBrushNodeDefect(node, defect); return null; }
            placements.Add(new(node.Brush!, world));
        }
        return placements;
    }

    /// <summary>
    /// Synchronously recompiles the static world from the graph's brush nodes
    /// and rebuilds the per-chunk meshes, with no caches. For load time and
    /// tests; frame-to-frame edits go through
    /// <see cref="ProcessStaticWorldCompilation"/>. Throws
    /// <see cref="InvalidOperationException"/> when a brush node's world
    /// transform is not rigid. Does nothing while a compiled map is installed.
    /// </summary>
    public void RebuildStaticWorld(Renderer renderer)
    {
        // Refuse, do not throw: callers are load paths with a level on screen.
        if (RefuseForCompiledWorld("A synchronous static-world rebuild", isRebuild: true)) return;

        // Wait out a compile in flight and drop its result, or the pump would
        // later harvest it over this rebuild.
        if (_inFlightCompile is not null)
        {
            try { _inFlightCompile.Wait(); }
            catch (AggregateException) { /* superseded, its failure no longer matters */ }
            _inFlightCompile = null;

            // It never landed, so its cells are still stale.
            if (_inFlightDirtyCells is not null)
            {
                MarkCellsDirty(_inFlightDirtyCells);
                _inFlightDirtyCells = null;
            }
        }

        PlacementSnapshot? placements = SnapshotBrushPlacements(out string? defectMessage);
        if (placements is null)
            throw new InvalidOperationException(defectMessage);

        _handledStaticWorldVersion = _staticWorldVersion;

        // The build below compiles everything, but the diff still runs so
        // later background compiles diff against this snapshot.
        ChunkCoord[] dirtyCells = CollectDirtyCells(placements);
        LastCompileDirtyCells = dirtyCells;

        if (placements.Count == 0)
        {
            _staticWorldCarry = null;
            ReplaceStaticWorld(renderer, null);
            _staticWorldStructureVersion = _graphStructureVersion;
            return;
        }

        CsgWorld world = CsgWorld.Build(placements);
        // Publish first, then commit the carry. If the swap throws, the carry
        // must still describe the world on screen.
        try
        {
            ReplaceStaticWorld(renderer, world);
        }
        catch
        {
            MarkCellsDirty(dirtyCells);
            throw;
        }

        _staticWorldCarry = world;
        _carryStructureVersion = _graphStructureVersion;
        _staticWorldStructureVersion = _graphStructureVersion;
    }

    /// <summary>
    /// Drives the background static-world compile; call once per frame.
    /// Swaps in a finished compile and uploads its changed chunks, then
    /// launches the next one if brush nodes changed and nothing is in flight.
    /// </summary>
    public void ProcessStaticWorldCompilation(Renderer renderer, ILogger logger)
    {
        // A baked world must never compile. The only place the guard logs.
        if (_compiledStaticWorld is not null)
        {
            ReportCompiledWorldGuard(logger);
            return;
        }

        if (_inFlightCompile is { } inFlight)
        {
            if (!inFlight.IsCompleted)
                return;
            _inFlightCompile = null;

            if (inFlight.IsFaulted)
            {
                // Keeps the last good world. Nothing retries until the next
                // dirty mark, so this logs once.
                logger.LogError(inFlight.Exception,
                    "Static world compile v{Version} failed; keeping the previous world", _inFlightVersion);

                // Restore the dirty cells and the carry the failed compile
                // consumed, so the next one covers them again.
                if (_inFlightDirtyCells is not null)
                    MarkCellsDirty(_inFlightDirtyCells);
                _inFlightDirtyCells = null;
                _staticWorldCarry = StaticWorld;
                _carryStructureVersion = _staticWorldStructureVersion;
            }
            else
            {
                StaticWorldCompilation result = inFlight.Result;
                // Publish first, commit the bookkeeping after. The swap can
                // throw (CreateMesh), and committing early would pair the old
                // published world with the new carry and lose the dirty set.
                try
                {
                    ReplaceStaticWorld(renderer, result.World);
                }
                catch
                {
                    // Same restoration as a faulted compile.
                    if (_inFlightDirtyCells is not null)
                        MarkCellsDirty(_inFlightDirtyCells);
                    _inFlightDirtyCells = null;
                    _staticWorldCarry = StaticWorld;
                    _carryStructureVersion = _staticWorldStructureVersion;
                    throw;
                }

                _inFlightDirtyCells = null;
                _staticWorldCarry = result.World;
                _carryStructureVersion = _inFlightStructureVersion;
                _staticWorldStructureVersion = _inFlightStructureVersion;

                CsgCacheStats stats = result.World.CacheStats ?? default;
                _compileStatsLanded++;
                _compileStatsCacheHits += stats.Hits;
                _compileStatsCacheMisses += stats.Misses;
                long now = Environment.TickCount64;
                if (now >= _nextCompileStatsLogTicks)
                {
                    _nextCompileStatsLogTicks = now + CompileStatsLogIntervalMs;
                    logger.LogDebug(
                        "Static world compiles: {Landed} landed since last report, latest v{Version}: " +
                        "{Surfaces} surfaces in {DurationMs:0.00} ms " +
                        "(compile #{Count}, carve cache {Hits} hits / {Misses} misses)",
                        _compileStatsLanded, _inFlightVersion, result.World.SurfaceCount, result.DurationMs,
                        StaticWorldCompileCount, _compileStatsCacheHits, _compileStatsCacheMisses);
                    _compileStatsLanded = 0;
                    _compileStatsCacheHits = 0;
                    _compileStatsCacheMisses = 0;
                }
            }
        }

        if (_staticWorldVersion == _handledStaticWorldVersion)
            return;

        int version = _staticWorldVersion;
        PlacementSnapshot? placements = SnapshotBrushPlacements(out string? defectMessage);

        // Handled even when defective, so it does not retry every frame.
        _handledStaticWorldVersion = version;

        if (placements is null)
        {
            // An animating brush re-arms the pump every frame, so only log
            // when the defect changes.
            if (defectMessage != _lastLoggedSnapshotDefect)
            {
                _lastLoggedSnapshotDefect = defectMessage;
                logger.LogError(
                    "Static world compile v{Version} skipped: {Defect} Keeping the last good world.",
                    version, defectMessage);
            }

            StaticWorldDefect = defectMessage;
            return;
        }
        _lastLoggedSnapshotDefect = null;
        StaticWorldDefect = null;

        // Before the count check: an emptied scene still has to dirty the
        // cells its brushes left.
        ChunkCoord[] dirtyCells = CollectDirtyCells(placements);
        LastCompileDirtyCells = dirtyCells;

        if (placements.Count == 0)
        {
            _staticWorldCarry = null;
            ReplaceStaticWorld(renderer, null);
            _staticWorldStructureVersion = _graphStructureVersion;
            return;
        }

        _inFlightVersion = version;
        _inFlightDirtyCells = dirtyCells;
        _inFlightStructureVersion = _graphStructureVersion;
        // Placements and dirty cells are immutable and shared with the task.
        // The carry moves to the task. After a structural edit it is handed
        // over as untrusted, and the compile uses only its validation caches.
        CsgWorld? carry = _staticWorldCarry;
        bool orderStable = carry is not null && _carryStructureVersion == _graphStructureVersion;
        _staticWorldCarry = null;
        _inFlightCompile = Task.Run(
            () => CompileStaticWorld(placements, dirtyCells, carry, orderStable));
    }

    // Runs on a pool thread. Reads only the snapshot and the previous world.
    // Must not touch Scene, SceneNode, Brush.Transform or anything on the GPU.
    private static StaticWorldCompilation CompileStaticWorld(
        IReadOnlyList<BrushPlacement> placements, ChunkCoord[] dirtyCells, CsgWorld? previous, bool orderStable)
    {
        var stopwatch = Stopwatch.StartNew();
        CsgWorld world = orderStable || placements is PlacementSnapshot
            ? CsgWorld.Build(placements, dirtyCells, previous)
            : CsgWorld.Build(
                placements, dirtyCells,
                previous?.CompileCache, previous?.WeldCache, previous?.BspCache, previous?.MeshCache);
        return new StaticWorldCompilation(world, stopwatch.Elapsed.TotalMilliseconds);
    }

    // Swaps the world and its chunk meshes. Only cells whose artifact instance
    // changed are re-uploaded. Null clears everything.
    // A patched world based on the published one applies just its delta;
    // anything else takes the full per-cell diff below.
    private void ReplaceStaticWorld(Renderer renderer, CsgWorld? world)
    {
        if (world is not null && StaticWorld is { } published &&
            world.ChunkMeshDelta is { } delta && world.PatchBaseId == published.Id)
        {
            ApplyChunkMeshDelta(renderer, world, delta);
            return;
        }

        IReadOnlyList<ChunkMesh> artifacts = world?.ChunkMeshes ?? Array.Empty<ChunkMesh>();

        // Create before destroy: if CreateMesh throws, the new meshes are
        // rolled back and the last good world stays whole.
        var replacement = new StaticWorldChunkMesh[artifacts.Count];
        var createdChunks = new List<StaticWorldSubmesh[]>();
        var carriedCells = new HashSet<ChunkCoord>();
        try
        {
            for (int i = 0; i < artifacts.Count; i++)
            {
                ChunkMesh artifact = artifacts[i];
                if (_staticWorldChunkMeshes.TryGetValue(artifact.Coord, out StaticWorldChunkMesh existing) &&
                    ReferenceEquals(existing.Artifact, artifact))
                {
                    // Same artifact instance: the cell is unchanged.
                    replacement[i] = existing;
                    carriedCells.Add(artifact.Coord);
                    continue;
                }

                StaticWorldSubmesh[] submeshes = CreateChunkSubmeshes(renderer, artifact);
                createdChunks.Add(submeshes);
                replacement[i] = new StaticWorldChunkMesh(artifact, submeshes);
            }
        }
        catch
        {
            // The throwing chunk already rolled back its own submeshes.
            foreach (StaticWorldSubmesh[] submeshes in createdChunks)
                DestroyChunkSubmeshes(renderer, submeshes);
            throw;
        }

        // Commit. DestroyMesh, not Dispose: Dispose leaves the mesh in the
        // renderer's tracking list until shutdown.
        foreach (KeyValuePair<ChunkCoord, StaticWorldChunkMesh> stale in _staticWorldChunkMeshes)
        {
            if (!carriedCells.Contains(stale.Key))
                DestroyChunkSubmeshes(renderer, stale.Value.Submeshes);
        }

        _staticWorldChunkMeshes.Clear();
        _staticWorldChunkList.Clear();
        foreach (StaticWorldChunkMesh chunk in replacement)
        {
            _staticWorldChunkMeshes.Add(chunk.Coord, chunk);
            _staticWorldChunkList.Add(chunk);
        }

        // Z-order, so a run of consecutive entries is a compact block and the
        // cluster boxes can reject something. The delta path uses the same key.
        _staticWorldChunkList.Sort(static (a, b) =>
            a.Coord.MortonKey.CompareTo(b.Coord.MortonKey));

        RebuildChunkClusters();
        StaticWorld = world;
        StaticWorldCompileCount++;
        RecordWorldPublication(world);
    }

    // Applies only the changed cells. Create before destroy, like the full
    // rebuild.
    private void ApplyChunkMeshDelta(
        Renderer renderer, CsgWorld world, IReadOnlyList<(ChunkCoord Coord, ChunkMesh? Mesh)> delta)
    {
        var createdChunks = new List<StaticWorldSubmesh[]>(delta.Count);
        try
        {
            foreach ((_, ChunkMesh? artifact) in delta)
            {
                if (artifact is not null)
                    createdChunks.Add(CreateChunkSubmeshes(renderer, artifact));
            }
        }
        catch
        {
            foreach (StaticWorldSubmesh[] submeshes in createdChunks)
                DestroyChunkSubmeshes(renderer, submeshes);
            throw;
        }

        // Commit. Each entry lands at its sorted position, so the list stays
        // ordered.
        int createdIndex = 0;
        bool shapeChanged = false;
        _dirtyChunkClusters.Clear();
        foreach ((ChunkCoord coord, ChunkMesh? artifact) in delta)
        {
            bool existed = _staticWorldChunkMeshes.TryGetValue(coord, out StaticWorldChunkMesh old);
            if (existed)
                DestroyChunkSubmeshes(renderer, old.Submeshes);

            int position = ChunkListLowerBound(coord);
            _staticWorldBatchTotal += (artifact?.Submeshes.Count ?? 0) - (existed ? old.Submeshes.Length : 0);
            if (artifact is null)
            {
                if (existed)
                {
                    _staticWorldChunkMeshes.Remove(coord);
                    _staticWorldChunkList.RemoveAt(position);
                    shapeChanged = true;
                }
                continue;
            }

            var entry = new StaticWorldChunkMesh(artifact, createdChunks[createdIndex++]);
            _staticWorldChunkMeshes[coord] = entry;
            if (existed)
            {
                _staticWorldChunkList[position] = entry;
                _dirtyChunkClusters.Add(position / ChunkClusterSize);
            }
            else
            {
                _staticWorldChunkList.Insert(position, entry);
                shapeChanged = true;
            }
        }

        if (shapeChanged) RebuildChunkClusters();
        else foreach (int cluster in _dirtyChunkClusters) RefitChunkCluster(cluster);
        StaticWorld = world;
        StaticWorldCompileCount++;
        RecordWorldPublication(world);
    }

    // One GPU mesh per material in the cell. On a throw, this cell's meshes
    // are destroyed before it propagates, so callers roll back whole cells.
    private StaticWorldSubmesh[] CreateChunkSubmeshes(Renderer renderer, ChunkMesh artifact)
    {
        IReadOnlyList<ChunkSubmesh> sources = artifact.Submeshes;
        var submeshes = new StaticWorldSubmesh[sources.Count];
        int created = 0;
        try
        {
            for (; created < submeshes.Length; created++)
            {
                ChunkSubmesh source = sources[created];
                // No CPU copy: nothing reads a chunk mesh's arrays.
                Mesh gpuMesh = renderer.CreateMesh(
                    source.Vertices, source.Indices, VertexAttribute.StandardLayout, MeshCpuAccess.None);
                submeshes[created] = new StaticWorldSubmesh(
                    source.Material, gpuMesh, ResolveWorldMaterial(source.Material));
            }
        }
        catch
        {
            for (int i = 0; i < created; i++)
                renderer.DestroyMesh(submeshes[i].Mesh);
            throw;
        }

        return submeshes;
    }

    private static void DestroyChunkSubmeshes(Renderer renderer, StaticWorldSubmesh[] submeshes)
    {
        for (int i = 0; i < submeshes.Length; i++)
            renderer.DestroyMesh(submeshes[i].Mesh);
    }

    private int ChunkListLowerBound(ChunkCoord coord)
    {
        int lo = 0, hi = _staticWorldChunkList.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_staticWorldChunkList[mid].Coord.MortonKey < coord.MortonKey)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    private static string DescribeBrushNodeDefect(SceneNode node, string defect) =>
        $"Brush node '{node.Name}' has a non-rigid world transform ({defect}). " +
        "Brush node transforms must be rigid — rotation and translation only; " +
        "size belongs in the brush planes, not in node scale.";

    /// <summary>Enumerates every node in the scene, depth-first from the root.</summary>
    public IEnumerable<SceneNode> Nodes => Root.Traverse();

    private const float RigidTransformTolerance = 1e-4f;

    // Null when the matrix is rigid. The CSG tolerances assume it: under scale
    // or shear, Plane.Transform returns planes that are not normalized.
    internal static string? DescribeNonRigidDefect(in Matrix4x4 m)
    {
        const float tolerance = RigidTransformTolerance;

        // Finiteness first: every check below is "x > tolerance", which a NaN
        // passes. One non-finite element makes the sum non-finite.
        if (!float.IsFinite(
                m.M11 + m.M12 + m.M13 + m.M14 +
                m.M21 + m.M22 + m.M23 + m.M24 +
                m.M31 + m.M32 + m.M33 + m.M34 +
                m.M41 + m.M42 + m.M43 + m.M44))
            return "non-finite elements: NaN or Infinity in the matrix";

        if (MathF.Abs(m.M14) > tolerance || MathF.Abs(m.M24) > tolerance ||
            MathF.Abs(m.M34) > tolerance || MathF.Abs(m.M44 - 1f) > tolerance)
            return "projective components";

        // Row vectors: the basis is the upper 3x3 rows.
        var r0 = new Vector3(m.M11, m.M12, m.M13);
        var r1 = new Vector3(m.M21, m.M22, m.M23);
        var r2 = new Vector3(m.M31, m.M32, m.M33);

        if (MathF.Abs(r0.Length() - 1f) > tolerance ||
            MathF.Abs(r1.Length() - 1f) > tolerance ||
            MathF.Abs(r2.Length() - 1f) > tolerance)
            return "scale or a singular basis: basis vectors are not unit length";

        if (MathF.Abs(Vector3.Dot(r0, r1)) > tolerance ||
            MathF.Abs(Vector3.Dot(r1, r2)) > tolerance ||
            MathF.Abs(Vector3.Dot(r2, r0)) > tolerance)
            return "shear: basis vectors are not orthogonal";

        // A mirror turns every brush plane inside out.
        if (Vector3.Dot(Vector3.Cross(r0, r1), r2) < 0f)
            return "reflection: negative determinant";

        return null;
    }

    private sealed record StaticWorldCompilation(CsgWorld World, double DurationMs);
}
