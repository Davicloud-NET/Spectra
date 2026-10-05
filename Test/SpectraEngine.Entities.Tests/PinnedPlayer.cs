using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

// A player that stays where it is put, whatever closes on it. 1.8 tall and
// 0.35 in radius, as the character is.
internal sealed class PinnedPlayer : IPlayerPresence
{
    public const float Height = 1.8f;

    public PinnedPlayer(Vector3 feet) => Feet = feet;

    public Vector3 Feet { get; set; }

    public bool IsPresent { get; set; } = true;

    public CharacterCapsule Capsule => CharacterCapsule.FromFeet(Feet, Height, 0.35f);

    public void Teleport(Vector3 feet, float? yaw = null) => Feet = feet;
}
