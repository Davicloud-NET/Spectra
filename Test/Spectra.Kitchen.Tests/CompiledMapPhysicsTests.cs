using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Physics.Box3D;
using System;
using System.IO;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// A cooked level under the physics backend: its world brushes become the same
/// static hulls the authored level's do.
/// </summary>
public class CompiledMapPhysicsTests
{
    private static void RequireNative() =>
        Assert.SkipWhen(
            !File.Exists(Path.Combine(AppContext.BaseDirectory, "box3d.dll")),
            "box3d.dll is not present beside the test binary. Build it with native/build-box3d.ps1");

    [Fact]
    public void A_cooked_level_gets_the_static_hulls_its_authored_level_gets()
    {
        RequireNative();
        using CookedLevel level = CookedLevel.Bake(MapFixture.Fresh().BuildScene());

        using var cooked = new Box3DScenePhysics(NullLogger.Instance);
        cooked.SyncStaticWorld(level.Scene);

        using var authored = new Box3DScenePhysics(NullLogger.Instance);
        authored.SyncStaticWorld(level.Authored);

        // The floor and two walls. The doorway cut gets no hull of its own.
        cooked.StaticShapeCount.ShouldBe(3);
        cooked.StaticShapeCount.ShouldBe(authored.StaticShapeCount);
        cooked.BodyCount.ShouldBe(authored.BodyCount);
        cooked.CutBrushesWithoutCollision.ShouldBe(authored.CutBrushesWithoutCollision);

        // A baked world does not change, so a second sync builds nothing.
        int bodies = cooked.BodyCount;
        cooked.SyncStaticWorld(level.Scene);
        cooked.BodyCount.ShouldBe(bodies);
        cooked.StaticShapeCount.ShouldBe(3);
    }

    [Fact]
    public void Releasing_a_cooked_level_takes_its_hulls_with_it()
    {
        RequireNative();
        using CookedLevel level = CookedLevel.Bake(MapFixture.Fresh().BuildScene());

        var renderer = new FakeRenderer();
        var scene = new SpectraEngine.Core.Scene.Scene("empty");
        CompiledMapLoader.Load(scene, renderer, ContentBlob.CopyOf(level.File), "Maps/Room.scmap");

        using var physics = new Box3DScenePhysics(NullLogger.Instance);
        physics.SyncStaticWorld(scene);
        physics.StaticShapeCount.ShouldBeGreaterThan(0);

        scene.ReleaseCompiledStaticWorld(renderer);
        physics.SyncStaticWorld(scene);

        physics.StaticShapeCount.ShouldBe(0);
        physics.BodyCount.ShouldBe(0);
    }
}
