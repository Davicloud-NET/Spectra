using System;
using System.Numerics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The demo's obstacle course, walked by the real mover over the compiled
/// world. Fails when a tuning default moves and part of the level stops working.
/// </summary>
public sealed class DemoPlayAreaTests
{
    private const float Dt = PhysicsDefaults.FixedDeltaTime;

    // Yaw is (cos, 0, sin), so 0 walks +x and π/2 walks +z.
    private const float East = 0f;
    private const float West = MathF.PI;
    private const float South = MathF.PI / 2f;
    private const float North = -MathF.PI / 2f;

    [Fact]
    public void The_spawn_point_is_standing_on_solid_ground()
    {
        var course = new Course();
        CharacterState state = course.Spawn();
        state = course.Settle(state, 30);

        Assert.True(state.Grounded, "the character should be standing at the spawn point");

        // A resting character sits one skin width above the surface.
        Assert.Equal(course.Tuning.SkinWidth, state.Position.Y, 4);
    }

    [Fact]
    public void The_perimeter_wall_stops_a_character_walking_off_the_edge()
    {
        var course = new Course();
        CharacterState state = course.Settle(course.Spawn(), 10);

        // The west boundary is four units from the spawn.
        state = course.Walk(state, West, 120);

        Assert.True(state.Position.X > 130.5f,
            $"the west wall should have stopped the character, but it reached x={state.Position.X:0.000}");
        Assert.True(state.Grounded, "the character should still be on the floor at the wall");
    }

    // All three staircases climb 2.0 over 4.0 of run and differ only in rise,
    // so together they bracket StepHeight.

    [Theory]
    [InlineData(-8f, "gentle (0.25 rise)")]
    [InlineData(0f, "at the limit (0.40 rise)")]
    public void A_staircase_within_the_step_height_is_climbed(float z, string description)
    {
        var course = new Course();
        CharacterState state = course.Settle(Course.SpawnAt(new Vector3(133f, 0.05f, z)), 10);

        state = course.Walk(state, East, 150);

        Assert.True(state.Position.Y > 1.9f,
            $"the {description} staircase should have been climbed to the terrace, " +
            $"but the character reached only y={state.Position.Y:0.000}");
    }

    [Fact]
    public void A_staircase_above_the_step_height_refuses_to_be_climbed()
    {
        var course = new Course();
        CharacterState state = course.Settle(Course.SpawnAt(new Vector3(133f, 0.05f, 8f)), 10);

        state = course.Walk(state, East, 150);

        Assert.True(state.Position.Y < 0.5f,
            $"a 0.50 riser exceeds the 0.45 step height and must not be climbable, " +
            $"but the character reached y={state.Position.Y:0.000}");
        Assert.True(state.Position.X < 136.5f,
            $"the character should be stopped at the first riser, not past it at x={state.Position.X:0.000}");
    }

    [Theory]
    [InlineData(-8f, "gentle (0.25 rise)")]
    [InlineData(0f, "at the limit (0.40 rise)")]
    public void Climbing_a_staircase_does_not_teleport_the_character_forward(float z, string description)
    {
        var course = new Course();

        // Flat control run, measured so the test survives a WalkSpeed or tick
        // rate change.
        (float flatTravel, float flatStep) = course.MeasureRun(new Vector3(133f, 0.05f, 14f), 90);
        (float stairTravel, float stairStep) = course.MeasureRun(new Vector3(133f, 0.05f, z), 90);

        // The step probe must advance only r(1 - sin(slopeLimit)), enough for
        // the down sweep to land on a walkable part of the ledge. A full
        // capsule radius per riser is a visible lurch.
        Assert.True(stairTravel < flatTravel * 1.25f,
            $"climbing the {description} staircase covered {stairTravel:0.000} horizontally where flat " +
            $"ground covered {flatTravel:0.000} — the step probe is carrying the character forward");

        Assert.True(stairStep < flatStep * 3f,
            $"one tick on the {description} staircase moved {stairStep:0.000} where a flat tick moves " +
            $"{flatStep:0.000} — that is a visible lurch, not a step");
    }

    [Fact]
    public void The_terrace_doorway_can_be_walked_through()
    {
        var course = new Course();

        // On the terrace, west of the wall, in line with the opening.
        CharacterState state = course.Settle(Course.SpawnAt(new Vector3(141.5f, 2.05f, 0f)), 20);
        Assert.True(state.Grounded, "the character should be standing on the terrace");

        state = course.Walk(state, East, 90);

        Assert.True(state.Position.X > 144.5f,
            $"the doorway is 1.4 x 2.2 and the character is 0.7 across and 1.8 tall, so it must pass " +
            $"through — it stopped at x={state.Position.X:0.000}");
        Assert.True(state.Grounded, "the character should still be on the terrace after the doorway");
    }

    [Fact]
    public void The_wall_beside_the_doorway_is_solid()
    {
        var course = new Course();
        CharacterState state = course.Settle(Course.SpawnAt(new Vector3(141.5f, 2.05f, 4f)), 20);

        state = course.Walk(state, East, 90);

        // Wall's west face is at x = 143.3; the capsule radius is 0.35.
        Assert.True(state.Position.X < 143.0f,
            $"the wall beside the opening must block, but the character reached x={state.Position.X:0.000}");
    }

    [Fact]
    public void The_tunnel_has_a_ceiling_that_is_solid()
    {
        var course = new Course();

        // The tunnel is 2.2 tall, cut by a subtractive brush.
        CharacterState state = course.Settle(Course.SpawnAt(new Vector3(146f, 2.05f, 8f)), 20);
        Assert.True(state.Grounded, "the character should be standing inside the tunnel");

        // 1.2 of jump height against 0.4 of headroom.
        state = course.Step(state, East, jump: true);
        for (int i = 0; i < 40; i++)
            state = course.Step(state, East);

        Assert.True(state.Position.Y < 2.6f,
            $"the tunnel ceiling must stop a jump inside it, but the character reached " +
            $"y={state.Position.Y:0.000}");
    }

    [Theory]
    [InlineData(-17f, 25)]
    [InlineData(-13f, 40)]
    public void A_ramp_within_the_slope_limit_can_be_walked_up(float z, int degrees)
    {
        var course = new Course();
        CharacterState state = course.Settle(Course.SpawnAt(new Vector3(150f, 0.05f, z)), 10);

        state = course.Walk(state, East, 150);

        Assert.True(state.Position.Y > 1.9f,
            $"the {degrees} degree ramp is inside the 46 degree slope limit and must be walkable, " +
            $"but the character reached only y={state.Position.Y:0.000}");
        Assert.True(state.Grounded, "the character should be standing on the platform at the top");
    }

    [Fact]
    public void A_ramp_beyond_the_slope_limit_is_not_walkable()
    {
        var course = new Course();
        CharacterState state = course.Settle(Course.SpawnAt(new Vector3(150f, 0.05f, -8.5f)), 10);

        state = course.Walk(state, East, 150);

        Assert.True(state.Position.Y < 1.0f,
            $"a 55 degree ramp exceeds the 46 degree slope limit and must not be walkable, " +
            $"but the character reached y={state.Position.Y:0.000}");
    }

    [Fact]
    public void Cresting_a_ramp_does_not_launch_the_character()
    {
        var course = new Course();
        CharacterState state = course.Settle(Course.SpawnAt(new Vector3(150f, 0.05f, -13f)), 10);

        // The character reaches the crest with upward velocity. A ground snap
        // that only fires on downward velocity launches it here.
        float highest = 0f;
        bool everAirborneOnTop = false;

        for (int i = 0; i < 200; i++)
        {
            state = course.Step(state, East, forward: 1f);

            // Only the platform top: still on the ramp before 156, and past
            // 161 it walks off the east edge at 162.
            if (state.Position.X < 156f || state.Position.X > 161f)
                continue;

            highest = MathF.Max(highest, state.Position.Y);
            if (!state.Grounded)
                everAirborneOnTop = true;
        }

        Assert.False(everAirborneOnTop,
            "the character left the ground after cresting the ramp — the ground snap is not holding");
        Assert.True(highest < 2.1f,
            $"the character rose to y={highest:0.000} on the platform, which is above its top at 2.0");
        Assert.True(highest > 1.9f, "the character never reached the platform at all");
    }

    [Fact]
    public void The_chasm_goes_all_the_way_through_the_floor()
    {
        var course = new Course();

        // Dropped in from above. The cut runs past the slab's underside.
        CharacterState state = Course.SpawnAt(new Vector3(153.5f, 2f, 10f));
        state = course.Settle(state, 180);

        Assert.False(state.Grounded, "the chasm must have no floor to stand on");
        Assert.True(state.Position.Y < DemoPlayArea.FallOutHeight,
            $"three seconds of falling should be well past the fall-out height, " +
            $"but the character is at y={state.Position.Y:0.0}");
    }

    [Fact]
    public void The_chasm_can_be_jumped_at_walking_speed()
    {
        var course = new Course();

        // Run-up toward the chasm's west lip at x = 152.
        CharacterState state = course.Settle(Course.SpawnAt(new Vector3(148.5f, 0.05f, 10f)), 10);

        bool jumped = false;
        for (int i = 0; i < 120; i++)
        {
            bool jumpNow = !jumped && state.Position.X > 151.2f;
            state = course.Step(state, East, forward: 1f, jump: jumpNow);
            if (jumpNow)
                jumped = true;
        }

        Assert.True(jumped, "the character never reached the near lip of the chasm");
        Assert.True(state.Position.X > 155.4f,
            $"a 3.0 gap must be clearable at walk speed with a 1.2 jump, but the character " +
            $"ended at x={state.Position.X:0.000}, y={state.Position.Y:0.000}");
        Assert.True(state.Grounded, "the character should have landed on the far side");
    }

    [Fact]
    public void The_part_brush_platform_is_solid()
    {
        var course = new Course();

        // Platform top is at y = 1.0. It is the only geometry here that
        // reaches the mover through the spatial index, not the compiled world.
        CharacterState state = Course.SpawnAt(new Vector3(164f, 3f, -3f));
        state = course.Settle(state, 120);

        Assert.True(state.Grounded, "the character should be standing on the part brush");
        Assert.Equal(1f + course.Tuning.SkinWidth, state.Position.Y, 4);
    }

    [Fact]
    public void No_direction_walked_from_the_spawn_leaves_the_world()
    {
        var course = new Course();

        // Sixteen headings, five seconds each. A gap between two brushes
        // shows up as a fall.
        for (int i = 0; i < 16; i++)
        {
            float yaw = i * MathF.Tau / 16f;
            CharacterState state = course.Settle(course.Spawn(), 10);

            for (int tick = 0; tick < 300; tick++)
            {
                state = course.Step(state, yaw, forward: 1f);

                // The chasm is the one authored way out of the world.
                if (InsideChasm(state.Position))
                    break;

                Assert.True(state.Position.Y > DemoPlayArea.FallOutHeight,
                    $"walking at yaw {yaw:0.00} rad fell out of the world at tick {tick}, " +
                    $"y={state.Position.Y:0.000}, x={state.Position.X:0.0}, z={state.Position.Z:0.0}");
            }
        }
    }

    [Fact]
    public void Walking_does_not_rebuild_the_world_lane_every_tick()
    {
        var course = new Course();
        CharacterState state = course.Settle(course.Spawn(), 10);

        int afterSettle = course.Source.WorldLaneRebuilds;
        state = course.Walk(state, South, 300);

        int rebuilds = course.Source.WorldLaneRebuilds - afterSettle;

        // Five seconds of walking is about 22 units, inside one region margin.
        Assert.True(rebuilds <= 2,
            $"the world lane was rebuilt {rebuilds} times over 300 ticks of walking");
    }

    private static bool InsideChasm(Vector3 position) =>
        position.X > 151.5f && position.X < 155.5f &&
        position.Z > 1.5f && position.Z < 18.5f;

    private sealed class Course
    {
        private readonly Scene _scene = new("PlayAreaTest");

        public Course()
        {
            DemoPlayArea.Build(_scene, MaterialRef.Default, MaterialRef.Default, MaterialRef.Default);
            _scene.RebuildStaticWorld(new FakeRenderer());
            Source = new BrushPlaneCollisionSource(_scene, Tuning);
        }

        public CharacterTuning Tuning { get; } = new();

        public BrushPlaneCollisionSource Source { get; }

        public CharacterState Spawn() => CharacterState.AtFeet(DemoPlayArea.Spawn);

        public static CharacterState SpawnAt(Vector3 feet) => CharacterState.AtFeet(feet);

        public CharacterState Step(
            CharacterState state, float yaw, float forward = 0f, bool jump = false, bool sprint = false)
        {
            var buttons = CharacterButtons.None;
            if (jump) buttons |= CharacterButtons.Jump;
            if (sprint) buttons |= CharacterButtons.Sprint;

            var command = new CharacterCommand
            {
                MoveForward = CharacterCommand.Axis(forward),
                Yaw = yaw,
                Buttons = buttons,
            };

            CharacterMover.Tick(ref state, in command, Source, Tuning, Dt);
            return state;
        }

        public CharacterState Settle(CharacterState state, int ticks)
        {
            for (int i = 0; i < ticks; i++)
                state = Step(state, 0f);
            return state;
        }

        // Walks east; returns total travel and the biggest single-tick advance.
        public (float Travel, float BiggestStep) MeasureRun(Vector3 feet, int ticks)
        {
            CharacterState state = Settle(CharacterState.AtFeet(feet), 10);
            float startX = state.Position.X;
            float previousX = startX;
            float biggest = 0f;

            for (int i = 0; i < ticks; i++)
            {
                state = Step(state, East, forward: 1f);
                biggest = MathF.Max(biggest, state.Position.X - previousX);
                previousX = state.Position.X;
            }

            return (state.Position.X - startX, biggest);
        }

        public CharacterState Walk(CharacterState state, float yaw, int ticks)
        {
            for (int i = 0; i < ticks; i++)
                state = Step(state, yaw, forward: 1f);
            return state;
        }
    }
}
