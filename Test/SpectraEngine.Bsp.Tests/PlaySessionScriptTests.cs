using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Play;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A level's script runtime through a play session: when it is made, where a
/// tick calls it, and everything that closes it.
/// </summary>
public sealed class PlaySessionScriptTests
{
    private const float Dt = PhysicsDefaults.FixedDeltaTime;

    private readonly List<string> _log = [];
    private FakeScriptRuntime? _runtime;
    private int _made;

    [Fact]
    public void A_tick_tells_the_scripts_before_the_physics_step_and_at_its_end()
    {
        SceneManager manager = Hosted(withScript: true);
        CharacterSimulation walker = Walker(manager);
        walker.Mover = new RecordingMover(_log);
        var session = new PlaySession(manager, walker);
        session.Enter();

        session.Tick(Dt, default);
        session.Tick(Dt, default);

        // The end comes after the character has walked, not only after physics.
        string[] everyTick = ["before simulation", "push", "step", "drain", "character", "after simulation"];
        _log.ShouldBe(["make runtime", "start scripts", .. everyTick, .. everyTick]);
    }

    [Fact]
    public void A_tick_in_which_nothing_walks_still_has_its_end()
    {
        SceneManager manager = Hosted(withScript: true);
        var session = new PlaySession(manager);
        manager.StartEntityWorld();

        session.Tick(Dt, default);

        session.IsActive.ShouldBeFalse();
        _log.ShouldBe(["make runtime", "start scripts", "before simulation", "push", "step", "drain", "after simulation"]);
    }

    [Fact]
    public void The_runtime_is_made_before_the_entities_spawn_and_closed_after_they_are_removed()
    {
        SceneManager manager = Hosted(withScript: true);
        EntityRuntime.Place(manager.ActiveScene.ShouldNotBeNull().Root, "door", "lifecycle");
        var session = new PlaySession(manager, Walker(manager));

        session.Enter();
        manager.EntityWorld.ShouldNotBeNull().Scripts.ShouldBeSameAs(_runtime);
        session.Exit();

        _log.ShouldBe(["make runtime", "spawn:door", "activate:door", "remove:door", "close scripts"]);
        manager.EntityWorld.ShouldBeNull();
    }

    [Fact]
    public void Every_play_session_gets_a_runtime_of_its_own()
    {
        SceneManager manager = Hosted(withScript: true);
        var session = new PlaySession(manager, Walker(manager));

        session.Enter();
        FakeScriptRuntime first = _runtime.ShouldNotBeNull();
        session.Exit();
        session.Enter();

        _made.ShouldBe(2);
        _runtime.ShouldNotBeSameAs(first);
        first.IsClosed.ShouldBeTrue();
        _runtime.ShouldNotBeNull().IsClosed.ShouldBeFalse();
    }

    [Fact]
    public void Taking_the_authored_map_closes_the_runtime()
    {
        SceneManager manager = Hosted(withScript: true);
        new PlaySession(manager, Walker(manager)).Enter();

        manager.TakeAuthoredMap().ShouldNotBeNull();

        _runtime.ShouldNotBeNull().IsClosed.ShouldBeTrue();
    }

    [Fact]
    public void A_scene_replaced_during_play_closes_the_runtime()
    {
        SceneManager manager = Hosted(withScript: true);
        new PlaySession(manager, Walker(manager)).Enter();

        manager.OnSceneReplaced();

        _runtime.ShouldNotBeNull().IsClosed.ShouldBeTrue();
    }

    [Fact]
    public void A_level_with_no_script_on_it_is_given_no_runtime()
    {
        SceneManager manager = Hosted(withScript: false);
        var session = new PlaySession(manager, Walker(manager));

        session.Enter();
        session.Tick(Dt, default);

        _made.ShouldBe(0);
        manager.EntityWorld.ShouldNotBeNull().Scripts.ShouldBeNull();
        _log.ShouldBe(["push", "step", "drain"]);
    }

    // Own catalogue: EntityCatalog.Shared freezes on first read, which would
    // make the tests order-dependent.
    private SceneManager Hosted(bool withScript)
    {
        var manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = EntityRuntime.Catalog(_log),
            PhysicsFactory = _ => new FakeScenePhysics(_log),
        };
        manager.Scripts.RuntimeFactory = Make;

        manager.LoadStartupScene(new FakeRenderer(), new AssetManager(NullLogger<AssetManager>.Instance));
        if (withScript)
            manager.ActiveScene.ShouldNotBeNull().Root.CreateChild("Script").Script = new Script("Scripts/any.luau");

        return manager;
    }

    private IScriptRuntime Make(ScriptRuntimeSetup setup)
    {
        // The world is handed over before it comes to life.
        setup.World.IsActive.ShouldBeFalse();

        _made++;
        _log.Add("make runtime");
        return _runtime = new FakeScriptRuntime(_log);
    }

    // No spawn: the session places it on every Enter.
    private static CharacterSimulation Walker(SceneManager manager) =>
        new(manager.ActiveScene.ShouldNotBeNull())
        {
            FallOutHeight = manager.PlayerFallOutHeight,
        };
}
