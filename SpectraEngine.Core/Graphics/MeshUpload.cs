using System;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Graphics;

/// <summary>Resumable mesh-buffer upload. Dispose cancels and destroys unpublished resources.</summary>
public sealed class MeshUpload : IDisposable
{
    private readonly Renderer _renderer;
    private Mesh? _mesh;
    private ReadOnlyMemory<float> _vertices;
    private ReadOnlyMemory<uint> _indices;
    private int _vertexOffset, _indexOffset;
    public bool IsComplete => _vertexOffset == _vertices.Length * sizeof(float) && _indexOffset == _indices.Length * sizeof(uint);
    internal MeshUpload(Renderer renderer, Mesh mesh, ReadOnlyMemory<float> vertices, ReadOnlyMemory<uint> indices, bool uploaded)
    {
        _renderer = renderer; _mesh = mesh; _vertices = vertices; _indices = indices;
        if (uploaded) { _vertexOffset = checked(vertices.Length * sizeof(float)); _indexOffset = checked(indices.Length * sizeof(uint)); }
    }
    public int Step(int maxBytes = 256 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, sizeof(uint));
        maxBytes = Math.Min(maxBytes, 256 * 1024);
        ObjectDisposedException.ThrowIf(_mesh is null, this);
        maxBytes &= ~3;
        int written = 0;
        if (_vertexOffset < _vertices.Length * sizeof(float))
        {
            int count = Math.Min(maxBytes, _vertices.Length * sizeof(float) - _vertexOffset);
            _mesh.WriteUploadBytes(false, _vertexOffset, MemoryMarshal.AsBytes(_vertices.Span).Slice(_vertexOffset, count));
            _vertexOffset += count; written += count;
        }
        if (written < maxBytes && _indexOffset < _indices.Length * sizeof(uint))
        {
            int count = Math.Min(maxBytes - written, _indices.Length * sizeof(uint) - _indexOffset);
            _mesh.WriteUploadBytes(true, _indexOffset, MemoryMarshal.AsBytes(_indices.Span).Slice(_indexOffset, count));
            _indexOffset += count; written += count;
        }
        return written;
    }
    public Mesh Complete()
    {
        if (!IsComplete) throw new InvalidOperationException("The mesh upload is incomplete.");
        ObjectDisposedException.ThrowIf(_mesh is null, this);
        Mesh mesh = _mesh; _mesh = null; _vertices = default; _indices = default;
        return mesh;
    }
    public void Dispose()
    {
        if (_mesh is { } mesh) _renderer.DestroyMesh(mesh);
        _mesh = null; _vertices = default; _indices = default;
    }
}
