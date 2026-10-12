using SpectraEngine.Core.Physics.Character;
using System.Numerics;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// The player as the entity runtime sees it: a capsule to sense, where it is
/// and how it moves, and a way to move it. The player is neither an entity
/// nor a node.
/// </summary>
public interface IPlayerPresence
{
    /// <summary>Whether the player is in the level. Nothing else here means anything otherwise.</summary>
    bool IsPresent { get; }

    /// <summary>The player's capsule in world space.</summary>
    CharacterCapsule Capsule { get; }

    /// <summary>Where the feet are, how fast the player moves and whether it stands, as of the last tick.</summary>
    CharacterState State { get; }

    /// <summary>The way the player faces, in radians, as of the last tick.</summary>
    float Yaw { get; }

    /// <summary>
    /// How the player moves. A change takes effect on the next tick and
    /// outlives the level, so a running level changes it only through what
    /// records the change, as <see cref="Scripting.PlayerWrites"/> does.
    /// </summary>
    CharacterTuning Tuning { get; }

    /// <summary>Puts the player at rest with its feet at a point.</summary>
    /// <param name="feet">Where the feet go, in world space.</param>
    /// <param name="yaw">The facing to take, in radians, or null to keep looking the same way.</param>
    void Teleport(Vector3 feet, float? yaw = null);

    /// <summary>Sends the player back to where it started, at rest and facing as it started.</summary>
    void Respawn();

    /// <summary>
    /// Gives the player a velocity, which the next tick carries on from. One
    /// that points up takes it off the ground.
    /// </summary>
    void SetVelocity(Vector3 velocity);
}
