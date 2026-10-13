using System.Numerics;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>The numbers the built-in mover moves the player by, as a mover is handed them: sizes in units, speeds in units a second. A mover reads what it wants of them and is free to go by numbers of its own. The collision goes by Radius, StandHeight and SkinWidth whatever the mover does.</summary>
// Read only. A script sets WalkSpeed and JumpHeight on the character, where
// the write is recorded for Stop.
[ScriptClass("CharacterTuning")]
internal sealed partial class MoverTuning : ScriptObject
{
    private readonly MoverFrame _frame;

    public MoverTuning(MoverFrame frame) => _frame = frame;

    public override string Name => "CharacterTuning";

    /// <summary>How wide the player's capsule is from its middle.</summary>
    [ScriptProperty]
    public float Radius => Tuning.Radius;

    /// <summary>How tall the player's capsule is, from the feet to the top.</summary>
    [ScriptProperty]
    public float StandHeight => Tuning.StandHeight;

    /// <summary>How high above the feet the player looks from.</summary>
    [ScriptProperty]
    public float EyeHeight => Tuning.EyeHeight;

    /// <summary>How fast the player walks. It is the character's WalkSpeed, which a script can set.</summary>
    [ScriptProperty]
    public float WalkSpeed => Tuning.WalkSpeed;

    /// <summary>How many times faster a sprint is than a walk.</summary>
    [ScriptProperty]
    public float SprintMultiplier => Tuning.SprintMultiplier;

    /// <summary>How quickly the player gets up to speed on the ground.</summary>
    [ScriptProperty]
    public float GroundAcceleration => Tuning.GroundAcceleration;

    /// <summary>How quickly the player steers in the air.</summary>
    [ScriptProperty]
    public float AirAcceleration => Tuning.AirAcceleration;

    /// <summary>The most speed a push on the stick asks for in the air. Low, so steering in the air gains no speed.</summary>
    [ScriptProperty]
    public float AirSpeedCap => Tuning.AirSpeedCap;

    /// <summary>How strongly the ground slows the player.</summary>
    [ScriptProperty]
    public float GroundFriction => Tuning.GroundFriction;

    /// <summary>The speed below which the ground slows the player as if it had this speed, so it comes to a stop.</summary>
    [ScriptProperty]
    public float StopSpeed => Tuning.StopSpeed;

    /// <summary>What pulls the player down, in units a second each second. It points down.</summary>
    [ScriptProperty]
    public Vector3 Gravity => PhysicsDefaults.Gravity * Tuning.GravityScale;

    /// <summary>The fastest the player falls.</summary>
    [ScriptProperty]
    public float MaxFallSpeed => Tuning.MaxFallSpeed;

    /// <summary>How high the player jumps. It is the character's JumpHeight, which a script can set.</summary>
    [ScriptProperty]
    public float JumpHeight => Tuning.JumpHeight;

    /// <summary>The speed upward a jump starts with, which lifts the player to JumpHeight.</summary>
    [ScriptProperty]
    public float JumpVelocity => Tuning.JumpVelocity;

    /// <summary>How many ticks after it left the ground the player can still jump.</summary>
    [ScriptProperty]
    public float CoyoteTicks => Tuning.CoyoteTicks;

    /// <summary>How many ticks a jump asked for in the air stays wanted.</summary>
    [ScriptProperty]
    public float JumpBufferTicks => Tuning.JumpBufferTicks;

    /// <summary>How many ticks after a jump the ground is not looked for.</summary>
    [ScriptProperty]
    public float GroundSuppressTicks => Tuning.GroundSuppressTicks;

    /// <summary>The Y of the normal of the steepest ground the player can stand on.</summary>
    [ScriptProperty]
    public float MinGroundNormalY => Tuning.MinGroundNormalY;

    /// <summary>The highest ledge the player steps onto without a jump.</summary>
    [ScriptProperty]
    public float StepHeight => Tuning.StepHeight;

    /// <summary>How far down the player reaches for the ground when it walks off an edge or over a crest.</summary>
    [ScriptProperty]
    public float GroundSnapDistance => Tuning.GroundSnapDistance;

    /// <summary>The gap the collision keeps between the player and anything solid. A sweep stops this far off.</summary>
    [ScriptProperty]
    public float SkinWidth => Tuning.SkinWidth;

    /// <summary>How many times the built-in mover sweeps and slides in one tick.</summary>
    [ScriptProperty]
    public float MaxSlideIterations => Tuning.MaxSlideIterations;

    /// <summary>How many times the built-in mover pushes the player out of what it is in, in one tick.</summary>
    [ScriptProperty]
    public float MaxDepenetrationIterations => Tuning.MaxDepenetrationIterations;

    /// <summary>The furthest one such push moves the player.</summary>
    [ScriptProperty]
    public float MaxPushPerTick => Tuning.MaxPushPerTick;

    private CharacterTuning Tuning => _frame.TuningNow();
}
