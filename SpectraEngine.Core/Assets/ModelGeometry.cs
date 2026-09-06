using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Core.Assets;

/// <summary>Immutable shared upload backing. Ownership transfers from importer to asset manager.</summary>
public sealed class ModelGeometry
{
    public float[] Vertices { get; }
    public uint[] Indices { get; }
    public long ByteLength => (long)Vertices.Length * sizeof(float) + (long)Indices.Length * sizeof(uint);
    public int VertexCount => Vertices.Length / ModelVertexLayout.FloatsPerVertex;
    public ModelGeometry(float[] vertices, uint[] indices)
    { Vertices = vertices; Indices = indices; }

    internal void SharePickingData(Mesh mesh) => mesh.SetCpuViews(
        new VectorView(Vertices, ModelVertexLayout.PositionOffset),
        new VectorView(Vertices, ModelVertexLayout.NormalOffset), Indices);

    internal PickingData PreparePickingData()
    {
        var positions = new Vector3[VertexCount];
        var normals = new Vector3[VertexCount];
        for (int i = 0; i < VertexCount; i++)
        {
            int start = i * ModelVertexLayout.FloatsPerVertex;
            positions[i] = new(Vertices[start], Vertices[start + 1], Vertices[start + 2]);
            normals[i] = new(Vertices[start + 3], Vertices[start + 4], Vertices[start + 5]);
        }
        return new(positions, normals, Indices);
    }

    internal sealed record PickingData(Vector3[] Positions, Vector3[] Normals, uint[] Indices)
    {
        internal long AdditionalBytes => (long)(Positions.Length + Normals.Length) * 12;
        internal void Apply(Mesh mesh) => mesh.SetCpuViews(Positions, Normals, Indices);
    }

    private sealed class VectorView(float[] vertices, int offset) : IReadOnlyList<Vector3>
    {
        public int Count => vertices.Length / ModelVertexLayout.FloatsPerVertex;
        public Vector3 this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                int start = index * ModelVertexLayout.FloatsPerVertex + offset;
                return new(vertices[start], vertices[start + 1], vertices[start + 2]);
            }
        }
        public IEnumerator<Vector3> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>Full preserves import arrays; Picking retains geometry needed by raycasts; GpuOnly retains metadata.</summary>
public enum ModelCpuRetention { Full, Picking, GpuOnly }
