namespace SpectraEngine.Core.Scene;

/// <summary>
/// Whether a node takes part in the level, as the node itself declares it. The
/// answer for a node that declares nothing comes from the nodes above it.
/// </summary>
// Numbered as ScmapNodeState is: the compiled map stores these two bits.
public enum NodeState : byte
{
    /// <summary>Take the parent's answer. A map leaves it out.</summary>
    Inherit = 0,

    /// <summary>Part of the level.</summary>
    Active = 1,

    /// <summary>Kept in the level and left out of it: a template to copy later.</summary>
    Dormant = 2,
}
