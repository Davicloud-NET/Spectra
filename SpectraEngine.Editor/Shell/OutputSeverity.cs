namespace SpectraEngine.Editor.Shell;

/// <summary>How loud one line in the output is.</summary>
public enum OutputSeverity
{
    /// <summary>Something happened and it worked.</summary>
    Info,

    /// <summary>Something is not right but the editor carried on.</summary>
    Warning,

    /// <summary>Something the user asked for did not happen.</summary>
    Error,

    /// <summary>A line the user typed into the console.</summary>
    Command,
}
