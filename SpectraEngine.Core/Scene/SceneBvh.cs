using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;

[assembly: InternalsVisibleTo("SpectraEngine.Bsp.Tests")]
[assembly: InternalsVisibleTo("SpectraEngine.Editing.Tests")]
[assembly: InternalsVisibleTo("SpectraEngine.Graphics.Tests")]
[assembly: InternalsVisibleTo("SpectraEngine.Physics.Tests")]
[assembly: InternalsVisibleTo("Spectra.Kitchen.Tests")]
[assembly: InternalsVisibleTo("SceneProbe")]

namespace SpectraEngine.Core.Scene;

/// <summary>
/// Result of a <see cref="Scene.Raycast"/>: the nearest spatial node the ray
/// struck, with the world-space hit point and surface normal.
/// </summary>
/// <param name="Normal">Unit world-space surface normal at the hit, facing the ray.</param>
/// <param name="PlaneIndex">
/// Which of the brush's <see cref="Bsp.Brush.LocalPlanes"/> the ray entered
/// through, or -1 when the hit was not a brush face.
/// </param>
public readonly record struct SceneRaycastHit(
    SceneNode Node, float Distance, Vector3 Point, Vector3 Normal, int PlaneIndex = -1);

// Dynamic AABB tree over a scene's spatial nodes (those with a MeshRenderer or
// a Brush). Leaf boxes are fat, so small moves only update the cached tight
// box. Moved leaves are refit lazily at the start of the next query.
// Render thread only, like the Scene that owns it.
internal sealed class SceneBvh
{
    // World units a leaf's fat box extends past the tight box on every side.
    // Larger tolerates more movement before a re-insert but visits more nodes.
    internal const float FatMargin = 0.2f;

    private const int Null = -1;

    // For a mesh with no bounds at all. Validity comes from HasLocalBounds, not
    // Positions: a MeshCpuAccess.None mesh has empty arrays and good bounds.
    // The Positions check covers test doubles that skip InitializeCpuData.
    private static readonly Aabb UnitFallbackBox = new(new Vector3(-0.5f), new Vector3(0.5f));

    private struct Node
    {
        public Aabb FatBox;     // internal nodes: union of the children's
        public Aabb TightBox;   // leaves only
        public int Parent;
        public int Child1;      // free-list next while the node is free
        public int Child2;
        public SceneNode? Leaf; // null for internal and free nodes
        public bool Dirty;      // leaf is queued in _dirtyNodes
        public bool CountsAsMesh; // leaf's scene node carried a MeshRenderer when last counted
    }

    private Node[] _nodes;
    private int _freeList;
    private int _root = Null;

    private readonly Dictionary<SceneNode, int> _leaves = [];

    // References, not leaf indices: an index can be freed and reused between
    // the mark and the flush.
    private readonly List<SceneNode> _dirtyNodes = [];

    // Reused so queries and dirty-marking walks do not allocate.
    private int[] _traversalStack = new int[64];
    private readonly List<SceneNode> _subtreeStack = [];

    private readonly bool _drawableOnly;
    internal SceneBvh(Scene scene, bool drawableOnly = false)
    {
        _drawableOnly = drawableOnly;
        _nodes = new Node[16];
        _freeList = Null;
        for (int i = _nodes.Length - 1; i >= 0; i--)
        {
            _nodes[i].Child1 = _freeList;
            _nodes[i].Parent = Null;
            _freeList = i;
        }

        scene.NodeAdded += OnNodeAdded;
        scene.NodeRemoved += OnNodeRemoved;
        scene.NodeTransformChanged += OnSubtreeMoved;
    }

    internal int LeafCount => _leaves.Count;

    // Leaves with a MeshRenderer. Feeds RenderView.TotalCount, read per frame.
    private int _meshLeafCount;

    internal int MeshLeafCount => _meshLeafCount;

    // The drawable index holds what draws on its own: rendered meshes and
    // parts. The main index keeps a hidden node, so it can still be picked.
    private bool IsSpatial(SceneNode node)
    {
        if (!_drawableOnly)
            return node.MeshRenderer is not null || node.Brush is not null;

        return node.IsRendered &&
               (node.MeshRenderer is not null ||
                (node.Brush is not null && node.BrushKind == BrushKind.Part));
    }

    private void OnNodeAdded(SceneNode node)
    {
        OnSpatialComponentChanged(node);
    }

    private void OnNodeRemoved(SceneNode node)
    {
        if (_leaves.Remove(node, out int leaf))
        {
            if (_nodes[leaf].CountsAsMesh)
                _meshLeafCount--;
            RemoveLeafFromTree(leaf);
            FreeNode(leaf);
        }
    }

    // Transform changes and same-scene reparents: every leaf in the subtree
    // has a new world matrix.
    internal void OnSubtreeMoved(SceneNode node)
    {
        if (_leaves.Count == 0)
            return;

        _subtreeStack.Add(node);
        while (_subtreeStack.Count > 0)
        {
            int last = _subtreeStack.Count - 1;
            SceneNode current = _subtreeStack[last];
            _subtreeStack.RemoveAt(last);

            MarkDirty(current);

            IReadOnlyList<SceneNode> children = current.Children;
            for (int i = 0; i < children.Count; i++)
                _subtreeStack.Add(children[i]);
        }
    }

    // A mesh or brush was assigned, cleared or replaced on an owned node.
    internal void OnSpatialComponentChanged(SceneNode node)
    {
        bool spatial = IsSpatial(node);
        if (_leaves.TryGetValue(node, out int leaf))
        {
            if (spatial)
            {
                bool countsAsMesh = node.MeshRenderer is not null;
                if (countsAsMesh != _nodes[leaf].CountsAsMesh)
                {
                    _nodes[leaf].CountsAsMesh = countsAsMesh;
                    _meshLeafCount += countsAsMesh ? 1 : -1;
                }
                MarkDirty(node);
            }
            else
            {
                OnNodeRemoved(node);
            }
        }
        else if (spatial)
        {
            Insert(node);
        }
    }

    private void MarkDirty(SceneNode node)
    {
        if (_leaves.TryGetValue(node, out int leaf) && !_nodes[leaf].Dirty)
        {
            _nodes[leaf].Dirty = true;
            _dirtyNodes.Add(node);
        }
    }

    // Runs at the start of every query. A leaf still inside its fat box only
    // gets a new tight box; one that escaped is re-inserted.
    private void FlushDirtyLeaves()
    {
        if (_dirtyNodes.Count == 0)
            return;

        for (int i = 0; i < _dirtyNodes.Count; i++)
        {
            SceneNode sceneNode = _dirtyNodes[i];
            // Removed, or removed and re-inserted, since it was marked.
            if (!_leaves.TryGetValue(sceneNode, out int leaf) || !_nodes[leaf].Dirty)
                continue;
            _nodes[leaf].Dirty = false;

            Aabb tight = ComputeWorldBounds(sceneNode);
            if (ContainsBox(_nodes[leaf].FatBox, tight))
            {
                _nodes[leaf].TightBox = tight;
                continue;
            }

            RemoveLeafFromTree(leaf);
            _nodes[leaf].TightBox = tight;
            _nodes[leaf].FatBox = tight.Expanded(FatMargin);
            InsertLeafIntoTree(leaf);
        }
        _dirtyNodes.Clear();
    }

    private static Aabb ComputeWorldBounds(SceneNode node)
    {
        Matrix4x4 world = node.WorldMatrix;

        bool has = false;
        Aabb result = default;
        if (node.Brush is { } brush)
        {
            result = brush.LocalBounds.Transform(world);
            has = true;
        }
        if (node.MeshRenderer is { } meshRenderer)
        {
            Aabb meshBox = MeshLocalBounds(meshRenderer.Mesh).Transform(world);
            result = has ? Union(result, meshBox) : meshBox;
            has = true;
        }
        // Only spatial nodes reach here; the point box is a fallback.
        return has ? result : new Aabb(world.Translation, world.Translation);
    }

    private static Aabb MeshLocalBounds(Mesh mesh) =>
        mesh.HasLocalBounds || mesh.Positions.Count > 0 ? mesh.LocalBounds : UnitFallbackBox;

    private int AllocateNode()
    {
        if (_freeList == Null)
        {
            int oldCapacity = _nodes.Length;
            Array.Resize(ref _nodes, oldCapacity * 2);
            for (int i = _nodes.Length - 1; i >= oldCapacity; i--)
            {
                _nodes[i].Child1 = _freeList;
                _nodes[i].Parent = Null;
                _freeList = i;
            }
        }

        int index = _freeList;
        _freeList = _nodes[index].Child1;
        _nodes[index].Parent = Null;
        _nodes[index].Child1 = Null;
        _nodes[index].Child2 = Null;
        _nodes[index].Leaf = null;
        _nodes[index].Dirty = false;
        _nodes[index].CountsAsMesh = false;
        return index;
    }

    private void FreeNode(int index)
    {
        _nodes[index].Leaf = null;
        _nodes[index].Dirty = false;
        _nodes[index].CountsAsMesh = false;
        _nodes[index].Child1 = _freeList;
        _freeList = index;
    }

    private void Insert(SceneNode sceneNode)
    {
        Aabb tight = ComputeWorldBounds(sceneNode);
        int leaf = AllocateNode();
        _nodes[leaf].Leaf = sceneNode;
        _nodes[leaf].TightBox = tight;
        _nodes[leaf].FatBox = tight.Expanded(FatMargin);
        if (sceneNode.MeshRenderer is not null)
        {
            _nodes[leaf].CountsAsMesh = true;
            _meshLeafCount++;
        }
        _leaves.Add(sceneNode, leaf);
        InsertLeafIntoTree(leaf);
    }

    // Box2D-style insertion by the surface-area heuristic.
    private void InsertLeafIntoTree(int leaf)
    {
        if (_root == Null)
        {
            _root = leaf;
            _nodes[leaf].Parent = Null;
            return;
        }

        Aabb leafBox = _nodes[leaf].FatBox;

        int index = _root;
        while (_nodes[index].Leaf is null)
        {
            int child1 = _nodes[index].Child1;
            int child2 = _nodes[index].Child2;

            float area = SurfaceArea(_nodes[index].FatBox);
            float combinedArea = SurfaceArea(Union(_nodes[index].FatBox, leafBox));

            // New parent here, or descend. Descending inherits the growth it
            // forces on this node's box.
            float costHere = 2f * combinedArea;
            float inheritance = 2f * (combinedArea - area);
            float cost1 = DescendCost(child1, leafBox) + inheritance;
            float cost2 = DescendCost(child2, leafBox) + inheritance;

            if (costHere < cost1 && costHere < cost2)
                break;

            index = cost1 < cost2 ? child1 : child2;
        }

        int sibling = index;
        int oldParent = _nodes[sibling].Parent;
        int newParent = AllocateNode();
        _nodes[newParent].Parent = oldParent;
        _nodes[newParent].FatBox = Union(leafBox, _nodes[sibling].FatBox);
        _nodes[newParent].Child1 = sibling;
        _nodes[newParent].Child2 = leaf;
        _nodes[sibling].Parent = newParent;
        _nodes[leaf].Parent = newParent;

        if (oldParent == Null)
        {
            _root = newParent;
        }
        else if (_nodes[oldParent].Child1 == sibling)
        {
            _nodes[oldParent].Child1 = newParent;
        }
        else
        {
            _nodes[oldParent].Child2 = newParent;
        }

        RefitAncestors(oldParent);
    }

    private float DescendCost(int child, in Aabb leafBox)
    {
        float unionArea = SurfaceArea(Union(_nodes[child].FatBox, leafBox));
        // A leaf child becomes a sibling, so its whole box counts. An internal
        // child only charges its growth.
        return _nodes[child].Leaf is not null ? unionArea : unionArea - SurfaceArea(_nodes[child].FatBox);
    }

    // The leaf node stays allocated; the refit path re-inserts it.
    private void RemoveLeafFromTree(int leaf)
    {
        if (leaf == _root)
        {
            _root = Null;
            return;
        }

        int parent = _nodes[leaf].Parent;
        int grandParent = _nodes[parent].Parent;
        int sibling = _nodes[parent].Child1 == leaf ? _nodes[parent].Child2 : _nodes[parent].Child1;

        _nodes[sibling].Parent = grandParent;
        if (grandParent == Null)
        {
            _root = sibling;
        }
        else
        {
            if (_nodes[grandParent].Child1 == parent)
                _nodes[grandParent].Child1 = sibling;
            else
                _nodes[grandParent].Child2 = sibling;
            RefitAncestors(grandParent);
        }
        FreeNode(parent);
    }

    private void RefitAncestors(int index)
    {
        while (index != Null)
        {
            _nodes[index].FatBox = Union(_nodes[_nodes[index].Child1].FatBox, _nodes[_nodes[index].Child2].FatBox);
            index = _nodes[index].Parent;
        }
    }

    // False when the node is not indexed.
    internal bool TryGetWorldBounds(SceneNode node, out Aabb bounds)
    {
        FlushDirtyLeaves();

        if (_leaves.TryGetValue(node, out int leaf))
        {
            bounds = _nodes[leaf].TightBox;
            return true;
        }

        bounds = default;
        return false;
    }

    public bool Raycast(in Ray3 ray, out SceneRaycastHit hit, float maxDistance = float.PositiveInfinity) =>
        Raycast(ray, out hit, default, maxDistance);

    // The filter runs at the leaf: after the box test, before the narrow phase.
    public bool Raycast(
        in Ray3 ray, out SceneRaycastHit hit, in SceneQueryFilter filter,
        float maxDistance = float.PositiveInfinity)
    {
        FlushDirtyLeaves();

        hit = default;
        if (_root == Null)
            return false;

        // Nearest narrow-phase hit so far. Boxes are pruned against it, but a
        // box entered earlier can still hold only farther geometry, so there
        // is no early-out on box order.
        float best = maxDistance;
        bool found = false;

        int stackTop = 0;
        _traversalStack[stackTop++] = _root;

        while (stackTop > 0)
        {
            int index = _traversalStack[--stackTop];

            if (_nodes[index].Leaf is { } sceneNode)
            {
                // best may have shrunk since this leaf was pushed.
                if (!RayIntersectsBox(ray, _nodes[index].TightBox, best, out _))
                    continue;

                if (!filter.Accepts(sceneNode))
                    continue;

                if (sceneNode.Brush is { } brush &&
                    RaycastBrush(
                        sceneNode, brush, ray, best,
                        out float tBrush, out Vector3 nBrush, out int planeBrush))
                {
                    best = tBrush;
                    hit = new SceneRaycastHit(sceneNode, tBrush, ray.PointAt(tBrush), nBrush, planeBrush);
                    found = true;
                }
                if (sceneNode.MeshRenderer is { } meshRenderer &&
                    RaycastMesh(sceneNode, meshRenderer.Mesh, ray, best, out float tMesh, out Vector3 nMesh))
                {
                    best = tMesh;
                    // No plane index: what was hit is the mesh, even on a node
                    // that also has a brush.
                    hit = new SceneRaycastHit(sceneNode, tMesh, ray.PointAt(tMesh), nMesh);
                    found = true;
                }
                continue;
            }

            int child1 = _nodes[index].Child1;
            int child2 = _nodes[index].Child2;
            bool hit1 = RayIntersectsBox(ray, _nodes[child1].FatBox, best, out float t1);
            bool hit2 = RayIntersectsBox(ray, _nodes[child2].FatBox, best, out float t2);

            if (stackTop + 2 > _traversalStack.Length)
                Array.Resize(ref _traversalStack, _traversalStack.Length * 2);

            // Near child first, so its hit can prune the far one.
            if (hit1 && hit2)
            {
                if (t1 <= t2)
                {
                    _traversalStack[stackTop++] = child2;
                    _traversalStack[stackTop++] = child1;
                }
                else
                {
                    _traversalStack[stackTop++] = child1;
                    _traversalStack[stackTop++] = child2;
                }
            }
            else if (hit1)
            {
                _traversalStack[stackTop++] = child1;
            }
            else if (hit2)
            {
                _traversalStack[stackTop++] = child2;
            }
        }

        return found;
    }

    internal bool EntirelyInside(in Frustum frustum)
    {
        FlushDirtyLeaves();
        return _root == Null || frustum.Contains(_nodes[_root].FatBox);
    }

    public void QueryFrustum(in Frustum frustum, List<SceneNode> results)
    {
        FlushDirtyLeaves();

        if (_root == Null)
            return;

        int stackTop = 0;
        _traversalStack[stackTop++] = _root;

        while (stackTop > 0)
        {
            int index = _traversalStack[--stackTop];

            if (_nodes[index].Leaf is { } sceneNode)
            {
                // Leaves test the tight box, so the result matches a brute-force
                // test over true bounds.
                if (frustum.Intersects(_nodes[index].TightBox))
                    results.Add(sceneNode);
                continue;
            }

            if (!frustum.Intersects(_nodes[index].FatBox))
                continue;

            if (stackTop + 2 > _traversalStack.Length)
                Array.Resize(ref _traversalStack, _traversalStack.Length * 2);
            _traversalStack[stackTop++] = _nodes[index].Child1;
            _traversalStack[stackTop++] = _nodes[index].Child2;
        }
    }

    public void QueryBox(in Aabb box, List<SceneNode> results, in SceneQueryFilter filter)
    {
        FlushDirtyLeaves();

        if (_root == Null)
            return;

        int stackTop = 0;
        _traversalStack[stackTop++] = _root;

        while (stackTop > 0)
        {
            int index = _traversalStack[--stackTop];

            if (_nodes[index].Leaf is { } sceneNode)
            {
                if (_nodes[index].TightBox.Intersects(box) && filter.Accepts(sceneNode))
                    results.Add(sceneNode);
                continue;
            }

            if (!_nodes[index].FatBox.Intersects(box))
                continue;

            if (stackTop + 2 > _traversalStack.Length)
                Array.Resize(ref _traversalStack, _traversalStack.Length * 2);
            _traversalStack[stackTop++] = _nodes[index].Child1;
            _traversalStack[stackTop++] = _nodes[index].Child2;
        }
    }

    // Bounds against the sphere, not geometry. A broad phase: the result is a
    // superset.
    public void QuerySphere(Vector3 center, float radius, List<SceneNode> results, in SceneQueryFilter filter)
    {
        FlushDirtyLeaves();

        if (_root == Null || radius < 0f)
            return;

        int stackTop = 0;
        _traversalStack[stackTop++] = _root;

        while (stackTop > 0)
        {
            int index = _traversalStack[--stackTop];

            if (_nodes[index].Leaf is { } sceneNode)
            {
                if (_nodes[index].TightBox.IntersectsSphere(center, radius) && filter.Accepts(sceneNode))
                    results.Add(sceneNode);
                continue;
            }

            if (!_nodes[index].FatBox.IntersectsSphere(center, radius))
                continue;

            if (stackTop + 2 > _traversalStack.Length)
                Array.Resize(ref _traversalStack, _traversalStack.Length * 2);
            _traversalStack[stackTop++] = _nodes[index].Child1;
            _traversalStack[stackTop++] = _nodes[index].Child2;
        }
    }

    // Clips the ray against the brush planes in brush-local space. A ray
    // starting inside reports no hit.
    // General inverse, not a rigid shortcut: the graph allows scale on a brush
    // node and picking has to stay exact under it. t still measures world
    // distance because only the direction is scaled.
    private static bool RaycastBrush(
        SceneNode node, Brush brush, in Ray3 ray, float best,
        out float t, out Vector3 normal, out int planeIndex)
    {
        t = 0f;
        normal = default;
        planeIndex = -1;

        if (!Matrix4x4.Invert(node.WorldMatrix, out Matrix4x4 inverse))
            return false; // zero scale or similar
        Vector3 origin = Vector3.Transform(ray.Origin, inverse);
        Vector3 direction = Vector3.TransformNormal(ray.Direction, inverse);

        float tEnter = 0f;
        float tExit = best;
        int enterPlane = -1;

        IReadOnlyList<Plane> planes = brush.LocalPlanes;
        for (int i = 0; i < planes.Count; i++)
        {
            Plane plane = planes[i]; // outward normal: inside is distance <= 0
            float distance = Plane.DotCoordinate(plane, origin);
            float denom = Vector3.Dot(plane.Normal, direction);

            if (MathF.Abs(denom) < 1e-9f)
            {
                if (distance > 0f)
                    return false; // parallel and outside
                continue;
            }

            float tPlane = -distance / denom;
            if (denom < 0f)
            {
                if (tPlane > tEnter)
                {
                    tEnter = tPlane;
                    enterPlane = i;
                }
            }
            else if (tPlane < tExit)
            {
                tExit = tPlane;
            }

            if (tEnter > tExit)
                return false;
        }

        if (enterPlane < 0 || tEnter >= best)
            return false;

        t = tEnter;
        normal = WorldNormal(planes[enterPlane].Normal, inverse);
        planeIndex = enterPlane;
        return true;
    }

    // Möller–Trumbore in mesh-local space. A mesh with no CPU geometry is
    // tested against its local bounds instead.
    private static bool RaycastMesh(
        SceneNode node, Mesh mesh, in Ray3 ray, float best, out float t, out Vector3 normal)
    {
        t = 0f;
        normal = default;

        Matrix4x4 world = node.WorldMatrix;
        if (!Matrix4x4.Invert(world, out Matrix4x4 inverse))
            return false; // zero scale or similar

        Vector3 origin = Vector3.Transform(ray.Origin, inverse);
        Vector3 direction = Vector3.TransformNormal(ray.Direction, inverse);

        IReadOnlyList<Vector3> positions = mesh.Positions;
        IReadOnlyList<uint> indices = mesh.Indices;
        if (positions.Count == 0 || indices.Count == 0)
        {
            if (!RaycastLocalBox(MeshLocalBounds(mesh), origin, direction, best, out t, out Vector3 boxNormal))
                return false;
            normal = WorldNormal(boxNormal, inverse);
            return true;
        }

        float bestT = best;
        Vector3 bestLocalNormal = default;
        bool found = false;

        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            Vector3 a = positions[(int)indices[i]];
            Vector3 b = positions[(int)indices[i + 1]];
            Vector3 c = positions[(int)indices[i + 2]];

            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 pVec = Vector3.Cross(direction, edge2);
            float det = Vector3.Dot(edge1, pVec);
            if (MathF.Abs(det) < 1e-12f)
                continue; // parallel

            float invDet = 1f / det;
            Vector3 tVec = origin - a;
            float u = Vector3.Dot(tVec, pVec) * invDet;
            if (u < 0f || u > 1f)
                continue;

            Vector3 qVec = Vector3.Cross(tVec, edge1);
            float v = Vector3.Dot(direction, qVec) * invDet;
            if (v < 0f || u + v > 1f)
                continue;

            float tTri = Vector3.Dot(edge2, qVec) * invDet;
            if (tTri < 0f || tTri >= bestT)
                continue;

            bestT = tTri;
            // No backface culling: flip the normal to face the ray.
            Vector3 n = Vector3.Cross(edge1, edge2);
            bestLocalNormal = Vector3.Dot(n, direction) > 0f ? -n : n;
            found = true;
        }

        if (!found)
            return false;

        t = bestT;
        normal = WorldNormal(bestLocalNormal, inverse);
        return true;
    }

    // A ray starting inside reports no hit, like the brush test.
    private static bool RaycastLocalBox(
        in Aabb box, in Vector3 origin, in Vector3 direction, float best, out float t, out Vector3 localNormal)
    {
        t = 0f;
        localNormal = default;

        float tMin = 0f;
        float tMax = best;
        int entryAxis = -1;
        float entrySign = 0f;

        for (int axis = 0; axis < 3; axis++)
        {
            float o = Component(origin, axis);
            float d = Component(direction, axis);
            float min = Component(box.Min, axis);
            float max = Component(box.Max, axis);

            if (MathF.Abs(d) < 1e-12f)
            {
                if (o < min || o > max)
                    return false;
                continue;
            }

            float inv = 1f / d;
            float t1 = (min - o) * inv;
            float t2 = (max - o) * inv;
            if (t1 > t2)
                (t1, t2) = (t2, t1);

            if (t1 > tMin)
            {
                tMin = t1;
                entryAxis = axis;
                entrySign = d > 0f ? -1f : 1f; // entry face opposes the ray
            }
            if (t2 < tMax)
                tMax = t2;
            if (tMin > tMax)
                return false;
        }

        if (entryAxis < 0 || tMin >= best)
            return false;

        t = tMin;
        localNormal = entryAxis switch
        {
            0 => new Vector3(entrySign, 0f, 0f),
            1 => new Vector3(0f, entrySign, 0f),
            _ => new Vector3(0f, 0f, entrySign),
        };
        return true;
    }

    // Normals go through the inverse transpose.
    private static Vector3 WorldNormal(in Vector3 localNormal, in Matrix4x4 inverseWorld) =>
        Vector3.Normalize(Vector3.TransformNormal(localNormal, Matrix4x4.Transpose(inverseWorld)));

    // tEntry is 0 when the origin is inside the box.
    private static bool RayIntersectsBox(in Ray3 ray, in Aabb box, float tLimit, out float tEntry)
    {
        float tMin = 0f;
        float tMax = tLimit;

        for (int axis = 0; axis < 3; axis++)
        {
            float o = Component(ray.Origin, axis);
            float d = Component(ray.Direction, axis);
            float min = Component(box.Min, axis);
            float max = Component(box.Max, axis);

            if (MathF.Abs(d) < 1e-12f)
            {
                if (o < min || o > max)
                {
                    tEntry = 0f;
                    return false;
                }
                continue;
            }

            float inv = 1f / d;
            float t1 = (min - o) * inv;
            float t2 = (max - o) * inv;
            if (t1 > t2)
                (t1, t2) = (t2, t1);

            if (t1 > tMin) tMin = t1;
            if (t2 < tMax) tMax = t2;
            if (tMin > tMax)
            {
                tEntry = 0f;
                return false;
            }
        }

        tEntry = tMin;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Component(in Vector3 v, int axis) =>
        axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    private static Aabb Union(in Aabb a, in Aabb b) =>
        new(Vector3.Min(a.Min, b.Min), Vector3.Max(a.Max, b.Max));

    private static bool ContainsBox(in Aabb outer, in Aabb inner) =>
        outer.Min.X <= inner.Min.X && outer.Min.Y <= inner.Min.Y && outer.Min.Z <= inner.Min.Z &&
        outer.Max.X >= inner.Max.X && outer.Max.Y >= inner.Max.Y && outer.Max.Z >= inner.Max.Z;

    private static float SurfaceArea(in Aabb box)
    {
        Vector3 size = box.Size;
        return 2f * (size.X * size.Y + size.Y * size.Z + size.Z * size.X);
    }

    // Test hook: throws on the first broken tree invariant. Allocates.
    internal void Validate()
    {
        FlushDirtyLeaves();

        if (_root == Null)
        {
            if (_leaves.Count != 0)
                throw new InvalidOperationException($"Tree is empty but {_leaves.Count} leaves are tracked.");
            return;
        }

        if (_nodes[_root].Parent != Null)
            throw new InvalidOperationException("Root node has a parent.");

        int reachableLeaves = 0;
        var stack = new Stack<int>();
        stack.Push(_root);
        while (stack.Count > 0)
        {
            int index = stack.Pop();
            Node node = _nodes[index];

            if (node.Leaf is { } sceneNode)
            {
                if (node.Child1 != Null || node.Child2 != Null)
                    throw new InvalidOperationException($"Leaf {index} has children.");
                if (!_leaves.TryGetValue(sceneNode, out int tracked) || tracked != index)
                    throw new InvalidOperationException(
                        $"Leaf {index} ('{sceneNode.Name}') is not tracked at its own index.");
                if (!ContainsBox(node.FatBox, node.TightBox))
                    throw new InvalidOperationException($"Leaf {index} ('{sceneNode.Name}'): fat box lost its tight box.");
                Aabb expected = ComputeWorldBounds(sceneNode);
                if (node.TightBox.Min != expected.Min || node.TightBox.Max != expected.Max)
                    throw new InvalidOperationException($"Leaf {index} ('{sceneNode.Name}'): stale tight box.");
                reachableLeaves++;
                continue;
            }

            if (node.Child1 == Null || node.Child2 == Null)
                throw new InvalidOperationException($"Internal node {index} is missing a child.");
            if (_nodes[node.Child1].Parent != index || _nodes[node.Child2].Parent != index)
                throw new InvalidOperationException($"Internal node {index}: child parent links are wrong.");
            Aabb union = Union(_nodes[node.Child1].FatBox, _nodes[node.Child2].FatBox);
            if (node.FatBox.Min != union.Min || node.FatBox.Max != union.Max)
                throw new InvalidOperationException($"Internal node {index}: box is not the union of its children.");

            stack.Push(node.Child1);
            stack.Push(node.Child2);
        }

        if (reachableLeaves != _leaves.Count)
            throw new InvalidOperationException(
                $"{reachableLeaves} leaves reachable from the root, but {_leaves.Count} are tracked.");
    }
}
