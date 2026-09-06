using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SpectraEngine.Core.Assets;

internal readonly record struct AssetUploadStep(int Bytes, bool Complete, bool Applied);
internal interface IAssetUpload : IDisposable
{
    long PayloadBytes { get; }
    bool IsStale { get; }
    AssetUploadStep Step(int maxBytes);
}

/// <summary>One fair queue for textures/models, with coalesced work and bounded decoder admission.</summary>
internal sealed class AssetUploadPipeline
{
    private sealed record Decode(Func<bool> IsStale, Func<IAssetUpload> Run, Action Discard);
    private readonly object _sync = new();
    private readonly Queue<object> _order = new();
    private readonly Dictionary<object, Decode> _pending = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<IAssetUpload> _uploads = new();
    private bool _stopped;
    private int _workers;
    private long _bytes, _workerBytes, _peakBytes, _peakWorkerBytes;
    private long _applied, _stale, _cpuOverruns, _byteOverruns, _stepOverruns;
    private double _lastMs;
    private long _lastBytes;
    internal AssetUploadBudget Budget { get; }
    internal AssetUploadPipeline(AssetUploadBudget budget) { budget.Validate(); Budget = budget; }
    internal AssetQueueStatistics Snapshot
    {
        get { lock (_sync) return new(_pending.Count, _workers, _uploads.Count, _bytes, _workerBytes, _peakBytes,
            _peakWorkerBytes, _applied, _stale, _cpuOverruns, _byteOverruns, _stepOverruns, _lastMs, _lastBytes); }
    }

    internal void Queue(object key, Func<bool> isStale, Func<IAssetUpload> run, Action discard)
    {
        Decode? replaced = null;
        lock (_sync)
        {
            if (_stopped) { replaced = new(isStale, run, discard); }
            else
            {
                if (_pending.TryGetValue(key, out replaced)) _stale++;
                else _order.Enqueue(key);
                _pending[key] = new(isStale, run, discard);
                if (_workers < Budget.DecodeWorkers) { _workers++; _ = Task.Run(Worker); }
            }
        }
        replaced?.Discard();
    }

    private void Worker()
    {
        try
        {
            while (true)
            {
                Decode work;
                lock (_sync)
                {
                    while (!_stopped && _bytes >= Budget.QueuedPayloadBytes) Monitor.Wait(_sync);
                    if (_stopped || !_order.TryDequeue(out var key)) return;
                    work = _pending[key]; _pending.Remove(key);
                }
                if (work.IsStale()) { work.Discard(); lock (_sync) _stale++; continue; }
                IAssetUpload upload;
                try { upload = work.Run(); }
                catch { work.Discard(); continue; } // Factory reports content failures as failed upload results.
                bool discard;
                lock (_sync)
                {
                    _workerBytes += upload.PayloadBytes;
                    _peakWorkerBytes = Math.Max(_peakWorkerBytes, _workerBytes);
                    // One oversized result is admitted exclusively. An indivisible
                    // import can hold extra bytes in its worker until admission.
                    while (!_stopped && _bytes != 0 && upload.PayloadBytes > Budget.QueuedPayloadBytes - _bytes)
                        Monitor.Wait(_sync);
                    _workerBytes -= upload.PayloadBytes;
                    discard = _stopped;
                    if (!discard)
                    {
                        _bytes += upload.PayloadBytes;
                        _peakBytes = Math.Max(_peakBytes, _bytes);
                        _uploads.Enqueue(upload);
                    }
                }
                if (discard) upload.Dispose(); // No GPU work has started.
            }
        }
        finally
        {
            lock (_sync)
            {
                _workers--;
                // An enqueue can race a worker's empty-queue return.
                if (!_stopped && _order.Count != 0) { _workers++; _ = Task.Run(Worker); }
                Monitor.PulseAll(_sync);
            }
        }
    }

    internal int Pump()
    {
        long start = Stopwatch.GetTimestamp();
        long bytes = 0;
        int applied = 0;
        while (bytes < Budget.BytesPerFrame)
        {
            IAssetUpload upload;
            lock (_sync) { if (!_uploads.TryDequeue(out upload!)) break; }
            bool finished = false;
            try
            {
                if (upload.IsStale) { finished = true; lock (_sync) _stale++; }
                else
                {
                    int allowance = (int)Math.Min(Budget.BytesPerStep, Budget.BytesPerFrame - bytes);
                    if (allowance < 4) allowance = 4;
                    AssetUploadStep result = upload.Step(allowance);
                    bytes += result.Bytes;
                    finished = result.Complete;
                    if (result.Applied) applied++;
                    if (result.Bytes > allowance) _stepOverruns++;
                }
            }
            catch
            {
                finished = true;
                throw;
            }
            finally
            {
                if (finished)
                {
                    upload.Dispose();
                    lock (_sync) { _bytes -= upload.PayloadBytes; Monitor.PulseAll(_sync); }
                }
                else lock (_sync) _uploads.Enqueue(upload);
            }
            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= Budget.CpuMilliseconds) break;
        }
        _lastMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _lastBytes = bytes; _applied += applied;
        if (_lastMs > Budget.CpuMilliseconds) _cpuOverruns++;
        if (bytes > Budget.BytesPerFrame) _byteOverruns++;
        return applied;
    }

    // Joins CPU workers before the source stack can be torn down. Cancellation
    // wakes memory admission; native imports finish their indivisible operation.
    internal void StopWorkers()
    {
        Decode[] pending;
        lock (_sync)
        {
            _stopped = true;
            pending = new Decode[_pending.Count]; _pending.Values.CopyTo(pending, 0);
            _pending.Clear(); _order.Clear(); Monitor.PulseAll(_sync);
        }
        foreach (var work in pending) work.Discard();
        lock (_sync) while (_workers != 0) Monitor.Wait(_sync);
    }

    internal void ReleaseUploads()
    {
        while (true)
        {
            IAssetUpload upload;
            lock (_sync) { if (!_uploads.TryDequeue(out upload!)) return; _bytes -= upload.PayloadBytes; }
            upload.Dispose();
        }
    }
}
