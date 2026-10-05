namespace SpectraEngine.Editor.Shell;

/// <summary>How a level came through the death of its engine session.</summary>
public enum LevelOutcome
{
    /// <summary>It was taken from the dead session, so the new one shows it as the user left it.</summary>
    Kept,

    /// <summary>It could not be taken, so the new session starts from the last save or a new level.</summary>
    Lost,

    /// <summary>The dead session had not shown it yet, so the new one is started the same way.</summary>
    NotShownYet,
}
