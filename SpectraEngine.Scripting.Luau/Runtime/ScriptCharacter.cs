using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>The player's body in the level. It is no node: the player is not in the level's tree.</summary>
// Reads are the character's state as of the last tick. Every write goes
// through PlayerWrites, which records what Stop has to put back.
[ScriptClass("Character")]
internal sealed partial class ScriptCharacter : ScriptObject
{
    private readonly PlayerWrites _door;

    public ScriptCharacter(PlayerWrites door) => _door = door;

    public override string Name => "Character";

    /// <summary>Where the player's feet are in the world.</summary>
    [ScriptProperty(CannotSet = "Position cannot be set. Move the player with Teleport")]
    public Vector3 Position => Body.State.Position;

    /// <summary>Where the player's feet are and which way the player faces: along the frame's LookVector.</summary>
    [ScriptProperty(CannotSet = "CFrame cannot be set. Move the player with Teleport")]
    public CFrame CFrame => CharacterFacing.FrameAt(Body.State.Position, Body.Yaw);

    /// <summary>How fast the player moves and which way, in units a second. A script sets it to push or throw the player. One that rises off the ground the player stands on lifts the player off it, and the rise of a walk up a slope does not.</summary>
    [ScriptProperty(SetBy = nameof(SetVelocity))]
    public Vector3 Velocity => Body.State.Velocity;

    /// <summary>How fast the player walks, in units a second. Stop puts back what a script set.</summary>
    [ScriptProperty(SetBy = nameof(SetWalkSpeed))]
    public float WalkSpeed => Body.Tuning.WalkSpeed;

    /// <summary>How high the player jumps, in units. Stop puts back what a script set.</summary>
    [ScriptProperty(SetBy = nameof(SetJumpHeight))]
    public float JumpHeight => Body.Tuning.JumpHeight;

    /// <summary>Whether the player stands on ground.</summary>
    [ScriptProperty]
    public bool IsGrounded => Body.State.Grounded;

    // A script that kept the character of a player that has left reads an
    // error at its line. What calls a host function catches it.
    private IPlayerPresence Body => _door.Player ?? throw new InvalidOperationException(ScriptMember.HasLeft);

    /// <summary>Puts the player at rest at a position, or at a frame's position, facing along the frame's LookVector. A player start faces along its ZVector, so its own frame turns the player round.</summary>
    [ScriptMethod(Parameters = "target: CFrame | Vector3")]
    private int Teleport(LuauStack stack)
    {
        if (FrameValue.TryGet(stack, 2, out CFrame frame))
            return _door.TryTeleport(frame, out string? refused) ? 0 : stack.Fail(refused);
        if (stack.TryGetVector(2, out Vector3 feet))
            return _door.TryTeleport(feet, out string? refused) ? 0 : stack.Fail(refused);

        return ScriptArguments.Wrong(stack, 2, "Teleport", "CFrame or Vector3", isMethod: true);
    }

    /// <summary>Sends the player back to where it started, as falling out of the level does.</summary>
    [ScriptMethod]
    private int Respawn(LuauStack stack) =>
        _door.TryRespawn(out string? refused) ? 0 : stack.Fail(refused);

    private string? SetVelocity(Vector3 value) => _door.TrySetVelocity(value, out string? refused) ? null : refused;

    private string? SetWalkSpeed(float value) => _door.TrySetWalkSpeed(value, out string? refused) ? null : refused;

    private string? SetJumpHeight(float value) => _door.TrySetJumpHeight(value, out string? refused) ? null : refused;
}
