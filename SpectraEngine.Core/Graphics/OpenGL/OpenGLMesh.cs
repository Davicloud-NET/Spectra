using Silk.NET.OpenGL;
using System;

namespace SpectraEngine.Core.Graphics.OpenGL;

internal sealed class OpenGLMesh : Mesh
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private bool _disposed;

    private OpenGLMesh(GL gl, uint vao, uint vbo, uint ebo, uint indexCount)
    {
        _gl = gl;
        _vao = vao;
        _vbo = vbo;
        _ebo = ebo;
        IndexCount = indexCount;
    }

    internal static unsafe OpenGLMesh Create(GL gl, ReadOnlySpan<float> vertices, ReadOnlySpan<uint> indices,
        ReadOnlySpan<VertexAttribute> attributes, MeshCpuAccess cpuAccess, bool deferred = false, Bsp.Aabb? knownBounds = null)
    {
        uint vao = gl.GenVertexArray();
        uint vbo = gl.GenBuffer();
        uint ebo = gl.GenBuffer();

        gl.BindVertexArray(vao);

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        fixed (float* v = vertices)
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), deferred ? null : v, BufferUsageARB.StaticDraw);
        }

        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        fixed (uint* i = indices)
        {
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), deferred ? null : i, BufferUsageARB.StaticDraw);
        }

        uint stride = 0;
        for (int i = 0; i < attributes.Length; i++)
            stride += attributes[i].ComponentCount * sizeof(float);

        uint offset = 0;
        for (int i = 0; i < attributes.Length; i++)
        {
            var attr = attributes[i];
            gl.VertexAttribPointer(attr.Location, (int)attr.ComponentCount, VertexAttribPointerType.Float, false, stride, (void*)offset);
            gl.EnableVertexAttribArray(attr.Location);
            offset += attr.ComponentCount * sizeof(float);
        }

        gl.BindVertexArray(0);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);

        var mesh = new OpenGLMesh(gl, vao, vbo, ebo, (uint)indices.Length);
        if (knownBounds is { } bounds && cpuAccess == MeshCpuAccess.None) mesh.SetKnownBounds(bounds);
        else mesh.InitializeCpuData(vertices, indices, attributes, cpuAccess);
        return mesh;
    }

    public override void Draw() => DrawRange(new(0, IndexCount));

    public override unsafe void DrawRange(MeshDrawRange range)
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawElementsBaseVertex(PrimitiveType.Triangles, range.IndexCount, DrawElementsType.UnsignedInt, (void*)((nuint)range.FirstIndex * sizeof(uint)), range.BaseVertex);
    }

    // Instance buffer currently wired into the VAO, by generation (GL recycles
    // names). Zero means none.
    private uint _wiredInstanceGeneration;
    private int _wiredFirstInstance;

    /// <inheritdoc/>
    public override void DrawInstanced(InstanceBuffer instances, int instanceCount, int firstInstance = 0) =>
        DrawInstancedRange(new(0, IndexCount), instances, instanceCount, firstInstance);

    public override unsafe void DrawInstancedRange(MeshDrawRange range, InstanceBuffer instances, int instanceCount, int firstInstance = 0)
    {
        ArgumentNullException.ThrowIfNull(instances);
        if (instanceCount <= 0)
            return;

        if (instances is not OpenGLInstanceBuffer gl)
            throw new ArgumentException("Instance buffer belongs to another backend.", nameof(instances));

        _gl.BindVertexArray(_vao);

        // GL keeps attribute pointers and divisors in the VAO, so wire them
        // only when the buffer or offset changes. GL 3.3 has no BaseInstance,
        // so firstInstance is folded into the pointer offset.
        if (_wiredInstanceGeneration != gl.Generation || _wiredFirstInstance != firstInstance)
        {
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, gl.Handle);

            uint offset = (uint)firstInstance * gl.Stride;
            foreach (VertexAttribute attr in gl.Attributes)
            {
                _gl.VertexAttribPointer(
                    attr.Location, (int)attr.ComponentCount,
                    VertexAttribPointerType.Float, false, gl.Stride, (void*)offset);
                _gl.EnableVertexAttribArray(attr.Location);

                // Without the divisor the attribute advances per vertex.
                _gl.VertexAttribDivisor(attr.Location, 1);

                offset += attr.ComponentCount * sizeof(float);
            }

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            _wiredInstanceGeneration = gl.Generation;
            _wiredFirstInstance = firstInstance;
        }

        _gl.DrawElementsInstancedBaseVertex(
            PrimitiveType.Triangles, range.IndexCount, DrawElementsType.UnsignedInt, (void*)((nuint)range.FirstIndex * sizeof(uint)), (uint)instanceCount, range.BaseVertex);
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _gl.DeleteBuffer(_ebo);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }

    internal override unsafe void WriteUploadBytes(bool indices, int offset, ReadOnlySpan<byte> bytes)
    {
        // CopyWriteBuffer leaves VAO element bindings intact.
        _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, indices ? _ebo : _vbo);
        fixed (byte* source = bytes)
            _gl.BufferSubData(BufferTargetARB.CopyWriteBuffer, (nint)offset, (nuint)bytes.Length, source);
        _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, 0);
    }
}
