using System;
using System.Diagnostics;

namespace SpectraEngine.Core.Diagnostics;

/// <summary>
/// The phases of one frame, in the order the render loop runs them.
/// </summary>
/// <remarks>
/// An enum rather than strings so a scope costs an array index and no
/// allocation. Anything measured every frame has to be cheaper than what it
/// measures, or the profiler becomes the profile.
/// </remarks>
public enum FramePhase
{
    /// <summary>Editor tools, camera controllers, demo animation.</summary>
    Update,

    /// <summary>Fixed-tick physics, however many ticks this frame owed.</summary>
    Physics,

    /// <summary>Landing a finished static-world compile: GPU meshes created and destroyed.</summary>
    WorldSwap,

    /// <summary>Budgeted texture/model uploads and reload dispatch.</summary>
    Assets,

    /// <summary>Frustum culling and draw-list building for the camera.</summary>
    ViewBuild,

    /// <summary>The shadow cascades: culling and depth-only draws.</summary>
    Shadows,

    /// <summary>The G-buffer fill, or the forward pass.</summary>
    Geometry,

    /// <summary>The deferred light pass.</summary>
    Lighting,

    /// <summary>Tone mapping, the debug overlay, and anything else after the scene.</summary>
    Resolve,

    /// <summary>Presentation and consumer pacing; explicit fence waits have their own scope.</summary>
    Present,
    Editor,
    CollisionSync,
    PartMeshes,
    Audio,
    Snapshot,
    GpuWait,
    Unaccounted,
}

/// <summary>Immutable rolling CPU-frame distribution, in milliseconds.</summary>
public readonly record struct FrameProfileSnapshot(int Samples, double P50, double P95, double P99, double Max);

/// <summary>
/// Where a frame's time went, measured on the CPU, phase by phase.
/// </summary>
/// <remarks>
/// <para>
/// <b>CPU time, and it says so.</b> A phase's number is how long the render
/// thread spent inside it, which on an immediate-mode API is mostly the cost of
/// building and submitting commands rather than of the GPU executing them. That
/// is the right thing to measure first here: the engine's frame time was
/// identical at 1280x720 and at 2560x1440, which is only possible if the
/// bottleneck is on this side.
/// </para>
/// <para>
/// Explicit GPU fence waits are separated from presentation. Nested scopes
/// account exclusively for elapsed wall time; any remaining time belongs to
/// Unaccounted, so the phase sum covers the complete frame.
/// </para>
/// <para>
/// Values are smoothed with an exponential average because a raw frame is
/// dominated by whichever one happened to land a compile or a resize. Render
/// thread only, and lock-free for that reason.
/// </para>
/// </remarks>
public sealed class FrameProfiler
{
    private static readonly double MillisecondsPerTick = 1000.0 / Stopwatch.Frequency;
    private static readonly int PhaseCount = Enum.GetValues<FramePhase>().Length;

    private readonly long[] _current;
    private readonly double[] _smoothed;
    private readonly double _smoothing;
    private readonly long[] _frames = new long[2048];
    private readonly long[] _sortScratch = new long[2048];
    private readonly FramePhase[] _scopeStack = new FramePhase[64];
    private int _depth, _frameCursor, _frameCount;
    private long _frameStart, _transition;
    private bool _frameOpen;
    internal GpuTimestampTimer? GpuTimer { get; set; }
    public GpuProfileSnapshot GpuSnapshot => GpuTimer?.Snapshot ?? default;

    /// <summary>Creates a profiler. <paramref name="smoothing"/> is the weight of each new frame.</summary>
    public FrameProfiler(double smoothing = 0.05)
    {
        _current = new long[PhaseCount];
        _smoothed = new double[PhaseCount];
        _smoothing = smoothing;
    }

    /// <summary>Whether phases are being timed at all. Off costs one branch per scope.</summary>
    public bool Enabled { get; set; }

    /// <summary>Milliseconds spent in <paramref name="phase"/>, smoothed over recent frames.</summary>
    public double this[FramePhase phase] => _smoothed[(int)phase];

    /// <summary>Total of every phase, smoothed. Close to the frame time when nothing is unmeasured.</summary>
    public double TotalMs
    {
        get
        {
            double total = 0;
            for (int i = 0; i < _smoothed.Length; i++) total += _smoothed[i];
            return total;
        }
    }

    /// <summary>Opens a timing scope; dispose to close it. Use with <c>using</c>.</summary>
    public Scope Measure(FramePhase phase) => new(this, phase);

    /// <summary>Starts complete wall-clock frame accounting. Render thread.</summary>
    public void BeginFrame() => BeginFrame(Stopwatch.GetTimestamp());

    internal void BeginFrame(long timestamp)
    {
        Array.Clear(_current);
        _depth = 0;
        _frameOpen = Enabled;
        _frameStart = _transition = timestamp;
    }

    /// <summary>Sorts retained samples only when a consumer requests a report.</summary>
    public FrameProfileSnapshot Snapshot()
    {
        if (_frameCount == 0) return default;
        Array.Copy(_frames, _sortScratch, _frameCount);
        Array.Sort(_sortScratch, 0, _frameCount);
        double Percentile(double p) => _sortScratch[Math.Max(0, (int)Math.Ceiling(p * _frameCount) - 1)] * MillisecondsPerTick;
        return new(_frameCount, Percentile(.50), Percentile(.95), Percentile(.99), Percentile(1));
    }

    /// <summary>Folds this frame's measurements into the smoothed averages and resets.</summary>
    public void EndFrame() => EndFrame(Stopwatch.GetTimestamp());

    internal void EndFrame(long timestamp)
    {
        if (!Enabled) return;

        if (_frameOpen)
        {
            Charge(timestamp);
            _frames[_frameCursor] = Math.Max(0, timestamp - _frameStart);
            _frameCursor = (_frameCursor + 1) % _frames.Length;
            _frameCount = Math.Min(_frameCount + 1, _frames.Length);
            _frameOpen = false;
        }

        for (int i = 0; i < _current.Length; i++)
        {
            double ms = _current[i] * MillisecondsPerTick;
            _smoothed[i] += (ms - _smoothed[i]) * _smoothing;
            _current[i] = 0;
        }
    }

    /// <summary>
    /// The phases worth naming, as "name ms" pairs, largest first, for a log
    /// line. Allocates: call it on a log cadence, never per frame.
    /// </summary>
    public string Describe(int top = 6)
    {
        Span<int> order = stackalloc int[PhaseCount];
        for (int i = 0; i < PhaseCount; i++) order[i] = i;

        // Selection sort over ten items: shorter than explaining why a real
        // sort was worth allocating for a log line.
        for (int i = 0; i < PhaseCount; i++)
        {
            for (int j = i + 1; j < PhaseCount; j++)
            {
                if (_smoothed[order[j]] > _smoothed[order[i]])
                    (order[i], order[j]) = (order[j], order[i]);
            }
        }

        var text = new System.Text.StringBuilder();
        for (int i = 0; i < Math.Min(top, PhaseCount); i++)
        {
            double ms = _smoothed[order[i]];
            if (ms < 0.005) break;
            if (text.Length > 0) text.Append(", ");
            text.Append((FramePhase)order[i]).Append(' ').Append(ms.ToString("0.00")).Append(" ms");
        }

        text.Append($"; CPU frame EMA {TotalMs:0.00} ms");
        FrameProfileSnapshot stats = Snapshot();
        if (stats.Samples > 0)
            text.Append($"; frame p50/p95/p99/max {stats.P50:0.00}/{stats.P95:0.00}/{stats.P99:0.00}/{stats.Max:0.00} ms ({stats.Samples} samples)");
        GpuProfileSnapshot gpu = GpuSnapshot;
        text.Append(gpu.Available
            ? $"; GPU total/shadow/geometry/light/resolve {gpu.Total:0.00}/{gpu.Shadows:0.00}/{gpu.Geometry:0.00}/{gpu.Lighting:0.00}/{gpu.Resolve:0.00} ms (completed sample {gpu.Frame})"
            : "; GPU unavailable");
        return text.Length == 0 ? "not measured" : text.ToString();
    }

    // Exclusive accounting: a nested GPU wait is not also charged to Present.
    private void Charge(long timestamp)
    {
        if (_frameOpen || _depth > 0)
            _current[(int)(_depth > 0 ? _scopeStack[_depth - 1] : FramePhase.Unaccounted)] += timestamp - _transition;
        _transition = timestamp;
    }

    internal void Open(FramePhase phase, long timestamp)
    {
        if (_depth == _scopeStack.Length) throw new InvalidOperationException("Profiler scope nesting exceeded 64.");
        Charge(timestamp);
        _scopeStack[_depth++] = phase;
        GpuTimer?.Mark(phase, true);
    }

    internal void Close(FramePhase phase, long timestamp)
    {
        if (_depth == 0 || _scopeStack[_depth - 1] != phase)
            throw new InvalidOperationException("Profiler scopes must close in reverse order.");
        Charge(timestamp);
        _depth--;
        GpuTimer?.Mark(phase, false);
    }

    /// <summary>One open phase. A ref struct so it cannot outlive its frame or escape to the heap.</summary>
    public readonly ref struct Scope
    {
        private readonly FrameProfiler? _profiler;
        private readonly FramePhase _phase;

        internal Scope(FrameProfiler profiler, FramePhase phase)
        {
            // Null when disabled, so Dispose is a null check rather than a
            // second lookup of the same flag.
            _profiler = profiler.Enabled ? profiler : null;
            _phase = phase;
            _profiler?.Open(phase, Stopwatch.GetTimestamp());
        }

        public void Dispose() => _profiler?.Close(_phase, Stopwatch.GetTimestamp());
    }
}
