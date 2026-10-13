using SpectraEngine.Core.Physics.Character;
using System.Numerics;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// The player as the entity runtime sees it: a capsule to sense, where it is
/// and how it moves, and a way to move it. The player is neither an entity
/// nor a node.
/// </summary>
// The tuning is not here. It outlives the level, and a level changes it
// through Scripting.PlayerWrites, which records what Stop puts back.
public interface IPlayerPresence
{
    /// <summary>Whether the player is in the level. Nothing else here means anything otherwise.</summary>
    bool IsPresent { get; }

    /// <summary>The player's capsule in world space.</summary>
    CharacterCapsule Capsule { get; }

    /// <summary>
    /// Where the feet are, how fast the player moves, whether it stands and
    /// which way it faces, as of the last tick.
    /// </summary>
    CharacterState State { get; }

    /// <summary>
    /// How far off anything solid the player's mover stops it, in units. A
    /// solid part within twice this is touched. A host whose mover keeps
    /// another gap than the engine's says so here.
    /// </summary>
    // What CharacterTuning starts with.
    float SkinWidth => 0.010f;

    /// <summary>Puts the player at rest with its feet at a point.</summary>
    /// <param name="feet">Where the feet go, in world space.</param>
    /// <param name="yaw">The facing to take, in radians, or null to keep looking the same way.</param>
    void Teleport(Vector3 feet, float? yaw = null);

    /// <summary>Sends the player back to where it started, at rest and facing as it started.</summary>
    void Respawn();

    /// <summary>
    /// Gives the player a velocity, which the next tick carries on from. One
    /// that rises off the ground the player stands on takes it off the ground.
    /// </summary>
    void SetVelocity(Vector3 velocity);
}
