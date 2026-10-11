namespace SpectraEngine.Core.Scripting;

/// <summary>
/// What a script can set on a node. <see cref="ScriptWrites.Settable"/>
/// lists them in this order.
/// </summary>
public enum SettableMemberId
{
    /// <summary>The node's position in the world.</summary>
    Position,

    /// <summary>The node's position and rotation in the world.</summary>
    CFrame,

    /// <summary>The node's position under its parent.</summary>
    LocalPosition,

    /// <summary>The node's position and rotation under its parent.</summary>
    LocalCFrame,

    /// <summary>The node's name, which is also the name a wire finds its entity by.</summary>
    Name,
}
