namespace SpectraEngine.Core.Physics.Character;

/// <summary>
/// One fixed tick of character movement. Implement it to replace the built-in
/// mover, for example from script.
/// </summary>
public interface ICharacterMover
{
    /// <summary>
    /// Advances <paramref name="state"/> by one fixed tick. Must be a pure
    /// function of its arguments, or replay breaks.
    /// </summary>
    // Touch reads what a mover leaves in the state: GroundNodeId while it is
    // Grounded, and SlideContacts. One that leaves them empty touches a part
    // only by where the tick ends, and misses a corner it glances off.
    // What it keeps for a later tick goes into the state's Memory.
    void Tick(
        ref CharacterState state,
        in CharacterCommand command,
        ICharacterCollisionSource source,
        CharacterTuning tuning,
        float deltaTime);
}

/// <summary>
/// The engine's built-in mover, as an <see cref="ICharacterMover"/>.
/// </summary>
public sealed class DefaultCharacterMover : ICharacterMover
{
    /// <summary>The shared, stateless instance.</summary>
    public static readonly DefaultCharacterMover Instance = new();

    private DefaultCharacterMover()
    {
    }

    /// <inheritdoc/>
    public void Tick(
        ref CharacterState state,
        in CharacterCommand command,
        ICharacterCollisionSource source,
        CharacterTuning tuning,
        float deltaTime)
        => CharacterMover.Tick(ref state, in command, source, tuning, deltaTime);
}
