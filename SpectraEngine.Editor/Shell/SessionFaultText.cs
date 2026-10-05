using System.Text;

namespace SpectraEngine.Editor.Shell;

/// <summary>The words the shell uses about an engine session that died.</summary>
// The status line trims a long message, so each one leads with what happened
// and what to do. The log's folder comes last.
public static class SessionFaultText
{
    /// <summary>The console verb that restarts a stopped viewport.</summary>
    public const string RestartVerb = "restart";

    /// <summary>The line for a save asked for while the viewport is stopped.</summary>
    public const string SaveWhileStopped =
        "The viewport is stopped, so the level cannot be saved yet. Type " + RestartVerb + " in the console first.";

    /// <summary>The line for a viewport that is drawing again.</summary>
    /// <param name="notice">What happened.</param>
    /// <param name="hasSavedLevel">Whether the level has a save on disk it was opened from again.</param>
    /// <param name="logFolder">Where the run log is written.</param>
    public static string Restarted(SessionFaultNotice notice, bool hasSavedLevel, string logFolder)
    {
        var text = new StringBuilder(notice.Fault switch
        {
            null => "The viewport was restarted.",
            { IsDeviceLoss: true } => "The graphics device was lost, so the viewport was restarted.",
            _ => "The engine stopped on an error, so the viewport was restarted.",
        });

        switch (notice.Level)
        {
            case LevelOutcome.Kept:
                text.Append(" The level is as you left it. Undo history and the selection did not survive.");
                break;

            case LevelOutcome.Lost when hasSavedLevel:
                text.Append(
                    " The level could not be kept, so it was opened again from its last save. " +
                    "Changes since then are lost.");
                break;

            case LevelOutcome.Lost:
                text.Append(" The level could not be kept, so this is a new one.");
                break;
        }

        if (notice.WasPlaying)
            text.Append(" The run was stopped.");

        if (notice.Loss is { } loss)
            text.Append(" Not everything came back: ").Append(loss).Append('.');

        return text.Append(Details(notice, logFolder)).ToString();
    }

    /// <summary>The line for a viewport that died again and was left stopped.</summary>
    public static string Stopped(SessionFaultNotice notice, string logFolder) =>
        (notice.Fault is { IsDeviceLoss: true }
            ? "The graphics device was lost again right after the viewport restarted, so it was left stopped."
            : "The engine stopped on an error again right after the viewport restarted, so it was left stopped.")
        + WhileStopped(notice)
        + Details(notice, logFolder);

    /// <summary>The line for a restart whose session could not start at all.</summary>
    /// <param name="notice">What happened before.</param>
    /// <param name="reason">Why the session could not start, as a sentence.</param>
    public static string StartFailed(SessionFaultNotice notice, string reason) =>
        $"The viewport could not be started again: {reason}" + WhileStopped(notice);

    /// <summary>The line for a kept level the new session could not take.</summary>
    /// <param name="reason">Why, as a sentence.</param>
    public static string RestoreFailed(string reason) =>
        $"The level could not be put back after the restart: {reason} " +
        "This is a new level. The one on disk was not touched.";

    // The way out, then what became of the level.
    private static string WhileStopped(SessionFaultNotice notice) =>
        $" Type {RestartVerb} in the console to try again."
        + notice.Level switch
        {
            LevelOutcome.Kept => " The level is kept.",
            LevelOutcome.Lost => " The level could not be kept.",
            _ => string.Empty,
        };

    // A lost device explains itself. Any other fault is only explained by the log.
    private static string Details(SessionFaultNotice notice, string logFolder) =>
        notice.Fault is { IsDeviceLoss: false } ? $" The log in {logFolder} has the details." : string.Empty;
}
