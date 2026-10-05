using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using System;
using System.Linq;
using System.Numerics;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// A cooked level under the real character mover and the gameplay ray. It has a
/// floor, its walls are solid, its doorway is open, and it behaves as the
/// authored level does.
/// </summary>
// The room: floor top at y = 0 out to 6 on x and z. A wall fills z = -4.5 to -4
// up to y = 3, with a doorway at x = -1 to 1 up to y = 2.4. A crate, a part,
// stands at (2, 0.5, 2).
public class CompiledMapWalkTests
{
    // Floor, two walls and the doorway cut.
    private const int WorldBrushes = 4;

    // The far side of the wall for a capsule's centre: the wall's back face
    // less the capsule radius.
    private const float PastTheWall = -4.85f;

    [Fact]
    public void The_character_stands_on_a_cooked_floor()
    {
        using CookedLevel level = CookedLevel.Bake(MapFixture.Fresh().BuildScene());
        var walker = new ScriptedWalker(level.Scene);

        CharacterState state = walker.Settle(CharacterState.AtFeet(new Vector3(0f, 0.5f, 0f)), 60);

        state.Grounded.ShouldBeTrue("the cooked floor did not catch the character");

        // A resting character sits one skin width above the surface.
        state.Position.Y.ShouldBe(walker.Tuning.SkinWidth, 1e-3f);
        walker.Source.WorldPieceCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_doorway_cut_by_a_subtractive_brush_is_walkable_in_a_cooked_level()
    {
        using CookedLevel level = CookedLevel.Bake(MapFixture.Fresh().BuildScene());
        var walker = new ScriptedWalker(level.Scene);

        CharacterState state = walker.Settle(CharacterState.AtFeet(new Vector3(0f, 0.05f, -2f)), 10);
        state = walker.Walk(state, ScriptedWalker.North, 45);

        state.Position.Z.ShouldBeLessThan(
            PastTheWall, $"the doorway should let the character through, but it stopped at z={state.Position.Z}");
        state.Grounded.ShouldBeTrue("the floor carries on past the wall");
        walker.Source.UncoveredCutBrushes.ShouldBe(0);
    }

    [Fact]
    public void The_wall_beside_a_cooked_doorway_is_solid()
    {
        using CookedLevel level = CookedLevel.Bake(MapFixture.Fresh().BuildScene());
        var walker = new ScriptedWalker(level.Scene);

        CharacterState state = walker.Settle(CharacterState.AtFeet(new Vector3(3f, 0.05f, -2f)), 10);
        state = walker.Walk(state, ScriptedWalker.North, 60);

        // The wall's near face is at z = -4 and the capsule radius is 0.35.
        state.Position.Z.ShouldBeGreaterThan(-3.7f, "the character walked into the wall");
        state.Position.Z.ShouldBeLessThan(-3.5f, "the character never reached the wall");
    }

    [Fact]
    public void A_scripted_walk_goes_where_it_goes_in_the_authored_level()
    {
        using CookedLevel level = CookedLevel.Bake(MapFixture.Fresh().BuildScene());

        // Out through the doorway, along the back of the wall and into it,
        // back in, then into the crate and over it.
        (Vector3 lowest, Vector3 highest, CharacterState end) = WalkBoth(
            level,
            new Vector3(0f, 0.3f, -1f),
            (ScriptedWalker.East, 0f, false, 20),
            (ScriptedWalker.North, 1f, false, 45),
            (ScriptedWalker.West, 1f, false, 12),
            (ScriptedWalker.South, 1f, false, 40),
            (ScriptedWalker.East, 1f, false, 10),
            (ScriptedWalker.South, 1f, false, 60),
            (ScriptedWalker.East, 1f, true, 30),
            (ScriptedWalker.South, 1f, false, 40),
            (ScriptedWalker.South, 1f, true, 30));

        // The route did what it says: through the doorway and back inside.
        lowest.Z.ShouldBeLessThan(PastTheWall);
        end.Position.Z.ShouldBeGreaterThan(0f);
        lowest.Y.ShouldBeGreaterThan(-0.1f, "the walk went off the floor, which compares a fall");
        highest.Y.ShouldBeGreaterThan(0.5f, "neither jump left the ground");
    }

    [Fact]
    public void A_walk_up_a_slanted_brush_goes_where_it_goes_in_the_authored_level()
    {
        // Slanted planes are where a number written to a file and read back
        // could come out one bit different.
        SpectraEngine.Core.Scene.Scene scene = MapFixture.Fresh().BuildScene();

        // A wedge rising 20 degrees toward +x, sunk a little into the floor.
        SceneNode ramp = scene.Root.CreateChild("Ramp");
        ramp.LocalPosition = new Vector3(-3.5f, -0.1f, 2.5f);
        ramp.Brush = new Brush(
        [
            new Plane(-0.34202015f, 0.9396926f, 0f, -0.6840403f),
            new Plane(0f, -1f, 0f, 0f),
            new Plane(1f, 0f, 0f, -2f),
            new Plane(0f, 0f, 1f, -1f),
            new Plane(0f, 0f, -1f, -1f),
        ]);

        using CookedLevel level = CookedLevel.Bake(scene);

        // Up the ramp, off its high end and on into the crate.
        (Vector3 lowest, Vector3 highest, CharacterState end) = WalkBoth(
            level,
            new Vector3(-5.8f, 0.05f, 2.5f),
            (ScriptedWalker.East, 0f, false, 10),
            (ScriptedWalker.East, 1f, false, 110));

        highest.Y.ShouldBeGreaterThan(1f, "the character never climbed the ramp");
        end.Position.X.ShouldBeGreaterThan(-1.5f, "the character never left the ramp");
        end.Grounded.ShouldBeTrue();
        lowest.Y.ShouldBeGreaterThan(-0.1f);
    }

    [Fact]
    public void The_demo_course_is_climbed_and_walked_through_from_a_cooked_level()
    {
        // The level the packed boot loads: stairs, a terrace and a doorway cut
        // through its wall, far from the origin.
        var course = new SpectraEngine.Core.Scene.Scene("Course");
        DemoPlayArea.Build(course, MaterialRef.Default, MaterialRef.Default, MaterialRef.Default);

        using CookedLevel level = CookedLevel.Bake(course);

        (_, Vector3 top, _) = WalkBoth(
            level,
            new Vector3(133f, 0.05f, -8f),
            (ScriptedWalker.East, 0f, false, 10),
            (ScriptedWalker.East, 1f, false, 150));

        top.Y.ShouldBeGreaterThan(1.9f, "the staircase was not climbed to the terrace");

        (_, _, CharacterState through) = WalkBoth(
            level,
            new Vector3(141.5f, 2.05f, 0f),
            (ScriptedWalker.East, 0f, false, 20),
            (ScriptedWalker.East, 1f, false, 90));

        through.Position.X.ShouldBeGreaterThan(144.5f, "the terrace doorway did not let the character through");
        through.Grounded.ShouldBeTrue();
    }

    // Runs one command list over the cooked level and the authored one and
    // compares them every tick. Returns the cooked walk's extremes and end.
    private static (Vector3 Lowest, Vector3 Highest, CharacterState End) WalkBoth(
        CookedLevel level, Vector3 start, params (float Yaw, float Forward, bool Jump, int Ticks)[] route)
    {
        var cooked = new ScriptedWalker(level.Scene);
        var authored = new ScriptedWalker(level.Authored);

        CharacterState here = CharacterState.AtFeet(start);
        CharacterState there = here;
        Vector3 lowest = start;
        Vector3 highest = start;
        int tick = 0;

        foreach ((float yaw, float forward, bool jump, int ticks) in route)
        {
            for (int i = 0; i < ticks; i++, tick++)
            {
                here = cooked.Step(here, yaw, forward, jump);
                there = authored.Step(there, yaw, forward, jump);

                Vector3.Distance(here.Position, there.Position).ShouldBeLessThan(
                    1e-4f, $"tick {tick}: cooked {here.Position}, authored {there.Position}");
                here.Grounded.ShouldBe(there.Grounded, $"tick {tick}");

                lowest = Vector3.Min(lowest, here.Position);
                highest = Vector3.Max(highest, here.Position);
            }
        }

        return (lowest, highest, here);
    }

    [Fact]
    public void A_gameplay_ray_stops_at_a_cooked_wall_and_passes_through_its_doorway()
    {
        using CookedLevel level = CookedLevel.Bake(MapFixture.Fresh().BuildScene());

        var atTheWall = new Ray3(new Vector3(3f, 1f, 0f), -Vector3.UnitZ);
        level.Scene.RaycastGameplay(in atTheWall, out GameplayRayHit hit, 20f)
            .ShouldBeTrue("the ray went through a cooked wall");

        hit.StaticWorld.ShouldBeTrue();
        hit.Node.ShouldBeNull();
        hit.Distance.ShouldBe(4f, 1e-3f);
        hit.Point.Z.ShouldBe(-4f, 1e-3f);
        hit.Normal.ShouldBe(Vector3.UnitZ);

        // The authored level gives the same answer.
        level.Authored.RaycastGameplay(in atTheWall, out GameplayRayHit expected, 20f).ShouldBeTrue();
        hit.Point.ShouldBe(expected.Point);
        hit.Normal.ShouldBe(expected.Normal);
        hit.Distance.ShouldBe(expected.Distance);

        var throughTheDoorway = new Ray3(new Vector3(0f, 1f, 0f), -Vector3.UnitZ);
        level.Scene.RaycastGameplay(in throughTheDoorway, out _, 20f)
            .ShouldBeFalse("the doorway is open, so nothing is in the ray's way");
        level.Authored.RaycastGameplay(in throughTheDoorway, out _, 20f).ShouldBeFalse();

        // A part still answers, and names its node.
        var atTheCrate = new Ray3(new Vector3(2f, 0.5f, 0f), Vector3.UnitZ);
        level.Scene.RaycastGameplay(in atTheCrate, out GameplayRayHit crate, 20f).ShouldBeTrue();
        crate.StaticWorld.ShouldBeFalse();
        crate.Node.ShouldNotBeNull().Name.ShouldBe("Crate");
        crate.Distance.ShouldBe(1.5f, 1e-3f);
    }

    [Fact]
    public void The_baked_trees_answer_rays_as_the_authored_world_does()
    {
        using CookedLevel level = CookedLevel.Bake(MapFixture.Fresh().BuildScene());

        CompiledStaticWorld baked = level.Scene.CompiledStaticWorld.ShouldNotBeNull();
        CsgWorld live = level.Authored.StaticWorld.ShouldNotBeNull();

        // The room straddles the origin, so these cross cell borders on every
        // axis. Some start inside the floor and some miss everything.
        Vector3[] directions =
        [
            Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ,
            new(1f, -0.4f, 0.3f), new(-0.2f, -1f, 0.7f), new(0.5f, 0.1f, -1f), new(-1f, 0.25f, -0.6f),
        ];

        int hits = 0;
        int misses = 0;

        for (float x = -5f; x <= 5f; x += 2.5f)
        {
            for (float y = -0.25f; y <= 3.25f; y += 1.75f)
            {
                for (float z = -5.5f; z <= 5.5f; z += 2.75f)
                {
                    foreach (Vector3 direction in directions)
                    {
                        var origin = new Vector3(x, y, z);
                        bool expected = live.Raycast(origin, direction, 40f, out BspRaycastHit want);
                        bool actual = baked.Raycast(origin, direction, 40f, out BspRaycastHit got);

                        actual.ShouldBe(expected, $"from {origin} along {direction}");
                        got.ShouldBe(want, $"from {origin} along {direction}");

                        if (expected) hits++;
                        else misses++;
                    }
                }
            }
        }

        hits.ShouldBeGreaterThan(50);
        misses.ShouldBeGreaterThan(50);
    }

    [Fact]
    public void A_cooked_level_gets_its_collision_without_a_carve()
    {
        using CookedLevel level = CookedLevel.Bake(MapFixture.Fresh().BuildScene(withLights: true));

        level.CarvesDuringLoad.ShouldBe(0, "a compiled map must load with no CSG at all");
        level.Report.CollisionHullsLoaded.ShouldBe(WorldBrushes);
        level.Report.IsComplete.ShouldBeTrue(level.Report.Describe());

        // Collision came from the hulls, not from brushes put back on nodes.
        level.Scene.CompiledStaticWorld.ShouldNotBeNull().CollisionPlacements.Count.ShouldBe(WorldBrushes);
        level.Scene.Root.Traverse().Count(node => node.IsStaticWorldBrush).ShouldBe(0);

        long before = Csg.CarveInvocationsOnThisThread;

        var walker = new ScriptedWalker(level.Scene);
        CharacterState state = walker.Settle(CharacterState.AtFeet(new Vector3(0f, 0.05f, -2f)), 10);
        state = walker.Walk(state, ScriptedWalker.North, 45);
        level.Scene.RaycastGameplay(new Ray3(new Vector3(3f, 1f, 0f), -Vector3.UnitZ), out _, 20f);

        state.Position.Z.ShouldBeLessThan(PastTheWall);
        (Csg.CarveInvocationsOnThisThread - before).ShouldBe(0, "walking and looking must not carve");

        level.Scene.StaticWorld.ShouldBeNull();
        level.Scene.StaticWorldCompileCount.ShouldBe(0);
        level.Scene.RefusedStaticWorldRebuilds.ShouldBe(0);
        level.Scene.RefusedStaticWorldDirtyMarks.ShouldBe(0);
    }

    [Fact]
    public void A_collision_source_built_before_a_level_loads_follows_the_new_level()
    {
        // A baked world never compiles, so the compile counter cannot tell the
        // mover that the world under it was replaced.
        using CookedLevel level = CookedLevel.Bake(MapFixture.Fresh().BuildScene());

        var renderer = new FakeRenderer();
        var scene = new SpectraEngine.Core.Scene.Scene("before");
        Box(scene, "OldFloor", new Vector3(0f, -0.5f, 0f), new Vector3(6f, 0.5f, 6f));
        Box(scene, "OldWall", new Vector3(0f, 1.5f, -2f), new Vector3(6f, 1.5f, 0.25f));
        scene.RebuildStaticWorld(renderer);

        var walker = new ScriptedWalker(scene);
        CharacterState state = walker.Settle(CharacterState.AtFeet(new Vector3(0f, 0.05f, 0f)), 10);
        int revision = walker.Source.Revision;

        CompiledMapLoader.Load(scene, renderer, ContentBlob.CopyOf(level.File), "Maps/Room.scmap");

        // Where the old wall stood, the new room has open floor.
        state = walker.Walk(state, ScriptedWalker.North, 45);

        state.Position.Z.ShouldBeLessThan(-2.5f, "the mover is still colliding with the level that was replaced");
        walker.Source.Revision.ShouldBeGreaterThan(revision);

        scene.ReleaseCompiledStaticWorld(renderer);
    }

    [Fact]
    public void A_map_from_before_collision_and_lights_is_refused_rather_than_loaded_without_them()
    {
        // Version 2 had no collision hulls. Loaded anyway, the player falls
        // through the floor of an unlit level.
        using CookedLevel level = CookedLevel.Bake(MapFixture.Fresh().BuildScene());
        byte[] file = (byte[])level.File.Clone();
        BitConverter.GetBytes((ushort)2).CopyTo(file, 0x04);

        ScmapFormatException refused = Should.Throw<ScmapFormatException>(
            () => CompiledMapLoader.Load(
                new SpectraEngine.Core.Scene.Scene("empty"),
                new FakeRenderer(),
                ContentBlob.CopyOf(file),
                "Maps/Room.scmap"));

        EngineInfo.CompiledMapFormatVersion.ShouldBeGreaterThan((ushort)2);
        refused.Message.ShouldContain("format version 2,");
        refused.Message.ShouldContain($"reads version {EngineInfo.CompiledMapFormatVersion}");
        refused.Message.ShouldContain("Recook");
    }

    private static void Box(SpectraEngine.Core.Scene.Scene scene, string name, Vector3 center, Vector3 half)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.LocalPosition = center;
        node.Brush = Brush.CreateBox(-half, half);
    }
}
