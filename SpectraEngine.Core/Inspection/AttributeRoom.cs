using System;

namespace SpectraEngine.Core.Inspection;

/// <summary>
/// What stands in the way of giving a node an attribute of a name it does
/// not have yet. More than one can hold, on one node and over several.
/// </summary>
[Flags]
public enum AttributeRoom
{
    /// <summary>Nothing does.</summary>
    Open = 0,

    /// <summary>The node is the level's root, and a map saves nothing of the root.</summary>
    Root = 1,

    /// <summary>The node holds as many attributes as a node can.</summary>
    Full = 2,

    /// <summary>The node's entity has a keyvalue of the name, which scripts read as the attribute.</summary>
    Entity = 4,
}
