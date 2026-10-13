using System.Numerics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>What the player asks for on one tick, as a mover is handed it: which way to walk, where it looks and which buttons it holds. A frame can run several ticks with one command.</summary>
[ScriptClass("CharacterCommand")]
internal sealed partial class MoverCommand : ScriptObject
{
    private readonly MoverFrame _frame;

    public MoverCommand(MoverFrame frame) => _frame = frame;

    public override string Name => "CharacterCommand";

    /// <summary>The way the player wants to walk, in the world: level, and of length 1, or zero when it wants to stand.</summary>
    [ScriptProperty]
    public Vector3 MoveDirection => _frame.MoveDirection;

    /// <summary>How far the walk is pushed forward, from -1 to 1.</summary>
    [ScriptProperty]
    public float MoveForward => _frame.Command.ForwardAxis;

    /// <summary>How far the walk is pushed to the right, from -1 to 1.</summary>
    [ScriptProperty]
    public float MoveStrafe => _frame.Command.StrafeAxis;

    /// <summary>The way the player looks, turned about the up axis, in radians.</summary>
    [ScriptProperty]
    public float Yaw => _frame.Command.Yaw;

    /// <summary>How far the player looks up, in radians. Down is below zero.</summary>
    [ScriptProperty]
    public float Pitch => _frame.Command.Pitch;

    /// <summary>Whether jump is held.</summary>
    [ScriptProperty]
    public bool Jump => _frame.IsDown(CharacterButtons.Jump);

    /// <summary>Whether jump is held now and was not on the tick before.</summary>
    [ScriptProperty]
    public bool JumpPressed => _frame.IsPressed(CharacterButtons.Jump);

    /// <summary>Whether sprint is held.</summary>
    [ScriptProperty]
    public bool Sprint => _frame.IsDown(CharacterButtons.Sprint);

    /// <summary>Whether sprint is held now and was not on the tick before.</summary>
    [ScriptProperty]
    public bool SprintPressed => _frame.IsPressed(CharacterButtons.Sprint);

    /// <summary>Whether crouch is held.</summary>
    [ScriptProperty]
    public bool Crouch => _frame.IsDown(CharacterButtons.Crouch);

    /// <summary>Whether crouch is held now and was not on the tick before.</summary>
    [ScriptProperty]
    public bool CrouchPressed => _frame.IsPressed(CharacterButtons.Crouch);

    /// <summary>Whether use is held.</summary>
    [ScriptProperty]
    public bool Use => _frame.IsDown(CharacterButtons.Use);

    /// <summary>Whether use is held now and was not on the tick before.</summary>
    [ScriptProperty]
    public bool UsePressed => _frame.IsPressed(CharacterButtons.Use);
}
