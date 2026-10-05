namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>Why an edit of an entity's wires made no list.</summary>
public enum LogicWireRefusal
{
    /// <summary>It made one.</summary>
    None,

    /// <summary>The entity already has a wire like the one to add.</summary>
    AlreadyThere,

    /// <summary>The entity that sends is not in the level any more.</summary>
    SenderGone,

    /// <summary>The entity has no wire at the place to remove one from.</summary>
    WireGone,
}
