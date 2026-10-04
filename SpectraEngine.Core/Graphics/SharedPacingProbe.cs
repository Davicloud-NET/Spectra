using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// Measures how the engine's frame rate follows the shared target's consumer.
/// Runs the real producer against a consumer on a second device that takes its
/// turn at a set cadence. Call <see cref="Update"/> on the render thread.
/// </summary>
// The consumer has to be a second device: a keyed mutex is owned per device,
// and a turn taken on the producer's own device fails with
// DXGI_ERROR_INVALID_CALL instead of waiting.
public sealed class SharedPacingProbe
{
    private const double PhaseSeconds = 2.0;

    // Keeps the shader compile and first static-world build out of the first phase.
    private const int WarmupFrames = 30;

    private enum ConsumerMode
    {
        Stopped,

        // A turn every Phase.PeriodMs.
        Paced,

        // Turns as fast as the mutex allows.
        Free,
    }

    private sealed record Phase(string Name, ConsumerMode Mode, double PeriodMs, string Meaning);

    private static readonly Phase[] Script =
    [
        new("free", ConsumerMode.Free, 0.0,
            "turns taken as fast as the mutex allows: the producer's ceiling"),
        new("vsync-60", ConsumerMode.Paced, 1000.0 / 60.0,
            "a turn every display refresh, which is a compositor keeping up"),
        new("late-40", ConsumerMode.Paced, 25.0,
            "a turn every 25 ms, which is a compositor missing every other refresh"),
        new("slow-30", ConsumerMode.Paced, 1000.0 / 30.0,
            "a turn every other refresh"),
        new("hidden", ConsumerMode.Stopped, 0.0,
            "no turns at all: the acquire timeout, which is meant to cost this"),
    ];

    private readonly ILogger _logger;
    private readonly List<Reading> _readings = [];

    private PacedConsumer? _consumer;
    private Stopwatch? _phaseClock;
    private int _phaseIndex;
    private int _warmupFrames;
    private int _phaseFrames;
    private long _phaseHandOversAtStart;

    private double _waitSum;
    private int _waitWindows;
    private float _waitPeak;

    /// <summary>True until the probe has run its whole script and reported.</summary>
    public bool Running { get; private set; } = true;

    /// <summary>
    /// Whether the run produced a measurement. Not a threshold on the numbers:
    /// a slow machine still passes.
    /// </summary>
    public bool Passed { get; private set; }

    public SharedPacingProbe(ILogger logger) => _logger = logger;

    /// <summary>One phase's measurement.</summary>
    /// <param name="Fps">Engine frames per second over the phase.</param>
    /// <param name="HandOversPerSecond">Turns the consumer completed per second.</param>
    /// <param name="AcquireAverageMs">Mean producer wait for the key.</param>
    /// <param name="AcquirePeakMs">Worst producer wait for the key.</param>
    public readonly record struct Reading(
        string Name,
        double Fps,
        double HandOversPerSecond,
        float AcquireAverageMs,
        float AcquirePeakMs,
        string Meaning);

    /// <summary>What each phase measured, once the run has finished.</summary>
    public IReadOnlyList<Reading> Readings => _readings;

    /// <summary>
    /// Takes the producer's acquire wait from a published frame. Render thread.
    /// </summary>
    // Renderer.DrainSharedAcquireWait resets on read and the snapshot publisher
    // already drains it, so reading it here would see almost nothing.
    // The average is a mean of per-window averages, not weighted by frame count.
    public void ObserveSnapshot(FrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Running || _consumer is null) return;

        _waitSum += snapshot.SharedAcquireWaitMs;
        _waitWindows++;
        if (snapshot.SharedAcquirePeakMs > _waitPeak) _waitPeak = snapshot.SharedAcquirePeakMs;
    }

    /// <summary>Call once per frame on the render thread, before <see cref="Renderer.Render"/>.</summary>
    public void Update(Renderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (!Running) return;

        try
        {
            if (_consumer is null)
            {
                Begin(renderer);
                return;
            }

            _phaseFrames++;

            if (_phaseClock!.Elapsed.TotalSeconds < PhaseSeconds) return;

            RecordPhase();

            _phaseIndex++;
            if (_phaseIndex >= Script.Length)
            {
                Report();
                Finish(passed: true);
                return;
            }

            StartPhase();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Shared pacing probe: FAIL - the measurement threw");
            Finish(passed: false);
        }
    }

    private void Begin(Renderer renderer)
    {
        if (++_warmupFrames < WarmupFrames) return;

        if (!renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle handle))
        {
            _logger.LogError(
                "Shared pacing probe: FAIL - {Backend} has no shared present target, so there is no " +
                "hand-over to pace. This probe needs a composited surface.",
                renderer.Backend);
            Finish(passed: false);
            return;
        }

        // A D3D11 consumer on both backends: D3D12 creates the shared texture
        // through the D3D11On12 bridge, so it is a D3D11 texture either way.
        if (D3D11.D3D11SharedTargetConsumer.TryOpen(
                handle.NtHandle, handle.Width, handle.Height, _logger) is not { } consumer)
        {
            _logger.LogError(
                "Shared pacing probe: FAIL - the shared target would not open on a second device, so " +
                "nothing can take the consumer's turn while a frame is in flight.");
            Finish(passed: false);
            return;
        }

        _logger.LogInformation(
            "Shared pacing probe on {Backend} at {Width}x{Height} (shared generation {Generation}, " +
            "handle 0x{Handle:X}). The producer is real and so is the consumer - a second device that " +
            "opens the same handle - and only the cadence of its turn is set here. Each phase runs " +
            "{Seconds:0.0} s.",
            renderer.Backend, handle.Width, handle.Height, handle.Generation, handle.NtHandle,
            PhaseSeconds);

        _consumer = new PacedConsumer(consumer);
        StartPhase();
    }

    private void StartPhase()
    {
        Phase phase = Script[_phaseIndex];
        _consumer!.Configure(phase.Mode, phase.PeriodMs);

        _waitSum = 0;
        _waitWindows = 0;
        _waitPeak = 0f;

        _phaseFrames = 0;
        _phaseHandOversAtStart = _consumer.HandOvers;
        _phaseClock = Stopwatch.StartNew();
    }

    private void RecordPhase()
    {
        Phase phase = Script[_phaseIndex];
        double seconds = _phaseClock!.Elapsed.TotalSeconds;
        long handOvers = _consumer!.HandOvers - _phaseHandOversAtStart;

        _readings.Add(new Reading(
            phase.Name,
            _phaseFrames / seconds,
            handOvers / seconds,
            _waitWindows > 0 ? (float)(_waitSum / _waitWindows) : 0f,
            _waitPeak,
            phase.Meaning));
    }

    private void Report()
    {
        _logger.LogInformation(
            "Shared pacing:  {Header,-10} {Fps,9} {Handovers,9} {Average,11} {Peak,11}",
            "phase", "eng fps", "hand/s", "wait avg", "wait peak");

        foreach (Reading reading in _readings)
        {
            _logger.LogInformation(
                "Shared pacing:  {Name,-10} {Fps,9:0.0} {Handovers,9:0.0} {Average,8:0.00} ms " +
                "{Peak,8:0.00} ms   {Meaning}",
                reading.Name, reading.Fps, reading.HandOversPerSecond,
                reading.AcquireAverageMs, reading.AcquirePeakMs, reading.Meaning);
        }

        _logger.LogInformation(
            "Shared pacing: the engine's frame rate equals the consumer's turn rate in every paced row - " +
            "the producer cannot start a frame until key 0 comes back, so the wait is the difference " +
            "between the two rates. The 'free' row is the producer's own speed with nothing pacing it.");
    }

    private void Finish(bool passed)
    {
        _consumer?.Dispose();
        _consumer = null;
        Running = false;
        Passed = passed;
    }

    // Takes the consumer's turn on its own thread. The deadline advances from the
    // scheduled time, and resets when more than a period behind so missed turns
    // are not made up. Spins instead of sleeping: the default Windows timer
    // granularity is 15.6 ms.
    private sealed class PacedConsumer : IDisposable
    {
        private readonly ISharedTargetConsumer _consumer;
        private readonly Thread _thread;

        private long _handOvers;
        private volatile bool _running = true;
        private volatile int _mode = (int)ConsumerMode.Stopped;

        // Written between phases from the render thread.
        private volatile int _periodTicks;

        internal PacedConsumer(ISharedTargetConsumer consumer)
        {
            _consumer = consumer;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "Shared pacing consumer",
            };
            _thread.Start();
        }

        internal long HandOvers => Interlocked.Read(ref _handOvers);

        internal void Configure(ConsumerMode mode, double periodMs)
        {
            _periodTicks = (int)Math.Max(1, periodMs * Stopwatch.Frequency / 1000.0);
            _mode = (int)mode;
        }

        public void Dispose()
        {
            _running = false;

            // Join first: freeing a keyed-mutex resource under a live acquire crashes the driver.
            _thread.Join(TimeSpan.FromSeconds(2));
            _consumer.Dispose();
        }

        private void Run()
        {
            long next = Stopwatch.GetTimestamp();

            while (_running)
            {
                var mode = (ConsumerMode)_mode;

                if (mode == ConsumerMode.Stopped)
                {
                    next = Stopwatch.GetTimestamp();
                    Thread.Sleep(1);
                    continue;
                }

                if (mode == ConsumerMode.Free)
                {
                    next = Stopwatch.GetTimestamp();
                    TakeTurn();
                    continue;
                }

                long period = _periodTicks;
                long now = Stopwatch.GetTimestamp();
                if (now < next)
                {
                    Thread.SpinWait(200);
                    continue;
                }

                next = now - next > period ? now + period : next + period;
                TakeTurn();
            }
        }

        private void TakeTurn()
        {
            if (_consumer.TakeTurn(100))
                Interlocked.Increment(ref _handOvers);
        }
    }
}
