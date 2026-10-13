using SpectraEngine.Core.Physics.Character;

namespace SpectraEngine.Scripting.Tests;

// Picks what a tick can touch, as every mover does first, and moves nothing.
// What a mover that does nothing costs past this is the seam's own.
internal sealed class BeginOnlyMover : ICharacterMover
{
    public void Tick(
        ref CharacterState state,
        in CharacterCommand command,
        ICharacterCollisionSource source,
        CharacterTuning tuning,
        float deltaTime)
    {
        var filter = CharacterQueryFilter.Default;
        source.BeginTick(CharacterMover.TickVolume(in state, tuning, deltaTime), in filter);
    }
}
