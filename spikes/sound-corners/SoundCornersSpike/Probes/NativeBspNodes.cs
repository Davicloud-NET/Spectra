using System;
using System.Buffers;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Bsp;

namespace SoundCornersSpike.Probes;

// Flat BSP nodes in native memory behind a MemoryManager, the way the engine's
// internal MappedBspNodes hands a mapped .scmap to FlatBspTree. A tree over a
// plain array skips GetSpan, so it would flatter the baked numbers.
// The block is never freed: the spike's worlds live as long as the process.
internal sealed unsafe class NativeBspNodes : MemoryManager<FlatBspNode>
{
    private readonly FlatBspNode* _nodes;
    private readonly int _count;

    public NativeBspNodes(ReadOnlySpan<FlatBspNode> source)
    {
        _count = source.Length;
        _nodes = (FlatBspNode*)NativeMemory.Alloc((nuint)Math.Max(source.Length, 1), (nuint)sizeof(FlatBspNode));
        source.CopyTo(new Span<FlatBspNode>(_nodes, _count));
    }

    public override Span<FlatBspNode> GetSpan() => new(_nodes, _count);

    public override MemoryHandle Pin(int elementIndex = 0) => new(_nodes + elementIndex);

    public override void Unpin()
    {
    }

    protected override void Dispose(bool disposing)
    {
    }
}
