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

    /// <summary>Whether a light shines.</summary>
    Enabled,

    /// <summary>What a light's colour is multiplied by.</summary>
    Brightness,

    /// <summary>A light's colour, as a person picks one.</summary>
    Color,

    /// <summary>How far a light reaches. A directional light has none.</summary>
    Range,

    /// <summary>Whether a part or a mesh is solid.</summary>
    CanCollide,

    /// <summary>Whether a ray finds a part or a mesh.</summary>
    CanQuery,

    /// <summary>Whether a part or a mesh notices a touch.</summary>
    CanTouch,

    /// <summary>Whether a part or a mesh is drawn.</summary>
    Visible,

    /// <summary>The node above the node in the tree, or none for a node that is in no tree.</summary>
    Parent,
}
