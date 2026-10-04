using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using SpectraEngine.Core.Diagnostics;

namespace SpectraEngine.Core.Graphics.D3D12;

public sealed unsafe partial class D3D12Renderer
{
    private sealed class FrameContext
    {
        internal ComPtr<ID3D12CommandAllocator> Allocator;
        internal ComPtr<ID3D12GraphicsCommandList> List;
        internal ComPtr<ID3D12Resource> UploadRing;
        internal byte* UploadCpu;
        internal ulong UploadGpuVa;
        internal uint UploadCapacity = 1024 * 1024;
        internal ComPtr<ID3D12DescriptorHeap> SrvRing, SamplerRing;
        internal uint SrvCapacity = 512, SamplerCapacity = 256, SrvPeak, SamplerPeak;
        internal ulong Completion;
        internal readonly List<ComPtr<ID3D12Resource>> RetiredUploadRings = [];
    }

    private FrameContext _frame = new();
    private readonly List<FrameContext> _frames = [];
    private int _nextFrame, _frameContextCount = 2;
    private ulong _submittedFrames;
    private readonly List<(nint Resource, ulong Fence)> _retiredResources = [];

    /// <summary>CPU frames allowed in flight, 1–3. Set before initialization; default 2.</summary>
    public int FrameContextCount
    {
        get => _frameContextCount;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 3);
            if (_frames.Count != 0) throw new InvalidOperationException("Set frame contexts before initialization.");
            _frameContextCount = value;
        }
    }

    private void CreateFrameContexts()
    {
        for (int i = 0; i < FrameContextCount; i++)
        {
            _frame = new FrameContext();
            _frames.Add(_frame); // register before any allocation can fail
            ID3D12CommandAllocator* allocator = null;
            SilkMarshal.ThrowHResult(DevicePtr->CreateCommandAllocator(CommandListType.Direct,
                SilkMarshal.GuidPtrOf<ID3D12CommandAllocator>(), (void**)&allocator));
            _commandAllocator = ComOwnership.Own(allocator);
            ID3D12GraphicsCommandList* list = null;
            SilkMarshal.ThrowHResult(DevicePtr->CreateCommandList(0, CommandListType.Direct,
                allocator, null, SilkMarshal.GuidPtrOf<ID3D12GraphicsCommandList>(), (void**)&list));
            _commandList = ComOwnership.Own(list);
            SilkMarshal.ThrowHResult(list->Close());
            _uploadRing = CreateUploadBuffer(_uploadRingCapacity, "FrameUploadRing");
            MapUploadRing();
            _srvRing = CreateDescriptorHeap(DescriptorHeapType.CbvSrvUav, _srvRingCapacity, true);
            _samplerRing = CreateDescriptorHeap(DescriptorHeapType.Sampler, _samplerRingCapacity, true);
        }
        _logger.LogInformation("D3D12 frame contexts: {Count}", FrameContextCount);
    }

    private void AcquireFrameContext()
    {
        if (_isRecording) throw new InvalidOperationException("A command list is already recording.");
        _frame = _frames[_nextFrame];
        _nextFrame = (_nextFrame + 1) % _frames.Count;
        WaitForFence(_frame.Completion);
        DisposeRetiredUploadRings();
        GrowDescriptorRingsIfNeeded();
        ReleaseCompletedResources();
        RecycleRetiredMeshBuffers();
    }

    private void SignalSubmission()
    {
        ulong value = ++_fenceValue;
        SilkMarshal.ThrowHResult(((ID3D12CommandQueue*)_queue.Handle)->Signal((ID3D12Fence*)_fence.Handle, value));
        _frame.Completion = value;
        _submittedFrames++;
        // MaxValue denotes a resource referenced by the command list just submitted.
        for (int i = 0; i < _retiredResources.Count; i++)
            if (_retiredResources[i].Fence == ulong.MaxValue)
                _retiredResources[i] = (_retiredResources[i].Resource, value);
        for (int i = 0; i < _retiredMeshBuffers.Count; i++)
            if (_retiredMeshBuffers[i].Fence == ulong.MaxValue)
            {
                var entry = _retiredMeshBuffers[i];
                _retiredMeshBuffers[i] = (entry.Capacity, entry.Buffer, value);
            }
    }

    private ulong CompletedFence => _deviceLost ? ulong.MaxValue :
        _fence.Handle is null ? 0 : ((ID3D12Fence*)_fence.Handle)->GetCompletedValue();

    private void WaitForFence(ulong value)
    {
        if (_fence.Handle is null || _deviceLost || CompletedFence >= value) return;
        using var timing = Profiler.Measure(FramePhase.GpuWait);
        SilkMarshal.ThrowHResult(((ID3D12Fence*)_fence.Handle)->SetEventOnCompletion(value, (void*)_fenceEvent));
        Kernel32.WaitForSingleObject(_fenceEvent, Kernel32.Infinite);
    }

    // Takes over the reference; released once the GPU is done with it. Render thread only.
    internal void Retire<T>(ref ComPtr<T> resource) where T : unmanaged, IComVtbl<T>
    {
        if (resource.Handle is null) return;
        if (!_isRecording && CompletedFence >= _fenceValue)
            ComOwnership.Release(ref resource);
        else
        {
            _retiredResources.Add(((nint)resource.Handle, _isRecording ? ulong.MaxValue : _fenceValue));
            resource = default;
        }
    }

    private void ReleaseCompletedResources(bool abandoningRecording = false)
    {
        ulong completed = CompletedFence;
        int keep = 0;
        for (int i = 0; i < _retiredResources.Count; i++)
        {
            var entry = _retiredResources[i];
            if ((entry.Fence != ulong.MaxValue && entry.Fence <= completed) || abandoningRecording)
                ((IUnknown*)entry.Resource)->Release();
            else _retiredResources[keep++] = entry;
        }
        if (keep != _retiredResources.Count) _retiredResources.RemoveRange(keep, _retiredResources.Count - keep);
    }

    private void ReleaseFrameContexts()
    {
        foreach (var frame in _frames)
        {
            _frame = frame;
            ComOwnership.Release(ref _samplerRing);
            ComOwnership.Release(ref _srvRing);
            if (_uploadRingCpu is not null && _uploadRing.Handle is not null)
                ((ID3D12Resource*)_uploadRing.Handle)->Unmap(0, null);
            _uploadRingCpu = null;
            ComOwnership.Release(ref _uploadRing);
            DisposeRetiredUploadRings();
            ComOwnership.Release(ref _commandList);
            ComOwnership.Release(ref _commandAllocator);
        }
        _frames.Clear();
    }
}
