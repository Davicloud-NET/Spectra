using SpectraEngine.Core;
using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Follows an engine session from its death to a new session showing the same
/// level, and stops that from looping. UI thread only.
/// </summary>
public sealed class SessionRecovery
{
    /// <summary>A session that dies this soon after a restart is not restarted again unasked.</summary>
    // A fault that comes back with every new session would restart forever.
    public static readonly TimeSpan RetryWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a dead engine is kept answering a viewport that still waits on
    /// it before the shell goes on regardless.
    /// </summary>
    // Going on can freeze the window. Waiting for ever leaves the viewport
    // dead with nothing said, which is no better.
    public static readonly TimeSpan AnswerDeadline = TimeSpan.FromSeconds(2);

    // What the running session was started to show, until it shows it. A
    // session that dies before then never had the level, so its scene is not
    // the one to keep.
    private SessionLaunch? _unconfirmed;

    private TimeSpan? _restartedAt;

    /// <summary>
    /// What the next session is started with: at once after
    /// <see cref="SessionFaultAction.Restart"/>, when the user asks after
    /// <see cref="SessionFaultAction.Stop"/>. Null while a session is running.
    /// </summary>
    public SessionLaunch? Pending { get; private set; }

    /// <summary>Whether the viewport is stopped and waits for <see cref="RestartByHand"/>.</summary>
    public bool IsStopped { get; private set; }

    /// <summary>What to say once the restarted session shows its level, or null.</summary>
    public SessionFaultNotice? Notice { get; private set; }

    /// <summary>A session was started with <paramref name="launch"/>.</summary>
    public void Launched(SessionLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);

        _unconfirmed = launch;
        Pending = null;
        IsStopped = false;
    }

    /// <summary>
    /// The running session shows the level it was started for. Returns what to
    /// tell the user about the restart that led here, or null.
    /// </summary>
    public SessionFaultNotice? LevelShown()
    {
        _unconfirmed = null;

        SessionFaultNotice? notice = Notice;
        Notice = null;
        return notice;
    }

    /// <summary>The running session died. Says what to do and sets <see cref="Pending"/>.</summary>
    /// <param name="now">A clock that only moves forward.</param>
    /// <param name="fault">Why it died.</param>
    /// <param name="wasPlaying">Whether a run was on.</param>
    /// <param name="capture">
    /// Takes the dead session's level as a launch, with no
    /// <see cref="SessionLaunch.Restore"/> when it could not be taken. Called
    /// only when that session had shown its level.
    /// </param>
    public SessionFaultAction Died(TimeSpan now, EngineFault fault, bool wasPlaying, Func<SessionLaunch> capture)
    {
        ArgumentNullException.ThrowIfNull(fault);
        ArgumentNullException.ThrowIfNull(capture);

        LevelOutcome level;
        SessionLaunch launch;

        if (_unconfirmed is { } never)
        {
            launch = never;
            level = never.Restore is null ? LevelOutcome.NotShownYet : LevelOutcome.Kept;

            // A notice nobody has seen yet is about this same level.
            wasPlaying |= Notice?.WasPlaying ?? false;
        }
        else
        {
            launch = capture();
            level = launch.Restore is null ? LevelOutcome.Lost : LevelOutcome.Kept;
        }

        _unconfirmed = null;
        Pending = launch;
        Notice = new SessionFaultNotice(fault, level, wasPlaying, launch.RestoreLoss);

        if (_restartedAt is { } last && now - last < RetryWindow)
        {
            IsStopped = true;
            return SessionFaultAction.Stop;
        }

        _restartedAt = now;
        return SessionFaultAction.Restart;
    }

    /// <summary>
    /// The session a restart was starting could not start at all. Stops the
    /// viewport with the same launch kept, and returns what to tell the user.
    /// Null when no restart was under way, and nothing changes.
    /// </summary>
    public SessionFaultNotice? RestartFailed()
    {
        if (Notice is not { } notice || _unconfirmed is not { } launch)
            return null;

        _unconfirmed = null;
        Pending = launch;
        IsStopped = true;
        return notice;
    }

    /// <summary>
    /// The user asked for a stopped viewport to start again. Returns what to
    /// start it with, or null when it is not stopped.
    /// </summary>
    public SessionLaunch? RestartByHand(TimeSpan now)
    {
        if (!IsStopped || Pending is not { } launch)
            return null;

        IsStopped = false;

        // Counts as a restart: one that dies at once stops again.
        _restartedAt = now;

        if (Notice is { } notice)
            Notice = notice with { Fault = null };

        return launch;
    }

    /// <summary>The session was closed on purpose. Forgets everything.</summary>
    public void Reset()
    {
        _unconfirmed = null;
        _restartedAt = null;
        Pending = null;
        IsStopped = false;
        Notice = null;
    }
}
