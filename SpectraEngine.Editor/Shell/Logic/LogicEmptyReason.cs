namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>Why a scope shows no cards, for a view to turn into a sentence.</summary>
public enum LogicEmptyReason
{
    /// <summary>It shows cards.</summary>
    None,

    /// <summary>The scope follows the selection and nothing is selected.</summary>
    NothingSelected,

    /// <summary>The scope follows the selection and nothing selected has a wire.</summary>
    SelectionHasNoWires,

    /// <summary>The level has no wires at all.</summary>
    LevelHasNoWires,
}
