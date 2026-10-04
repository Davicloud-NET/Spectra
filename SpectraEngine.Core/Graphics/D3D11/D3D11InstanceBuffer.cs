using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System;

namespace SpectraEngine.Core.Graphics.D3D11;

// Dynamic vertex buffer of per-instance data, plus the input layout that binds
// it in slot 1 beside a mesh's vertices in slot 0.
//
// The layout must be built against the vertex bytecode of the program the
// buffer is drawn under. Built against another shader it creates fine and then
// every instanced draw fails.
internal sealed unsafe class D3D11InstanceBuffer : InstanceBuffer
{
    private static readonly byte[] TexcoordSemantic =
        [(byte)'T', (byte)'E', (byte)'X', (byte)'C', (byte)'O', (byte)'O', (byte)'R', (byte)'D', 0];

    private ComPtr<ID3D11Buffer> _buffer;
    private ComPtr<ID3D11InputLayout> _layout;
    private ComPtr<ID3D11DeviceContext> _context;
    private bool _disposed;

    internal uint Stride { get; }

    internal ID3D11Buffer* Buffer => (ID3D11Buffer*)_buffer.Handle;

    // Covers slot 0 (the mesh) and slot 1 (this buffer).
    internal ID3D11InputLayout* Layout => (ID3D11InputLayout*)_layout.Handle;

    internal D3D11InstanceBuffer(
        ComPtr<ID3D11Device> device,
        int capacityInstances,
        ReadOnlySpan<VertexAttribute> vertexAttributes,
        ReadOnlySpan<VertexAttribute> instanceAttributes,
        int floatsPerInstance,
        ReadOnlyMemory<byte> vsBytecodeForLayout)
    {
        Capacity = capacityInstances;
        FloatsPerInstance = floatsPerInstance;
        Stride = (uint)(floatsPerInstance * sizeof(float));

        var desc = new BufferDesc
        {
            ByteWidth = (uint)(capacityInstances * Stride),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.VertexBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
            MiscFlags = 0,
            StructureByteStride = 0,
        };

        ID3D11Buffer* bufPtr = null;
        SilkMarshal.ThrowHResult(((ID3D11Device*)device.Handle)->CreateBuffer(&desc, null, &bufPtr));
        _buffer = ComOwnership.Own(bufPtr);

        _layout = CreateCombinedLayout(device, vertexAttributes, instanceAttributes, vsBytecodeForLayout);

        ID3D11DeviceContext* ctxPtr = null;
        ((ID3D11Device*)device.Handle)->GetImmediateContext(&ctxPtr);
        _context = ComOwnership.Own(ctxPtr);
    }

    private static ComPtr<ID3D11InputLayout> CreateCombinedLayout(
        ComPtr<ID3D11Device> device,
        ReadOnlySpan<VertexAttribute> vertexAttributes,
        ReadOnlySpan<VertexAttribute> instanceAttributes,
        ReadOnlyMemory<byte> vsBytecode)
    {
        int total = vertexAttributes.Length + instanceAttributes.Length;
        Span<InputElementDesc> elements = stackalloc InputElementDesc[total];

        fixed (byte* semName = TexcoordSemantic)
        {
            int next = 0;

            uint offset = 0;
            for (int i = 0; i < vertexAttributes.Length; i++)
            {
                elements[next++] = new InputElementDesc
                {
                    SemanticName = semName,
                    SemanticIndex = vertexAttributes[i].Location,
                    Format = FormatFor(vertexAttributes[i].ComponentCount),
                    InputSlot = VertexAttribute.VertexSlot,
                    AlignedByteOffset = offset,
                    InputSlotClass = InputClassification.PerVertexData,
                    InstanceDataStepRate = 0,
                };
                offset += vertexAttributes[i].ComponentCount * sizeof(float);
            }

            // Offsets are per slot.
            offset = 0;
            for (int i = 0; i < instanceAttributes.Length; i++)
            {
                elements[next++] = new InputElementDesc
                {
                    SemanticName = semName,
                    SemanticIndex = instanceAttributes[i].Location,
                    Format = FormatFor(instanceAttributes[i].ComponentCount),
                    InputSlot = VertexAttribute.InstanceSlot,
                    AlignedByteOffset = offset,
                    // With PerVertexData and step 0 every instance draws on
                    // top of the first, with no error.
                    InputSlotClass = InputClassification.PerInstanceData,
                    InstanceDataStepRate = 1,
                };
                offset += instanceAttributes[i].ComponentCount * sizeof(float);
            }

            using var pin = vsBytecode.Pin();
            ID3D11InputLayout* layoutPtr = null;
            fixed (InputElementDesc* pElements = elements)
            {
                SilkMarshal.ThrowHResult(((ID3D11Device*)device.Handle)->CreateInputLayout(
                    pElements, (uint)total, pin.Pointer, (nuint)vsBytecode.Length, &layoutPtr));
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
        _ => throw new ArgumentOutOfRangeException(
            nameof(componentCount), $"Unsupported component count {componentCount}"),
    };

    /// <inheritdoc/>
    // WriteDiscard on the frame's first write, WriteNoOverwrite after, so an
    // append never changes what an earlier draw reads.
    public override int Append(ReadOnlySpan<float> data, int instanceCount)
    {
        ValidateUpdate(data, instanceCount);
        int first = Cursor;
        if (instanceCount == 0)
            return first;

        var ctx = (ID3D11DeviceContext*)_context.Handle;
        MappedSubresource mapped;
        SilkMarshal.ThrowHResult(ctx->Map(
            (ID3D11Resource*)_buffer.Handle, 0,
            first == 0 ? Map.WriteDiscard : Map.WriteNoOverwrite, 0, &mapped));

        byte* dst = (byte*)mapped.PData + (long)first * Stride;
        fixed (float* src = data)
            System.Buffer.MemoryCopy(src, dst, (long)(Capacity - first) * Stride, (long)data.Length * sizeof(float));

        ctx->Unmap((ID3D11Resource*)_buffer.Handle, 0);

        Cursor = first + instanceCount;
        return first;
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        ComOwnership.Release(ref _layout);
        ComOwnership.Release(ref _buffer);
        ComOwnership.Release(ref _context);
    }
}
