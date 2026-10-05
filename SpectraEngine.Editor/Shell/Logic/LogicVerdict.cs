namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>Whether a wire can do anything, as far as the schemas can tell.</summary>
public enum LogicVerdict
{
    /// <summary>Nothing wrong that can be seen before the level plays.</summary>
    Fine,

    /// <summary>The target names no entity.</summary>
    TargetMissing,

    /// <summary>The target's class is known and has no such input.</summary>
    NoSuchInput,

    /// <summary>The sender's class is known and has no such output, so the wire never fires.</summary>
    NoSuchOutput,
}
