using System;
using System.Collections.Generic;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace SpectraEngine.Core.Graphics.D3D12;

public sealed unsafe partial class D3D12Renderer
{
    internal const ulong MeshPoolLimit = 64 * 1024 * 1024;
    internal const ulong MeshPoolBucketLimit = 16 * 1024 * 1024;
    internal const ulong MeshPoolIdleFrames = 300;
    private readonly Dictionary<uint, Queue<(ComPtr<ID3D12Resource> Buffer, ulong Frame)>> _freeMeshBuffers = [];
    private readonly List<(uint Capacity, ComPtr<ID3D12Resource> Buffer, ulong Fence)> _retiredMeshBuffers = [];
    private ulong _activeMeshBytes, _retiredMeshBytes, _pooledMeshBytes;
    internal int PooledMeshBufferCount { get; private set; }
    public override int PooledBufferCount => PooledMeshBufferCount;
    public MeshBufferMemory MeshBufferMemory => new(_activeMeshBytes, _retiredMeshBytes, _pooledMeshBytes);
    public override MeshBufferMemory? MeshMemory => MeshBufferMemory;

    internal static uint MeshBufferBucket(uint sizeBytes)
    {
        // No representable power of two above this; retain the requested size.
        if (sizeBytes > 0x80000000u) return sizeBytes;
        uint bucket = 256;
        while (bucket < sizeBytes) bucket <<= 1;
        return bucket;
    }

    internal ComPtr<ID3D12Resource> RentMeshBuffer(uint capacity)
    {
        ComPtr<ID3D12Resource> result;
        if (_freeMeshBuffers.TryGetValue(capacity, out var bucket) && bucket.Count > 0)
        {
            result = bucket.Dequeue().Buffer;
            _pooledMeshBytes -= capacity;
            PooledMeshBufferCount--;
        }
        else result = CreateUploadBuffer(capacity, "MeshBuffer");
        _activeMeshBytes += capacity;
        return result;
    }

    internal void ReturnMeshBuffer(uint capacity, ComPtr<ID3D12Resource> buffer)
    {
        if (buffer.Handle is null) return;
        _activeMeshBytes -= capacity;
        _retiredMeshBytes += capacity;
        _retiredMeshBuffers.Add((capacity, buffer, _isRecording ? ulong.MaxValue : _fenceValue));
    }

    private void RecycleRetiredMeshBuffers()
    {
        ulong completed = CompletedFence;
        int keep = 0;
        for (int i = 0; i < _retiredMeshBuffers.Count; i++)
        {
            var entry = _retiredMeshBuffers[i];
            if (entry.Fence == ulong.MaxValue || entry.Fence > completed)
            {
                _retiredMeshBuffers[keep++] = entry;
                continue;
            }
            _retiredMeshBytes -= entry.Capacity;
            if (entry.Capacity > MeshPoolBucketLimit)
            {
                entry.Buffer.Dispose();
                continue;
            }
            if (!_freeMeshBuffers.TryGetValue(entry.Capacity, out var bucket))
                _freeMeshBuffers.Add(entry.Capacity, bucket = new());
            // Completed buffers enter in last-use order; evict the oldest first.
            while ((ulong)(bucket.Count + 1) * entry.Capacity > MeshPoolBucketLimit)
                EvictMeshBuffer(entry.Capacity, bucket);
            bucket.Enqueue((entry.Buffer, _submittedFrames));
            _pooledMeshBytes += entry.Capacity;
            PooledMeshBufferCount++;
        }
        if (keep != _retiredMeshBuffers.Count)
            _retiredMeshBuffers.RemoveRange(keep, _retiredMeshBuffers.Count - keep);
        foreach (var (capacity, bucket) in _freeMeshBuffers)
            while (bucket.TryPeek(out var item) && _submittedFrames - item.Frame >= MeshPoolIdleFrames)
                EvictMeshBuffer(capacity, bucket);
        while (_pooledMeshBytes > MeshPoolLimit)
        {
            uint oldestCapacity = 0;
            ulong oldestFrame = ulong.MaxValue;
            foreach (var (capacity, bucket) in _freeMeshBuffers)
                if (bucket.TryPeek(out var item) && item.Frame < oldestFrame)
                { oldestFrame = item.Frame; oldestCapacity = capacity; }
            EvictMeshBuffer(oldestCapacity, _freeMeshBuffers[oldestCapacity]);
        }
    }

    private void EvictMeshBuffer(uint capacity, Queue<(ComPtr<ID3D12Resource> Buffer, ulong Frame)> bucket)
    {
        var buffer = bucket.Dequeue().Buffer;
        buffer.Dispose();
        _pooledMeshBytes -= capacity;
        PooledMeshBufferCount--;
    }

    private void ReleaseMeshBufferPool()
    {
        // Shutdown drained submitted work and abandons any unsubmitted recording.
        foreach (var entry in _retiredMeshBuffers) entry.Buffer.Dispose();
        _retiredMeshBuffers.Clear();
        _retiredMeshBytes = 0;
        foreach (var (capacity, bucket) in _freeMeshBuffers)
            while (bucket.Count > 0) EvictMeshBuffer(capacity, bucket);
        _freeMeshBuffers.Clear();
    }
}
