using System;
using System.Numerics;

namespace SpectraEngine.Core.Physics.Character;

/// <summary>
/// A character being simulated: its state, its tuning, the world it collides
/// against, and one fixed tick. Needs only a scene, so it runs headless.
/// </summary>
// No camera, input or renderer here: a server, a rollback replay and a
// scripted mover all tick this without them. The view lives elsewhere.
public sealed class CharacterSimulation
{
    private readonly ICharacterCollisionSource _source;
    private CharacterState _state;

    /// <summary>Builds a character over a scene's geometry.</summary>
    public CharacterSimulation(Scene.Scene scene, CharacterTuning? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        Tuning = tuning ?? new CharacterTuning();

        // Plane sets, not hulls: a doorway cut by a subtractive brush has to
        // be walkable.
        var brushSource = new BrushPlaneCollisionSource(scene, Tuning);
        _source = brushSource;
        Collision = brushSource;

        _state = CharacterState.AtFeet(Vector3.Zero);
    }

    /// <summary>
    /// The movement algorithm. Defaults to the engine's own; assign to replace
    /// it, at any time.
    /// </summary>
    public ICharacterMover Mover { get; set; } = DefaultCharacterMover.Instance;

    /// <summary>Movement constants. An edit takes effect next tick.</summary>
    public CharacterTuning Tuning { get; }

    /// <summary>The brush-plane source, for its counters.</summary>
    public BrushPlaneCollisionSource Collision { get; }

    /// <summary>Feet position, velocity and ground state.</summary>
    public CharacterState State => _state;

    /// <summary>Where <see cref="Spawn"/> and the fall-out guard put the character.</summary>
    public Vector3 SpawnPosition { get; set; }

    /// <summary>Below this height the character is respawned rather than left falling.</summary>
    public float FallOutHeight { get; set; } = -1000f;

    /// <summary>Times the fall-out guard has fired.</summary>
    public int Respawns { get; private set; }

    /// <summary>Horizontal speed in spectraunits per second.</summary>
    public float HorizontalSpeed => new Vector2(_state.Velocity.X, _state.Velocity.Z).Length();

    /// <summary>Puts the character at its spawn, at rest.</summary>
    public void Spawn() => _state = CharacterState.AtFeet(SpawnPosition);

    /// <summary>
    /// Replaces the whole state, for a network correction or a replay.
    /// </summary>
    public void Restore(in CharacterState state) => _state = state;

    /// <summary>
    /// Advances by one fixed tick. Returns true if the fall-out guard fired.
    /// </summary>
    public bool Tick(in CharacterCommand command, float deltaTime)
    {
        Mover.Tick(ref _state, in command, _source, Tuning, deltaTime);

        if (_state.Position.Y >= FallOutHeight)
            return false;

        Respawns++;
        Spawn();
        return true;
    }
}
