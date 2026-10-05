using System;
using System.Numerics;
using Microsoft.Extensions.Logging;

using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Play;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Physics.Character;

/// <summary>
/// Drives the character from a keyboard, a mouse and a <see cref="Camera"/>.
/// Render thread only.
/// </summary>
// Input is sampled once per frame into one command that every tick of the
// frame replays. That works because the command carries absolute yaw and pitch,
// not mouse deltas. Per-tick deltas would scale look speed with the tick count.
public sealed class FirstPersonController
{
    // Same clamp as the camera's. Without it the stored pitch runs past the
    // limit and the view stops responding until it unwinds.
    private const float PitchLimit = MathF.PI / 2f - 0.01f;

    // About four ticks: smooths a riser without lagging into the next step.
    private const float EyeSmoothingSeconds = 0.06f;

    private const InputKey UseKey = InputKey.E;

    private readonly Camera _camera;
    private readonly InputManager _input;
    private readonly CharacterSimulation _simulation;
    private readonly ILogger _logger;

    private CharacterCommand _command;

    // A use press no tick has carried yet. A tap can start and end on a frame
    // that runs no tick.
    private bool _usePending;

    private float _yaw;
    private float _pitch;

    // How far below its true height the eye is after a step. Render-only,
    // never fed to the mover.
    private float _eyeLag;

    // Feet before and after the last tick, blended by alpha so the view moves
    // at the frame rate.
    private Vector3 _renderPrevious;
    private Vector3 _renderPosition;

    // Restored on Exit.
    private Vector3 _restoreCameraPosition;
    private float _restoreCameraYaw;
    private float _restoreCameraPitch;

    /// <summary>
    /// Builds a controller over a scene's camera. Nothing happens until
    /// <see cref="Enter"/>.
    /// </summary>
    public FirstPersonController(
        ILogger logger,
        Scene.Scene scene,
        InputManager input,
        CharacterTuning? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(input);

        _logger = logger;
        _camera = scene.Camera;
        _input = input;
        _simulation = new CharacterSimulation(scene, tuning);
    }

    /// <summary>
    /// The simulated half: state, tuning, collision and the tick. A headless
    /// host can drive it directly.
    /// </summary>
    public CharacterSimulation Simulation => _simulation;

    /// <summary>Movement constants. An edit takes effect next tick.</summary>
    public CharacterTuning Tuning => _simulation.Tuning;

    /// <summary>The brush-plane source, for its counters.</summary>
    public BrushPlaneCollisionSource Collision => _simulation.Collision;

    /// <summary>Whether the character is being simulated and owns the camera.</summary>
    public bool Active { get; private set; }

    /// <summary>Feet position, velocity and ground state.</summary>
    public CharacterState State => _simulation.State;

    /// <summary>Where <see cref="Enter"/> and the fall-out guard put the character.</summary>
    public Vector3 SpawnPosition
    {
        get => _simulation.SpawnPosition;
        set => _simulation.SpawnPosition = value;
    }

    /// <summary>The yaw <see cref="Enter"/> and a respawn start with, in radians.</summary>
    public float SpawnYaw
    {
        get => _simulation.SpawnYaw;
        set => _simulation.SpawnYaw = value;
    }

    /// <summary>Below this height the character is respawned rather than left falling.</summary>
    public float FallOutHeight
    {
        get => _simulation.FallOutHeight;
        set => _simulation.FallOutHeight = value;
    }

    /// <summary>Radians of look per pixel of mouse motion.</summary>
    public float LookSensitivity { get; set; } = 0.0022f;

    /// <summary>Times the fall-out guard has fired.</summary>
    public int Respawns => _simulation.Respawns;

    /// <summary>Horizontal speed in spectraunits per second.</summary>
    public float HorizontalSpeed => _simulation.HorizontalSpeed;

    /// <summary>
    /// The command <see cref="BeginFrame"/> sampled, for every tick of the frame.
    /// </summary>
    public CharacterCommand Command => _command;

    /// <summary>
    /// Takes the camera and locks the cursor. Call once the
    /// <see cref="PlaySession"/> has put the character at its spawn.
    /// </summary>
    public void Enter()
    {
        if (Active)
            return;

        _restoreCameraPosition = _camera.Position;
        _restoreCameraYaw = _camera.Yaw;
        _restoreCameraPitch = _camera.Pitch;

        _command = default;
        _usePending = false;
        _yaw = SpawnYaw;
        _pitch = 0f;
        _eyeLag = 0f;
        _renderPrevious = SpawnPosition;
        _renderPosition = SpawnPosition;
        Active = true;

        _input.RequestCursorMode(Input.CursorMode.Locked);
        UpdateView(0d, 1f);

        _logger.LogInformation(
            "Play mode ON: WASD walks, Shift sprints, Space jumps, E uses, mouse looks, Escape or the toggle " +
            "key leaves. Spawned at ({X:0.0}, {Y:0.0}, {Z:0.0}); {Speed:0.0} sunit/s walk, {Jump:0.00} sunit jump, " +
            "{Step:0.00} sunit step, {Slope:0} degree slope limit",
            SpawnPosition.X, SpawnPosition.Y, SpawnPosition.Z,
            Tuning.WalkSpeed, Tuning.JumpHeight, Tuning.StepHeight, Tuning.MaxSlopeAngleDegrees);
    }

    /// <summary>Releases the cursor and restores the camera.</summary>
    public void Exit()
    {
        if (!Active)
            return;

        Active = false;
        _input.RequestCursorMode(Input.CursorMode.Normal);

        _camera.Position = _restoreCameraPosition;
        _camera.Yaw = _restoreCameraYaw;
        _camera.Pitch = _restoreCameraPitch;

        _logger.LogInformation("Play mode OFF: camera restored to where it was left");
    }

    /// <summary>
    /// Samples the frame's input into the one command every tick of this frame
    /// will replay.
    /// </summary>
    public void BeginFrame(double deltaTime)
    {
        if (!Active)
            return;

        // The lock lands a frame or two after the request. Looking before
        // then would turn the cursor's jump to centre into a flick.
        if (_input.IsCursorLocked)
        {
            Vector2 delta = _input.MouseDelta;
            _yaw += delta.X * LookSensitivity;
            _pitch = Math.Clamp(_pitch - delta.Y * LookSensitivity, -PitchLimit, PitchLimit);

            // Wrap so float precision holds.
            if (_yaw > MathF.PI) _yaw -= MathF.Tau;
            else if (_yaw < -MathF.PI) _yaw += MathF.Tau;
        }

        var buttons = CharacterButtons.None;
        if (_input.IsKeyDown(InputKey.Space))
            buttons |= CharacterButtons.Jump;
        if (_input.IsKeyDown(InputKey.ShiftLeft) || _input.IsKeyDown(InputKey.ShiftRight))
            buttons |= CharacterButtons.Sprint;

        // Held, or pressed since the last tick.
        if (_input.WasKeyPressed(UseKey))
            _usePending = true;
        if (_usePending || _input.IsKeyDown(UseKey))
            buttons |= CharacterButtons.Use;

        // Crouch is unbound: the mover does nothing with it yet.

        float forward = (_input.IsKeyDown(InputKey.W) ? 1f : 0f) - (_input.IsKeyDown(InputKey.S) ? 1f : 0f);
        float strafe = (_input.IsKeyDown(InputKey.D) ? 1f : 0f) - (_input.IsKeyDown(InputKey.A) ? 1f : 0f);

        _command = new CharacterCommand
        {
            MoveForward = CharacterCommand.Axis(forward),
            MoveStrafe = CharacterCommand.Axis(strafe),
            Yaw = _yaw,
            Pitch = _pitch,
            Buttons = buttons,
        };
    }

    /// <summary>Takes in one tick the <see cref="PlaySession"/> ran with <see cref="Command"/>.</summary>
    public void OnTick(in PlayTickResult tick)
    {
        if (!Active)
            return;

        // This tick carried the press.
        _usePending = false;

        _renderPrevious = tick.PreviousPosition;

        // Accumulate: a frame can step up twice.
        _eyeLag += _simulation.State.SteppedUpBy;

        if (tick.Respawned)
        {
            _logger.LogWarning(
                "Character fell below y={Limit:0.0} and was respawned (respawn {Count})",
                FallOutHeight, Respawns);

            SnapView(SpawnYaw);
        }
    }

    /// <summary>
    /// Places the camera at the eye. Call once per frame, after the last tick.
    /// </summary>
    /// <param name="deltaTime">The frame's duration.</param>
    /// <param name="alpha">
    /// How far this frame sits between the last two ticks, in <c>[0, 1)</c>.
    /// </param>
    public void UpdateView(double deltaTime, float alpha)
    {
        if (!Active)
            return;

        // Once a frame, not per tick: a teleport can come from outside a tick.
        if (_simulation.TryTakeTeleport(out float? facing))
            SnapView(facing);

        if (_eyeLag > 0f)
        {
            _eyeLag *= MathF.Exp(-(float)deltaTime / EyeSmoothingSeconds);
            if (_eyeLag < 1e-3f)
                _eyeLag = 0f;
        }

        _renderPosition = Vector3.Lerp(_renderPrevious, _simulation.State.Position, Math.Clamp(alpha, 0f, 1f));

        _camera.Position = _renderPosition + new Vector3(0f, Tuning.EyeHeight - _eyeLag, 0f);
        _camera.Yaw = _yaw;
        _camera.Pitch = _pitch;
    }

    // Puts the view where the character now is, with nothing to blend from.
    // Both ends, or the view slides there across the map.
    private void SnapView(float? yaw)
    {
        _eyeLag = 0f;
        _renderPrevious = _simulation.State.Position;
        _renderPosition = _renderPrevious;

        if (yaw is { } facing)
            _yaw = facing;
    }

    /// <summary>
    /// Draws the capsule, its ground normal and its velocity.
    /// </summary>
    public void Draw(DebugDraw output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!Active)
            return;

        // Interpolated pose, same as the eye.
        var capsule = CharacterCapsule.FromFeet(_renderPosition, Tuning.StandHeight, Tuning.Radius);
        Vector3 color = _simulation.State.Grounded ? new Vector3(0.2f, 1f, 0.4f) : new Vector3(1f, 0.7f, 0.2f);

        DrawCapsule(output, in capsule, color);

        if (_simulation.State.Grounded)
        {
            output.Arrow(_renderPosition, _renderPosition + _simulation.State.GroundNormal * 0.75f,
                new Vector3(0.3f, 0.6f, 1f));
        }

        Vector3 velocity = _simulation.State.Velocity;
        if (velocity.LengthSquared() > 1e-4f)
            output.Arrow(_renderPosition, _renderPosition + velocity * 0.2f, new Vector3(1f, 0.3f, 0.8f));
    }

    private static void DrawCapsule(DebugDraw output, in CharacterCapsule capsule, Vector3 color)
    {
        const int Segments = 16;
        float radius = capsule.Radius;

        Ring(output, capsule.Center1, radius, color, Segments);
        Ring(output, capsule.Center2, radius, color, Segments);

        for (int i = 0; i < 4; i++)
        {
            float angle = i * MathF.Tau / 4f;
            var offset = new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
            output.Line(capsule.Center1 + offset, capsule.Center2 + offset, color);
        }

        Arc(output, capsule.Center1, radius, Vector3.UnitX, -Vector3.UnitY, color, Segments / 2);
        Arc(output, capsule.Center1, radius, Vector3.UnitZ, -Vector3.UnitY, color, Segments / 2);
        Arc(output, capsule.Center2, radius, Vector3.UnitX, Vector3.UnitY, color, Segments / 2);
        Arc(output, capsule.Center2, radius, Vector3.UnitZ, Vector3.UnitY, color, Segments / 2);
    }

    private static void Ring(DebugDraw output, Vector3 center, float radius, Vector3 color, int segments)
    {
        Vector3 previous = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * MathF.Tau / segments;
            Vector3 next = center + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
            output.Line(previous, next, color);
            previous = next;
        }
    }

    // Half-turn. from and to must be unit and perpendicular.
    private static void Arc(
        DebugDraw output, Vector3 center, float radius, Vector3 from, Vector3 to, Vector3 color, int segments)
    {
        Vector3 previous = center + from * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * MathF.PI / segments;
            Vector3 next = center + (from * MathF.Cos(angle) + to * MathF.Sin(angle)) * radius;
            output.Line(previous, next, color);
            previous = next;
        }
    }
}
