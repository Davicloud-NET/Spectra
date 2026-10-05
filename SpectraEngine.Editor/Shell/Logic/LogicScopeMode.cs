namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>How much of a level's wiring a view shows.</summary>
public enum LogicScopeMode
{
    /// <summary>Every card.</summary>
    WholeLevel,

    /// <summary>The selected entities and what is wired to them, a few steps out.</summary>
    AroundSelection,
}
