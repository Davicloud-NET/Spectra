using SpectraEngine.Core.Physics.Character;

namespace SpectraEngine.Bsp.Tests;

// Moves nobody. Logs one line per character tick, into a list a test shares
// with other recorders to assert the order across them.
internal sealed class RecordingMover : ICharacterMover
{
    private readonly List<string> _log;

    public RecordingMover(List<string> log) => _log = log;

    public void Tick(
        ref CharacterState state,
        in CharacterCommand command,
        ICharacterCollisionSource source,
        CharacterTuning tuning,
        float deltaTime) =>
        _log.Add("character");
}
