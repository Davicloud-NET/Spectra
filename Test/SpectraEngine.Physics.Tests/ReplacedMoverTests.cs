using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Play;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Physics.Box3D;
using Xunit;

namespace SpectraEngine.Physics.Tests;

/// <summary>
/// A mover a level put in the built-in one's place, played through a play
/// session with Box3D stepping the level's world. A mover asks the brush
/// collision for everything and Box3D for nothing, so the two have to see
/// the same floor.
/// </summary>
[Collection(NativeWorldCollection.Name)]
public sealed class ReplacedMoverTests
{
    private const float Dt = PhysicsDefaults.FixedDeltaTime;

    private static void RequireNative() =>
        Assert.SkipWhen(
            !File.Exists(Path.Combine(AppContext.BaseDirectory, "box3d.dll")),
            "box3d.dll is not beside the test binary. Build it with: native/build-box3d.ps1");

    [Fact]
    public void A_replaced_mover_lands_on_the_floor_Box3D_holds_too()
    {
        RequireNative();
        SceneManager manager = Baseplate();
        var character = new CharacterSimulation(manager.ActiveScene.ShouldNotBeNull());
        var session = new PlaySession(manager, character);
        var mover = new FallingMover();
        session.Enter();
        new PlayerWrites(manager.EntityWorld.ShouldNotBeNull()).TrySetMover(mover, out string? refusal).ShouldBeTrue(refusal);

        for (int i = 0; i < 90; i++)
            session.Tick(Dt, default);

        manager.Physics.IsSimulating.ShouldBeTrue("the level's physics is Box3D");
        mover.Ticks.ShouldBe(90, "the mover runs once on every tick, after the step");
        character.State.Grounded.ShouldBeTrue();
        character.State.Position.Y.ShouldBe(character.Tuning.SkinWidth, 1e-3f);
        character.State.Memory[FallingMover.AirTicksSlot].ShouldBeGreaterThan(10f, "it fell a unit, and counted");

        session.Exit();
        manager.Shutdown();
    }

    [Fact]
    public void What_a_replaced_mover_keeps_in_the_state_comes_back_with_the_state()
    {
        RequireNative();
        SceneManager manager = Baseplate();
        var character = new CharacterSimulation(manager.ActiveScene.ShouldNotBeNull());
        var session = new PlaySession(manager, character);
        session.Enter();
        new PlayerWrites(manager.EntityWorld.ShouldNotBeNull()).TrySetMover(new FallingMover(), out _).ShouldBeTrue();

        for (int i = 0; i < 5; i++)
            session.Tick(Dt, default);
        CharacterState kept = character.State;
        for (int i = 0; i < 60; i++)
            session.Tick(Dt, default);
        CharacterState first = character.State;

        character.Restore(in kept);
        for (int i = 0; i < 60; i++)
            session.Tick(Dt, default);

        kept.Memory[FallingMover.AirTicksSlot].ShouldBe(5f);
        character.State.Position.ShouldBe(first.Position);
        character.State.Memory.ShouldBe(first.Memory);

        session.Exit();
        manager.Shutdown();
    }

    [Fact]
    public void Leaving_play_puts_the_built_in_mover_back_and_the_next_play_falls_by_it()
    {
        RequireNative();
        SceneManager manager = Baseplate();
        var character = new CharacterSimulation(manager.ActiveScene.ShouldNotBeNull());
        var session = new PlaySession(manager, character);
        var mover = new FallingMover();
        session.Enter();
        new PlayerWrites(manager.EntityWorld.ShouldNotBeNull()).TrySetMover(mover, out _).ShouldBeTrue();
        session.Tick(Dt, default);

        session.Exit();
        session.Enter();
        for (int i = 0; i < 60; i++)
            session.Tick(Dt, default);

        character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);
        mover.Ticks.ShouldBe(1);
        character.State.Grounded.ShouldBeTrue();
        character.State.Memory.ShouldBe(default);

        session.Exit();
        manager.Shutdown();
    }

    private static SceneManager Baseplate()
    {
        var manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            PhysicsFactory = _ => new Box3DScenePhysics(NullLogger.Instance),
        };
        manager.LoadStartupScene(new FakeRenderer(), new AssetManager(NullLogger<AssetManager>.Instance));
        manager.Physics.SyncStaticWorld(manager.ActiveScene.ShouldNotBeNull());
        return manager;
    }
}
