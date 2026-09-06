using System;
using System.Collections;
using System.Collections.Generic;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Graphics;

/// <summary>An indexed draw into shared geometry. Indices are relative to BaseVertex.</summary>
public readonly record struct MeshDrawRange(uint FirstIndex, uint IndexCount, int BaseVertex = 0);

/// <summary>
/// Owns one GPU vertex/index allocation. Views hold references independently of
/// the owner, and the final release destroys the buffers through their renderer.
/// Render thread only, like Mesh.
/// </summary>
public sealed class SharedMeshStorage : IDisposable
{
    private readonly Renderer _renderer;
    private readonly Mesh _mesh;
    private int _references = 1;
    private bool _disposed;

    internal SharedMeshStorage(Renderer renderer, Mesh mesh) { _renderer = renderer; _mesh = mesh; }
    internal Mesh BackingMesh => _mesh;

    /// <summary>Creates a draw view with independently culled local bounds.</summary>
    public Mesh CreateRange(MeshDrawRange range, in Aabb bounds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((ulong)range.FirstIndex + range.IndexCount > _mesh.IndexCount)
            throw new ArgumentOutOfRangeException(nameof(range));
        _references++;
        return new MeshRange(this, range, bounds);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Release();
    }

    private void Release()
    {
        if (--_references == 0) _renderer.DestroyMesh(_mesh);
    }

    private sealed class MeshRange : Mesh
    {
        private readonly SharedMeshStorage _storage;
        private readonly MeshDrawRange _range;
        private bool _disposed;
        internal MeshRange(SharedMeshStorage storage, MeshDrawRange range, in Aabb bounds)
        {
            _storage = storage; _range = range;
            IndexCount = range.IndexCount;
            LocalBounds = bounds; HasLocalBounds = range.IndexCount != 0;
            Positions = storage._mesh.Positions;
            Normals = storage._mesh.Normals;
            if (storage._mesh.Indices.Count != 0)
                Indices = new IndexView(storage._mesh.Indices, range);
        }
        public override void Draw() { ObjectDisposedException.ThrowIf(_disposed, this); _storage._mesh.DrawRange(_range); }
        public override void DrawInstanced(InstanceBuffer instances, int instanceCount, int firstInstance = 0)
        { ObjectDisposedException.ThrowIf(_disposed, this); _storage._mesh.DrawInstancedRange(_range, instances, instanceCount, firstInstance); }
        public override void Dispose() { if (_disposed) return; _disposed = true; _storage.Release(); }
    }

    private sealed class IndexView(IReadOnlyList<uint> indices, MeshDrawRange range) : IReadOnlyList<uint>
    {
        public int Count => checked((int)range.IndexCount);
        public uint this[int index] => (uint)index >= range.IndexCount
            ? throw new ArgumentOutOfRangeException(nameof(index))
            : checked((uint)((long)indices[checked((int)range.FirstIndex) + index] + range.BaseVertex));
        public IEnumerator<uint> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
