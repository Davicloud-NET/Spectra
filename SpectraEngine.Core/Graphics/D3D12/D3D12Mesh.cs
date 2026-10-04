using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using System;

namespace SpectraEngine.Core.Graphics.D3D12;

// Vertex and index buffers on the upload heap. Slower for the GPU to read
// than a default heap, but needs no copy queue. The layout travels with the
// mesh because input layouts are part of the PSO on D3D12.
internal sealed unsafe class D3D12Mesh : Mesh
{
    private readonly D3D12Renderer _renderer;
    private ComPtr<ID3D12Resource> _vertexBuffer;
    private ComPtr<ID3D12Resource> _indexBuffer;

    // Pool bucket sizes. The buffers must be returned under these, not the data size.
    private readonly uint _vertexCapacity;
    private readonly uint _indexCapacity;
    private readonly VertexBufferView _vbView;
    private readonly IndexBufferView _ibView;
    private bool _disposed;

    internal D3D12VertexLayout Layout { get; }

    internal D3D12Mesh(
        D3D12Renderer renderer,
        ReadOnlySpan<float> vertices,
        ReadOnlySpan<uint> indices,
        ReadOnlySpan<VertexAttribute> attributes,
        MeshCpuAccess cpuAccess, bool deferred = false, Bsp.Aabb? knownBounds = null)
    {
        _renderer = renderer;

        var elements = new D3D12VertexLayout.Element[attributes.Length];
        uint offset = 0;
        for (int i = 0; i < attributes.Length; i++)
        {
            elements[i] = new D3D12VertexLayout.Element(
                attributes[i].Location,
                FormatFor(attributes[i].ComponentCount),
                offset);
            offset += attributes[i].ComponentCount * sizeof(float);
        }
        Layout = new D3D12VertexLayout(elements, offset);

        uint vbBytes = (uint)(vertices.Length * sizeof(float));
        uint ibBytes = (uint)(indices.Length * sizeof(uint));
        // Rented: CreateCommittedResource costs about 0.5 ms a call, and chunk
        // meshes are recreated every frame a world brush moves.
        _vertexCapacity = D3D12Renderer.MeshBufferBucket(vbBytes);
        _indexCapacity = D3D12Renderer.MeshBufferBucket(ibBytes);
        try
        {
            _vertexBuffer = renderer.RentMeshBuffer(_vertexCapacity);
            _indexBuffer = renderer.RentMeshBuffer(_indexCapacity);
            if (!deferred)
            {
                CopyInto(_vertexBuffer, vertices, vbBytes);
                CopyInto(_indexBuffer, indices, ibBytes);
            }

            _vbView = new VertexBufferView
            {
                BufferLocation = ((ID3D12Resource*)_vertexBuffer.Handle)->GetGPUVirtualAddress(),
                SizeInBytes = vbBytes,
                StrideInBytes = Layout.StrideBytes,
            };
            _ibView = new IndexBufferView
            {
                BufferLocation = ((ID3D12Resource*)_indexBuffer.Handle)->GetGPUVirtualAddress(),
                SizeInBytes = ibBytes,
                Format = Format.FormatR32Uint,
            };

            IndexCount = (uint)indices.Length;
            if (knownBounds is { } bounds && cpuAccess == MeshCpuAccess.None) SetKnownBounds(bounds);
            else InitializeCpuData(vertices, indices, attributes, cpuAccess);
        }
        catch
        {
            if (_indexBuffer.Handle is not null) renderer.ReturnMeshBuffer(_indexCapacity, _indexBuffer);
            if (_vertexBuffer.Handle is not null) renderer.ReturnMeshBuffer(_vertexCapacity, _vertexBuffer);
            throw;
        }
    }

    private static void CopyInto<T>(ComPtr<ID3D12Resource> buffer, ReadOnlySpan<T> data, uint byteSize) where T : unmanaged
    {
        var res = (ID3D12Resource*)buffer.Handle;
        void* mapped = null;
        var readRange = new Silk.NET.Direct3D12.Range { Begin = 0, End = 0 };
        SilkMarshal.ThrowHResult(res->Map(0, &readRange, &mapped));
        fixed (T* src = data)
        {
            System.Buffer.MemoryCopy(src, mapped, byteSize, byteSize);
        }
        res->Unmap(0, null);
    }

    private static Format FormatFor(uint componentCount) => componentCount switch
    {
        1 => Format.FormatR32Float,
        2 => Format.FormatR32G32Float,
        3 => Format.FormatR32G32B32Float,
        4 => Format.FormatR32G32B32A32Float,
        _ => throw new ArgumentOutOfRangeException(nameof(componentCount), $"Unsupported component count {componentCount}"),
    };

    public override void Draw() => DrawRange(new(0, IndexCount));

    public override unsafe void DrawRange(MeshDrawRange range)
    {
        var list = _renderer.CurrentList;
        var program = _renderer.CurrentProgram;
        if (list is null || program is null) return;

        // Target formats come from the open pass: a PSO is only valid for the
        // formats it was compiled against.
        var target = _renderer.CurrentTargetState;
        var pso = program.GetPso(
            Layout, _renderer.CurrentFillMode, PrimitiveTopologyType.Triangle,
            _renderer.CurrentDepthMode, BlendMode.Opaque, _renderer.CurrentDepthBias, in target);
        _renderer.BindPipelineState(list, pso);
        _renderer.BindTopology(list, D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        fixed (VertexBufferView* vb = &_vbView)
        {
            list->IASetVertexBuffers(0, 1, vb);
        }
        fixed (IndexBufferView* ib = &_ibView)
        {
            list->IASetIndexBuffer(ib);
        }
        list->DrawIndexedInstanced(range.IndexCount, 1, range.FirstIndex, range.BaseVertex, 0);
    }

    /// <inheritdoc/>
    public override void DrawInstanced(InstanceBuffer instances, int instanceCount, int firstInstance = 0) =>
        DrawInstancedRange(new(0, IndexCount), instances, instanceCount, firstInstance);

    public override unsafe void DrawInstancedRange(MeshDrawRange range, InstanceBuffer instances, int instanceCount, int firstInstance = 0)
    {
        ArgumentNullException.ThrowIfNull(instances);
        if (instanceCount <= 0)
            return;

        if (instances is not D3D12InstanceBuffer d3d)
            throw new ArgumentException("Instance buffer belongs to another backend.", nameof(instances));

        var list = _renderer.CurrentList;
        var program = _renderer.CurrentProgram;
        if (list is null || program is null) return;

        var target = _renderer.CurrentTargetState;

        // The combined layout, not the mesh's own: an instanced draw is a different PSO.
        var pso = program.GetPso(
            d3d.CombinedLayout, _renderer.CurrentFillMode, PrimitiveTopologyType.Triangle,
            _renderer.CurrentDepthMode, BlendMode.Opaque, _renderer.CurrentDepthBias, in target);
        _renderer.BindPipelineState(list, pso);
        _renderer.BindTopology(list, D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);

        VertexBufferView* views = stackalloc VertexBufferView[2];
        views[0] = _vbView;
        views[1] = d3d.View;
        list->IASetVertexBuffers(0, 2, views);

        fixed (IndexBufferView* ib = &_ibView)
        {
            list->IASetIndexBuffer(ib);
        }
        list->DrawIndexedInstanced(range.IndexCount, (uint)instanceCount, range.FirstIndex, range.BaseVertex, (uint)firstInstance);
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _renderer.ReturnMeshBuffer(_indexCapacity, _indexBuffer);
        _renderer.ReturnMeshBuffer(_vertexCapacity, _vertexBuffer);
        _indexBuffer = default;
        _vertexBuffer = default;
    }

    internal override void WriteUploadBytes(bool indices, int offset, ReadOnlySpan<byte> bytes)
    {
        var resource = (ID3D12Resource*)(indices ? _indexBuffer.Handle : _vertexBuffer.Handle);
        void* mapped = null;
        var noRead = new Silk.NET.Direct3D12.Range(0, 0);
        SilkMarshal.ThrowHResult(resource->Map(0, &noRead, &mapped));
        bytes.CopyTo(new Span<byte>((byte*)mapped + offset, bytes.Length));
        var written = new Silk.NET.Direct3D12.Range((nuint)offset, (nuint)(offset + bytes.Length));
        resource->Unmap(0, &written);
    }
}
