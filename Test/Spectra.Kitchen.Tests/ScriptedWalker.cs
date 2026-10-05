using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using System;

namespace Spectra.Kitchen.Tests;

// The real mover over a scene's collision, driven by commands and nothing
// else: no camera, no input, no renderer.
internal sealed class ScriptedWalker
{
    // Yaw is (cos, 0, sin), so 0 walks +x and a quarter turn walks +z.
    public const float East = 0f;
    public const float West = MathF.PI;
    public const float South = MathF.PI / 2f;
    public const float North = -MathF.PI / 2f;

    public ScriptedWalker(SpectraEngine.Core.Scene.Scene scene)
    {
        Source = new BrushPlaneCollisionSource(scene, Tuning);
    }

    public CharacterTuning Tuning { get; } = new();

    public BrushPlaneCollisionSource Source { get; }

    public CharacterState Step(CharacterState state, float yaw, float forward = 0f, bool jump = false)
    {
        var command = new CharacterCommand
        {
            MoveForward = CharacterCommand.Axis(forward),
            Yaw = yaw,
            Buttons = jump ? CharacterButtons.Jump : CharacterButtons.None,
        };

        CharacterMover.Tick(ref state, in command, Source, Tuning, PhysicsDefaults.FixedDeltaTime);
        return state;
    }

    public CharacterState Settle(CharacterState state, int ticks)
    {
        for (int i = 0; i < ticks; i++) state = Step(state, East);
        return state;
    }

    public CharacterState Walk(CharacterState state, float yaw, int ticks)
    {
        for (int i = 0; i < ticks; i++) state = Step(state, yaw, forward: 1f);
        return state;
    }
}
