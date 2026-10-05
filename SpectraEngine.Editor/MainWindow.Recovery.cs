using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Projects;
using SpectraEngine.Editor.Shell;
using System;

namespace SpectraEngine.Editor;

// What the window does when the engine under the viewport dies: the calls in
// order. The decisions are SessionRecovery's.
public partial class MainWindow
{
    private readonly SessionRecovery _recovery = new();

    // One death is handled once, however many pump passes see it.
    private bool _deathNoticed;

    // When the pump first saw the engine had faulted.
    private TimeSpan? _dyingSince;

    // Environment.TickCount64 never goes back, which a wall clock can.
    private static TimeSpan SessionClock => TimeSpan.FromMilliseconds(Environment.TickCount64);

    // Called on every pump pass once the engine has faulted, until it is
    // through. A composited viewport has frames on order, the compositor waits
    // for each with no deadline, and the dying engine goes on answering. So:
    // stop ordering, wait for the last answer, then let the engine go.
    private void FollowDeath(EditorSession dying)
    {
        if (_viewport is { } viewport)
        {
            viewport.Host = null;

            TimeSpan since = _dyingSince ??= SessionClock;
            if (viewport.IsAwaitingEngine)
            {
                if (SessionClock - since < SessionRecovery.AnswerDeadline)
                    return;

                // Once: the request below is what ends the passes that get here.
                if (!dying.Host.ShutdownRequested)
                {
                    _logger.LogWarning(
                        "The viewport still waits on the engine that died, {Seconds} s on. Going on without it.",
                        SessionRecovery.AnswerDeadline.TotalSeconds);
                }
            }
        }

        dying.Host.RequestShutdown();

        if (!dying.HasDied)
            return;

        _deathNoticed = true;

        // Behind everything the dead session already posted: a load's reply
        // renames the document, and must land before the level is taken
        // under that name.
        Dispatcher.UIThread.Post(() => OnSessionDied(dying), DispatcherPriority.Background);
    }

    // The engine's render thread ended on an exception. Takes the level out of
    // the dead session, then starts a new one with it or leaves the viewport
    // stopped.
    private void OnSessionDied(EditorSession dead)
    {
        // Closed or replaced while this waited its turn.
        if (!ReferenceEquals(dead, _session) || dead.Fault is not { } fault || _viewport is not { } viewport)
            return;

        // The shell's own flag too: the snapshot that would have confirmed
        // a Play may have died with the engine.
        bool wasPlaying = _latest.IsPlaying || _shell.IsPlaying;

        // Before the detach: it stops the session, which drops the scene.
        SessionFaultAction action = _recovery.Died(SessionClock, fault, wasPlaying, () => TakeLevel(dead));

        _sessionEngineDied = true;
        RecordSessionOutcome();
        DetachViewport(viewport);

        // No session for now, so no verb may stay enabled.
        _shell.ClearFilter();
        _shell.ApplySnapshot(FrameSnapshot.Empty);

        if (action is SessionFaultAction.Restart && _recovery.Pending is { } launch)
        {
            LaunchSession(launch);
            return;
        }

        if (_recovery.Notice is { } notice)
            _shell.SetError(SessionFaultText.Stopped(notice, Program.LogFolder));
    }

    // The dead session's level as a launch. It has no Restore when the scene
    // could not be read, and the new session then starts from the last save.
    private SessionLaunch TakeLevel(EditorSession dead)
    {
        ProjectLayout? project = _document.Project;

        try
        {
            var lost = new MapSaveReport();
            if (dead.CaptureLevelAfterFault(lost) is { } remains)
            {
                // The last edits may have died with the frame that would
                // have published them.
                if (remains.UndoDepth != _lastUndoDepth || remains.RedoDepth != _lastRedoDepth)
                    _document.MarkDirty();

                return new SessionLaunch(project, project?.AssetsPath, null, remains.Level, lost.Describe());
            }
        }
        // The boundary for a scene the fault left unreadable: log it and fall
        // back to the last save.
        catch (Exception ex)
        {
            _logger.LogError(ex, "The level could not be taken from the engine session that died");
        }

        return new SessionLaunch(project, project?.AssetsPath, _document.MapPath);
    }

    // Puts back a level taken from a session that died. The document keeps
    // its path and its dirty mark: it is the same level.
    private void RestoreLevel(EditorSession session, MapDocument level)
    {
        session.ApplyMap(level, (report, error) => Dispatcher.UIThread.Post(() =>
        {
            // Stale-session guard, as in OnNewMapClicked.
            if (!ReferenceEquals(session, _session))
                return;

            if (error is not null)
            {
                // Forget the path too, or a save writes this baseplate over
                // the level on disk.
                _recovery.LevelShown();
                _document.MarkNew();
                _shell.SetError(SessionFaultText.RestoreFailed(error.Message));
                return;
            }

            ResetDirtyBaseline();

            _shell.Problems.ClearScope(ProblemScope.Map);
            RecordMapProblems(report);
            RequestEntityAudit();

            ReportRestart();
        }));
    }

    // Says what a restart after a fault did, now that the new session shows
    // its level. False when no restart led here.
    private bool ReportRestart()
    {
        if (_recovery.LevelShown() is not { } notice)
            return false;

        _shell.SetWarning(SessionFaultText.Restarted(notice, _document.HasMapPath, Program.LogFolder));
        return true;
    }

    // The console's restart verb. False when the viewport is not stopped.
    private bool RestartStoppedViewport()
    {
        if (_recovery.RestartByHand(SessionClock) is not { } launch)
            return false;

        LaunchSession(launch);
        return true;
    }
}
