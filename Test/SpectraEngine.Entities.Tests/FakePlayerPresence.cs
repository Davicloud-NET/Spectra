using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

// A player a test puts where it likes: a standing capsule at Feet.
internal sealed class FakePlayerPresence : IPlayerPresence
{
    public const float Height = 1.8f;

    public const float Radius = 0.35f;

    public bool IsPresent { get; set; } = true;

    public Vector3 Feet { get; set; }

    public CharacterCapsule Capsule => CharacterCapsule.FromFeet(Feet, Height, Radius);

    public List<(Vector3 Feet, float? Yaw)> Teleports { get; } = [];

    public void Teleport(Vector3 feet, float? yaw = null)
    {
        Feet = feet;
        Teleports.Add((feet, yaw));
    }
}
