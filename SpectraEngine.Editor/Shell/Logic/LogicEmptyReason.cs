namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>Why a scope shows no cards, for a view to turn into a sentence.</summary>
public enum LogicEmptyReason
{
    /// <summary>It shows cards.</summary>
    None,

    /// <summary>The scope follows the selection and no entity is selected.</summary>
    NoEntitySelected,

    /// <summary>The level has no wires at all, and no entity is selected.</summary>
    LevelHasNoWires,
}
