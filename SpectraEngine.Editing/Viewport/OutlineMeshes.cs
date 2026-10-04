using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// Hands scene nodes to the renderer's outline pass. A mesh node gives its own
/// mesh; a brush gets one built from its faces, because a world brush is fused
/// into chunk meshes and has no mesh of its own.
/// </summary>
// Render thread, outside any open pass: it creates and destroys meshes.
public sealed class OutlineMeshes
{
    private const int FloatsPerVertex = 8;

    private readonly Renderer _renderer;

    // By reference: an edited brush is a new instance, which is what retires
    // the old mesh.
    private readonly Dictionary<Brush, Entry> _brushes = new(ReferenceEqualityComparer.Instance);
    private readonly List<Brush> _retired = [];
    private readonly List<float> _vertices = [];
    private readonly List<uint> _indices = [];
    private int _frame;

    private sealed class Entry(Mesh mesh)
    {
        public readonly Mesh Mesh = mesh;
        public int LastFrame;
    }

    /// <summary>Feeds <paramref name="renderer"/>'s outline list.</summary>
    public OutlineMeshes(Renderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    /// <summary>
    /// Whether the renderer draws outlines at all. When it does not, the caller
    /// falls back to drawing edges as lines.
    /// </summary>
    public bool Supported => _renderer.SupportsOutlines;

    /// <summary>Brush meshes alive right now.</summary>
    public int BrushMeshCount => _brushes.Count;

    /// <summary>Starts a frame. Every brush added before <see cref="EndFrame"/> stays alive.</summary>
    public void BeginFrame() => _frame++;

    /// <summary>
    /// Queues <paramref name="node"/>'s own shape. False for a node with neither
    /// a brush nor a mesh.
    /// </summary>
    public bool TryAdd(SceneNode node, OutlineGroup group)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Brush is { } brush)
        {
            if (!TryGetBrushMesh(brush, out Mesh? brushMesh))
                return false;

            _renderer.Outlines.Add(brushMesh, node.WorldMatrix, group);
            return true;
        }

        if (node.MeshRenderer?.Mesh is { } mesh)
        {
            _renderer.Outlines.Add(mesh, node.WorldMatrix, group);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Queues everything under <paramref name="node"/> that has a shape, so a
    /// selected group is outlined as the things in it. Returns how many were queued.
    /// </summary>
    public int AddSubtree(SceneNode node, OutlineGroup group)
    {
        ArgumentNullException.ThrowIfNull(node);

        int added = TryAdd(node, group) ? 1 : 0;

        IReadOnlyList<SceneNode> children = node.Children;
        for (int i = 0; i < children.Count; i++)
            added += AddSubtree(children[i], group);

        return added;
    }

    /// <summary>Ends a frame: frees the mesh of every brush nothing asked for.</summary>
    public void EndFrame()
    {
        _retired.Clear();

        foreach (KeyValuePair<Brush, Entry> pair in _brushes)
        {
            if (pair.Value.LastFrame != _frame)
                _retired.Add(pair.Key);
        }

        for (int i = 0; i < _retired.Count; i++)
        {
            _renderer.DestroyMesh(_brushes[_retired[i]].Mesh);
            _brushes.Remove(_retired[i]);
        }
    }

    /// <summary>Frees every brush mesh. Call before the renderer shuts down or the scene is replaced.</summary>
    public void Release()
    {
        foreach (KeyValuePair<Brush, Entry> pair in _brushes)
            _renderer.DestroyMesh(pair.Value.Mesh);

        _brushes.Clear();
    }

    private bool TryGetBrushMesh(Brush brush, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Mesh? mesh)
    {
        if (_brushes.TryGetValue(brush, out Entry? entry))
        {
            entry.LastFrame = _frame;
            mesh = entry.Mesh;
            return true;
        }

        mesh = Build(brush);
        if (mesh is null)
            return false;

        _brushes[brush] = new Entry(mesh) { LastFrame = _frame };
        return true;
    }

    // Every face as a triangle fan, in brush-local space. Faces are convex, so
    // a fan covers them exactly.
    private Mesh? Build(Brush brush)
    {
        _vertices.Clear();
        _indices.Clear();

        IReadOnlyList<Polygon> faces = brush.LocalFaces;
        for (int f = 0; f < faces.Count; f++)
        {
            ReadOnlySpan<Vector3> points = faces[f].VertexSpan;
            if (points.Length < 3)
                continue;

            Vector3 normal = faces[f].Surface.Normal;
            uint first = (uint)(_vertices.Count / FloatsPerVertex);

            for (int v = 0; v < points.Length; v++)
            {
                _vertices.Add(points[v].X);
                _vertices.Add(points[v].Y);
                _vertices.Add(points[v].Z);
                _vertices.Add(normal.X);
                _vertices.Add(normal.Y);
                _vertices.Add(normal.Z);
                _vertices.Add(0f);
                _vertices.Add(0f);
            }

            for (uint v = 1; v + 1 < points.Length; v++)
            {
                _indices.Add(first);
                _indices.Add(first + v);
                _indices.Add(first + v + 1);
            }
        }

        if (_indices.Count == 0)
            return null;

        return _renderer.CreateMesh(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices),
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_indices),
            VertexAttribute.StandardLayout,
            MeshCpuAccess.None);
    }
}
