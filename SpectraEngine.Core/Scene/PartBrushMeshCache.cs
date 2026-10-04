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
// GPU meshes for part brushes, built once in brush-local space and moved by
// the node's world matrix. Keyed by brush identity: a brush is immutable, so
// an edited brush is a new key, and nodes sharing a brush share its mesh.
// Membership is refcounted and applied in Pump. Render thread only.
internal sealed class PartBrushMeshCache
{
    private sealed class Entry
    {
        public BrushSubmesh[] Submeshes = [];
        public int References;
        public bool Built;
    }

    private readonly Dictionary<Brush, Entry> _entries = new(BrushIdentity.Comparer);
    private readonly Dictionary<SceneNode, Brush> _references = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Brush> _dirty = new(BrushIdentity.Comparer);

    // Distinct part brushes that hold GPU meshes.
    public int Count { get; private set; }
    internal int PendingCount => _dirty.Count;

    // Draw calls the cached brushes expand to.
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

    // No GPU work here; Pump applies it.
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

    // A detach and reattach within one frame keeps its meshes.
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

    // Before renderer shutdown. Membership is kept, so the next Pump rebuilds.
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
        // Snapped like the world path, so a brush's triangles do not shift
        // when it is converted between part and world.
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
                // No CPU copy: a part is picked through its brush planes.
                Mesh gpuMesh = renderer.CreateMesh(
                    source.Vertices, source.Indices, VertexAttribute.StandardLayout, MeshCpuAccess.None);
                submeshes[created] = new BrushSubmesh(
                    source.Material, gpuMesh, resolveMaterial(source.Material));
            }
        }
        catch
        {
            // Do not leak the meshes already made.
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

// Explicit reference identity, so the caches stay correct if Brush ever gets
// value equality.
internal static class BrushIdentity
{
    public static IEqualityComparer<Brush> Comparer { get; } = new IdentityComparer();

    private sealed class IdentityComparer : IEqualityComparer<Brush>
    {
        public bool Equals(Brush? x, Brush? y) => ReferenceEquals(x, y);

        public int GetHashCode(Brush obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
