using System;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// The per-node physics and query bits, packed into one byte on
/// <see cref="SceneNode"/>.
/// </summary>
[Flags]
public enum PhysicsFlags : byte
{
    /// <summary>Nothing set: invisible to collision, to queries and to touch.</summary>
    None = 0,

    /// <summary>The node's geometry participates in collision.</summary>
    CanCollide = 1 << 0,

    /// <summary>
    /// The node's geometry is visible to raycasts, overlaps and shape casts.
    /// Independent of <see cref="CanCollide"/>, unlike in Roblox.
    /// </summary>
    CanQuery = 1 << 1,

    /// <summary>The node generates touch/trigger events.</summary>
    CanTouch = 1 << 2,

    /// <summary>
    /// The node is not moved by simulation. On by default, unlike a Roblox
    /// part; creating a part clears it.
    /// </summary>
    Anchored = 1 << 3,

    /// <summary>The node contributes no mass to a body it is attached to.</summary>
    Massless = 1 << 4,

    /// <summary>
    /// A physics body exists for this node. Owned by the physics layer; user
    /// code must not write it.
    /// </summary>
    HasBody = 1 << 5,

    /// <summary>Solid, queryable, touchable and not simulated.</summary>
    Default = CanCollide | CanQuery | CanTouch | Anchored,
}
