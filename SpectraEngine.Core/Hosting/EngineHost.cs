using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace SpectraEngine.Core.Hosting;

/// <summary>
/// What a UI thread gets from a running engine: post work, submit input,
/// ask it to stop, and hear about finished frames. The scene itself stays
/// with the render thread.
/// </summary>
public sealed class EngineHost
{
    /// <summary>How many console lines may wait to run. The next one is refused.</summary>
    public const int MaxQueuedConsoleLines = 4096;

    private readonly ConcurrentQueue<Action<Scene.Scene>> _commands = new();
    private readonly ConcurrentQueue<string> _consoleLines = new();
    private int _queuedConsoleLines;
    private readonly SceneChangeLog _changeLog = new();
    private readonly ILogger _logger;

    private volatile bool _shutdownRequested;
    private long _frameNumber;

    /// <summary>Creates a host that logs the things a shell cannot see.</summary>
    public EngineHost(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    // Null only so a host can be built and tested without one.
    private InputManager? _input;

    internal void AttachInput(InputManager input) => _input = input;

    /// <summary>
    /// Feeds one input event to the engine. Applied at once, not queued. Call
    /// from the thread that owns the window; an embedded engine gets input no other way.
    /// </summary>
    public void SubmitInput(in InputEvent input) => _input?.Submit(in input);

    /// <summary>
    /// The cursor mode the engine is asking for, whether or not it has been
    /// applied. See <see cref="ApplyPendingCursorMode"/>.
    /// </summary>
    public CursorMode RequestedCursorMode => _input?.RequestedCursorMode ?? CursorMode.Normal;

    /// <summary>
    /// Acknowledges the requested cursor mode. An embedded host polls
    /// <see cref="RequestedCursorMode"/>, captures the pointer itself, then calls this.
    /// </summary>
    public void ApplyPendingCursorMode() => _input?.ApplyPendingCursorMode();

    /// <summary>The pointer shape most recently requested by whatever is under the cursor.</summary>
    // A poll, not an event: on Windows the host must answer inside WM_SETCURSOR,
    // since a SetCursor issued anywhere else is reverted on the next mouse move.
    public CursorShape RequestedCursorShape => _input?.CursorShape ?? CursorShape.Arrow;

    /// <summary>
    /// Raised on the render thread once per published snapshot. A handler runs
    /// inside the engine's frame: stash the snapshot, post to the UI thread, return.
    /// </summary>
    public event Action<FrameSnapshot>? FrameCompleted;

    /// <summary>
    /// How often a snapshot is published, about thirty a second by default.
    /// Structural changes are never dropped by this; they ride the next snapshot.
    /// </summary>
    public TimeSpan SnapshotInterval { get; set; } = TimeSpan.FromMilliseconds(33);

    /// <summary>
    /// The interval used while the editor is mid-gesture, about 120 a second by default.
    /// A shell that drains snapshots should size its queue in time, not in count.
    /// </summary>
    public TimeSpan InteractiveSnapshotInterval { get; set; } = TimeSpan.FromMilliseconds(8);

    /// <summary>The most recently published snapshot, or <see cref="FrameSnapshot.Empty"/>.</summary>
    public FrameSnapshot LastSnapshot { get; private set; } = FrameSnapshot.Empty;

    /// <summary>True once <see cref="RequestShutdown"/> has been called.</summary>
    public bool ShutdownRequested => _shutdownRequested;

    /// <summary>How many commands are waiting to run.</summary>
    public int PendingCommandCount => _commands.Count;

    /// <summary>
    /// Queues work to run on the render thread in the next frame, against
    /// whichever scene is active then. Safe from any thread. A command that
    /// throws is logged and the frame continues.
    /// </summary>
    public void EnqueueCommand(Action<Scene.Scene> command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Enqueue(command);

        // Publish the echo on the next frame instead of waiting for the interval.
        _stateDirty = true;
    }

    /// <summary>
    /// Queues a typed console line to run on the render thread, as typed. Safe
    /// from any thread. Lines run in the order they were submitted, and what
    /// they print comes back in <see cref="FrameSnapshot.ConsoleLines"/>.
    /// </summary>
    /// <returns>False when <see cref="MaxQueuedConsoleLines"/> are already waiting.</returns>
    // A queue, not a latch: a line is a command and every one must run.
    public bool SubmitConsoleLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (Interlocked.Increment(ref _queuedConsoleLines) > MaxQueuedConsoleLines)
        {
            Interlocked.Decrement(ref _queuedConsoleLines);
            return false;
        }

        _consoleLines.Enqueue(line);
        MarkDirty();
        return true;
    }

    /// <summary>
    /// Asks the engine to stop after the current frame. Safe to call from any
    /// thread, and idempotent.
    /// </summary>
    public void RequestShutdown() => _shutdownRequested = true;

    // Play mode, debug visualisations and the pipeline are engine state, which
    // EnqueueCommand's Action<Scene> cannot reach. Each is a last-write-wins
    // latch the render loop takes where the matching key press is read.
    // A queue would replay stale intermediate clicks.

    private volatile bool _stateDirty;

    // Forces a publish on the next frame. Every request latch calls it.
    internal void MarkDirty() => _stateDirty = true;

    private int _playModeRequest = -1;
    private int _debugFlagsToSet;
    private int _debugFlagsToClear;
    private string? _pipelineRequest;

    // Not marked dirty: no panel displays it, and it changes per resize step.
    private int _sharedTargetReleased;

    /// <summary>
    /// Asks the engine to enter or leave play mode. Idempotent, safe from any
    /// thread. The outcome is reported by <see cref="FrameSnapshot.IsPlaying"/>;
    /// a scene with <see cref="FrameSnapshot.CanPlay"/> false ignores it.
    /// </summary>
    public void RequestPlayMode(bool active)
    {
        Interlocked.Exchange(ref _playModeRequest, active ? 1 : 0);
        MarkDirty();
    }

    /// <summary>
    /// Asks the engine to turn the given debug visualisations on or off.
    /// Idempotent, and safe from any thread; flags accumulate until the render
    /// loop applies them, with the newest request winning where two conflict.
    /// </summary>
    // Set, not toggle: a toggle sent against a stale snapshot flips the wrong way.
    public void RequestDebugVisualization(DebugVisualization flags, bool enabled)
    {
        int bits = (int)flags;
        if (enabled)
        {
            Interlocked.Or(ref _debugFlagsToSet, bits);
            Interlocked.And(ref _debugFlagsToClear, ~bits);
        }
        else
        {
            Interlocked.Or(ref _debugFlagsToClear, bits);
            Interlocked.And(ref _debugFlagsToSet, ~bits);
        }

        MarkDirty();
    }

    /// <summary>
    /// Asks the engine to switch to the named rendering pipeline. A name the
    /// backend does not offer is a logged warning and no change. Safe from any thread.
    /// </summary>
    public void RequestPipeline(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Interlocked.Exchange(ref _pipelineRequest, name);
        MarkDirty();
    }

    private LogicViewRequest? _logicViewRequest;

    /// <summary>
    /// Tells the engine whether a wiring view is showing. While one is,
    /// snapshots carry <see cref="FrameSnapshot.LogicGraph"/>. The newest
    /// request wins. Safe from any thread.
    /// </summary>
    public void RequestLogicView(LogicViewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Interlocked.Exchange(ref _logicViewRequest, request);
        MarkDirty();
    }

    /// <summary>
    /// Reports that a composited host has finished with every shared-target
    /// generation up to and including <paramref name="generation"/>, so the
    /// renderer may free them. Safe from any thread.
    /// </summary>
    public void NotifySharedTargetReleased(int generation)
    {
        // Keeps the maximum, so a retry or a second host cannot walk it backwards.
        int seen = Volatile.Read(ref _sharedTargetReleased);
        while (generation > seen)
        {
            int previous = Interlocked.CompareExchange(ref _sharedTargetReleased, generation, seen);
            if (previous == seen)
                break;
            seen = previous;
        }
    }

    // The Take* methods below are render thread only.

    internal bool TryTakeSharedTargetRelease(out int generation)
    {
        generation = Interlocked.Exchange(ref _sharedTargetReleased, 0);
        return generation > 0;
    }

    internal bool TryTakePlayModeRequest(out bool enter)
    {
        int request = Interlocked.Exchange(ref _playModeRequest, -1);
        enter = request == 1;
        return request >= 0;
    }

    // Caller applies set before clear, so a write landing between the two
    // exchanges ends up cleared. A missing overlay costs one more click.
    internal void TakeDebugVisualizationRequests(out DebugVisualization set, out DebugVisualization clear)
    {
        set = (DebugVisualization)Interlocked.Exchange(ref _debugFlagsToSet, 0);
        clear = (DebugVisualization)Interlocked.Exchange(ref _debugFlagsToClear, 0);
    }

    internal string? TakeRequestedPipeline() =>
        Interlocked.Exchange(ref _pipelineRequest, null);

    internal bool TryTakeLogicViewRequest([NotNullWhen(true)] out LogicViewRequest? request)
    {
        request = Interlocked.Exchange(ref _logicViewRequest, null);
        return request is not null;
    }

    internal bool TryTakeConsoleLine([NotNullWhen(true)] out string? line)
    {
        if (!_consoleLines.TryDequeue(out line))
            return false;

        Interlocked.Decrement(ref _queuedConsoleLines);
        return true;
    }

    /// <summary>
    /// Runs queued commands against the active scene, at most
    /// <paramref name="maxPerFrame"/> per call. Render thread, once per frame,
    /// before the static-world compile pump so an edit and its recompile share a frame.
    /// </summary>
    public void DrainCommands(Scene.Scene? scene, int maxPerFrame = 256)
    {
        // No scene yet: commands stay queued.
        if (scene is null)
            return;

        for (int i = 0; i < maxPerFrame && _commands.TryDequeue(out Action<Scene.Scene>? command); i++)
        {
            try
            {
                command(scene);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A host command threw; the frame continues");
            }
        }
    }

    /// <summary>
    /// Points the change log at the scene whose structure should be reported.
    /// Called on the render thread whenever the active scene changes.
    /// </summary>
    public void ObserveScene(Scene.Scene? scene) => _changeLog.Observe(scene);

    /// <summary>
    /// Publishes a snapshot if the interval has passed or there is news that
    /// should not wait, and returns it; null when none was due. Render thread,
    /// end of frame.
    /// </summary>
    /// <param name="elapsed">The engine's total elapsed time.</param>
    /// <param name="build">Produces the frame's values. Invoked only when a snapshot goes out.</param>
    /// <param name="interactive">True while a gesture is in flight, which selects <see cref="InteractiveSnapshotInterval"/>.</param>
    public FrameSnapshot? PublishFrame(
        TimeSpan elapsed,
        Func<FrameSnapshotBuilder, FrameSnapshot> build,
        bool interactive = false)
    {
        ArgumentNullException.ThrowIfNull(build);

        _frameNumber++;

        // Nullable, not a TimeSpan.MinValue sentinel: that overflows the subtraction.
        TimeSpan interval = interactive ? InteractiveSnapshotInterval : SnapshotInterval;
        bool due = _lastPublished is not { } last || elapsed - last >= interval;

        // Structural changes and user-requested state go out regardless of the clock.
        if (!due && !_stateDirty && _changeLog.Count == 0 && !_changeLog.Overflowed)
            return null;

        _lastPublished = elapsed;
        _stateDirty = false;

        (IReadOnlyList<SceneChange> changes, bool overflowed) = _changeLog.Drain();
        FrameSnapshot snapshot = build(new FrameSnapshotBuilder(_frameNumber, changes, overflowed));

        LastSnapshot = snapshot;
        FrameCompleted?.Invoke(snapshot);
        return snapshot;
    }

    private TimeSpan? _lastPublished;
}

/// <summary>
/// The parts of a <see cref="FrameSnapshot"/> the host already knows, handed to
/// the engine so it only has to add what it alone can read.
/// </summary>
/// <param name="FrameNumber">How many frames have completed.</param>
/// <param name="Changes">The structural changes since the previous snapshot.</param>
/// <param name="ChangesOverflowed">True when changes were dropped and <paramref name="Changes"/> is incomplete.</param>
public readonly record struct FrameSnapshotBuilder(
    long FrameNumber,
    IReadOnlyList<SceneChange> Changes,
    bool ChangesOverflowed);
