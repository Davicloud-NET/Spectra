using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Taking a scene as its authored map, which is what a host keeps of a level
/// when the engine under it dies.
/// </summary>
public sealed class AuthoredMapTests
{
    private static SceneManager Hosted()
    {
        var manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = EntityMotion.Catalog([]),
        };

        manager.LoadStartupScene(new FakeRenderer(), new AssetManager(NullLogger<AssetManager>.Instance));
        return manager;
    }

    private static MapNode Named(MapDocument map, string name) =>
        map.Nodes.Single(node => node.Name == name);

    [Fact]
    public void A_scene_at_rest_comes_out_as_it_stands()
    {
        SceneManager manager = Hosted();
        Scene scene = manager.ActiveScene.ShouldNotBeNull();
        SceneNode block = EntityMotion.BrushNode(scene.Root, "block", BrushKind.World, new Vector3(4f, 1f, -2f));

        MapDocument map = manager.TakeAuthoredMap().ShouldNotBeNull();

        map.Nodes.Count.ShouldBe(scene.Root.Children.Count);
        MapNode taken = Named(map, "block");
        taken.Id.ShouldBe(block.Id);
        taken.Transform.Position.ShouldBe(new Vector3(4f, 1f, -2f));
    }

    // A run moves nodes. Taken as they stand, a door caught open would be
    // saved open, and its closed pose would be gone for good.
    [Fact]
    public void A_level_that_is_running_is_stopped_and_comes_out_in_its_authored_pose()
    {
        SceneManager manager = Hosted();
        Scene scene = manager.ActiveScene.ShouldNotBeNull();
        var authored = new Vector3(2.2f, 0.45f, -1.1f);
        SceneNode door = EntityMotion.Slider(
            EntityMotion.BrushNode(scene.Root, "door", BrushKind.Part, authored),
            new Vector3(0f, 1.9f, 0f), ticks: 20);

        manager.StartEntityWorld();
        EntityWorld world = manager.EntityWorld.ShouldNotBeNull();
        for (int i = 0; i < 7; i++)
            world.Tick(EntityMotion.Dt);

        door.LocalPosition.ShouldNotBe(authored, "the run has to have moved the door for this to mean anything");

        MapDocument map = manager.TakeAuthoredMap().ShouldNotBeNull();

        Named(map, "door").Transform.Position.ShouldBe(authored);
        door.LocalPosition.ShouldBe(authored);
        manager.EntityWorld.ShouldBeNull();
    }

    [Fact]
    public void A_manager_with_no_scene_has_no_map_to_give()
    {
        var manager = new SceneManager(NullLogger<SceneManager>.Instance);

        manager.TakeAuthoredMap().ShouldBeNull();
    }
}
