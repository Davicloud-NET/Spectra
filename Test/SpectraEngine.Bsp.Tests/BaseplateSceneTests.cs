using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

// The baseplate is what a fresh project boots into and what "new map" makes:
// a sun and solid ground under y = 0.
public sealed class BaseplateSceneTests
{
    [Fact]
    public void The_baseplate_is_a_sun_and_a_ground_plate()
    {
        var scene = new Scene("Fresh");

        SceneManager.PopulateBaseplate(scene);

        scene.Root.Children.Count.ShouldBe(2);

        SceneNode sun = scene.Root.Children[0];
        sun.Name.ShouldBe("Sun");
        sun.Light.ShouldNotBeNull();
        sun.Light.Kind.ShouldBe(LightKind.Directional);
        sun.Light.Intensity.ShouldBeGreaterThan(0f);

        SceneNode plate = scene.Root.Children[1];
        plate.Name.ShouldBe("Baseplate");
        plate.Brush.ShouldNotBeNull();
        plate.BrushKind.ShouldBe(BrushKind.World);
    }

    [Fact]
    public void The_baseplate_scene_has_solid_ground_under_the_spawn()
    {
        var manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
        };

        manager.LoadStartupScene(
            new FakeRenderer(),
            new AssetManager(NullLogger<AssetManager>.Instance));

        Scene scene = manager.ActiveScene.ShouldNotBeNull();

        // Sampled off the origin: x = 0 and z = 0 are chunk-cell boundaries.
        scene.StaticWorld.ContainsPoint(new Vector3(1f, -0.5f, 1f)).ShouldBeTrue();
        scene.StaticWorld.ContainsPoint(new Vector3(1f, 0.5f, 1f)).ShouldBeFalse();

        manager.PlayerSpawn.Y.ShouldBeGreaterThan(0f);
        MathF.Abs(manager.PlayerSpawn.X).ShouldBeLessThan(32f);
        MathF.Abs(manager.PlayerSpawn.Z).ShouldBeLessThan(32f);
    }

    [Fact]
    public void The_demo_stays_the_default_startup_scene()
    {
        new SceneManager(NullLogger<SceneManager>.Instance)
            .Startup.ShouldBe(StartupSceneKind.Demo);
    }
}
