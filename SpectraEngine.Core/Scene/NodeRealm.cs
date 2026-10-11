namespace SpectraEngine.Core.Scene;

/// <summary>
/// Who holds a node, as the node itself declares it. The answer for a node
/// that declares nothing comes from the nodes above it.
/// </summary>
// Numbered as ScmapNodeRealm is: the compiled map stores these two bits.
public enum NodeRealm : byte
{
    /// <summary>Take the parent's answer. A map leaves it out.</summary>
    Inherit = 0,

    /// <summary>The server and every client.</summary>
    Shared = 1,

    /// <summary>The server only.</summary>
    Server = 2,

    /// <summary>Every client, each with its own copy.</summary>
    Client = 3,
}
