using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;

namespace SpectraEngine.Bsp.Tests;

// A player a test puts where it likes: a standing capsule at Feet.
internal sealed class FakePlayerPresence : IPlayerPresence
{
    public const float Height = 1.8f;

    public const float Radius = 0.35f;

    public bool IsPresent { get; set; } = true;

    public Vector3 Feet { get; set; }

    public Vector3 Velocity { get; set; }

    public float Yaw { get; set; }

    public CharacterCapsule Capsule => CharacterCapsule.FromFeet(Feet, Height, Radius);

    public CharacterState State
    {
        get
        {
            CharacterState state = CharacterState.AtFeet(Feet);
            state.Velocity = Velocity;
            return state;
        }
    }

    public CharacterTuning Tuning { get; } = new();

    public List<(Vector3 Feet, float? Yaw)> Teleports { get; } = [];

    public int Respawns { get; private set; }

    public void Teleport(Vector3 feet, float? yaw = null)
    {
        Feet = feet;
        Yaw = yaw ?? Yaw;
        Teleports.Add((feet, yaw));
    }

    public void Respawn() => Respawns++;

    public void SetVelocity(Vector3 velocity) => Velocity = velocity;
}
