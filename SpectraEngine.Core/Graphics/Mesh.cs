using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// Base class for renderer-owned meshes. Can keep a CPU copy of positions,
/// normals and indices for picking, bounds and debug drawing; see
/// <see cref="MeshCpuAccess"/>.
/// </summary>
public abstract class Mesh : IDisposable
{
    public uint IndexCount { get; protected set; }

    /// <summary>Local-space positions. Empty without CPU access.</summary>
    public IReadOnlyList<Vector3> Positions { get; protected set; } = [];

    /// <summary>Per-vertex normals. Empty without CPU access or a normal attribute.</summary>
    public IReadOnlyList<Vector3> Normals { get; protected set; } = [];

    /// <summary>Indices, three per triangle. Empty without CPU access.</summary>
    public IReadOnlyList<uint> Indices { get; protected set; } = [];

    /// <summary>Local-space bounds. Computed for every mesh, CPU access or not.</summary>
    public Aabb LocalBounds { get; protected set; }

    /// <summary>
    /// Whether <see cref="LocalBounds"/> describes real geometry. Check this, not
    /// <see cref="Positions"/>: a GPU-only mesh has valid bounds and empty arrays.
    /// </summary>
    public bool HasLocalBounds { get; protected set; }

    /// <summary>
    /// Computes <see cref="LocalBounds"/> from the interleaved vertex data and,
    /// under <see cref="MeshCpuAccess.Retained"/>, keeps the CPU copy.
    /// </summary>
    protected void InitializeCpuData(
        ReadOnlySpan<float> vertices,
        ReadOnlySpan<uint> indices,
        ReadOnlySpan<VertexAttribute> attributes,
        MeshCpuAccess cpuAccess)
    {
        // Layout convention: position at location 0, normal at 1.
        int stride = 0;
        int positionOffset = -1;
        int normalOffset = -1;
        for (int i = 0; i < attributes.Length; i++)
        {
            if (attributes[i].Location == 0) positionOffset = stride;
            else if (attributes[i].Location == 1) normalOffset = stride;
            stride += (int)attributes[i].ComponentCount;
        }

        int vertexCount = positionOffset >= 0 && stride > 0 ? vertices.Length / stride : 0;
        if (vertexCount == 0)
        {
            LocalBounds = new Aabb(Vector3.Zero, Vector3.Zero);
            return;
        }

        bool retain = cpuAccess == MeshCpuAccess.Retained;
        Vector3[] positions = retain ? new Vector3[vertexCount] : [];
        Vector3[] normals = retain && normalOffset >= 0 ? new Vector3[vertexCount] : [];

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i < vertexCount; i++)
        {
            int b = i * stride;
            var p = new Vector3(
                vertices[b + positionOffset],
                vertices[b + positionOffset + 1],
                vertices[b + positionOffset + 2]);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);

            if (positions.Length != 0) positions[i] = p;
            if (normals.Length != 0)
                normals[i] = new Vector3(
                    vertices[b + normalOffset],
                    vertices[b + normalOffset + 1],
                    vertices[b + normalOffset + 2]);
        }

        LocalBounds = new Aabb(min, max);
        HasLocalBounds = true;
        if (retain)
        {
            Positions = positions;
            Normals = normals;
            Indices = indices.ToArray();
        }
    }

    // Removes this mesh from its renderer's tracking list. Render thread only.
    internal Action? Unregister { get; set; }

    internal void SetCpuViews(IReadOnlyList<Vector3> positions, IReadOnlyList<Vector3> normals, IReadOnlyList<uint> indices)
    { Positions = positions; Normals = normals; Indices = indices; }

    internal void SetKnownBounds(in Aabb bounds) { LocalBounds = bounds; HasLocalBounds = true; }

    internal virtual void WriteUploadBytes(bool indices, int offset, ReadOnlySpan<byte> bytes) =>
        throw new NotSupportedException("This mesh has no resumable upload path.");

    public abstract void Draw();

    /// <summary>Draws an indexed range. Backends support a signed base-vertex offset.</summary>
    public virtual void DrawRange(MeshDrawRange range)
    {
        if (range != new MeshDrawRange(0, IndexCount)) throw new NotSupportedException("This mesh does not support draw ranges.");
        Draw();
    }

    public virtual void DrawInstancedRange(MeshDrawRange range, InstanceBuffer instances, int instanceCount, int firstInstance = 0)
    {
        if (range != new MeshDrawRange(0, IndexCount)) throw new NotSupportedException("This mesh does not support draw ranges.");
        DrawInstanced(instances, instanceCount, firstInstance);
    }

    /// <summary>
    /// Draws this mesh <paramref name="instanceCount"/> times with per-instance
    /// attributes from <paramref name="instances"/>. The bound shader must
    /// declare those attributes. A count of zero draws nothing.
    /// </summary>
    /// <param name="firstInstance">Index of the first instance to read, so several batches can share one upload.</param>
    public abstract void DrawInstanced(InstanceBuffer instances, int instanceCount, int firstInstance = 0);

    public abstract void Dispose();
}
