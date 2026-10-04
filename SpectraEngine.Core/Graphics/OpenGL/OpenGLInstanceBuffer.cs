using Silk.NET.OpenGL;
using System;

namespace SpectraEngine.Core.Graphics.OpenGL;

// Carries its attribute layout: GL binds attribute pointers into the mesh's
// VAO, so OpenGLMesh needs them when it wires this buffer in.
internal sealed class OpenGLInstanceBuffer : InstanceBuffer
{
    private readonly GL _gl;
    private bool _disposed;

    internal uint Handle { get; }

    internal VertexAttribute[] Attributes { get; }

    // In bytes.
    internal uint Stride { get; }

    // Unique per buffer. Meshes key their VAO wiring on this because GL
    // reuses buffer names.
    internal uint Generation { get; }

    private static uint _nextGeneration = 1;

    internal OpenGLInstanceBuffer(GL gl, int capacityInstances, ReadOnlySpan<VertexAttribute> attributes, int floatsPerInstance)
    {
        _gl = gl;
        Capacity = capacityInstances;
        FloatsPerInstance = floatsPerInstance;
        Attributes = attributes.ToArray();
        Stride = (uint)(floatsPerInstance * sizeof(float));
        Generation = _nextGeneration++;

        Handle = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, Handle);
        unsafe
        {
            gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(capacityInstances * Stride),
                null,
                BufferUsageARB.DynamicDraw);
        }
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    /// <inheritdoc/>
    // Orphan once per frame so the driver does not stall on in-flight draws.
    // Not per append: that would drop the frame's earlier appends.
    protected override unsafe void OnBeginFrame()
    {
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, Handle);
        _gl.BufferData(
            BufferTargetARB.ArrayBuffer, (nuint)(Capacity * Stride), null, BufferUsageARB.DynamicDraw);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    /// <inheritdoc/>
    public override unsafe int Append(ReadOnlySpan<float> data, int instanceCount)
    {
        ValidateUpdate(data, instanceCount);
        int first = Cursor;
        if (instanceCount == 0)
            return first;

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, Handle);
        fixed (float* p = data)
        {
            _gl.BufferSubData(
                BufferTargetARB.ArrayBuffer,
                (nint)((nuint)first * Stride),
                (nuint)(data.Length * sizeof(float)),
                p);
        }
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

        Cursor = first + instanceCount;
        return first;
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _gl.DeleteBuffer(Handle);
    }
}
