namespace SpectraEngine.Scripting.Luau;

/// <summary>How a thread came back from being started or resumed.</summary>
public enum LuauResume
{
    /// <summary>It waits. What it yielded is in its values.</summary>
    Yielded,

    /// <summary>It ran to its end. What it returned is in its values until the next thread ends.</summary>
    Finished,

    /// <summary>It ended with an error, which went to the sink.</summary>
    Failed,

    /// <summary>It did not run: it was not waiting, its script is switched off or the host is closed.</summary>
    Refused,
}
