using System;
using System.Collections.Generic;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// One drawable material group of a part brush: the GPU mesh built from the
/// brush's own faces, plus the resolved material those faces name.
/// </summary>
public readonly record struct BrushSubmesh(MaterialRef Source, Mesh Mesh, Material? Material);

/// <summary>
/// GPU meshes for <see cref="BrushKind.Part"/> brushes — the geometry a brush
/// renders with when it is <em>not</em> fused into the static world.
/// </summary>
/// <remarks>
/// <para>
/// <b>Brush-local, never world-space.</b> The static world bakes world-space
/// vertices and draws them at identity, because a fused surface never moves
/// without a recompile. A part is the opposite case by construction: it moves
/// constantly and must never recompile, so its mesh is built once from
/// <see cref="Brush.LocalFaces"/> and the node's world matrix does the moving.
/// A door opening is a matrix write. The silent failure this exists to prevent
/// is a mesh <em>rebuilt</em> per frame instead of <em>transformed</em> per
/// frame — it renders identically and destroys the frame budget.
/// </para>
/// <para>
/// <b>Keyed by brush identity, which is why invalidation is free.</b>
/// <see cref="Brush"/> is immutable: retexturing or resizing returns a
/// <em>new</em> instance (<c>WithFaceMaterial</c>, <c>WithScaledExtents</c>),
/// so a changed brush is a cache miss by construction and a stale entry is
/// impossible. Two nodes sharing one brush instance share one mesh, which is
/// the prefab case and costs nothing extra.
/// </para>
/// <para>
/// Membership changes adjust reference counts on the render thread. The pump
/// reconciles their final counts before creating or destroying meshes; a
/// detach/reattach in one frame preserves a shared mesh. Transform changes
/// enqueue no work. Releasing graphics resources retains membership so the
/// next pump can recreate the same shared geometry.
/// </para>
/// <para>
/// <b>The faces are snapped, exactly as the world path snaps them.</b> Skipping
/// it would make a brush's triangles depend on which kind it happened to be,
/// so converting a resting part to world geometry — or back — could visibly
/// shift a surface by up to the grid quantum. Snapping is what makes the two
/// paths agree; see <see cref="VertexSnapper"/>. It runs in brush-local space
/// here and in world space there, so the guarantee is agreement <em>at
/// identity</em>, not a bit-for-bit identity at arbitrary placements — a
/// distinction worth stating because the first draft of this design claimed
/// the stronger version and it was false.
/// </para>
/// </remarks>
internal sealed class PartBrushMeshCache
{
    private sealed class Entry
    {
        public BrushSubmesh[] Submeshes = [];
        public int References;
        public bool Built;
    }

    // Reference identity, deliberately: Brush does not override equality, and
    // even if it did, two structurally-equal brushes are still two independent
    // upload sites — sharing a GPU mesh between them would need refcounting
    // this cache does not want.
    private readonly Dictionary<Brush, Entry> _entries = new(BrushIdentity.Comparer);
    private readonly Dictionary<SceneNode, Brush> _references = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Brush> _dirty = new(BrushIdentity.Comparer);

    /// <summary>How many distinct part brushes currently hold GPU meshes.</summary>
    public int Count { get; private set; }
    internal int PendingCount => _dirty.Count;

    /// <summary>Total draw calls the cached part brushes expand to.</summary>
    public int SubmeshCount
    {
        get
        {
            int total = 0;
            foreach (Entry entry in _entries.Values)
                total += entry.Submeshes.Length;
            return total;
        }
    }

    /// <summary>Records final brush-reference membership without touching GPU resources.</summary>
    public void SetReference(SceneNode node, Brush? brush)
    {
        _references.TryGetValue(node, out Brush? old);
        if (ReferenceEquals(old, brush)) return;
        if (old is not null)
        {
            _entries[old].References--;
            _dirty.Add(old);
            _references.Remove(node);
        }
        if (brush is not null)
        {
            if (!_entries.TryGetValue(brush, out Entry? entry))
                _entries.Add(brush, entry = new Entry());
            entry.References++;
            _references.Add(node, brush);
            _dirty.Add(brush);
        }
    }

    /// <summary>Applies only changed memberships. A detach/reattach within a frame reuses its meshes.</summary>
    public void Pump(Renderer renderer, Func<MaterialRef, Material?> resolveMaterial)
    {
        while (_dirty.Count > 0)
        {
            using var iterator = _dirty.GetEnumerator();
            iterator.MoveNext();
            Brush brush = iterator.Current;
            Entry entry = _entries[brush];
            if (entry.References == 0)
            {
                Destroy(renderer, entry.Submeshes);
                if (entry.Built) Count--;
                _entries.Remove(brush);
            }
            else if (!entry.Built)
            {
                entry.Submeshes = Build(renderer, brush, resolveMaterial);
                entry.Built = true;
                Count++;
            }
            _dirty.Remove(brush);
        }
    }

    public void RefreshMaterials(Func<MaterialRef, Material?> resolveMaterial)
    {
        foreach (Entry entry in _entries.Values)
            for (int i = 0; i < entry.Submeshes.Length; i++)
            {
                BrushSubmesh mesh = entry.Submeshes[i];
                entry.Submeshes[i] = mesh with { Material = resolveMaterial(mesh.Source) };
            }
    }
    public bool TryGet(Brush brush, out BrushSubmesh[] submeshes)
    {
        if (_entries.TryGetValue(brush, out Entry? entry))
        {
            submeshes = entry.Submeshes;
            return submeshes.Length > 0;
        }
        submeshes = [];
        return false;
    }

    /// <summary>Destroys every GPU mesh this cache owns. Render thread, before renderer shutdown.</summary>
    public void ReleaseGraphicsResources(Renderer renderer)
    {
        foreach (Entry entry in _entries.Values)
            Destroy(renderer, entry.Submeshes);
        foreach ((Brush brush, Entry entry) in _entries)
        {
            entry.Submeshes = [];
            entry.Built = false;
            _dirty.Add(brush);
        }
        Count = 0;
    }

    private static BrushSubmesh[] Build(Renderer renderer, Brush brush, Func<MaterialRef, Material?> resolveMaterial)
    {
        // Same three stages the world path runs, minus every stage that only
        // makes sense between brushes: no carve (a part has no neighbours to
        // merge with), no weld, no T-junction pass, no BSP. Snap and split.
        Polygon[] snapped = VertexSnapper.Snap(brush.LocalFaces);
        ChunkSubmesh[] sources = ChunkMeshBuilder.BuildSubmeshes(snapped);
        if (sources.Length == 0)
            return [];

        var submeshes = new BrushSubmesh[sources.Length];
        int created = 0;
        try
        {
            for (; created < sources.Length; created++)
            {
                ChunkSubmesh source = sources[created];
                // No CPU copy: a part brush is picked and measured through its
                // brush planes, never through this mesh (see MeshCpuAccess).
                Mesh gpuMesh = renderer.CreateMesh(
                    source.Vertices, source.Indices, VertexAttribute.StandardLayout, MeshCpuAccess.None);
                submeshes[created] = new BrushSubmesh(
                    source.Material, gpuMesh, resolveMaterial(source.Material));
            }
        }
        catch
        {
            // Atomic per brush, mirroring CreateChunkSubmeshes one level down:
            // a throw partway through must not leak the meshes already made.
            for (int i = 0; i < created; i++)
                renderer.DestroyMesh(submeshes[i].Mesh);
            throw;
        }

        return submeshes;
    }

    // DestroyMesh, not Dispose: the renderer's tracking list must lose them too.
    private static void Destroy(Renderer renderer, BrushSubmesh[] submeshes)
    {
        for (int i = 0; i < submeshes.Length; i++)
            renderer.DestroyMesh(submeshes[i].Mesh);
    }
}

// Reference identity for brush cache keys. Brush does not override equality,
// but relying on that implicitly would make this cache silently wrong the day
// somebody gives it value semantics — two structurally-equal brushes are still
// two independent upload sites, and sharing one GPU mesh between them would
// need refcounting this cache deliberately does not have.
internal static class BrushIdentity
{
    public static IEqualityComparer<Brush> Comparer { get; } = new IdentityComparer();

    private sealed class IdentityComparer : IEqualityComparer<Brush>
    {
        public bool Equals(Brush? x, Brush? y) => ReferenceEquals(x, y);

        public int GetHashCode(Brush obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
