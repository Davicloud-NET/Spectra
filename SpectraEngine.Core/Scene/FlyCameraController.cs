using System.Numerics;
using SpectraEngine.Core.Input;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// A simple free-fly camera driver. WASD moves along the camera's own axes,
/// Space/Ctrl move along world up/down, Shift sprints. Hold the right mouse
/// button to look around; the cursor stays visible.
/// </summary>
public sealed class FlyCameraController
{
    private readonly Camera _camera;
    private readonly InputManager _input;
    private bool _wasLookingLastFrame;

    public FlyCameraController(Camera camera, InputManager input)
    {
        _camera = camera;
        _input = input;
    }

    public float MoveSpeed { get; set; } = 4f;

    public float SprintMultiplier { get; set; } = 3f;

    public float LookSensitivity { get; set; } = 0.0025f;

    public void Update(double deltaTime)
    {
        float dt = (float)deltaTime;

        bool looking = _input.IsMouseButtonDown(PointerButtons.Right);

        // Skip the press frame: its delta built up before the look started.
        if (looking && _wasLookingLastFrame)
        {
            Vector2 delta = _input.MouseDelta;
            _camera.Yaw += delta.X * LookSensitivity;
            _camera.Pitch -= delta.Y * LookSensitivity;
        }
        _wasLookingLastFrame = looking;

        Vector3 move = Vector3.Zero;
        if (_input.IsKeyDown(InputKey.W)) move += _camera.Forward;
        if (_input.IsKeyDown(InputKey.S)) move -= _camera.Forward;
        if (_input.IsKeyDown(InputKey.D)) move += _camera.Right;
        if (_input.IsKeyDown(InputKey.A)) move -= _camera.Right;
        if (_input.IsKeyDown(InputKey.Space)) move += Vector3.UnitY;
        if (_input.IsKeyDown(InputKey.ControlLeft)) move -= Vector3.UnitY;

        if (move.LengthSquared() > 0f)
        {
            move = Vector3.Normalize(move);
            float speed = _input.IsKeyDown(InputKey.ShiftLeft) ? MoveSpeed * SprintMultiplier : MoveSpeed;
            _camera.Position += move * speed * dt;
        }
    }
}
