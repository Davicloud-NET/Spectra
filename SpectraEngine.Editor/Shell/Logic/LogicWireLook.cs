namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>How a wire is drawn.</summary>
public enum LogicWireLook
{
    /// <summary>Nothing to say about it.</summary>
    Plain,

    /// <summary>It touches a selected card.</summary>
    Focus,

    /// <summary>It can never deliver, or it missed or was refused while the level ran.</summary>
    Broken,

    /// <summary>It fired a moment ago.</summary>
    Firing,

    /// <summary>It has fired, but not just now.</summary>
    Fired,

    /// <summary>It has sent something that is not due yet.</summary>
    Waiting,
}
