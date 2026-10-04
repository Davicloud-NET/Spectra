using System;
using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Physics.Character;

/// <summary>
/// The per-tick character movement algorithm: accelerate, jump, fall, sweep and
/// slide, step, depenetrate, find ground.
/// </summary>
// Must stay a pure function of (state, command, source, tuning, dt): no clock,
// no input read, no scene write. Rollback depends on it.
public static class CharacterMover
{
    /// <summary>Contact planes one tick may hold.</summary>
    public const int MaxContactPlanes = 8;

    /// <summary>Padding added to the tick's query volume.</summary>
    public const float BroadphaseMargin = 0.5f;

    // Normal only, no offset: these planes only feed ClipVelocity, which
    // ignores D.
    private const float PlaneDedupeDot = 0.999f;

    /// <summary>Advances the character by exactly one fixed tick.</summary>
    public static void Tick(
        ref CharacterState state,
        in CharacterCommand command,
        ICharacterCollisionSource source,
        CharacterTuning tuning,
        float dt)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tuning);

        var filter = CharacterQueryFilter.Default;

        // Jump edge comes from state, not the command: one command is replayed
        // for every tick of a frame and must not jump each time.
        bool jumpPressed = (command.Buttons & CharacterButtons.Jump) != 0
            && (state.PrevButtons & CharacterButtons.Jump) == 0;
        state.PrevButtons = command.Buttons;

        bool wasGrounded = state.Grounded;
        state.SteppedUpBy = 0f;

        source.BeginTick(TickVolume(in state, tuning, dt), in filter);

        // Yaw only. Looking up or down must not change walk speed.
        float cos = MathF.Cos(command.Yaw);
        float sin = MathF.Sin(command.Yaw);
        var forward = new Vector3(cos, 0f, sin);
        var right = new Vector3(-sin, 0f, cos);

        Vector3 wish = forward * command.ForwardAxis + right * command.StrafeAxis;
        float wishLength = wish.Length();
        if (wishLength > 1f)
            wish /= wishLength;
        else if (wishLength > 1e-4f)
            wish = Vector3.Normalize(wish);

        if (state.Grounded && wish != Vector3.Zero)
        {
            // Project onto the slope so a ramp does not slow you down.
            wish -= state.GroundNormal * Vector3.Dot(wish, state.GroundNormal);
            if (wish.LengthSquared() > 1e-8f)
                wish = Vector3.Normalize(wish);
        }

        bool sprinting = (command.Buttons & CharacterButtons.Sprint) != 0;
        float targetSpeed = tuning.WalkSpeed * (sprinting ? tuning.SprintMultiplier : 1f);

        if (state.Grounded)
        {
            var horizontal = new Vector3(state.Velocity.X, 0f, state.Velocity.Z);
            float speed = horizontal.Length();
            if (speed > 0f)
            {
                // StopSpeed floor: without it speed only approaches zero.
                float drop = MathF.Max(speed, tuning.StopSpeed) * tuning.GroundFriction * dt;
                float scale = MathF.Max(0f, speed - drop) / speed;
                state.Velocity.X *= scale;
                state.Velocity.Z *= scale;
            }

            Accelerate(ref state.Velocity, wish, targetSpeed, tuning.GroundAcceleration, dt);
        }
        else
        {
            // Cap the wish speed, not the velocity, or air strafing gains speed.
            Accelerate(ref state.Velocity, wish, MathF.Min(targetSpeed, tuning.AirSpeedCap),
                tuning.AirAcceleration, dt);
        }

        // Jump before gravity so it gets its full velocity.
        bool jumpedThisTick = false;
        bool wantJump = jumpPressed || state.JumpBufferTicks > 0;
        bool canJump = state.Grounded || state.AirTicks <= tuning.CoyoteTicks;

        if (wantJump && canJump && state.GroundSuppressTicks == 0)
        {
            state.Velocity.Y = tuning.JumpVelocity;
            // Clear ground now or the snap below undoes the jump this tick.
            state.Grounded = false;
            state.GroundNormal = Vector3.UnitY;
            state.GroundNodeId = default;
            state.JumpBufferTicks = 0;
            state.AirTicks = byte.MaxValue;
            state.GroundSuppressTicks = tuning.GroundSuppressTicks;
            jumpedThisTick = true;
        }
        else if (jumpPressed)
        {
            state.JumpBufferTicks = tuning.JumpBufferTicks;
        }
        else if (state.JumpBufferTicks > 0)
        {
            state.JumpBufferTicks--;
        }

        if (!state.Grounded)
        {
            state.Velocity += PhysicsDefaults.Gravity * tuning.GravityScale * dt;
            if (state.Velocity.Y < -tuning.MaxFallSpeed)
                state.Velocity.Y = -tuning.MaxFallSpeed;
        }

        Span<CharacterContactPlane> planes = stackalloc CharacterContactPlane[MaxContactPlanes];
        // Managed type, so no stackalloc. Thread-static to avoid allocating.
        Span<CharacterContactSource> sources = SourceScratch();
        int planeCount = 0;

        Vector3 remaining = state.Velocity * dt;
        Vector3 originalHorizontal = new(remaining.X, 0f, remaining.Z);

        bool blockedByWall = false;
        Vector3 blockingNormal = Vector3.UnitY;

        for (int iteration = 0; iteration < tuning.MaxSlideIterations; iteration++)
        {
            if (remaining.LengthSquared() < tuning.SkinWidth * tuning.SkinWidth)
                break;

            float fraction = source.SweepCapsule(
                CapsuleAt(in state, tuning), remaining, in filter,
                out CharacterContactPlane plane, out _);

            state.Position += remaining * fraction;

            if (fraction >= 1f)
                break;

            // Set from any blocking sweep, not from running out of iterations:
            // a riser stops the slide in one iteration.
            if (plane.Plane.Normal.Y < tuning.MinGroundNormalY)
            {
                blockedByWall = true;
                blockingNormal = plane.Plane.Normal;
            }

            AddPlane(planes, ref planeCount, plane);
            remaining = source.ClipVelocity(remaining * (1f - fraction), planes[..planeCount]);
            state.Velocity = source.ClipVelocity(state.Velocity, planes[..planeCount]);
        }

        if (blockedByWall && wasGrounded && !jumpedThisTick)
        {
            TryStepUp(ref state, source, tuning, in filter, originalHorizontal, blockingNormal);
        }

        // Depenetrate toward a zero delta. The sweep already moved us, and a
        // non-zero target fights the slide and jitters against walls.
        for (int iteration = 0; iteration < tuning.MaxDepenetrationIterations; iteration++)
        {
            int count = source.GatherPlanes(
                CapsuleAt(in state, tuning), 0f, in filter, planes, sources);
            if (count == 0)
                break;

            Vector3 push = source.SolvePlanes(Vector3.Zero, planes[..count]).Delta;
            float pushLength = push.Length();
            if (pushLength < 1e-5f)
                break;

            if (pushLength > tuning.MaxPushPerTick)
                push = push / pushLength * tuning.MaxPushPerTick;

            state.Position += push;
            state.Velocity = source.ClipVelocity(state.Velocity, planes[..count]);
        }

        GroundCheck(ref state, source, tuning, in filter, wasGrounded, jumpedThisTick, planes, sources);

        if (state.GroundSuppressTicks > 0)
            state.GroundSuppressTicks--;

        if (state.Grounded)
            state.AirTicks = 0;
        else if (state.AirTicks < byte.MaxValue)
            state.AirTicks++;
    }

    [ThreadStatic]
    private static CharacterContactSource[]? _sourceScratch;

    private static Span<CharacterContactSource> SourceScratch() =>
        _sourceScratch ??= new CharacterContactSource[MaxContactPlanes];

    /// <summary>The capsule the character currently occupies.</summary>
    public static CharacterCapsule CapsuleAt(in CharacterState state, CharacterTuning tuning) =>
        CharacterCapsule.FromFeet(state.Position, tuning.StandHeight, tuning.Radius);

    // Quake's form: add only what is missing along the wish direction.
    private static void Accelerate(
        ref Vector3 velocity, Vector3 wish, float wishSpeed, float acceleration, float dt)
    {
        if (wish == Vector3.Zero || wishSpeed <= 0f)
            return;

        float current = Vector3.Dot(velocity, wish);
        float add = wishSpeed - current;
        if (add <= 0f)
            return;

        velocity += wish * MathF.Min(acceleration * dt * wishSpeed, add);
    }

    private static void TryStepUp(
        ref CharacterState state,
        ICharacterCollisionSource source,
        CharacterTuning tuning,
        in CharacterQueryFilter filter,
        Vector3 horizontal,
        Vector3 blockingNormal)
    {
        if (horizontal.LengthSquared() < 1e-8f)
            return;

        Vector3 savedPosition = state.Position;
        Vector3 savedVelocity = state.Velocity;

        var up = new Vector3(0f, tuning.StepHeight, 0f);
        float upFraction = source.SweepCapsule(
            CapsuleAt(in state, tuning), up, in filter, out _, out _);
        state.Position += up * upFraction;

        // Forward along the original travel, not the clipped one, or the step
        // goes along the wall instead of over it.
        // One tick of walking lands the capsule on the step's edge with a
        // normal too steep to stand on, so the probe needs a minimum reach.
        // The edge contact becomes walkable once the axis is within
        // r*sin(limit) of it, which needs r*(1 - sin(limit)) + skin of advance.
        // A full radius would teleport the character forward on every riser.
        float desired = horizontal.Length();
        float sinSlopeLimit = MathF.Sqrt(
            MathF.Max(0f, 1f - tuning.MinGroundNormalY * tuning.MinGroundNormalY));
        float minimumProbe = tuning.Radius * (1f - sinSlopeLimit) + tuning.SkinWidth * 4f;
        float probeDistance = MathF.Max(desired, minimumProbe);
        Vector3 probeForward = horizontal / desired * probeDistance;

        float forwardFraction = source.SweepCapsule(
            CapsuleAt(in state, tuning), probeForward, in filter, out _, out _);
        state.Position += probeForward * forwardFraction;

        // Down, slightly further than we rose so the landing is found.
        var down = new Vector3(0f, -(tuning.StepHeight * upFraction + tuning.SkinWidth * 2f), 0f);
        float downFraction = source.SweepCapsule(
            CapsuleAt(in state, tuning), down, in filter,
            out CharacterContactPlane landing, out CharacterContactSource landingSource);

        bool landed = downFraction < 1f;
        bool walkable = landed && landing.Plane.Normal.Y >= tuning.MinGroundNormalY;
        bool progressed = forwardFraction > 1e-3f;

        // Bound the contact point's height, not the capsule's. The rounded
        // bottom can rest on the top edge of a riser taller than StepHeight
        // with a walkable normal, and would then climb it.
        // savedPosition already sits a skin above the floor.
        bool withinStep = landed
            && landingSource.Point.Y <= savedPosition.Y + tuning.StepHeight + tuning.SkinWidth;

        if (!landed || !walkable || !progressed || !withinStep)
        {
            state.Position = savedPosition;
            state.Velocity = savedVelocity;
            return;
        }

        state.Position += down * downFraction;
        state.SteppedUpBy = MathF.Max(0f, state.Position.Y - savedPosition.Y);
        state.Grounded = true;
        state.GroundNormal = landing.Plane.Normal;

        // Velocity is not re-clipped after a step, or stairs feel like a
        // series of stops.
        _ = blockingNormal;
    }

    private static void GroundCheck(
        ref CharacterState state,
        ICharacterCollisionSource source,
        CharacterTuning tuning,
        in CharacterQueryFilter filter,
        bool wasGrounded,
        bool jumpedThisTick,
        Span<CharacterContactPlane> planes,
        Span<CharacterContactSource> sources)
    {
        if (jumpedThisTick)
        {
            state.Grounded = false;
            return;
        }

        int count = source.GatherPlanes(
            CapsuleAt(in state, tuning), tuning.SkinWidth * 2f, in filter, planes, sources);

        int best = -1;
        for (int i = 0; i < count; i++)
        {
            if (planes[i].Plane.Normal.Y < tuning.MinGroundNormalY)
                continue;
            if (best < 0 || planes[i].Plane.Normal.Y > planes[best].Plane.Normal.Y)
                best = i;
        }

        Vector3 groundNormal = best >= 0 ? planes[best].Plane.Normal : Vector3.UnitY;
        bool found = best >= 0;
        Guid groundNode = best >= 0 ? sources[best].Node?.Id ?? default : default;

        // Snap is not gated on falling. Walking up a ramp has upward velocity,
        // and gating on it would launch the character off every crest.
        if (!found && wasGrounded && state.GroundSuppressTicks == 0)
        {
            var down = new Vector3(0f, -tuning.GroundSnapDistance, 0f);
            float fraction = source.SweepCapsule(
                CapsuleAt(in state, tuning), down, in filter,
                out CharacterContactPlane probe, out CharacterContactSource probeSource);

            if (fraction < 1f && probe.Plane.Normal.Y >= tuning.MinGroundNormalY)
            {
                state.Position += down * fraction;
                groundNormal = probe.Plane.Normal;
                groundNode = probeSource.Node?.Id ?? default;
                found = true;
            }
        }

        state.Grounded = found;
        if (!found)
            return;

        state.GroundNormal = groundNormal;
        state.GroundNodeId = groundNode;

        // Also drop the upward component a ramp left behind.
        float into = Vector3.Dot(state.Velocity, groundNormal);
        if (into < 0f)
            state.Velocity -= groundNormal * into;
        else if (state.Velocity.Y > 0f && into > 0f)
            state.Velocity -= groundNormal * into;
    }

    // Covers travel, step up and snap down.
    private static Aabb TickVolume(in CharacterState state, CharacterTuning tuning, float dt)
    {
        CharacterCapsule capsule = CapsuleAt(in state, tuning);
        Vector3 travel = state.Velocity * dt;

        Vector3 min = Vector3.Min(capsule.Center1, capsule.Center2);
        Vector3 max = Vector3.Max(capsule.Center1, capsule.Center2);

        min = Vector3.Min(min, min + travel);
        max = Vector3.Max(max, max + travel);

        float pad = capsule.Radius + BroadphaseMargin;
        min -= new Vector3(pad, pad + tuning.StepHeight + tuning.GroundSnapDistance, pad);
        max += new Vector3(pad, pad + tuning.StepHeight, pad);

        return new Aabb(min, max);
    }

    // Dedupes: two abutting coplanar faces would double the solver's push and
    // catch on the seam.
    private static void AddPlane(
        Span<CharacterContactPlane> planes, ref int count, in CharacterContactPlane plane)
    {
        for (int i = 0; i < count; i++)
        {
            if (Vector3.Dot(planes[i].Plane.Normal, plane.Plane.Normal) > PlaneDedupeDot)
                return;
        }

        if (count >= planes.Length)
            return;

        planes[count] = plane;
        // Mark engaged so ClipVelocity does not skip it.
        planes[count].Push = 1f;
        count++;
    }
}
