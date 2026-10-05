using System;
using System.Numerics;
using SpectraEngine.Core.Entities;

namespace SpectraEngine.Core.Physics.Character;

/// <summary>
/// A character being simulated: its state, its tuning, the world it collides
/// against, and one fixed tick. Needs only a scene, so it runs headless.
/// </summary>
// No camera, input or renderer here: a server, a rollback replay and a
// scripted mover all tick this without them. The view lives elsewhere.
public sealed class CharacterSimulation : IPlayerPresence
{
    private readonly ICharacterCollisionSource _source;
    private CharacterState _state;

    // View state, so it is not in CharacterState: a teleport the view has
    // not snapped to yet, and the facing it asked for.
    private bool _teleportPending;
    private float? _teleportYaw;

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

    /// <summary>Whether <see cref="Spawn"/> or <see cref="Restore"/> has placed the character.</summary>
    public bool IsPresent { get; private set; }

    /// <summary>The standing capsule at the feet, in world space.</summary>
    public CharacterCapsule Capsule =>
        CharacterCapsule.FromFeet(_state.Position, Tuning.StandHeight, Tuning.Radius);

    /// <summary>Puts the character at its spawn, at rest.</summary>
    public void Spawn()
    {
        _state = CharacterState.AtFeet(SpawnPosition);
        IsPresent = true;

        // A teleport no view took belongs to the run before this one.
        _teleportPending = false;
        _teleportYaw = null;
    }

    /// <summary>
    /// Replaces the whole state, for a network correction or a replay.
    /// </summary>
    public void Restore(in CharacterState state)
    {
        _state = state;
        IsPresent = true;
    }

    /// <summary>
    /// Puts the character at rest with its feet at a point, off the ground
    /// until the next tick finds it. The view picks the jump up through
    /// <see cref="TryTakeTeleport"/>.
    /// </summary>
    /// <param name="feet">Where the feet go, in world space.</param>
    /// <param name="yaw">The facing to take, in radians, or null to keep looking the same way.</param>
    public void Teleport(Vector3 feet, float? yaw = null)
    {
        // Held buttons carry over, or a held jump reads as a new press.
        CharacterButtons held = _state.PrevButtons;
        _state = CharacterState.AtFeet(feet);
        _state.PrevButtons = held;

        _teleportPending = true;
        _teleportYaw = yaw ?? _teleportYaw;
    }

    /// <summary>
    /// Takes the teleport a view has not snapped to yet. False when there is
    /// none.
    /// </summary>
    /// <param name="yaw">The facing the teleport asked for, or null to keep the view's own.</param>
    public bool TryTakeTeleport(out float? yaw)
    {
        yaw = _teleportYaw;
        bool pending = _teleportPending;
        _teleportPending = false;
        _teleportYaw = null;
        return pending;
    }

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
