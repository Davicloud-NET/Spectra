using System.Numerics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>Where the player is and how it moves, as a mover is handed it on every tick. What the mover leaves in it is where the player is after the tick. A mover keeps here what it needs on a later tick, so the same ticks played again move the player the same way. What it keeps anywhere else, as in a local outside the function, does not come back when the state is put back.</summary>
// A write is checked here, at the line that made it. What is no number
// never reaches the capsule maths.
[ScriptClass("CharacterState")]
internal sealed partial class MoverState : ScriptObject
{
    private readonly MoverFrame _frame;

    public MoverState(MoverFrame frame) => _frame = frame;

    public override string Name => "CharacterState";

    /// <summary>Where the player's feet are in the world.</summary>
    [ScriptProperty(SetBy = nameof(SetPosition))]
    public Vector3 Position => _frame.Read().Position;

    /// <summary>How fast the player moves and which way, in units a second.</summary>
    [ScriptProperty(SetBy = nameof(SetVelocity))]
    public Vector3 Velocity => _frame.Read().Velocity;

    /// <summary>Whether the player stands on ground. StandOn and LeaveGround set it.</summary>
    [ScriptProperty]
    public bool IsGrounded => _frame.Read().Grounded;

    /// <summary>The way the ground the player stands on faces. Straight up in the air.</summary>
    [ScriptProperty]
    public Vector3 GroundNormal => _frame.Read().GroundNormal;

    /// <summary>The part the player stands on, when the ground is one. Nil on the fused world and in the air.</summary>
    [ScriptProperty]
    public SceneNode? GroundPart => _frame.Read().Grounded ? _frame.NodeOf(_frame.Read().GroundNodeId) : null;

    /// <summary>How many ticks the player has been in the air, up to 255. A mover counts them to let a jump come a little late.</summary>
    [ScriptProperty(SetBy = nameof(SetAirTicks))]
    public float AirTicks => _frame.Read().AirTicks;

    /// <summary>How many ticks a jump asked for in the air is still wanted.</summary>
    [ScriptProperty(SetBy = nameof(SetJumpBufferTicks))]
    public float JumpBufferTicks => _frame.Read().JumpBufferTicks;

    /// <summary>How many ticks after a jump the ground is not looked for.</summary>
    [ScriptProperty(SetBy = nameof(SetGroundSuppressTicks))]
    public float GroundSuppressTicks => _frame.Read().GroundSuppressTicks;

    /// <summary>How far a step lifted the player on this tick, in units. The view uses it to rise smoothly.</summary>
    [ScriptProperty(SetBy = nameof(SetSteppedUpBy))]
    public float SteppedUpBy => _frame.Read().SteppedUpBy;

    /// <summary>Stands the player on ground that faces along a normal. The part is the ground when it is one, so the player rides it when it moves and is said to touch it.</summary>
    [ScriptMethod]
    public void StandOn(Vector3 normal, SceneNode? part = null)
    {
        float length = normal.Length();
        if (!(length > 1e-3f) || !float.IsFinite(length))
            throw new InvalidOperationException(MoverLimits.NotADirection(normal));

        ref CharacterState state = ref _frame.Write();
        state.Grounded = true;
        state.GroundNormal = normal / length;
        state.GroundNodeId = part?.Id ?? Guid.Empty;
    }

    /// <summary>Takes the player off the ground, as a jump and a walk off a ledge do.</summary>
    [ScriptMethod]
    public void LeaveGround()
    {
        ref CharacterState state = ref _frame.Write();
        state.Grounded = false;
        state.GroundNormal = Vector3.UnitY;
        state.GroundNodeId = Guid.Empty;
    }

    /// <summary>Says the player's slide stopped at a part on this tick, so the part is touched wherever the tick ends. A tick keeps the first four.</summary>
    [ScriptMethod]
    public void AddSlideContact(SceneNode part) => _frame.Write().SlideContacts.Add(part.Id);

    /// <summary>One of the eight numbers a mover keeps between ticks, by its slot from 1 to 8. All are 0 when the player is placed.</summary>
    [ScriptMethod]
    public float GetMemory(int slot) => _frame.Read().Memory[SlotOf(slot)];

    /// <summary>Keeps a number for later ticks in a slot from 1 to 8, such as the jumps made since the ground.</summary>
    [ScriptMethod]
    public void SetMemory(int slot, float value)
    {
        if (!float.IsFinite(value))
            throw new InvalidOperationException("A mover keeps numbers, and this is none.");

        _frame.Write().Memory[SlotOf(slot)] = value;
    }

    private static int SlotOf(int slot) =>
        slot >= 1 && slot <= CharacterMoverMemory.Capacity
            ? slot - 1
            : throw new InvalidOperationException($"A mover has the slots 1 to {CharacterMoverMemory.Capacity} to keep numbers in.");

    private string? SetPosition(Vector3 value)
    {
        if (!MoverLimits.IsPlace(value))
            return MoverLimits.NotAPlace("Position", value);

        _frame.Write().Position = value;
        return null;
    }

    private string? SetVelocity(Vector3 value)
    {
        if (!MoverLimits.IsWithin(value, MoverLimits.MaxSpeed))
            return MoverLimits.TooLong("Velocity", value, MoverLimits.MaxSpeed, "units a second");

        _frame.Write().Velocity = value;
        return null;
    }

    private string? SetAirTicks(float value)
    {
        if (!MoverLimits.IsTicks(value))
            return MoverLimits.NotTicks("AirTicks", value);

        _frame.Write().AirTicks = (byte)value;
        return null;
    }

    private string? SetJumpBufferTicks(float value)
    {
        if (!MoverLimits.IsTicks(value))
            return MoverLimits.NotTicks("JumpBufferTicks", value);

        _frame.Write().JumpBufferTicks = (byte)value;
        return null;
    }

    private string? SetGroundSuppressTicks(float value)
    {
        if (!MoverLimits.IsTicks(value))
            return MoverLimits.NotTicks("GroundSuppressTicks", value);

        _frame.Write().GroundSuppressTicks = (byte)value;
        return null;
    }

    private string? SetSteppedUpBy(float value)
    {
        if (!MoverLimits.IsFrom0To(MoverLimits.MaxReach, value))
            return MoverLimits.OutOfRange("SteppedUpBy", value, MoverLimits.MaxReach);

        _frame.Write().SteppedUpBy = value;
        return null;
    }
}
