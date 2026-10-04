using System;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// What a compiled node's payload is, in a node record's <c>PayloadKind</c>.
/// </summary>
public enum ScmapPayloadKind : ushort
{
    /// <summary>No payload: a group, a folder, a transform anchor.</summary>
    None = 0,

    /// <summary>
    /// A world brush whose geometry is already inside the chunk meshes. It must
    /// not be carved again on load.
    /// </summary>
    StaticWorldBrush = 1,

    /// <summary>
    /// A standalone brush: never carved, never fused, drawing from its own
    /// brush-local mesh under the node's world matrix.
    /// </summary>
    PartBrush = 2,

    /// <summary>
    /// Retired. Never written, and refused on read. Do not reuse the value.
    /// </summary>
    RetiredBrushModel = 3,

    /// <summary>A mesh instance: the node names a model and a submesh within it.</summary>
    MeshInstance = 4,

    /// <summary>The root of an instantiated prefab.</summary>
    PrefabRoot = 5,
}

/// <summary>
/// The declared realm of a compiled node, in <c>PayloadFlags</c> bits 3 and 4.
/// This is the node's own value, not the one resolved through its ancestors.
/// </summary>
// The numbering is the file format's. An engine realm enum must match it.
public enum ScmapNodeRealm : byte
{
    /// <summary>Take the parent's answer.</summary>
    Inherit = 0,

    /// <summary>Present on the server and on every client.</summary>
    Shared = 1,

    /// <summary>Server only.</summary>
    Server = 2,

    /// <summary>Client only.</summary>
    Client = 3,
}

/// <summary>
/// The declared state of a compiled node, in <c>PayloadFlags</c> bits 5 and 6.
/// The reader counts an <see cref="Invalid"/> node and carries on; the writer refuses one.
/// </summary>
public enum ScmapNodeState : byte
{
    /// <summary>Take the parent's answer.</summary>
    Inherit = 0,

    /// <summary>Simulating and rendering.</summary>
    Active = 1,

    /// <summary>Present but not simulating.</summary>
    Dormant = 2,

    /// <summary>Not a state. The unused encoding of a two-bit field.</summary>
    Invalid = 3,
}

/// <summary>
/// The bit flags of a compiled node record's <c>PayloadFlags</c> half-word.
/// Bits 3 to 6 are the two-bit realm and state fields, not flags: read them through
/// <see cref="ScmapNodeRecord.DeclaredRealm"/> and <see cref="ScmapNodeRecord.DeclaredState"/>.
/// </summary>
[Flags]
public enum ScmapPayloadFlags : ushort
{
    /// <summary>Nothing declared.</summary>
    None = 0,

    /// <summary>This node's authored source survives in <c>BRSH</c>.</summary>
    HasSource = 1 << 0,

    /// <summary>An entity owns this node's payload.</summary>
    IsEntityOwned = 1 << 1,

    /// <summary>The runtime may re-carve this brush, which requires <see cref="HasSource"/>.</summary>
    CanReCarve = 1 << 2,

    /// <summary>
    /// The brush subtracts rather than adds. Only meaningful on the two brush
    /// kinds; a reader ignores it on any other.
    /// </summary>
    SubtractiveBrush = 1 << 7,
}
