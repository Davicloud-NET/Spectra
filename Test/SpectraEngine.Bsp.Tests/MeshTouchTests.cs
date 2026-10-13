using System.Numerics;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The touch of a mesh: by the box around it, also when the mesh is scaled
/// by another amount along each axis. And what is said of a mesh a baked
/// map did not load, which nothing can touch.
/// </summary>
public sealed class MeshTouchTests
{
    private const float Dt = Touching.Dt;

    private readonly List<string> _log = [];
    private readonly FakePlayerPresence _player = new();

    private string[] Heard => [.. _log.Where(line => line.StartsWith("hear", StringComparison.Ordinal))];

    // The mesh is a cube a unit to a side. A coin lies flat on the ground
    // under the player's feet, and a plank hangs at the height of the waist.
    [Theory]
    [InlineData(0.5f, 0.05f, 0.5f, 0f, 0.025f, 0f, true)]
    [InlineData(0.5f, 0.05f, 0.5f, 0f, 0.025f, 0.40f, true)]
    [InlineData(0.5f, 0.05f, 0.5f, 0f, 0.025f, 0.46f, false)]
    [InlineData(4f, 0.2f, 1f, 0f, 1f, 2.33f, true)]
    [InlineData(4f, 0.2f, 1f, 0f, 1f, 2.37f, false)]
    [InlineData(4f, 0.2f, 1f, MathF.PI / 2f, 1f, 0.83f, true)]
    [InlineData(4f, 0.2f, 1f, MathF.PI / 2f, 1f, 0.87f, false)]
    public void A_mesh_scaled_by_another_amount_along_each_axis_is_touched_where_the_player_overlaps_its_box(
        float scaleX, float scaleY, float scaleZ, float turn, float meshY, float feetX, bool isTouched)
    {
        var scene = new Scene("Touch");
        SceneNode mesh = SpatialTestHelpers.CreateMeshNode(scene.Root, "mesh", new Vector3(0f, meshY, 0f));
        mesh.LocalScale = new Vector3(scaleX, scaleY, scaleZ);
        mesh.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn);
        EntityWorld world = Play(scene);
        Listen(world, mesh);

        _player.Feet = new Vector3(feetX, 0f, 0f);
        world.Tick(Dt);

        Heard.ShouldBe(isTouched ? ["hear touch 1 by 0"] : []);
    }

    // The margin is a length of the world, whatever the mesh is scaled by.
    [Theory]
    [InlineData(2.38f, false)]
    [InlineData(2.42f, true)]
    public void A_touch_of_a_stretched_mesh_ends_once_the_player_is_clear_by_the_exit_margin(float feetX, bool hasEnded)
    {
        var scene = new Scene("Touch");
        SceneNode plank = SpatialTestHelpers.CreateMeshNode(scene.Root, "plank", new Vector3(0f, 1f, 0f));
        plank.LocalScale = new Vector3(4f, 0.2f, 1f);
        EntityWorld world = Play(scene);
        Listen(world, plank);
        _player.Feet = new Vector3(2.3f, 0f, 0f);
        world.Tick(Dt);

        _player.Feet = new Vector3(feetX, 0f, 0f);
        world.Tick(Dt);

        Heard.ShouldBe(hasEnded ? ["hear touch 1 by 0", "hear touch 2 by 0"] : ["hear touch 1 by 0"]);
    }

    [Fact]
    public void A_listener_for_the_touch_of_a_mesh_a_baked_map_did_not_load_is_warned_of_once_for_the_node()
    {
        var scene = new Scene("Touch");
        var coin = new SceneNode("Coin") { BakedKind = SceneNodeKind.Mesh };
        scene.Root.AddChild(coin);
        var logger = new CapturingLogger();
        EntityWorld world = Touching.Play(scene, _log, _player, logger);

        world.Listeners.Add(coin, TouchSignal.Touched, 1);
        world.Listeners.Add(coin, TouchSignal.TouchEnded, 2);
        world.Listeners.Add(coin, TouchSignal.Touched, 3);

        logger.MessagesAt(LogLevel.Warning).ShouldBe(
        [
            "Coin is a mesh, and a baked map does not load a mesh yet. Nothing can touch it in this level, " +
            "so a script that listens for its touch is never called. Put a part around it and listen to that.",
        ]);

        // The next play of the level says it again.
        world.Deactivate();
        world.Activate();
        world.Listeners.Add(coin, TouchSignal.Touched, 1);
        logger.MessagesAt(LogLevel.Warning).Count.ShouldBe(2);
    }

    [Fact]
    public void A_listener_for_anything_else_is_warned_of_nothing()
    {
        var scene = new Scene("Touch");
        SceneNode loaded = SpatialTestHelpers.CreateMeshNode(scene.Root, "loaded", Vector3.Zero);
        var baked = new SceneNode("Baked") { BakedKind = SceneNodeKind.Mesh };
        scene.Root.AddChild(baked);
        var logger = new CapturingLogger();
        EntityWorld world = Touching.Play(scene, _log, _player, logger);

        // A mesh with its shape, and an output that is no touch.
        world.Listeners.Add(loaded, TouchSignal.Touched, 1);
        world.Listeners.Add(baked, "OnPing", 2);

        logger.MessagesAt(LogLevel.Warning).ShouldBeEmpty();
        TouchSignal.WhyNotHere(loaded).ShouldBeNull();
        TouchSignal.WhyNotHere(baked).ShouldNotBeNull().ShouldStartWith("Baked is a mesh, and a baked map");
    }

    private EntityWorld Play(Scene scene)
    {
        EntityWorld world = Touching.Play(scene, _log, _player);
        world.Scripts = new FakeScriptRuntime(_log);
        return world;
    }

    // A level's listeners are gone when it starts, so this comes after Play.
    private static void Listen(EntityWorld world, SceneNode node)
    {
        world.Listeners.Add(node, TouchSignal.Touched, 1);
        world.Listeners.Add(node, TouchSignal.TouchEnded, 2);
    }
}
