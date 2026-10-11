namespace SpectraEngine.Scripting.Luau;

/// <summary>One step of a walk over a table.</summary>
public enum LuauWalk
{
    /// <summary>A key and its value were pushed.</summary>
    Pair,

    /// <summary>The walk is over and nothing was pushed.</summary>
    End,

    /// <summary>The step could not be made and an error value was pushed.</summary>
    Error,
}
