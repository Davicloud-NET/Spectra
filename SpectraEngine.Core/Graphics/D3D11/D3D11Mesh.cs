using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System;

namespace SpectraEngine.Core.Graphics.D3D11;

internal sealed unsafe class D3D11Mesh : Mesh
{
    private static readonly byte[] TexcoordSemantic = new byte[] { (byte)'T', (byte)'E', (byte)'X', (byte)'C', (byte)'O', (byte)'O', (byte)'R', (byte)'D', 0 };

    private readonly ComPtr<ID3D11Buffer> _vertexBuffer;
    private readonly ComPtr<ID3D11Buffer> _indexBuffer;
    private readonly ComPtr<ID3D11InputLayout> _inputLayout;
    private readonly uint _stride;
    private bool _disposed;

    private readonly ComPtr<ID3D11DeviceContext> _context;

    private D3D11Mesh(
        ComPtr<ID3D11DeviceContext> context,
        ComPtr<ID3D11Buffer> vb,
        ComPtr<ID3D11Buffer> ib,
        ComPtr<ID3D11InputLayout> layout,
        uint stride,
        uint indexCount)
    {
        _context = context;
        _vertexBuffer = vb;
        _indexBuffer = ib;
        _inputLayout = layout;
        _stride = stride;
        IndexCount = indexCount;
    }

    internal static D3D11Mesh Create(
        ComPtr<ID3D11Device> device,
        ReadOnlySpan<float> vertices,
        ReadOnlySpan<uint> indices,
        ReadOnlySpan<VertexAttribute> attributes,
        ReadOnlyMemory<byte> vsBytecodeForLayout,
        MeshCpuAccess cpuAccess, bool deferred = false, Bsp.Aabb? knownBounds = null)
    {
        ComPtr<ID3D11Buffer> vb = default, ib = default;
        ComPtr<ID3D11InputLayout> layout = default;
        ComPtr<ID3D11DeviceContext> ctx = default;
        try
        {
            vb = CreateBuffer(device, vertices, BindFlag.VertexBuffer, deferred);
            ib = CreateBuffer(device, indices, BindFlag.IndexBuffer, deferred);
            layout = CreateInputLayout(device, attributes, vsBytecodeForLayout);

            uint stride = 0;
            for (int i = 0; i < attributes.Length; i++)
                stride += attributes[i].ComponentCount * sizeof(float);

            // GetImmediateContext returns a counted reference.
            ID3D11DeviceContext* ctxPtr = null;
            ((ID3D11Device*)device.Handle)->GetImmediateContext(&ctxPtr);
            ctx = ComOwnership.Own(ctxPtr);

            var mesh = new D3D11Mesh(ctx, vb, ib, layout, stride, (uint)indices.Length);
            if (knownBounds is { } bounds && cpuAccess == MeshCpuAccess.None) mesh.SetKnownBounds(bounds);
            else mesh.InitializeCpuData(vertices, indices, attributes, cpuAccess);
            return mesh;
        }
        catch
        {
            ctx.Dispose(); layout.Dispose(); ib.Dispose(); vb.Dispose();
            throw;
        }
    }

    private static ComPtr<ID3D11Buffer> CreateBuffer<T>(ComPtr<ID3D11Device> device, ReadOnlySpan<T> data, BindFlag bind, bool deferred) where T : unmanaged
    {
        var desc = new BufferDesc
        {
            ByteWidth = (uint)(data.Length * sizeof(T)),
            Usage = deferred ? Usage.Default : Usage.Immutable,
            BindFlags = (uint)bind,
            CPUAccessFlags = 0,
            MiscFlags = 0,
            StructureByteStride = 0,
        };

        ID3D11Buffer* bufPtr = null;
        fixed (T* p = data)
        {
            var init = new SubresourceData { PSysMem = p };
            SilkMarshal.ThrowHResult(((ID3D11Device*)device.Handle)->CreateBuffer(&desc, deferred ? null : &init, &bufPtr));
        }
        return ComOwnership.Own(bufPtr);
    }

    private static ComPtr<ID3D11InputLayout> CreateInputLayout(
        ComPtr<ID3D11Device> device,
        ReadOnlySpan<VertexAttribute> attributes,
        ReadOnlyMemory<byte> vsBytecode)
    {
        // SpectraShade emits vertex inputs as TEXCOORDn, n being the Location.
        Span<InputElementDesc> elements = stackalloc InputElementDesc[attributes.Length];

        fixed (byte* semName = TexcoordSemantic)
        {
            uint offset = 0;
            for (int i = 0; i < attributes.Length; i++)
            {
                elements[i] = new InputElementDesc
                {
                    SemanticName = semName,
                    SemanticIndex = attributes[i].Location,
                    Format = FormatFor(attributes[i].ComponentCount),
                    InputSlot = 0,
                    AlignedByteOffset = offset,
                    InputSlotClass = InputClassification.PerVertexData,
                    InstanceDataStepRate = 0,
                };
                offset += attributes[i].ComponentCount * sizeof(float);
            }

            using var bytecodePin = vsBytecode.Pin();
            ID3D11InputLayout* layoutPtr = null;
            fixed (InputElementDesc* pElements = elements)
            {
                SilkMarshal.ThrowHResult(((ID3D11Device*)device.Handle)->CreateInputLayout(
                    pElements,
                    (uint)attributes.Length,
                    bytecodePin.Pointer,
                    (nuint)vsBytecode.Length,
                    &layoutPtr));
            }
            return ComOwnership.Own(layoutPtr);
        }
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
        var ctx = (ID3D11DeviceContext*)_context.Handle;
        ID3D11Buffer* vb = (ID3D11Buffer*)_vertexBuffer.Handle;
        uint stride = _stride;
        uint offset = 0;
        ctx->IASetInputLayout((ID3D11InputLayout*)_inputLayout.Handle);
        ctx->IASetVertexBuffers(0, 1, &vb, &stride, &offset);
        ctx->IASetIndexBuffer((ID3D11Buffer*)_indexBuffer.Handle, Silk.NET.DXGI.Format.FormatR32Uint, 0);
        ctx->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyTrianglelist);
        ctx->DrawIndexed(range.IndexCount, range.FirstIndex, range.BaseVertex);
    }

    /// <inheritdoc/>
    public override void DrawInstanced(InstanceBuffer instances, int instanceCount, int firstInstance = 0) =>
        DrawInstancedRange(new(0, IndexCount), instances, instanceCount, firstInstance);

    public override unsafe void DrawInstancedRange(MeshDrawRange range, InstanceBuffer instances, int instanceCount, int firstInstance = 0)
    {
        ArgumentNullException.ThrowIfNull(instances);
        if (instanceCount <= 0)
            return;

        if (instances is not D3D11InstanceBuffer d3d)
            throw new ArgumentException("Instance buffer belongs to another backend.", nameof(instances));

        var ctx = (ID3D11DeviceContext*)_context.Handle;

        // Uses the instance buffer's two-slot layout, not this mesh's.
        ID3D11Buffer** buffers = stackalloc ID3D11Buffer*[2];
        buffers[0] = (ID3D11Buffer*)_vertexBuffer.Handle;
        buffers[1] = d3d.Buffer;

        uint* strides = stackalloc uint[2] { _stride, d3d.Stride };
        uint* offsets = stackalloc uint[2] { 0, 0 };

        ctx->IASetInputLayout(d3d.Layout);
        ctx->IASetVertexBuffers(0, 2, buffers, strides, offsets);
        ctx->IASetIndexBuffer((ID3D11Buffer*)_indexBuffer.Handle, Format.FormatR32Uint, 0);
        ctx->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyTrianglelist);
        ctx->DrawIndexedInstanced(range.IndexCount, (uint)instanceCount, range.FirstIndex, range.BaseVertex, (uint)firstInstance);

        // Unbind slot 1. Left bound, the debug layer warns on every later
        // non-instanced draw.
        ID3D11Buffer* none = null;
        uint zero = 0;
        ctx->IASetVertexBuffers(VertexAttribute.InstanceSlot, 1, &none, &zero, &zero);
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _inputLayout.Dispose();
        _indexBuffer.Dispose();
        _vertexBuffer.Dispose();
        _context.Dispose();
    }

    internal override void WriteUploadBytes(bool indices, int offset, ReadOnlySpan<byte> bytes)
    {
        var box = new Box((uint)offset, 0, 0, (uint)(offset + bytes.Length), 1, 1);
        fixed (byte* source = bytes)
            ((ID3D11DeviceContext*)_context.Handle)->UpdateSubresource(
                (ID3D11Resource*)(indices ? _indexBuffer.Handle : _vertexBuffer.Handle), 0, &box, source, 0, 0);
    }
}
