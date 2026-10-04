using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using System;

namespace SpectraEngine.Core.Graphics.D3D12;

// Per-instance data on the upload heap, kept mapped while in use.
// The input layout is part of the PSO on D3D12, so an instanced draw needs a
// combined two-slot layout to key its pipeline on.
internal sealed unsafe class D3D12InstanceBuffer : InstanceBuffer
{
    private readonly D3D12Renderer _renderer;
    private ComPtr<ID3D12Resource> _buffer;
    private uint _bufferCapacity;
    private bool _needsStorage = true;
    private void* _mapped;
    private bool _disposed;

    // Slot 0 + slot 1, for the PSO key.
    internal D3D12VertexLayout CombinedLayout { get; }

    // Binds this buffer into slot 1.
    internal VertexBufferView View { get; private set; }

    internal D3D12InstanceBuffer(
        D3D12Renderer renderer,
        int capacityInstances,
        ReadOnlySpan<VertexAttribute> vertexAttributes,
        ReadOnlySpan<VertexAttribute> instanceAttributes,
        int floatsPerInstance)
    {
        Capacity = capacityInstances;
        FloatsPerInstance = floatsPerInstance;

        _renderer = renderer;
        CombinedLayout = BuildCombinedLayout(vertexAttributes, instanceAttributes);
    }

    protected override void OnBeginFrame() => _needsStorage = true;
    private static D3D12VertexLayout BuildCombinedLayout(
        ReadOnlySpan<VertexAttribute> vertexAttributes,
        ReadOnlySpan<VertexAttribute> instanceAttributes)
    {
        var elements = new D3D12VertexLayout.Element[vertexAttributes.Length + instanceAttributes.Length];
        int next = 0;

        uint offset = 0;
        for (int i = 0; i < vertexAttributes.Length; i++)
        {
            elements[next++] = new D3D12VertexLayout.Element(
                vertexAttributes[i].Location, FormatFor(vertexAttributes[i].ComponentCount), offset,
                VertexAttribute.VertexSlot, PerInstance: false);
            offset += vertexAttributes[i].ComponentCount * sizeof(float);
        }

        // The layout carries slot 0's stride; slot 1's is on the buffer view.
        uint vertexStride = offset;

        // Offsets are per slot.
        offset = 0;
        for (int i = 0; i < instanceAttributes.Length; i++)
        {
            elements[next++] = new D3D12VertexLayout.Element(
                instanceAttributes[i].Location, FormatFor(instanceAttributes[i].ComponentCount), offset,
                VertexAttribute.InstanceSlot, PerInstance: true);
            offset += instanceAttributes[i].ComponentCount * sizeof(float);
        }

        return new D3D12VertexLayout(elements, vertexStride);
    }

    private static Format FormatFor(uint componentCount) => componentCount switch
    {
        1 => Format.FormatR32Float,
        2 => Format.FormatR32G32Float,
        3 => Format.FormatR32G32B32Float,
        4 => Format.FormatR32G32B32A32Float,
        _ => throw new ArgumentOutOfRangeException(
            nameof(componentCount), $"Unsupported component count {componentCount}"),
    };

    /// <inheritdoc/>
    // Append, never overwrite: the frame is one command list submitted at the
    // end, so rewriting a range changes what already recorded draws will read.
    public override int Append(ReadOnlySpan<float> data, int instanceCount)
    {
        ValidateUpdate(data, instanceCount);
        int first = Cursor;
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (instanceCount == 0)
            return first;

        if (_needsStorage)
        {
            uint stride = checked((uint)FloatsPerInstance * sizeof(float));
            uint bytes = checked((uint)Capacity * stride);
            uint capacity = D3D12Renderer.MeshBufferBucket(bytes);
            // A fresh pooled buffer per frame. The pool keeps the previous one
            // alive while submitted commands still read it.
            var buffer = _renderer.RentMeshBuffer(capacity);
            void* mapped = null;
            var range = new Silk.NET.Direct3D12.Range();
            try { SilkMarshal.ThrowHResult(((ID3D12Resource*)buffer.Handle)->Map(0, &range, &mapped)); }
            catch { _renderer.ReturnMeshBuffer(capacity, buffer); throw; }
            ReleaseStorage();
            _buffer = buffer;
            _bufferCapacity = capacity;
            _mapped = mapped;
            _needsStorage = false;
            View = new VertexBufferView
            {
                BufferLocation = ((ID3D12Resource*)buffer.Handle)->GetGPUVirtualAddress(),
                SizeInBytes = bytes,
                StrideInBytes = stride,
            };
        }
        byte* dst = (byte*)_mapped + (long)first * View.StrideInBytes;
        fixed (float* src = data)
        {
            System.Buffer.MemoryCopy(
                src, dst,
                (long)(Capacity - first) * View.StrideInBytes,
                (long)data.Length * sizeof(float));
        }

        Cursor = first + instanceCount;
        return first;
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        ReleaseStorage();
    }

    private void ReleaseStorage()
    {
        if (_buffer.Handle is null) return;
        if (_mapped is not null) ((ID3D12Resource*)_buffer.Handle)->Unmap(0, null);
        _mapped = null;
        _renderer.ReturnMeshBuffer(_bufferCapacity, _buffer);
        _buffer = default;
    }
}
