using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a mover in Luau costs: each thing it does with what it is handed,
/// and a whole tick beside the built-in mover's. Nothing of it may allocate
/// on the managed heap, and a tick may leave Luau's collector nothing. The
/// times are a measurement that runs when asked for, and are then written
/// beside the test binary, in mover-costs.txt.
/// </summary>
public sealed class MoverCostTests
{
    private const int Rounds = 500;
    private const int WarmTicks = 30;
    private const int Ticks = 60;
    private const int Runs = 3;

    // Many, so the last ones run the code the runtime has compiled in full.
    private const int TimedRuns = 12;

    private const string Nothing = "an empty loop";

    // The seam with nothing behind it.
    private const string DoesNothing = """
        game:GetService("Players").LocalPlayer.Character:SetMover(function() end)
        """;

    // What a mover does in one round, by what it is called in the table.
    private static readonly (string Name, string Statement)[] Crossings =
    [
        (Nothing, ""),
        ("read a Vector3 of the state", "local _ = state.Position"),
        ("write a Vector3 to the state", "state.Velocity = v"),
        ("read a boolean of the state", "local _ = state.IsGrounded"),
        ("read a number of the state", "local _ = state.AirTicks"),
        ("write a number to the state", "state.AirTicks = i % 200"),
        ("read the part the player stands on", "local _ = state.GroundPart"),
        ("stand on the ground", "state:StandOn(up)"),
        ("leave the ground", "state:LeaveGround()"),
        ("keep a number in the state", "state:SetMemory(1, i)"),
        ("read a number the state keeps", "local _ = state:GetMemory(1)"),
        ("read the walk direction of the command", "local _ = command.MoveDirection"),
        ("read a button of the command", "local _ = command.JumpPressed"),
        ("read a number of the tuning", "local _ = tuning.WalkSpeed"),
        ("read the gravity of the tuning", "local _ = tuning.Gravity"),
        ("sweep the capsule through free air", "collision:Sweep(air, ahead)"),
        ("sweep the capsule down to the floor", "collision:Sweep(air, down)"),
        ("gather the planes under the feet", "collision:Gather(floor, 0.05)"),
        ("read a plane of the list", "local _ = collision:Plane(1)"),
        ("solve the list for a move", "local _ = collision:Solve(v)"),
        ("clip a velocity by the list", "local _ = collision:Clip(v)"),
        ("clear the list and add a plane", "collision:ClearPlanes() collision:AddPlane(up)"),
    ];

    public static TheoryData<string> Names => new(Crossings.Select(crossing => crossing.Name));

    [Theory]
    [MemberData(nameof(Names))]
    public void What_a_mover_does_with_what_it_is_handed_allocates_nothing_on_the_managed_heap(string name)
    {
        MeasureCrossing(name).Allocated.ShouldBe(0, $"{Ticks * Rounds} rounds of '{name}' allocated");
    }

    // Away from every part: the collision builds the planes of a solid part
    // near the player on every tick, for whoever moves the player.
    [Fact]
    public void A_walk_a_jump_and_a_slide_along_a_wall_with_the_Luau_mover_allocate_nothing_on_the_managed_heap()
    {
        using MoverLevel level = new MoverLevel().WithTheShippedMover();
        level.Play().Tick(default, WarmTicks);

        long allocated = long.MaxValue;
        Vector3 end = default;
        for (int run = 0; run < Runs + 1; run++)
        {
            level.Character.Teleport(new Vector3(160f, 0.05f, 2f));
            level.Tick(default, WarmTicks);

            long before = GC.GetAllocatedBytesForCurrentThread();
            IntoTheWallAndAlongIt(level);
            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
            end = level.State.Position;
        }

        level.Errors.ShouldBeEmpty();
        level.Character.Mover.ShouldBeOfType<LuauCharacterMover>();
        end.X.ShouldBeGreaterThan(168f, "the walk reached the east wall");
        end.Z.ShouldBeGreaterThan(12f, "and slid along it");
        allocated.ShouldBe(0, "300 ticks of walking, jumping and sliding allocated");
    }

    [Fact]
    public void A_walk_with_the_Luau_mover_leaves_Luaus_collector_nothing()
    {
        using MoverLevel level = new MoverLevel().WithScript("""
            local walk = require("./movers/walk")
            local ticks, before = 0, 0
            game:GetService("Players").LocalPlayer.Character:SetMover(function(state, command, collision, tuning, dt)
                ticks += 1
                if ticks == 60 then
                    before = gcinfo()
                elseif ticks == 360 then
                    print(gcinfo() - before)
                end
                walk(state, command, collision, tuning, dt)
            end)
            """);
        level.Play().Tick(default, WarmTicks);
        level.Character.Teleport(new Vector3(160f, 0.05f, 2f));
        level.Tick(default, WarmTicks);

        IntoTheWallAndAlongIt(level);

        level.Errors.ShouldBeEmpty();
        // In kilobytes, which a thread made for every call would fill in a tick.
        level.Printed.ShouldBe(["0"], "what Luau held more after 300 ticks of the mover");
    }

    // A measurement and no test: it has nothing to fail on, so it runs only
    // when asked for, with -explicit only.
    [Fact(Explicit = true)]
    public void The_cost_of_a_tick_and_of_every_crossing_is_written_down()
    {
        // Once for nothing: the first walk runs code the runtime has not
        // compiled in full yet, and would be timed several times too slow.
        CourseTick(level => level);
        PlateTick(level => level);

        var table = new StringBuilder();
        table.AppendLine("One tick of the player's mover on the demo's course: the middle tick, the mean and the worst.");
        Row(table, "the built-in mover", CourseTick(level => level));
        Row(table, "the mover in Luau", CourseTick(level => level.WithTheShippedMover()));
        Row(table, "a mover in Luau that does nothing", StandingTick(hasCourse: true, DoesNothing, null));
        Row(table, "a mover in C# that only picks what the tick can touch", StandingTick(hasCourse: true, null, new BeginOnlyMover()));

        table.AppendLine().AppendLine("The same on a ground plate:");
        Row(table, "the built-in mover", PlateTick(level => level));
        Row(table, "the mover in Luau", PlateTick(level => level.WithTheShippedMover()));
        Row(table, "a mover in Luau that does nothing", StandingTick(hasCourse: false, DoesNothing, null));
        Row(table, "a mover in C# that only picks what the tick can touch", StandingTick(hasCourse: false, null, new BeginOnlyMover()));

        table.AppendLine().AppendLine("What a mover does with what it is handed, each:");
        double empty = MeasureCrossing(Nothing).Nanoseconds;
        foreach ((string name, _) in Crossings)
            Row(table, name, Math.Max(0, MeasureCrossing(name).Nanoseconds - empty));

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "mover-costs.txt"), table.ToString());
    }

    private static void Row(StringBuilder table, string name, double nanoseconds) =>
        table.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {name,-56} {nanoseconds,8:0} ns"));

    private static void Row(StringBuilder table, string name, TimedMover timed) =>
        table.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {name,-56} {timed.MedianNanoseconds,8:0} ns {timed.MeanNanoseconds,8:0} ns {timed.WorstNanoseconds,10:0} ns"));

    // South-east from the middle of the course into its east wall, and
    // along the wall, with a jump now and then.
    private static void IntoTheWallAndAlongIt(MoverLevel level)
    {
        for (int i = 0; i < 300; i++)
        {
            CharacterButtons buttons = i % 45 == 20 ? CharacterButtons.Jump : CharacterButtons.None;
            level.Tick(MoverCourse.Walk(MathF.PI / 4f, buttons));
        }
    }

    // The best of a few levels: the runtime compiles a method again once it
    // has run often, and that one time is no cost of the mover.
    private static TimedMover CourseTick(Func<MoverLevel, MoverLevel> prepare) =>
        BestOf(hasCourse: true, prepare, level => MoverCourse.Run(level));

    // In a circle a few units wide, with a jump now and then.
    private static TimedMover PlateTick(Func<MoverLevel, MoverLevel> prepare) =>
        BestOf(hasCourse: false, prepare, static level =>
        {
            for (int i = 0; i < 1200; i++)
            {
                CharacterButtons buttons = i % 50 == 25 ? CharacterButtons.Jump : CharacterButtons.None;
                level.Tick(MoverCourse.Walk(i * 0.02f, buttons));
            }
        });

    private static TimedMover StandingTick(bool hasCourse, string? script, ICharacterMover? mover) =>
        BestOf(
            hasCourse,
            level =>
            {
                if (mover is not null)
                    level.Character.Mover = mover;
                return script is null ? level : level.WithScript(script);
            },
            static level => level.Tick(default, 1200));

    private static TimedMover BestOf(bool hasCourse, Func<MoverLevel, MoverLevel> prepare, Action<MoverLevel> walk)
    {
        TimedMover? best = null;
        for (int run = 0; run < TimedRuns; run++)
        {
            using MoverLevel level = prepare(new MoverLevel(hasCourse));

            // A tick first: a script installs its mover when it starts.
            level.Play().Tick(default);
            var timed = new TimedMover(level.Character.Mover);
            level.Character.Mover = timed;

            walk(level);

            level.Errors.ShouldBeEmpty();
            if (best is null || timed.MedianNanoseconds < best.MedianNanoseconds)
                best = timed;
        }

        return best.ShouldNotBeNull();
    }

    // The time of one round and the bytes all rounds allocated.
    private static (double Nanoseconds, long Allocated) MeasureCrossing(string name)
    {
        string statement = Crossings.Single(crossing => crossing.Name == name).Statement;

        using MoverLevel level = new MoverLevel(hasCourse: false).WithScript($$"""
            local up = Vector3.new(0, 1, 0)
            local ahead = Vector3.new(0.05, 0, 0)
            local down = Vector3.new(0, -2, 0)
            local air = Vector3.new(0, 1, 0)
            local floor = Vector3.new(0, 0.01, 0)
            game:GetService("Players").LocalPlayer.Character:SetMover(function(state, command, collision, tuning, dt)
                local v = state.Velocity
                collision:Gather(floor, 0.05)
                for i = 1, {{Rounds}} do
                    {{statement}}
                end
            end)
            """);
        level.Play().Tick(default, WarmTicks);

        double nanoseconds = double.MaxValue;
        long allocated = long.MaxValue;
        for (int run = 0; run < Runs; run++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            level.Tick(default, Ticks);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);

            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
            nanoseconds = Math.Min(nanoseconds, elapsed.TotalNanoseconds / (Ticks * Rounds));
        }

        level.Errors.ShouldBeEmpty();
        return (nanoseconds, allocated);
    }
}
