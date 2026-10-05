using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// When an answer of <see cref="WallPropagation"/> is traced again: the
/// world is another, the listener or the sound has moved, or it is old.
/// </summary>
public sealed class WallPropagationStalenessTests
{
    private static readonly WallPropagationSettings Engine = WallPropagationSettings.Default;

    private static readonly Vector3 Ear = new(0f, 1.5f, 0f);
    private static readonly Vector3 Behind = new(0f, 1.5f, -8f);

    [Fact]
    public void An_answer_that_still_holds_is_not_traced_again()
    {
        WallRig rig = OneSound(out FakeSoundObstacles world);

        rig.Frame(5);

        world.Traces.ShouldBe(Engine.Lines);
    }

    [Fact]
    public void A_world_that_is_another_one_has_the_answer_traced_on_the_next_frame()
    {
        WallRig rig = OneSound(out FakeSoundObstacles world);
        rig.Frame();
        world.Traces = 0;

        world.Slab(1f, 1.05f, SpanLevel.Wood);
        world.Revision++;
        rig.Frame();

        world.Traces.ShouldBe(Engine.Lines);
        rig.Through(0).Gain.ShouldBe(AcousticPresets.Wood.GainsThrough(0.05f).Gain, 2e-3f);
    }

    [Fact]
    public void A_recompile_that_puts_a_wall_in_the_way_is_heard_on_the_next_frame()
    {
        var level = new SpanLevel();
        level.Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(6f, 0.5f, 10f));
        level.Compile();
        var rig = new WallRig(level) { Listener = Ear };
        int sound = rig.Add(Behind);
        rig.Frame();
        rig.Heard(sound).ShouldBe(rig.Direct(sound));

        level.Box("Wall", new Vector3(0f, 1.5f, -4f), new Vector3(6f, 1.5f, 0.025f), SpanLevel.Wood);
        level.Compile();
        rig.Frame();

        rig.Through(sound).Gain.ShouldBe(AcousticPresets.Wood.GainsThrough(0.05f).Gain, 2e-3f);
    }

    [Fact]
    public void A_listener_that_moved_less_than_the_move_distance_keeps_the_answer()
    {
        WallRig rig = OneSound(out FakeSoundObstacles world);
        rig.Frame();
        world.Traces = 0;

        rig.Listener += new Vector3(0f, 0f, Engine.MoveDistance * 0.9f);
        rig.Frame(3);

        world.Traces.ShouldBe(0);
    }

    [Fact]
    public void A_listener_that_moved_further_has_the_answer_traced_again()
    {
        WallRig rig = OneSound(out FakeSoundObstacles world);
        rig.Frame();
        world.Traces = 0;

        rig.Listener += new Vector3(0f, 0f, Engine.MoveDistance * 1.1f);
        rig.Frame();

        world.Traces.ShouldBe(Engine.Lines);
    }

    [Fact]
    public void A_listener_that_creeps_has_the_answer_traced_again_once_the_steps_add_up()
    {
        WallRig rig = OneSound(out FakeSoundObstacles world);
        rig.Frame();
        world.Traces = 0;

        for (int step = 0; step < 4; step++)
        {
            rig.Listener += new Vector3(0f, 0f, Engine.MoveDistance * 0.3f);
            rig.Frame();
        }

        world.Traces.ShouldBe(Engine.Lines);
    }

    [Fact]
    public void A_sound_that_moved_has_its_answer_traced_again()
    {
        WallRig rig = OneSound(out FakeSoundObstacles world);
        rig.Frame();
        world.Traces = 0;

        rig.Move(0, new Vector3(8f, 0f, Engine.MoveDistance * 1.1f));
        rig.Frame();

        world.Traces.ShouldBe(Engine.Lines);
    }

    [Fact]
    public void An_old_answer_is_traced_again_though_nothing_moved()
    {
        WallRig rig = OneSound(out FakeSoundObstacles world);
        rig.Frame();
        world.Traces = 0;

        int frames = (int)MathF.Ceiling(Engine.RefreshSeconds / WallRig.FrameSeconds);
        rig.Frame(frames - 1);
        world.Traces.ShouldBe(0);

        rig.Frame();
        world.Traces.ShouldBe(Engine.Lines);
    }

    [Fact]
    public void A_listener_that_jumped_gets_every_sound_answered_anew_on_that_frame()
    {
        var world = new FakeSoundObstacles();
        var rig = new WallRig(world, WallRig.SpanMaterials());
        for (int i = 0; i < 30; i++)
            rig.Add(new Vector3(2f + i, 0f, 0f), minDistance: 20f, maxDistance: 200f);

        rig.Frame(6);
        world.Asked.ShouldNotBeNull().Clear();

        // To the far side of a slab. Kept until their turn, the old answers
        // would be heard clear through it for a few frames.
        world.Slab(-0.05f, 0f, SpanLevel.Wood);
        rig.Listener = new Vector3(-WallPropagation.JumpDistance - 1f, 0f, 0f);
        rig.Frame();

        float wood = AcousticPresets.Wood.GainsThrough(0.05f).Gain;
        for (int sound = 0; sound < 30; sound++)
            rig.Through(sound).Gain.ShouldBe(wood, 2e-3f);
    }

    [Fact]
    public void A_sound_that_jumped_is_answered_anew_on_that_frame()
    {
        WallRig rig = OneSound(out FakeSoundObstacles world);
        for (int i = 0; i < 30; i++)
            rig.Add(new Vector3(2f + i, 0f, 1f), minDistance: 20f, maxDistance: 200f);

        rig.Frame(6);

        // Every answer is due at once, and the one that jumped does not wait.
        world.Slab(-20f, -19.95f, SpanLevel.Wood);
        world.Revision++;
        rig.Move(0, new Vector3(-25f, 0f, 0f));
        rig.Frame();

        rig.Through(0).Gain.ShouldBe(AcousticPresets.Wood.GainsThrough(0.05f).Gain, 2e-3f);
    }

    [Fact]
    public void With_no_world_every_sound_is_heard_by_distance_alone_and_nothing_is_traced()
    {
        WallRig rig = OneSound(out FakeSoundObstacles world);
        world.Slab(1f, 1.05f, SpanLevel.Wood);
        world.HasWorld = false;

        rig.Frame(20);

        world.Traces.ShouldBe(0);
        rig.Heard(0).ShouldBe(rig.Direct(0));
        rig.Walls.Stats.ShouldBe(default);
    }

    [Fact]
    public void A_world_that_comes_back_is_traced_on_that_frame()
    {
        WallRig rig = OneSound(out FakeSoundObstacles world);
        rig.Frame();
        world.HasWorld = false;
        rig.Frame();

        world.Slab(1f, 1.05f, SpanLevel.Wood);
        world.HasWorld = true;
        rig.Frame();

        rig.Through(0).Gain.ShouldBe(AcousticPresets.Wood.GainsThrough(0.05f).Gain, 2e-3f);
    }

    [Fact]
    public void Once_told_to_forget_every_sound_is_answered_anew_though_nothing_changed()
    {
        WallRig rig = OneSound(out FakeSoundObstacles world);
        rig.Frame();
        world.Traces = 0;

        // A wall the old answer does not know, and no sign of it.
        world.Slab(1f, 1.05f, SpanLevel.Wood);
        rig.Walls.Forget();
        rig.Frame();

        world.Traces.ShouldBe(Engine.Lines);
        rig.Through(0).Gain.ShouldBe(AcousticPresets.Wood.GainsThrough(0.05f).Gain, 2e-3f);
    }

    [Fact]
    public void A_sound_that_went_out_of_hearing_and_came_back_is_answered_anew()
    {
        WallRig rig = OneSound(out FakeSoundObstacles world);
        rig.Frame();

        rig.Move(0, new Vector3(40f, 0f, 0f));
        rig.Frame();
        world.Slab(1f, 1.05f, SpanLevel.Wood);
        rig.Move(0, new Vector3(8f, 0f, 0f));
        rig.Frame();

        rig.Through(0).Gain.ShouldBe(AcousticPresets.Wood.GainsThrough(0.05f).Gain, 2e-3f);
    }

    [Fact]
    public void When_a_sound_before_it_in_the_list_stops_a_sound_keeps_its_own_answer()
    {
        var world = new FakeSoundObstacles();
        var rig = new WallRig(world, WallRig.SpanMaterials());
        var first = new SceneNode("First");
        var second = new SceneNode("Second");
        rig.Add(new Vector3(8f, 0f, 0f), first);
        rig.Add(new Vector3(8f, 0f, 3f), second);
        rig.Frame();
        world.Traces = 0;

        rig.RemoveAt(0);
        rig.Frame();

        world.Traces.ShouldBe(0);
        rig.Walls.Stats.Sounds.ShouldBe(1);
    }

    private static WallRig OneSound(out FakeSoundObstacles world)
    {
        world = new FakeSoundObstacles();
        var rig = new WallRig(world, WallRig.SpanMaterials());
        rig.Add(new Vector3(8f, 0f, 0f));
        return rig;
    }
}
