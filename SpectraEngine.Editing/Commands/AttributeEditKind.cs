namespace SpectraEngine.Editing.Commands;

/// <summary>What an <see cref="AttributeEdit"/> does to the attribute it names.</summary>
public enum AttributeEditKind
{
    /// <summary>Gives it a value, on every selected node. A node that lacks it gets it.</summary>
    Set,

    /// <summary>Replaces one number of its value, and leaves each node's other numbers alone.</summary>
    SetPart,

    /// <summary>Takes it off every selected node that has it.</summary>
    Remove,

    /// <summary>Gives it another name, on every selected node that has it.</summary>
    Rename,
}
