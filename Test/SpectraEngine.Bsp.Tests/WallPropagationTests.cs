using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <see cref="WallPropagation"/> over real levels: what stands between a
/// sound and the listener makes it quieter and duller by its material and
/// its thickness, and what does not stand between changes nothing.
/// </summary>
// The sound is 8 units from the listener, straight down -z, and a wall
// crosses the line half way unless a test says otherwise.
public sealed class WallPropagationTests
{
    // The ring's lines cross a wall a little aslant, so a little more of it.
    private const float NearEnough = 2e-3f;

    private static readonly Vector3 Ear = new(0f, 1.5f, 0f);
    private static readonly Vector3 Behind = new(0f, 1.5f, -8f);
    private static readonly Vector3 HalfWay = new(0f, 1.5f, -4f);
    private static readonly Vector3 Across = new(6f, 1.5f, 0f);

    [Fact]
    public void With_nothing_in_the_way_the_answer_is_the_one_distance_alone_gives_to_the_last_bit()
    {
        var level = new SpanLevel();
        level.Box("Aside", new Vector3(20f, 1.5f, -4f), Across with { Z = 0.25f }, SpanLevel.Brick);
        level.Compile();
        var rig = new WallRig(level) { Listener = Ear };

        SceneNode speaker = level.Scene.Root.CreateChild("Speaker");
        speaker.LocalPosition = new Vector3(3f, 1f, -5f);

        rig.Add(new Vector3(0f, 1.5f, -1f));
        rig.Add(Behind);
        rig.Add(new Vector3(-7f, 4f, 11f), minDistance: 0.5f, maxDistance: 60f);
        rig.Add(new Vector3(0f, 1.5f, -29.5f));
        rig.Add(new Vector3(0f, 1.5f, -200f));
        rig.Add(speaker);

        rig.Frame(3);

        for (int sound = 0; sound < rig.Count; sound++)
        {
            SoundPath heard = rig.Heard(sound);
            SoundPath direct = rig.Direct(sound);

            heard.Position.ShouldBe(direct.Position);
            BitConverter.SingleToInt32Bits(heard.Gain).ShouldBe(BitConverter.SingleToInt32Bits(direct.Gain));
            BitConverter.SingleToInt32Bits(heard.GainHf).ShouldBe(BitConverter.SingleToInt32Bits(direct.GainHf));
        }
    }

    [Fact]
    public void Five_centimetres_of_wood_let_more_through_than_a_metre_of_concrete()
    {
        AcousticGains wood = ThroughOneWall(thickness: 0.05f, SpanLevel.Wood);
        AcousticGains concrete = ThroughOneWall(thickness: 1f, WallRig.Concrete);

        wood.Gain.ShouldBeGreaterThan(concrete.Gain * 5f);
        wood.GainHf.ShouldBeGreaterThan(concrete.GainHf * 5f);
    }

    [Fact]
    public void A_wall_takes_what_the_acoustic_table_says_for_its_material_and_thickness()
    {
        AcousticGains wood = ThroughOneWall(thickness: 0.05f, SpanLevel.Wood);
        AcousticGains concrete = ThroughOneWall(thickness: 1f, WallRig.Concrete);

        AcousticGains woodTable = AcousticPresets.Wood.GainsThrough(0.05f);
        wood.Gain.ShouldBe(woodTable.Gain, NearEnough);
        wood.GainHf.ShouldBe(woodTable.GainHf, NearEnough);

        AcousticGains concreteTable = AcousticPresets.Concrete.GainsThrough(1f);
        concrete.Gain.ShouldBe(concreteTable.Gain, NearEnough);
        concrete.GainHf.ShouldBe(concreteTable.GainHf, NearEnough);
    }

    [Fact]
    public void The_paths_gain_is_the_distance_gain_times_the_walls_and_its_high_end_is_the_walls()
    {
        WallRig rig = OneWall(thickness: 0.05f, SpanLevel.Wood, out int sound);

        AcousticGains table = AcousticPresets.Wood.GainsThrough(0.05f);
        rig.Heard(sound).Gain.ShouldBe(SoundFalloff.Gain(8f, 2f, 30f) * table.Gain, NearEnough);
        rig.Heard(sound).GainHf.ShouldBe(table.GainHf, NearEnough);
        rig.Heard(sound).Position.ShouldBe(Behind);
    }

    [Fact]
    public void Two_walls_take_more_than_one_and_what_they_take_adds_up()
    {
        var level = new SpanLevel();
        level.Box("First", new Vector3(0f, 1.5f, -3f), Across with { Z = 0.025f }, SpanLevel.Wood);
        level.Box("Second", new Vector3(0f, 1.5f, -5f), Across with { Z = 0.05f }, SpanLevel.Plaster);
        level.Compile();
        var rig = new WallRig(level) { Listener = Ear };
        int sound = rig.Add(Behind);

        rig.Frame();

        AcousticGains both = (AcousticPresets.Wood.LossThrough(0.05f) + AcousticPresets.Plaster.LossThrough(0.1f)).ToGains();
        rig.Through(sound).Gain.ShouldBe(both.Gain, NearEnough);
        rig.Through(sound).GainHf.ShouldBe(both.GainHf, NearEnough);
        rig.Through(sound).Gain.ShouldBeLessThan(AcousticPresets.Wood.GainsThrough(0.05f).Gain * 0.6f);
    }

    [Fact]
    public void A_sound_heard_through_a_doorway_is_clear_and_through_the_wall_beside_it_is_not()
    {
        var level = new SpanLevel();
        level.Wall();
        level.Doorway();
        level.Compile();
        var rig = new WallRig(level) { Listener = new Vector3(0f, 1.2f, 0f) };
        int sound = rig.Add(new Vector3(0f, 1.2f, -8f));

        rig.Frame();
        rig.Heard(sound).ShouldBe(rig.Direct(sound));

        // From here the line meets the wall 1.7 units beside the doorway.
        rig.Listener = new Vector3(5f, 1.2f, 0f);
        rig.Frame();

        rig.Through(sound).Gain.ShouldBeLessThan(0.1f);
        rig.Through(sound).GainHf.ShouldBeLessThan(0.1f);
    }

    [Fact]
    public void A_trigger_a_model_and_a_part_that_does_not_collide_do_not_muffle()
    {
        var level = new SpanLevel();
        SceneNode trigger = level.Part("Trigger", HalfWay, new Vector3(2f, 1.5f, 0.5f));
        trigger.CanCollide = false;
        trigger.CanQuery = false;
        trigger.IsRendered = false;

        SceneNode ghost = level.Part("Ghost", new Vector3(0f, 1.5f, -2f), new Vector3(2f, 1.5f, 0.25f));
        ghost.CanCollide = false;

        SpatialTestHelpers.CreateMeshNode(level.Scene.Root, "Statue", new Vector3(0f, 1.5f, -6f), half: 1f);

        var rig = new WallRig(level) { Listener = Ear };
        int sound = rig.Add(Behind);

        rig.Frame();

        rig.Heard(sound).ShouldBe(rig.Direct(sound));
    }

    [Fact]
    public void A_part_that_collides_muffles_even_when_rays_do_not_hit_it()
    {
        var level = new SpanLevel();
        SceneNode clip = level.Part("Clip", HalfWay, new Vector3(2f, 1.5f, 0.05f), SpanLevel.Wood);
        clip.CanQuery = false;
        var rig = new WallRig(level) { Listener = Ear };
        int sound = rig.Add(Behind);

        rig.Frame();

        rig.Through(sound).Gain.ShouldBe(AcousticPresets.Wood.GainsThrough(0.1f).Gain, NearEnough);
    }

    [Fact]
    public void A_list_of_solids_that_ran_out_of_room_counts_for_what_it_holds()
    {
        var world = new FakeSoundObstacles { MostSpans = 2 };
        world.Slab(2f, 2.05f, SpanLevel.Wood);
        world.Slab(4f, 4.05f, SpanLevel.Wood);
        world.Slab(6f, 6.05f, SpanLevel.Wood);
        var rig = new WallRig(world, WallRig.SpanMaterials(), new WallPropagationSettings { Lines = 1 });
        int sound = rig.Add(new Vector3(8f, 0f, 0f));

        rig.Frame();

        AcousticLoss one = AcousticPresets.Wood.LossThrough(0.05f);
        rig.Through(sound).Gain.ShouldBe((one + one).ToGains().Gain, 1e-4f);
    }

    [Fact]
    public void However_much_is_in_the_way_a_sound_keeps_the_floor_the_acoustic_table_sets()
    {
        var world = new FakeSoundObstacles();
        for (int i = 0; i < 6; i++)
            world.Slab(1f + i, 1.5f + i, WallRig.Concrete);

        var rig = new WallRig(world, WallRig.SpanMaterials());
        int sound = rig.Add(new Vector3(8f, 0f, 0f));

        rig.Frame();

        rig.Through(sound).Gain.ShouldBe(MathF.Pow(10f, -AcousticLoss.MaxDb / 20f), 1e-5f);
        rig.Through(sound).GainHf.ShouldBe(MathF.Pow(10f, -AcousticLoss.MaxHfDb / 20f), 1e-5f);
    }

    [Fact]
    public void A_view_that_is_partly_open_lets_that_share_of_the_sound_through_and_most_of_its_high_end()
    {
        // A thin slab stands edge on beside the listener's head. Three of the
        // five lines end on this side of it, and two cross it so aslant that
        // they run a metre or more through its concrete.
        var world = new FakeSoundObstacles();
        world.Slab(8.05f, 8.09f, WallRig.Concrete);
        var rig = new WallRig(world, WallRig.SpanMaterials()) { Listener = new Vector3(8f, 0f, 0f) };
        int sound = rig.Add(new Vector3(8f, 0f, 6f), minDistance: 10f);

        rig.Frame();

        float shut = MathF.Pow(10f, -AcousticLoss.MaxDb / 20f);
        float shutHigh = shut * MathF.Pow(10f, -AcousticLoss.MaxHfDb / 20f);

        AcousticGains heard = rig.Through(sound);
        heard.Gain.ShouldBe((3f + (2f * shut)) / 5f, 1e-4f);
        heard.GainHf.ShouldBe((3f + (2f * shutHigh)) / (3f + (2f * shut)), 1e-4f);
        heard.GainHf.ShouldBeGreaterThan(0.95f);
    }

    private static AcousticGains ThroughOneWall(float thickness, SpectraEngine.Core.Assets.MaterialRef material) =>
        OneWall(thickness, material, out int sound).Through(sound);

    private static WallRig OneWall(float thickness, SpectraEngine.Core.Assets.MaterialRef material, out int sound)
    {
        var level = new SpanLevel();
        level.Box("Wall", HalfWay, Across with { Z = thickness * 0.5f }, material);
        level.Compile();

        var rig = new WallRig(level) { Listener = Ear };
        sound = rig.Add(Behind);
        rig.Frame();
        return rig;
    }
}
