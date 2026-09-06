using System;

namespace SpectraEngine.Core.Assets;

/// <summary>Per-frame CPU and upload limits. Driver calls and aligned rows can overrun a limit.</summary>
public sealed record AssetUploadBudget
{
    public double CpuMilliseconds { get; init; } = 2;
    public long BytesPerFrame { get; init; } = 8 * 1024 * 1024;
    public int BytesPerStep { get; init; } = 256 * 1024;
    public long QueuedPayloadBytes { get; init; } = 256 * 1024 * 1024;
    public int DecodeWorkers { get; init; } = Math.Min(4, Math.Max(1, Environment.ProcessorCount - 1));
    internal void Validate()
    {
        if (!double.IsFinite(CpuMilliseconds) || CpuMilliseconds <= 0 || BytesPerFrame < 4 || BytesPerStep < 4 ||
            BytesPerStep > 256 * 1024 || QueuedPayloadBytes <= 0 || DecodeWorkers < 1 || DecodeWorkers > 4)
            throw new ArgumentOutOfRangeException(nameof(AssetUploadBudget));
    }
}

/// <summary>Immutable queue measurements; worker-held payloads are reported separately from admitted payloads.</summary>
public readonly record struct AssetQueueStatistics(int PendingDecodes, int ActiveWorkers, int PendingUploads,
    long QueuedPayloadBytes, long WorkerPayloadBytes, long PeakQueuedPayloadBytes, long PeakWorkerPayloadBytes,
    long Applied, long Stale, long CpuOverruns, long ByteOverruns, long AlignedStepOverruns,
    double LastPumpMilliseconds, long LastPumpBytes);
