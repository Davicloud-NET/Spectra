using System.Linq;
using System.Numerics;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// Sound through the walls of a cooked level. It is muffled as it is in the
/// level it was cooked from, and a baked map that arrives or leaves has the
/// answers traced again.
/// </summary>
public class CompiledMapWallPropagationTests
{
    // A baked hull's planes went through a file and were normalised once more.
    private const float SameGain = 1e-4f;

    private static readonly Vector3[] Sounds =
    [
        new(0f, 1.2f, -5.5f),
        new(3f, 1.2f, -5.5f),
        new(-4f, 2f, -5f),
        new(0f, 1.2f, -8f),
        new(4f, 0.5f, 2.5f),
        new(-3f, 1.5f, 3f),
    ];

    private static readonly Vector3[] Ears =
    [
        new(0f, 1.6f, 0f),
        new(3f, 1.6f, 0f),
        new(-5f, 1.6f, -2f),
        new(1.6f, 1.6f, -3f),
        new(0f, 1.6f, -5f),
        new(5f, 1.6f, 6f),
    ];

    [Fact]
    public void A_cooked_level_muffles_every_sound_as_the_level_it_was_cooked_from_does()
    {
        using CookedLevel level = CookedLevel.Bake(Room().Scene);
        WallRig baked = Listening(level.Scene);
        WallRig live = Listening(level.Authored);

        int muffled = 0;
        int clear = 0;

        foreach (Vector3 ear in Ears)
        {
            baked.Listener = live.Listener = ear;
            baked.Frame();
            live.Frame();

            for (int sound = 0; sound < baked.Count; sound++)
            {
                string where = $"sound {sound} heard from {ear}";
                baked.Heard(sound).Gain.ShouldBe(live.Heard(sound).Gain, SameGain, where);
                baked.Heard(sound).GainHf.ShouldBe(live.Heard(sound).GainHf, SameGain, where);

                if (live.Heard(sound).GainHf < 0.9f) muffled++;
                else clear++;
            }
        }

        // The comparison heard through walls and the door, and past them.
        muffled.ShouldBeGreaterThan(10);
        clear.ShouldBeGreaterThan(5);
    }

    [Fact]
    public void A_sound_under_a_door_of_a_cooked_level_is_not_muffled_by_that_door()
    {
        using CookedLevel level = CookedLevel.Bake(Room().Scene);
        SceneNode door = level.Scene.Root.Traverse().Single(node => node.Name == "Door");
        var rig = new WallRig(new SceneSoundObstacles(() => level.Scene), WallRig.SpanMaterials())
        {
            Listener = Ears[0],
        };
        int onDoor = rig.Add(door);
        int inDoor = rig.Add(door.WorldPosition);

        rig.Frame();

        rig.Heard(onDoor).ShouldBe(rig.Direct(onDoor));
        rig.Through(inDoor).Gain.ShouldBeLessThan(0.5f);
    }

    [Fact]
    public void A_baked_map_that_loads_or_leaves_has_the_answer_traced_on_the_next_frame()
    {
        using CookedLevel level = CookedLevel.Bake(Room(withDoor: false).Scene);

        var renderer = new FakeRenderer();
        var scene = new Scene("empty");
        var rig = new WallRig(new SceneSoundObstacles(() => scene), WallRig.SpanMaterials())
        {
            Listener = new Vector3(3f, 1.6f, 0f),
        };
        int sound = rig.Add(new Vector3(3f, 1.2f, -5.5f));
        rig.Frame();
        rig.Heard(sound).ShouldBe(rig.Direct(sound));

        // No compile lands and nothing moves: only the baked world is new.
        int compiles = scene.StaticWorldCompileCount;
        CompiledMapLoader.Load(scene, renderer, ContentBlob.CopyOf(level.File), "Maps/Room.scmap");
        rig.Frame();

        scene.StaticWorldCompileCount.ShouldBe(compiles);
        AcousticGains brick = AcousticPresets.Brick.GainsThrough(0.5f);
        rig.Through(sound).Gain.ShouldBe(brick.Gain, 0.01f);
        rig.Through(sound).GainHf.ShouldBe(brick.GainHf, 0.01f);

        scene.ReleaseCompiledStaticWorld(renderer);
        rig.Frame();

        rig.Heard(sound).ShouldBe(rig.Direct(sound));
    }

    private static WallRig Listening(Scene scene)
    {
        var rig = new WallRig(new SceneSoundObstacles(() => scene), WallRig.SpanMaterials());
        foreach (Vector3 sound in Sounds)
            rig.Add(sound);

        return rig;
    }

    // The wall with three materials on it and its doorway, a tiled floor, a
    // wooden wall behind, a concrete block and a door as a part.
    private static SpanLevel Room(bool withDoor = true)
    {
        var level = new SpanLevel();
        level.Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(6f, 0.5f, 8f), SpanLevel.Tile);
        level.Wall();
        level.Doorway();
        level.Box("Back", new Vector3(0f, 1.5f, -6.5f), new Vector3(6f, 1.5f, 0.5f), SpanLevel.Wood);
        level.Box("Block", new Vector3(4f, 0.75f, 2f), new Vector3(1f, 0.75f, 1f), WallRig.Concrete);

        if (withDoor)
            level.Part("Door", new Vector3(0f, 1.2f, -4.25f), new Vector3(1f, 1.2f, 0.2f), SpanLevel.Wood);

        return level;
    }
}
