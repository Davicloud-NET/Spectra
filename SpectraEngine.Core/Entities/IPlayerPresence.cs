using SpectraEngine.Core.Physics.Character;
using System.Numerics;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// The player as the entity runtime sees it: a capsule to sense and a way to
/// move it. The player is neither an entity nor a node.
/// </summary>
public interface IPlayerPresence
{
    /// <summary>Whether the player is in the level. The capsule means nothing otherwise.</summary>
    bool IsPresent { get; }

    /// <summary>The player's capsule in world space.</summary>
    CharacterCapsule Capsule { get; }

    /// <summary>Puts the player at rest with its feet at a point.</summary>
    /// <param name="feet">Where the feet go, in world space.</param>
    /// <param name="yaw">The facing to take, in radians, or null to keep looking the same way.</param>
    void Teleport(Vector3 feet, float? yaw = null);
}
