using System;
using System.Buffers;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Maps.Compiled;

// One cell's flat BSP nodes as Memory over the compiled map's own bytes, so a
// FlatBspTree queries the mapping instead of a copy.
// Do not cache a pointer: GetSpan re-reads ContentBlob.Span each time, so use
// after release is an ObjectDisposedException. A cached pointer would be an
// access violation on a mapped blob and a stale read on a pooled one.
internal sealed class MappedBspNodes : MemoryManager<FlatBspNode>
{
    private readonly ContentBlob _file;
    private readonly int _byteOffset;
    private readonly int _count;

    // byteOffset is from the first byte of the file.
    public MappedBspNodes(ContentBlob file, int byteOffset, int count)
    {
        _file = file;
        _byteOffset = byteOffset;
        _count = count;
    }

    /// <inheritdoc/>
    public override Span<FlatBspNode> GetSpan()
    {
        ReadOnlySpan<byte> bytes = _file.Span.Slice(_byteOffset, _count * ScmapFormat.FlatBspNodeSize);

        // MemoryManager wants a writable Span. Callers only see ReadOnlyMemory,
        // and nothing writes a BSP node; a mapped view is read-only anyway.
        return MemoryMarshal.Cast<byte, FlatBspNode>(
            MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(bytes), bytes.Length));
    }

    /// <inheritdoc/>
    public override MemoryHandle Pin(int elementIndex = 0) =>
        throw new NotSupportedException(
            "A compiled map's BSP nodes cannot be pinned. Their lifetime is the map's ContentBlob, which is " +
            "either a window into a memory-mapped file (already at a fixed address, so pinning says nothing) " +
            "or a pooled array (which the blob may return at any time, so a pin would outlive its own " +
            "promise). Read the span instead.");

    /// <inheritdoc/>
    public override void Unpin()
    {
    }

    // Nothing to release: the bytes belong to the map's blob.
    protected override void Dispose(bool disposing)
    {
    }
}
