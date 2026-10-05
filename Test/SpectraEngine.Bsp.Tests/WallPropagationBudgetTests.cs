using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Audio.Propagation;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The budget of <see cref="WallPropagation"/>: a fixed number of lines a
/// frame, spent on the sounds in turn, with a new sound answered at once.
/// </summary>
// The listener stands at the origin. The sounds stand in a block beyond a
// wooden slab at x from 1 to 1.05, the nearest ones first in the list.
public sealed class WallPropagationBudgetTests
{
    private static readonly WallPropagationSettings Engine = WallPropagationSettings.Default;

    // Every sound's turn at the engine's numbers: what one frame can answer in full.
    private static readonly int SoundsAFrame = Engine.TracesPerFrame / Engine.Lines;

    [Fact]
    public void With_200_sounds_no_frame_traces_more_lines_than_the_budget()
    {
        WallRig rig = Block(200, out FakeSoundObstacles world);
        int most = 0;
        int total = 0;

        for (int frame = 0; frame < 240; frame++)
        {
            // A walking listener, so every answer is due all the time.
            rig.Listener = new Vector3(0f, 0f, frame * 0.075f % 3f);
            world.Traces = 0;
            rig.Frame();

            rig.Walls.Stats.Traces.ShouldBe(world.Traces);
            most = Math.Max(most, world.Traces);
            total += world.Traces;
        }

        most.ShouldBe(Engine.TracesPerFrame);
        total.ShouldBeGreaterThan(200 * Engine.TracesPerFrame);
    }

    [Fact]
    public void Every_one_of_200_sounds_is_traced_again_within_the_refresh_time_and_one_round_of_turns()
    {
        WallRig rig = Block(200, out FakeSoundObstacles world);
        var lastTraced = new int[200];
        int longest = 0;

        for (int frame = 1; frame <= 400; frame++)
        {
            world.Asked.ShouldNotBeNull().Clear();
            rig.Frame();

            foreach ((Vector3 from, _, _) in world.Asked)
            {
                int sound = IndexOf(from);
                if (lastTraced[sound] > 0)
                    longest = Math.Max(longest, frame - lastTraced[sound]);

                lastTraced[sound] = frame;
            }
        }

        lastTraced.ShouldAllBe(frame => frame > 0);

        int refresh = (int)MathF.Ceiling(Engine.RefreshSeconds / WallRig.FrameSeconds);
        int round = (int)MathF.Ceiling(200f / SoundsAFrame);
        longest.ShouldBeLessThanOrEqualTo(refresh + round + 1);
        longest.ShouldBeGreaterThan(refresh);
    }

    [Fact]
    public void A_few_sounds_that_stand_still_are_traced_once_each_refresh_time_and_not_in_between()
    {
        WallRig rig = Block(8, out FakeSoundObstacles world);
        rig.Frame();
        world.Traces.ShouldBe(8 * Engine.Lines);

        world.Traces = 0;
        rig.Frame(10);
        world.Traces.ShouldBe(0);

        rig.Frame(10);
        world.Traces.ShouldBe(8 * Engine.Lines);
    }

    [Fact]
    public void A_new_sound_is_answered_on_its_first_frame()
    {
        WallRig rig = Block(40, out _);
        rig.Frame(30);

        int late = rig.Add(new Vector3(9f, 0f, 0f));
        rig.Frame();

        rig.HasPath(late).ShouldBeTrue();
        rig.Through(late).Gain.ShouldBe(AcousticPresets.Wood.GainsThrough(0.05f).Gain, 2e-3f);
    }

    [Fact]
    public void A_sound_too_quiet_to_hear_at_its_distance_costs_no_trace()
    {
        var world = new FakeSoundObstacles();
        var rig = new WallRig(world);
        int beyond = rig.Add(new Vector3(31f, 0f, 0f));
        int faint = rig.Add(new Vector3(29.9f, 0f, 0f));

        rig.Frame(20);

        world.Traces.ShouldBe(0);
        rig.Walls.Stats.Sounds.ShouldBe(0);
        rig.Heard(beyond).ShouldBe(rig.Direct(beyond));
        rig.Heard(faint).ShouldBe(rig.Direct(faint));
        rig.Direct(faint).Gain.ShouldBeInRange(1e-5f, WallPropagation.InaudibleGain);
    }

    [Fact]
    public void A_sound_keeps_its_answer_until_its_turn_comes_again()
    {
        WallRig rig = Block(1, out FakeSoundObstacles world);
        rig.Frame();
        SoundPath before = rig.Heard(0);

        // The wall goes and nothing says so.
        world.Clear();
        rig.Frame(5);

        rig.Heard(0).ShouldBe(before);

        rig.Frame(15);

        rig.Heard(0).ShouldBe(rig.Direct(0));
    }

    [Fact]
    public void When_the_new_sounds_do_not_all_fit_the_loudest_get_every_line_and_the_rest_one()
    {
        WallRig rig = Block(20, out FakeSoundObstacles world);

        rig.Frame();

        // Ten along five lines and ten along one is the whole budget.
        world.Traces.ShouldBe(Engine.TracesPerFrame);
        LinesTracedFor(world, sound: 0).ShouldBe(Engine.Lines);
        LinesTracedFor(world, sound: 9).ShouldBe(Engine.Lines);
        LinesTracedFor(world, sound: 10).ShouldBe(1);
        LinesTracedFor(world, sound: 19).ShouldBe(1);
        for (int sound = 0; sound < 20; sound++)
            rig.HasPath(sound).ShouldBeTrue();

        // The ones heard along one line are first in turn after that.
        world.Asked.ShouldNotBeNull().Clear();
        rig.Frame();

        LinesTracedFor(world, sound: 10).ShouldBe(Engine.Lines);
        LinesTracedFor(world, sound: 19).ShouldBe(Engine.Lines);
        LinesTracedFor(world, sound: 0).ShouldBe(0);
    }

    [Fact]
    public void More_new_sounds_than_the_frame_has_lines_leave_the_quietest_silent_until_their_turn()
    {
        WallRig rig = Block(200, out FakeSoundObstacles world);

        rig.Frame();

        world.Traces.ShouldBe(Engine.TracesPerFrame);
        rig.Walls.Stats.Unanswered.ShouldBe(200 - Engine.TracesPerFrame);
        rig.HasPath(0).ShouldBeTrue();
        rig.HasPath(Engine.TracesPerFrame - 1).ShouldBeTrue();
        rig.HasPath(Engine.TracesPerFrame).ShouldBeFalse();
        rig.Heard(199).Gain.ShouldBe(0f);

        rig.Frame(3);

        rig.Walls.Stats.Unanswered.ShouldBe(0);
        for (int sound = 0; sound < 200; sound++)
            rig.HasPath(sound).ShouldBeTrue();
    }

    [Fact]
    public void When_every_answer_goes_stale_at_once_the_loudest_sounds_are_traced_first()
    {
        WallRig rig = Block(40, out FakeSoundObstacles world);
        rig.Frame(6);
        world.Asked.ShouldNotBeNull().Clear();

        world.Revision++;
        rig.Frame();

        // The block's first sounds are the nearest.
        world.Asked.Select(trace => IndexOf(trace.From)).Distinct().Order()
            .ShouldBe(Enumerable.Range(0, SoundsAFrame));
        rig.Walls.Stats.Waiting.ShouldBe(40 - SoundsAFrame);
        rig.Walls.Stats.Refreshed.ShouldBe(SoundsAFrame);
    }

    [Fact]
    public void Two_hundred_sounds_and_a_walking_listener_allocate_nothing_a_frame_once_running()
    {
        WallRig rig = Block(200, out FakeSoundObstacles world);
        world.Asked = null;
        Walk(rig, 120);

        // The least of several rounds: a one-off from the runtime is not a
        // cost per frame.
        long least = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Walk(rig, 100);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        least.ShouldBe(0L);
        world.Traces.ShouldBeGreaterThan(600 * Engine.TracesPerFrame - 1);
    }

    private static void Walk(WallRig rig, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            rig.Listener = new Vector3(0f, 0f, frame * 0.075f % 3f);
            rig.Frame();
        }
    }

    // Sounds in rows of ten behind the slab, each row a unit further out.
    private static WallRig Block(int sounds, out FakeSoundObstacles world)
    {
        world = new FakeSoundObstacles();
        world.Slab(1f, 1.05f, SpanLevel.Wood);

        var rig = new WallRig(world, WallRig.SpanMaterials());
        for (int i = 0; i < sounds; i++)
            rig.Add(PlaceOf(i), minDistance: 0.5f, maxDistance: 60f);

        return rig;
    }

    private static Vector3 PlaceOf(int sound) => new(2f + (sound / 10), 0f, (sound % 10) * 0.01f);

    private static int IndexOf(Vector3 place) =>
        ((int)MathF.Round(place.X - 2f) * 10) + (int)MathF.Round(place.Z / 0.01f);

    private static int LinesTracedFor(FakeSoundObstacles world, int sound) =>
        world.Asked.ShouldNotBeNull().Count(trace => trace.From == PlaceOf(sound));
}
