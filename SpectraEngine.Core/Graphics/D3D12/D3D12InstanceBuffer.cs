using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using System;

namespace SpectraEngine.Core.Graphics.D3D12;

/// <summary>
/// An upload-heap buffer of per-instance data, its vertex buffer view, and the
/// combined two-slot layout the PSO is compiled against.
/// </summary>
/// <remarks>
/// <para>
/// <b>D3D12 has no standalone input layout object: it lives inside the PSO.</b>
/// So an instanced draw does not bind a different layout, it selects a different
/// pipeline, and the layout has to reach <c>GetPso</c> as part of the key. That
/// is why the combined layout is built here and handed over at draw time, and
/// why <c>D3D12VertexLayout.Element</c> carries the slot and the rate: the PSO
/// cache compares elements structurally, so an instanced layout produces a
/// distinct pipeline without the cache having to learn a new concept.
/// </para>
/// <para>
/// <b>Persistently mapped, unlike a mesh.</b> A mesh is written once at
/// creation; this is rewritten every frame, and mapping and unmapping an upload
/// resource per frame is pure overhead on a heap that is CPU-visible for its
/// whole life. The read range stays empty because nothing reads it back.
/// </para>
/// </remarks>
internal sealed unsafe class D3D12InstanceBuffer : InstanceBuffer
{
    private readonly D3D12Renderer _renderer;
    private ComPtr<ID3D12Resource> _buffer;
    private uint _bufferCapacity;
    private bool _needsStorage = true;
    private void* _mapped;
    private bool _disposed;

    /// <summary>The combined slot-0 + slot-1 layout, for the PSO key.</summary>
    internal D3D12VertexLayout CombinedLayout { get; }

    /// <summary>The view binding this buffer into slot 1.</summary>
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

        // The vertex stride, kept as the layout's own: slot 1's stride travels
        // on the vertex buffer view instead, and a layout has room for one.
        uint vertexStride = offset;

        // Offsets restart at zero, because they are offsets within slot 1.
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
    /// <remarks>
    /// <b>Appending at the cursor is the whole reason this API is not a plain
    /// overwrite.</b> The buffer is persistently mapped with no renaming and no
    /// versioning, and the frame is a single command list submitted at the end,
    /// so a second write at offset zero retroactively changes what every draw
    /// already recorded will read. Distinct ranges are what make several passes
    /// (or four shadow cascades) able to share one buffer in one frame.
    /// </remarks>
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
            // Every BeginFrame/Update gets an immutable buffer version. The
            // completed-buffer pool makes steady frames cheap and keeps both
            // submitted and currently recorded commands' previous versions live.
            // Unlike a transient ring address this also supports callers that
            // upload between frames, then draw unchanged data in later frames.
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
