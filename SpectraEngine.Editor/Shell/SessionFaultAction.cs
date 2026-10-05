namespace SpectraEngine.Editor.Shell;

/// <summary>What the shell does about an engine session that died.</summary>
public enum SessionFaultAction
{
    /// <summary>Start a new session with <see cref="SessionRecovery.Pending"/>.</summary>
    Restart,

    /// <summary>
    /// Leave the viewport stopped and keep <see cref="SessionRecovery.Pending"/>
    /// for a restart by hand.
    /// </summary>
    Stop,
}
