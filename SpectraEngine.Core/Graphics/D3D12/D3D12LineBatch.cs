using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using System;

namespace SpectraEngine.Core.Graphics.D3D12;

// Draws interleaved position + colour vertices as a line list. Vertices go
// into the renderer's frame upload ring, so the batch owns no buffer.
internal sealed unsafe class D3D12LineBatch : IDisposable
{
    private const int FloatsPerVertex = 6;
    private const uint StrideBytes = FloatsPerVertex * sizeof(float);

    private static readonly D3D12VertexLayout LineLayout = new(
        [
            new D3D12VertexLayout.Element(0, Format.FormatR32G32B32Float, 0),
            new D3D12VertexLayout.Element(1, Format.FormatR32G32B32Float, 12),
        ],
        StrideBytes);

    private readonly D3D12Renderer _renderer;
    private readonly D3D12ShaderProgram _shader;

    public D3D12LineBatch(D3D12Renderer renderer, D3D12ShaderProgram shader)
    {
        _renderer = renderer;
        _shader = shader;
    }

    // depth and blend go into the PSO key: both are pipeline state on D3D12.
    // program null means the batch's own shader.
    public void Draw(
        ReadOnlySpan<float> interleaved,
        uint vertexCount,
        DepthMode depth,
        D3D12ShaderProgram? program = null,
        BlendMode blend = BlendMode.Opaque)
    {
        if (vertexCount == 0) return;
        var list = _renderer.CurrentList;
        if (list is null) return;

        uint byteSize = vertexCount * StrideBytes;
        var slice = _renderer.AllocUpload(byteSize, 4);
        fixed (float* src = interleaved)
        {
            System.Buffer.MemoryCopy(src, slice.Cpu, byteSize, byteSize);
        }

        // Target formats come from the open pass: a PSO is only valid for the
        // formats it was compiled against.
        var target = _renderer.CurrentTargetState;
        var pso = (program ?? _shader).GetPso(
            LineLayout, FillMode.Solid, PrimitiveTopologyType.Line,
            depth, blend, DepthBias.None, in target);
        _renderer.BindPipelineState(list, pso);
        _renderer.BindTopology(list, D3DPrimitiveTopology.D3DPrimitiveTopologyLinelist);

        var view = new VertexBufferView
        {
            BufferLocation = slice.GpuVa,
            SizeInBytes = byteSize,
            StrideInBytes = StrideBytes,
        };
        list->IASetVertexBuffers(0, 1, &view);
        list->DrawInstanced(vertexCount, 1, 0, 0);
    }

    public void Dispose()
    {
    }
}
