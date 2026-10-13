using System.Numerics;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;

namespace SpectraEngine.Physics.Tests;

// A mover of a few lines, written against what every mover is handed: it
// falls, stops a skin above what is under it, and counts its ticks in the
// air in the state's memory.
internal sealed class FallingMover : ICharacterMover
{
    public const int AirTicksSlot = 0;

    public int Ticks { get; private set; }

    public void Tick(
        ref CharacterState state,
        in CharacterCommand command,
        ICharacterCollisionSource source,
        CharacterTuning tuning,
        float deltaTime)
    {
        Ticks++;
        var filter = CharacterQueryFilter.Default;
        source.BeginTick(CharacterMover.TickVolume(in state, tuning, deltaTime), in filter);

        state.Velocity += PhysicsDefaults.Gravity * deltaTime;
        Vector3 fall = state.Velocity * deltaTime;
        float went = source.SweepCapsule(
            CharacterMover.CapsuleAt(in state, tuning), fall, in filter, out CharacterContactPlane plane, out _);
        state.Position += fall * went;

        state.Grounded = went < 1f && plane.Plane.Normal.Y > 0.7f;
        if (state.Grounded)
        {
            state.GroundNormal = plane.Plane.Normal;
            state.Velocity = Vector3.Zero;
        }
        else
        {
            state.Memory[AirTicksSlot] += 1f;
        }
    }
}
