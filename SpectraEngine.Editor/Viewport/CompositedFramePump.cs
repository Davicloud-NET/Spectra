using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Editor.Viewport.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace SpectraEngine.Editor.Viewport;

// One imported generation of the engine's shared colour target. An interface
// so the pump can be tested with no GPU or compositor.
internal interface ICompositedImage
{
    // Completes when the compositor has opened the shared resource. Don't draw
    // from the image or close its source handle before then.
    Task ImportCompleted { get; }

    // The consumer's turn on the keyed mutex: acquire, snapshot the texture
    // into the surface, release.
    Task UpdateAsync(uint acquireKey, uint releaseKey);

    // The surface keeps the last frame it took.
    ValueTask DisposeAsync();
}

internal interface ICompositedImageSource : IAsyncDisposable
{
    // ntHandle must be owned by this process.
    ICompositedImage Import(nint ntHandle, int width, int height);
}

// One window of consumer-side pacing. Compositor time is issue to update
// completed; resume time is update completed to the loop running again on the
// UI thread. A low hand-over rate is one or the other, with opposite fixes.
internal readonly record struct HandOverPacing(
    int HandOvers,
    double Seconds,
    float CompositorAverageMs,
    float CompositorPeakMs,
    float ResumeAverageMs,
    float ResumePeakMs)
{
    // The producer's frame rate equals this.
    internal double PerSecond => Seconds > 0.0 ? HandOvers / Seconds : 0.0;
}

// Drives the composited viewport's picture: imports each generation of the
// engine's shared target, keeps taking the consumer's turn on the keyed mutex,
// and releases a retired generation once nothing reads it.
//
// The mutex is the clock. The producer acquires key 0 and releases key 1; this
// side acquires 1 and releases 0. So this loop's turn rate is the engine's
// frame rate.
//
// Generations, not handles: the OS recycles handle values across resizes.
//
// A superseded import is disposed only after its last update finished, and
// only then acknowledged to the renderer, which holds the resource until then.
//
// The pump owns the source and disposes it after the last import settled. A
// re-dock stops this pump while a hand-over may still be in the mutex bracket.
//
// UI thread only.
internal sealed class CompositedFramePump
{
    // How long a hand-over may take before the pump stops scheduling more.
    // Not an acquire timeout: the compositor waits on its render thread with
    // no usable deadline. This only stops adding to it and logs the cause.
    internal static readonly TimeSpan UpdateWatchdog = TimeSpan.FromSeconds(2);

    // Hand-overs kept outstanding at once. One deep, the next update is issued
    // just after the compositor tick that completed the last and often misses
    // the following tick: measured 40 hand-overs/s against 60 at two.
    // Costs a refresh of latency. Three buys nothing, the queue is full at two.
    // Both jobs snapshot the same texture, so the second blocks the compositor
    // for one producer frame.
    internal const int HandOverDepth = 2;

    internal static readonly TimeSpan PacingWindow = TimeSpan.FromSeconds(2);

    private static readonly long PacingWindowTicks =
        (long)(PacingWindow.TotalSeconds * Stopwatch.Frequency);

    private readonly ICompositedImageSource _source;
    private readonly Action<int> _acknowledgeRelease;
    private readonly Action? _onFault;
    private readonly Func<nint, nint> _duplicateHandle;
    private readonly Action<nint> _closeHandle;
    private readonly Action<Action> _resumeOnUiThread;
    private readonly ILogger _logger;

    private readonly List<Import> _retired = [];
    private Import? _live;

    private bool _visible = true;
    private bool _stopped;
    private bool _looping;
    private bool _stalled;

    // Imports not yet fully released. The source outlives all of them.
    private int _outstanding;
    private bool _sourceReleased;

    // Issue time of each outstanding hand-over, oldest first. The compositor
    // runs its jobs in order, so they complete in this order.
    private readonly Queue<long> _outstandingIssues = new();

    private long _pacingWindowStart;
    private int _pacingSamples;
    private long _compositorTicks;
    private long _compositorPeakTicks;
    private long _resumeTicks;
    private long _resumePeakTicks;

    // onFault is raised once, on the UI thread, when the picture stops for good:
    // a hand-over threw or the watchdog gave up. Report it; don't swap the
    // viewport's hosting mode mid-session.
    internal CompositedFramePump(
        ICompositedImageSource source,
        Action<int> acknowledgeRelease,
        ILogger logger,
        Func<nint, nint>? duplicateHandle = null,
        Action<nint>? closeHandle = null,
        Action? onFault = null,
        Action<Action>? resumeOnUiThread = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(acknowledgeRelease);
        ArgumentNullException.ThrowIfNull(logger);

        _source = source;
        _acknowledgeRelease = acknowledgeRelease;
        _onFault = onFault;
        _logger = logger;

        // We duplicate the handle: Avalonia's importer takes no ownership and
        // the renderer may retire its original at any resize.
        _duplicateHandle = duplicateHandle ?? Win32Interop.DuplicateForCaller;
        _closeHandle = closeHandle ?? (handle => Win32Interop.CloseHandle(handle));

        // Send, not the Default priority an await would resume at. Default is
        // behind input, layout and render, and this post paces the engine.
        _resumeOnUiThread = resumeOnUiThread
            ?? (action => Dispatcher.UIThread.Post(action, DispatcherPriority.Send));
    }

    // Zero before the first import.
    internal int LiveGeneration => _live?.Generation ?? 0;

    internal int RetiredCount => _retired.Count;

    internal bool SourceReleased => _sourceReleased;

    internal bool IsPumping => _looping;

    // Default before the first window closes.
    internal HandOverPacing LastPacing { get; private set; }

    internal bool IsStalled => _stalled;

    // Called with each frame's shared target. Imports on a new generation.
    internal void Observe(Renderer.SharedTargetHandle handle)
    {
        if (_stopped)
            return;

        if (handle.NtHandle == 0 || handle.Width <= 0 || handle.Height <= 0)
            return;

        if (_live is { } live && live.Generation == handle.Generation)
        {
            StartLoop();
            return;
        }

        if (_live is { } superseded)
        {
            _live = null;
            Retire(superseded);
        }

        nint owned = _duplicateHandle(handle.NtHandle);
        if (owned == 0)
        {
            // A resize outran us and the handle is gone. The next generation
            // is already on its way.
            _logger.LogWarning(
                "Shared target generation {Generation} could not be duplicated; waiting for the next one.",
                handle.Generation);
            return;
        }

        var import = new Import(
            handle.Generation, owned, _source.Import(owned, handle.Width, handle.Height));
        _outstanding++;
        _live = import;

        _logger.LogInformation(
            "Composited viewport importing shared target generation {Generation} at {Width}x{Height}.",
            handle.Generation, handle.Width, handle.Height);

        _ = AdoptAsync(import);
    }

    // Hidden stops the loop. The producer then times out its acquire and carries on.
    internal void SetVisible(bool visible)
    {
        if (_visible == visible)
            return;

        _visible = visible;
        if (visible)
            StartLoop();
    }

    // Stops for good and releases every import, then the source.
    // Call while the engine is still running: an update in flight waits on a
    // key only the producer releases, and that wait is on the compositor's
    // render thread. Used for a re-dock as well as for teardown.
    internal void Stop()
    {
        _stopped = true;

        if (_live is { } live)
        {
            _live = null;
            Retire(live);
        }

        ReleaseSourceIfSettled();
    }

    private async Task AdoptAsync(Import import)
    {
        try
        {
            // The compositor opens the resource on its render thread. Drawing
            // or closing the handle before that finishes is a race.
            await import.Image.ImportCompleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "The compositor refused shared target generation {Generation}.", import.Generation);

            if (ReferenceEquals(_live, import))
                _live = null;

            Retire(import);
            return;
        }
        finally
        {
            // The compositor holds its own reference from here on.
            import.CloseHandle(_closeHandle);
        }

        StartLoop();
    }

    private void StartLoop()
    {
        if (_looping || _stopped || _stalled || !_visible)
            return;

        if (_live is not { } live || !live.Image.ImportCompleted.IsCompletedSuccessfully)
            return;

        _looping = true;
        TopUp(live);
    }

    // Issues hand-overs up to HandOverDepth. Ends the loop only once nothing
    // is in flight: a superseded import still owes its hand-overs a completion.
    private void TopUp(Import import)
    {
        while (import.UpdatesInFlight < HandOverDepth)
        {
            if (_stopped || _stalled || !_visible || !ReferenceEquals(_live, import))
            {
                if (import.UpdatesInFlight == 0) EndLoop();
                return;
            }

            IssueHandOver(import);
        }
    }

    // Not an async loop. Awaiting an already completed resume continues on the
    // compositor's render thread, and the next UpdateAsync then fails
    // Dispatcher.VerifyAccess. Everything after the hand-over runs inside the
    // posted action instead.
    private void IssueHandOver(Import import)
    {
        import.UpdatesInFlight++;
        _outstandingIssues.Enqueue(Stopwatch.GetTimestamp());

        Task handOver;
        try
        {
            handOver = import.Image.UpdateAsync(
                (uint)Renderer.SharedConsumerKey, (uint)Renderer.SharedProducerKey);
        }
        catch (Exception ex)
        {
            // Threw synchronously, so we are still on the UI thread.
            CompleteHandOver(import, ExceptionDispatchInfo.Capture(ex), completedAt: null);
            return;
        }

        // Stamp on the completing thread, before the post, so the post's delay
        // counts as resume time and not compositor time.
        handOver.ContinueWith(
            finished =>
            {
                long completedAt = Stopwatch.GetTimestamp();
                _resumeOnUiThread(() => CompleteHandOver(import, Failure(finished), completedAt));
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void CompleteHandOver(Import import, ExceptionDispatchInfo? failure, long? completedAt)
    {
        long issuedAt = _outstandingIssues.Dequeue();
        import.UpdatesInFlight--;
        if (completedAt is { } finishedAt) RecordPacing(issuedAt, finishedAt);
        SettleIfRetired(import);

        if (failure is not null)
        {
            _stalled = true;
            _logger.LogError(
                failure.SourceException, "The composited viewport's frame pump stopped.");
            _onFault?.Invoke();
            EndLoop();
            return;
        }

        TopUp(import);
    }

    private static ExceptionDispatchInfo? Failure(Task finished) =>
        finished.Exception is { } aggregate
            ? ExceptionDispatchInfo.Capture(aggregate.InnerException ?? aggregate)
            : null;

    private void RecordPacing(long issuedAt, long completedAt)
    {
        long now = Stopwatch.GetTimestamp();
        if (_pacingWindowStart == 0L) _pacingWindowStart = issuedAt;

        long compositor = Math.Max(0L, completedAt - issuedAt);
        long resume = Math.Max(0L, now - completedAt);

        _pacingSamples++;
        _compositorTicks += compositor;
        _resumeTicks += resume;
        if (compositor > _compositorPeakTicks) _compositorPeakTicks = compositor;
        if (resume > _resumePeakTicks) _resumePeakTicks = resume;

        long windowTicks = now - _pacingWindowStart;
        if (windowTicks < PacingWindowTicks) return;

        double toMs = 1000.0 / Stopwatch.Frequency;
        var pacing = new HandOverPacing(
            _pacingSamples,
            windowTicks / (double)Stopwatch.Frequency,
            (float)(_compositorTicks * toMs / _pacingSamples),
            (float)(_compositorPeakTicks * toMs),
            (float)(_resumeTicks * toMs / _pacingSamples),
            (float)(_resumePeakTicks * toMs));

        LastPacing = pacing;

        _logger.LogDebug(
            "Composited pacing: {Rate:0.0} hand-overs/s; compositor {CompositorAvg:0.0}/{CompositorPeak:0.0} ms, " +
            "resume {ResumeAvg:0.0}/{ResumePeak:0.0} ms (avg/peak).",
            pacing.PerSecond, pacing.CompositorAverageMs, pacing.CompositorPeakMs,
            pacing.ResumeAverageMs, pacing.ResumePeakMs);

        _pacingWindowStart = now;
        _pacingSamples = 0;
        _compositorTicks = 0L;
        _compositorPeakTicks = 0L;
        _resumeTicks = 0L;
        _resumePeakTicks = 0L;
    }

    private void EndLoop()
    {
        _looping = false;

        // A newer import may have been adopted while this one's last hand-over
        // was in flight. Its loop never started, so start it now.
        StartLoop();
    }

    // Polled from the shell's pump. Flags a hand-over that hasn't completed
    // within UpdateWatchdog, resume hop included. Can't unblock it; it stops
    // scheduling and logs.
    internal void CheckForStall()
    {
        if (_stalled || !_outstandingIssues.TryPeek(out long oldest))
            return;

        double waitedSeconds = (Stopwatch.GetTimestamp() - oldest) / (double)Stopwatch.Frequency;
        if (waitedSeconds < UpdateWatchdog.TotalSeconds)
            return;

        _stalled = true;
        _logger.LogError(
            "The compositor has been waiting {Seconds:0.#} s for the engine to release the shared target's " +
            "key. The producer has stopped rendering; the viewport is frozen and the pump has stopped " +
            "scheduling. This is the one ordering the composited path cannot recover from: the consumer's " +
            "acquire has no usable deadline, so the engine must outlive the pump.",
            UpdateWatchdog.TotalSeconds);

        _onFault?.Invoke();
    }

    private void Retire(Import import)
    {
        import.Retired = true;
        _retired.Add(import);
        SettleIfRetired(import);
    }

    private void SettleIfRetired(Import import)
    {
        // Not while a hand-over is in flight: the compositor is inside the
        // keyed-mutex bracket and disposing under it crashes the driver.
        if (!import.Retired || import.UpdatesInFlight > 0)
            return;

        if (!_retired.Remove(import))
            return;

        _ = ReleaseAsync(import);
    }

    private async Task ReleaseAsync(Import import)
    {
        try
        {
            await import.Image.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Releasing shared target generation {Generation} failed.", import.Generation);
        }
        finally
        {
            // In case the import never completed. A second close is a no-op.
            import.CloseHandle(_closeHandle);
        }

        // Only after the import is gone: this frees the renderer's resource,
        // for every generation at or below this one.
        _acknowledgeRelease(import.Generation);

        _outstanding--;
        ReleaseSourceIfSettled();
    }

    // Only after Stop and with no import left: every hand-over snapshots into
    // the source's surface. A pump that never settles leaks it, which beats
    // freeing it under a live mutex bracket.
    private void ReleaseSourceIfSettled()
    {
        if (!_stopped || _sourceReleased || _outstanding > 0)
            return;

        _sourceReleased = true;
        _ = DisposeSourceAsync();
    }

    private async Task DisposeSourceAsync()
    {
        try
        {
            await _source.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Releasing the composited viewport's drawing surface failed.");
        }
    }

    private sealed class Import(int generation, nint handle, ICompositedImage image)
    {
        private nint _handle = handle;

        internal int Generation { get; } = generation;

        internal ICompositedImage Image { get; } = image;

        internal int UpdatesInFlight { get; set; }

        internal bool Retired { get; set; }

        internal void CloseHandle(Action<nint> close)
        {
            if (_handle == 0)
                return;

            nint handle = _handle;
            _handle = 0;
            close(handle);
        }
    }
}
