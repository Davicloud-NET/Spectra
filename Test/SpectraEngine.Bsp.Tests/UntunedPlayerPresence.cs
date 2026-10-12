using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;

namespace SpectraEngine.Bsp.Tests;

// A player of a host that keeps how it moves to itself: it stands at the
// origin and has no tuning a level can reach.
internal sealed class UntunedPlayerPresence : IPlayerPresence
{
    public bool IsPresent => true;

    public CharacterCapsule Capsule => CharacterCapsule.FromFeet(Vector3.Zero, 1.8f, 0.35f);

    public CharacterState State => CharacterState.AtFeet(Vector3.Zero);

    public void Teleport(Vector3 feet, float? yaw = null)
    {
    }

    public void Respawn()
    {
    }

    public void SetVelocity(Vector3 velocity)
    {
    }
}
