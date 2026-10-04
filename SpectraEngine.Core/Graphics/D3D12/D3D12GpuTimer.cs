using System;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using SpectraEngine.Core.Diagnostics;

namespace SpectraEngine.Core.Graphics.D3D12;

internal unsafe sealed class D3D12GpuTimer : GpuTimestampTimer
{
    private readonly D3D12Renderer _renderer;
    private readonly ID3D12CommandQueue* _queue;
    private ComPtr<ID3D12QueryHeap> _heap;
    private ComPtr<ID3D12Resource> _readback;
    private ComPtr<ID3D12Fence> _fence;
    private readonly ulong[] _completion = new ulong[Slots];
    private ulong _nextFence, _frequency;
    private ulong* _mapped;

    internal D3D12GpuTimer(D3D12Renderer renderer, ID3D12CommandQueue* queue)
    {
        _renderer = renderer;
        _queue = queue;
        try
        {
            ulong frequency;
            SilkMarshal.ThrowHResult(queue->GetTimestampFrequency(&frequency));
            _frequency = frequency;
            var desc = new QueryHeapDesc { Type = QueryHeapType.Timestamp, Count = Slots * Marks };
            ID3D12QueryHeap* heap = null;
            Guid guid = ID3D12QueryHeap.Guid;
            SilkMarshal.ThrowHResult(renderer.DevicePtr->CreateQueryHeap(&desc, &guid, (void**)&heap));
            _heap = ComOwnership.Own(heap);
            _readback = renderer.CreateReadbackBuffer(Slots * Marks * sizeof(ulong));
            void* mapped;
            var range = new Silk.NET.Direct3D12.Range { Begin = 0, End = Slots * Marks * sizeof(ulong) };
            SilkMarshal.ThrowHResult(((ID3D12Resource*)_readback.Handle)->Map(0, &range, &mapped));
            _mapped = (ulong*)mapped;
            ID3D12Fence* fence = null;
            guid = ID3D12Fence.Guid;
            SilkMarshal.ThrowHResult(renderer.DevicePtr->CreateFence(0, FenceFlags.None, &guid, (void**)&fence));
            _fence = ComOwnership.Own(fence);
        }
        catch { Dispose(); throw; }
    }
    protected override void WriteTimestamp(int slot, int mark) =>
        _renderer.CurrentList->EndQuery((ID3D12QueryHeap*)_heap.Handle, QueryType.Timestamp, (uint)(slot * Marks + mark));
    protected override void EndQueries(int slot, int count) =>
        _renderer.CurrentList->ResolveQueryData((ID3D12QueryHeap*)_heap.Handle, QueryType.Timestamp,
            (uint)(slot * Marks), (uint)count, (ID3D12Resource*)_readback.Handle, (ulong)(slot * Marks * sizeof(ulong)));

    // Call after the query-resolving command list is submitted.
    internal void Submitted()
    {
        if (EndedSlot < 0) return;
        ulong value = ++_nextFence;
        SilkMarshal.ThrowHResult(_queue->Signal((ID3D12Fence*)_fence.Handle, value));
        _completion[EndedSlot] = value;
    }
    protected override bool TryRead(int slot, int count, ulong[] values, out ulong frequency)
    {
        frequency = 0;
        ulong completed = ((ID3D12Fence*)_fence.Handle)->GetCompletedValue();
        if (completed == ulong.MaxValue) return true; // removed device: invalid sample
        if (_completion[slot] == 0 || completed < _completion[slot]) return false;
        for (int i = 0; i < count; i++) values[i] = _mapped[slot * Marks + i];
        frequency = _frequency;
        return true;
    }
    public override void Dispose()
    {
        // Renderer drains its queue before destroying this timer.
        if (_mapped != null)
        {
            ((ID3D12Resource*)_readback.Handle)->Unmap(0, null);
            _mapped = null;
        }
        ComOwnership.Release(ref _readback);
        ComOwnership.Release(ref _heap);
        ComOwnership.Release(ref _fence);
    }
}
