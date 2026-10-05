namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>What a card stands for when a wire's target is not an entity in the level.</summary>
public enum LogicStubKind
{
    /// <summary>An entity. Not a stub.</summary>
    None,

    /// <summary>Whoever set the wire off, known only while the level plays.</summary>
    Activator,

    /// <summary>A name no entity has.</summary>
    MissingName,

    /// <summary>A prefix no entity's name starts with.</summary>
    MissingPrefix,

    /// <summary>A wire whose target was left empty.</summary>
    NoTarget,
}
