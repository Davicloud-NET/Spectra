namespace SpectraEngine.Core.Scripting;

/// <summary>What kind of line a script reported.</summary>
public enum ScriptSeverity
{
    /// <summary>A line from <c>print</c>.</summary>
    Print,

    /// <summary>A line from <c>warn</c>.</summary>
    Warning,

    /// <summary>An error nothing caught, or a script that did not compile.</summary>
    Error,
}
