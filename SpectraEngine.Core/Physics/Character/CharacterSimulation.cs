using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using SpectraEngine.Core.Entities;

namespace SpectraEngine.Core.Physics.Character;

/// <summary>
/// A character being simulated: its state, its tuning, the world it collides
/// against, and one fixed tick. Needs only a scene, so it runs headless.
/// </summary>
// No camera, input or renderer here: a server, a rollback replay and a
// scripted mover all tick this without them. The view lives elsewhere.
public sealed class CharacterSimulation : IPlayerPresence, ITunedPlayer
{
    // A rise off the ground slower than this stays inside the mover's contact
    // band, and the ground check stands the character again on the same tick.
    // Leaving the ground for it would lift nothing and hold back a jump.
    private const float LiftOffSpeed = 0.5f;

    private readonly Scene.Scene _scene;
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

        _scene = scene;
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

    /// <inheritdoc/>
    public float SkinWidth => Tuning.SkinWidth;

    /// <summary>The brush-plane source, for its counters.</summary>
    public BrushPlaneCollisionSource Collision { get; }

    /// <summary>Feet position, velocity and ground state.</summary>
    public CharacterState State => _state;

    /// <summary>
    /// The way the character faces, in radians: the yaw of the last command
    /// ticked, or of the spawn before the first. A teleport that names a
    /// facing sets it, and it stays until <see cref="TryTakeTeleport"/> has
    /// handed that facing to a view.
    /// </summary>
    public float Yaw => _state.Yaw;

    /// <summary>Where <see cref="Spawn"/> and the fall-out guard put the character.</summary>
    public Vector3 SpawnPosition { get; set; }

    /// <summary>The yaw a view starts with at the spawn, in radians.</summary>
    // Kept for the view. The mover takes its yaw from the command.
    public float SpawnYaw { get; set; }

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
        _state.Yaw = SpawnYaw;
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
        float facing = yaw ?? _state.Yaw;
        _state = CharacterState.AtFeet(feet);
        _state.PrevButtons = held;
        _state.Yaw = facing;

        _teleportPending = true;
        _teleportYaw = yaw ?? _teleportYaw;
    }

    /// <summary>
    /// Sends the character back to its spawn, at rest and facing as it
    /// started. A view picks it up as it does a teleport.
    /// </summary>
    public void Respawn() => Teleport(SpawnPosition, SpawnYaw);

    /// <summary>
    /// Gives the character a velocity, which the next tick carries on from.
    /// One that points up and rises off the ground it stands on takes it off
    /// the ground, as a jump does. The rise of a walk up a slope does not.
    /// </summary>
    public void SetVelocity(Vector3 velocity)
    {
        _state.Velocity = velocity;
        if (!_state.Grounded || velocity.Y <= 0f)
            return;

        // Along the ground normal, so a ramp's own rise and a push along
        // the ground keep the character standing.
        if (Vector3.Dot(velocity, _state.GroundNormal) <= LiftOffSpeed)
            return;

        // As the mover leaves the ground for a jump. Left standing, its
        // ground snap takes the rise back on the next tick.
        _state.Grounded = false;
        _state.GroundNormal = Vector3.UnitY;
        _state.GroundNodeId = default;
        _state.AirTicks = byte.MaxValue;
        _state.GroundSuppressTicks = Tuning.GroundSuppressTicks;
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
        // Around the mover, not in it: a replaced mover is still carried.
        CarryWithGround();

        // Here as well as in the mover: a replaced mover that never writes
        // them would leave the parts of a tick long past as touched.
        _state.SlideContacts = default;
        Mover.Tick(ref _state, in command, _source, Tuning, deltaTime);
        _state.GroundOrigin = TryFindGround(out Scene.SceneNode? ground) ? ground.WorldPosition : default;

        // Here as well as in the mover: a replaced mover that forgets would
        // turn one held button into a press on every tick.
        _state.PrevButtons = command.Buttons;

        // A facing a teleport named holds until a view has turned to it.
        // Until then the command carries the yaw the view had before.
        if (_teleportYaw is null)
            _state.Yaw = command.Yaw;

        if (_state.Position.Y >= FallOutHeight)
            return false;

        Respawns++;
        Spawn();
        return true;
    }

    // Translation only. A ground that turns in place carries nobody.
    private void CarryWithGround()
    {
        if (!TryFindGround(out Scene.SceneNode? ground))
            return;

        // Skipped at rest, so a still ground adds no rounding.
        Vector3 moved = ground.WorldPosition - _state.GroundOrigin;
        if (moved == Vector3.Zero)
            return;

        // Swept, not added: a rider stops under a ceiling and is left inside
        // the lift, which then finds it squeezed.
        CharacterCapsule capsule = Capsule;
        var filter = CharacterQueryFilter.Default;
        _source.BeginTick(SweptBounds(in capsule, moved), in filter);
        _state.Position += moved * _source.SweepCapsule(in capsule, moved, in filter, out _, out _);
    }

    private static Bsp.Aabb SweptBounds(in CharacterCapsule capsule, Vector3 travel)
    {
        Vector3 min = Vector3.Min(capsule.Center1, capsule.Center2);
        Vector3 max = Vector3.Max(capsule.Center1, capsule.Center2);
        min = Vector3.Min(min, min + travel);
        max = Vector3.Max(max, max + travel);

        var pad = new Vector3(capsule.Radius + CharacterMover.BroadphaseMargin);
        return new Bsp.Aabb(min - pad, max + pad);
    }

    // The mover leaves the id behind when it leaves the ground.
    private bool TryFindGround([MaybeNullWhen(false)] out Scene.SceneNode ground)
    {
        ground = null;
        return _state.Grounded
            && _state.GroundNodeId != default
            && _scene.TryFindById(_state.GroundNodeId, out ground);
    }
}
