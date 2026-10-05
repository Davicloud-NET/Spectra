using System;
using System.Numerics;

namespace SpectraEngine.Core.Physics.Character;

/// <summary>A capsule in world space: two hemisphere centres and a radius.</summary>
public readonly struct CharacterCapsule
{
    public CharacterCapsule(Vector3 center1, Vector3 center2, float radius)
    {
        Center1 = center1;
        Center2 = center2;
        Radius = radius;
    }

    /// <summary>The lower hemisphere centre.</summary>
    public Vector3 Center1 { get; }

    /// <summary>The upper hemisphere centre.</summary>
    public Vector3 Center2 { get; }

    public float Radius { get; }

    /// <summary>
    /// Builds a standing capsule from a feet position and a tip-to-tip height.
    /// </summary>
    public static CharacterCapsule FromFeet(Vector3 feet, float height, float radius) =>
        new(feet + new Vector3(0f, radius, 0f),
            feet + new Vector3(0f, height - radius, 0f),
            radius);

    /// <summary>This capsule translated by <paramref name="delta"/>.</summary>
    public CharacterCapsule Translated(Vector3 delta) =>
        new(Center1 + delta, Center2 + delta, Radius);
}

/// <summary>
/// One contact constraint acting on the mover. The normal points out of the
/// obstacle.
/// </summary>
// D holds the capsule's separation at zero translation, so DotCoordinate
// against a translation gives the separation after moving by it. The solver
// never needs the shape.
public struct CharacterContactPlane
{
    /// <summary>Outward unit normal; <c>D</c> is the separation at zero translation.</summary>
    public Plane Plane;

    /// <summary>How far this plane may push. <see cref="float.MaxValue"/> is rigid.</summary>
    public float PushLimit;

    /// <summary>Written by the solver, read by velocity clipping. Zero means the plane never engaged.</summary>
    public float Push;

    /// <summary>Whether this plane removes velocity driving into it.</summary>
    public bool ClipVelocity;

    public static CharacterContactPlane Rigid(Vector3 normal, float separation) => new()
    {
        Plane = new Plane(normal, separation),
        PushLimit = float.MaxValue,
        Push = 0f,
        ClipVelocity = true,
    };
}

/// <summary>
/// What produced a contact plane: the node, the brush face and the point.
/// </summary>
// Kept in a parallel array so CharacterContactPlane stays blittable.
public readonly struct CharacterContactSource
{
    public Scene.SceneNode? Node { get; init; }

    public Bsp.Brush? Brush { get; init; }

    /// <summary>Index into the brush's planes, or −1 for a cavity wall from a subtractive brush.</summary>
    public int PlaneIndex { get; init; }

    /// <summary>The closest point on the obstacle, in world space.</summary>
    public Vector3 Point { get; init; }
}

/// <summary>What a character query is allowed to see.</summary>
public readonly record struct CharacterQueryFilter
{
    /// <summary>The character's own node, never collided with.</summary>
    public Scene.SceneNode? Self { get; init; }

    /// <summary>Whether live <see cref="Scene.BrushKind.Part"/> brushes are gathered as well as world geometry.</summary>
    public bool IncludeParts { get; init; }

    /// <summary>World geometry plus part brushes, nothing ignored.</summary>
    public static CharacterQueryFilter Default => new() { IncludeParts = true };
}

/// <summary>Buttons a character command can carry.</summary>
[Flags]
public enum CharacterButtons : byte
{
    None = 0,
    Jump = 1 << 0,
    Sprint = 1 << 1,
    Crouch = 1 << 2,

    /// <summary>Uses what the view points at. Acts once per press.</summary>
    Use = 1 << 3,
}

/// <summary>
/// One tick's worth of intent. The same command may be replayed for several
/// ticks of one frame.
/// </summary>
// Carries held buttons, not edges: the mover derives edges from its own state.
// Axes are bytes because a networked command has to be.
public readonly record struct CharacterCommand
{
    /// <summary>Forward axis, −127..127.</summary>
    public sbyte MoveForward { get; init; }

    /// <summary>Strafe axis, −127..127.</summary>
    public sbyte MoveStrafe { get; init; }

    /// <summary>View yaw in radians. Sets the movement direction.</summary>
    public float Yaw { get; init; }

    /// <summary>View pitch in radians. Not used for movement.</summary>
    public float Pitch { get; init; }

    public CharacterButtons Buttons { get; init; }

    /// <summary>Normalised forward axis in −1..1.</summary>
    public float ForwardAxis => MoveForward / 127f;

    /// <summary>Normalised strafe axis in −1..1.</summary>
    public float StrafeAxis => MoveStrafe / 127f;

    public static sbyte Axis(float value) =>
        (sbyte)Math.Clamp((int)MathF.Round(value * 127f), -127, 127);
}

/// <summary>
/// The mover's entire state, copyable in one assignment.
/// </summary>
// Must stay a struct with no references: rollback captures and restores it by
// copy.
public struct CharacterState
{
    /// <summary>Feet position in world space.</summary>
    public Vector3 Position;

    public Vector3 Velocity;

    /// <summary>Normal of the surface being stood on. Up when airborne.</summary>
    public Vector3 GroundNormal;

    public bool Grounded;

    /// <summary>Buttons from the previous tick, for edge detection.</summary>
    public CharacterButtons PrevButtons;

    /// <summary>Ticks spent airborne, saturating. Drives coyote time.</summary>
    public byte AirTicks;

    /// <summary>Ticks a buffered jump stays live.</summary>
    public byte JumpBufferTicks;

    /// <summary>Ticks during which ground detection is suppressed after a jump.</summary>
    public byte GroundSuppressTicks;

    /// <summary>How far the last step probe lifted the character. For eye smoothing only.</summary>
    public float SteppedUpBy;

    /// <summary>The node being stood on, when it is a part brush that can move.</summary>
    public Guid GroundNodeId;

    /// <summary>
    /// World position of the node being stood on when this state was written.
    /// Zero with no such node.
    /// </summary>
    // Written by CharacterSimulation, not by the mover: the carry is how far
    // the node has moved since.
    public Vector3 GroundOrigin;

    public static CharacterState AtFeet(Vector3 feet) => new()
    {
        Position = feet,
        Velocity = Vector3.Zero,
        GroundNormal = Vector3.UnitY,
        Grounded = false,
    };
}

/// <summary>
/// Character tuning constants, in spectraunits (one unit is one metre).
/// Gravity is not here; it comes from <see cref="PhysicsDefaults.Gravity"/>.
/// </summary>
public sealed class CharacterTuning
{
    /// <summary>Capsule radius.</summary>
    public float Radius { get; set; } = 0.35f;

    /// <summary>Tip-to-tip standing height.</summary>
    public float StandHeight { get; set; } = 1.80f;

    /// <summary>Eye height above the feet.</summary>
    public float EyeHeight { get; set; } = 1.62f;

    /// <summary>Ground speed. About Roblox's default 16 studs/s.</summary>
    public float WalkSpeed { get; set; } = 4.5f;

    public float SprintMultiplier { get; set; } = 1.6f;

    public float GroundAcceleration { get; set; } = 50f;

    public float AirAcceleration { get; set; } = 12f;

    /// <summary>
    /// Cap on the wish speed while airborne. At 1.0 you can steer in the air
    /// but not gain speed; near walk speed allows strafe-jumping.
    /// </summary>
    public float AirSpeedCap { get; set; } = 1.0f;

    public float GroundFriction { get; set; } = 8f;

    /// <summary>Below this speed friction is applied as if at this speed, so a character stops.</summary>
    public float StopSpeed { get; set; } = 1.0f;

    public float GravityScale { get; set; } = 1.0f;

    /// <summary>Terminal velocity. Also bounds the broadphase volume.</summary>
    public float MaxFallSpeed { get; set; } = 60f;

    /// <summary>Peak height of a jump.</summary>
    public float JumpHeight { get; set; } = 1.20f;

    /// <summary>Ticks after leaving ground during which a jump still works.</summary>
    public byte CoyoteTicks { get; set; } = 6;

    /// <summary>Ticks a jump pressed in mid-air stays buffered.</summary>
    public byte JumpBufferTicks { get; set; } = 6;

    /// <summary>Ticks after a jump during which ground detection is suppressed.</summary>
    public byte GroundSuppressTicks { get; set; } = 4;

    /// <summary>Steepest walkable slope.</summary>
    public float MaxSlopeAngleDegrees { get; set; } = 46f;

    /// <summary>Highest ledge the step probe will climb.</summary>
    public float StepHeight { get; set; } = 0.45f;

    /// <summary>How far down the character reaches for ground when leaving a surface.</summary>
    public float GroundSnapDistance { get; set; } = 0.35f;

    /// <summary>The rest offset every contact is expressed against.</summary>
    public float SkinWidth { get; set; } = 0.010f;

    public int MaxSlideIterations { get; set; } = 4;

    public int MaxDepenetrationIterations { get; set; } = 4;

    /// <summary>Caps one tick's depenetration.</summary>
    public float MaxPushPerTick { get; set; } = 0.5f;

    /// <summary>Minimum ground-normal Y for a surface to count as walkable.</summary>
    public float MinGroundNormalY => MathF.Cos(MaxSlopeAngleDegrees * MathF.PI / 180f);

    /// <summary>Initial jump velocity, from <see cref="JumpHeight"/> and gravity.</summary>
    public float JumpVelocity =>
        MathF.Sqrt(2f * MathF.Abs(PhysicsDefaults.Gravity.Y) * GravityScale * JumpHeight);
}
