using System.Numerics;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Play;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The baseplate level played through a <see cref="PlaySession"/> alone, the
/// way a server or a replay would: nothing here has a window, a view or input.
/// </summary>
public sealed class PlaySessionTests
{
    private const float Dt = PhysicsDefaults.FixedDeltaTime;

    private static readonly string[] PhysicsTick = ["push", "step", "drain"];

    [Fact]
    public void Entering_spawns_the_character_and_starts_the_entity_world()
    {
        var log = new List<string>();
        SceneManager manager = Hosted(log);
        Place(manager, "door", "lifecycle");
        CharacterSimulation character = Walker(manager);
        var session = new PlaySession(manager, character);

        session.IsActive.ShouldBeFalse();
        manager.EntityWorld.ShouldBeNull();

        session.Enter();

        session.IsActive.ShouldBeTrue();
        character.State.Position.ShouldBe(manager.PlayerSpawn);
        manager.EntityWorld.ShouldNotBeNull().IsActive.ShouldBeTrue();
        log.ShouldBe(new[] { "spawn:door", "activate:door" });
    }

    [Fact]
    public void Entering_gives_the_entities_the_character_as_their_player()
    {
        SceneManager manager = Hosted([]);
        CharacterSimulation character = Walker(manager);
        var session = new PlaySession(manager, character);

        session.Enter();
        EntityWorld world = manager.EntityWorld.ShouldNotBeNull();

        world.Player.ShouldBeSameAs(character);
        character.IsPresent.ShouldBeTrue();

        session.Exit();

        world.Player.ShouldBeNull();
    }

    [Fact]
    public void Entering_while_playing_changes_nothing()
    {
        var log = new List<string>();
        SceneManager manager = Hosted(log);
        Place(manager, "door", "lifecycle");
        CharacterSimulation character = Walker(manager);
        var session = new PlaySession(manager, character);
        session.Enter();
        EntityWorld world = manager.EntityWorld.ShouldNotBeNull();

        for (int i = 0; i < 30; i++)
            session.Tick(Dt, default);
        Vector3 feet = character.State.Position;

        session.Enter();

        character.State.Position.ShouldBe(feet);
        manager.EntityWorld.ShouldBeSameAs(world);
        log.ShouldBe(new[] { "spawn:door", "activate:door" });
    }

    [Fact]
    public void Exiting_stops_the_entity_world()
    {
        var log = new List<string>();
        SceneManager manager = Hosted(log);
        Place(manager, "door", "lifecycle");
        var session = new PlaySession(manager, Walker(manager));
        session.Enter();
        EntityWorld world = manager.EntityWorld.ShouldNotBeNull();

        session.Exit();

        session.IsActive.ShouldBeFalse();
        manager.EntityWorld.ShouldBeNull();
        world.IsActive.ShouldBeFalse();
        log[^1].ShouldBe("remove:door");
    }

    [Fact]
    public void A_tick_runs_entities_then_physics_then_the_character()
    {
        var log = new List<string>();
        SceneManager manager = Hosted(log, new FakeScenePhysics(log));
        Place(manager, "clock", "thinker");
        CharacterSimulation character = Walker(manager);
        character.Mover = new RecordingMover(log);
        var session = new PlaySession(manager, character);
        session.Enter();

        session.Tick(Dt, default);

        log.ShouldBe(new[] { "entities", "push", "step", "drain", "character" });
    }

    [Fact]
    public void A_session_with_no_character_never_ticks_entities()
    {
        var log = new List<string>();
        var physics = new FakeScenePhysics();
        SceneManager manager = Hosted(log, physics);
        Place(manager, "clock", "thinker");
        var session = new PlaySession(manager);

        session.Enter();
        for (int i = 0; i < 3; i++)
            session.Tick(Dt, default);

        session.IsActive.ShouldBeFalse();
        manager.EntityWorld.ShouldBeNull();
        log.ShouldBeEmpty();
        physics.Steps.ShouldBe(3);
    }

    [Fact]
    public void Outside_play_a_tick_steps_physics_and_nothing_else()
    {
        var log = new List<string>();
        SceneManager manager = Hosted(log, new FakeScenePhysics(log));
        Place(manager, "clock", "thinker");
        CharacterSimulation character = Walker(manager);
        character.Mover = new RecordingMover(log);
        var session = new PlaySession(manager, character);

        session.Tick(Dt, default);

        log.ShouldBe(PhysicsTick);

        session.Enter();
        session.Exit();
        log.Clear();
        session.Tick(Dt, default);

        log.ShouldBe(PhysicsTick);
    }

    [Fact]
    public void A_map_loaded_during_play_stops_the_entities_and_not_the_character()
    {
        var log = new List<string>();
        SceneManager manager = Hosted(log, new FakeScenePhysics(log));
        Place(manager, "clock", "thinker");
        CharacterSimulation character = Walker(manager);
        character.Mover = new RecordingMover(log);
        var session = new PlaySession(manager, character);
        session.Enter();

        manager.OnSceneReplaced();
        session.Tick(Dt, default);

        session.IsActive.ShouldBeTrue();
        manager.EntityWorld.ShouldBeNull();
        log.ShouldBe(new[] { "push", "step", "drain", "character" });
    }

    [Fact]
    public void A_level_can_be_walked_with_no_window_view_or_input_device()
    {
        SceneManager manager = Hosted([]);
        CharacterSimulation character = Walker(manager);
        var session = new PlaySession(manager, character);
        session.Enter();

        for (int i = 0; i < 60; i++)
            session.Tick(Dt, default);

        character.State.Grounded.ShouldBeTrue();
        character.State.Position.Y.ShouldBe(character.Tuning.SkinWidth, 1e-4f);

        // Yaw 0 walks +x.
        var walk = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f), Yaw = 0f };
        for (int i = 0; i < 60; i++)
            session.Tick(Dt, in walk);

        character.State.Position.X.ShouldBeGreaterThan(3f);
        character.State.Grounded.ShouldBeTrue();
        manager.EntityWorld.ShouldNotBeNull().Time.ShouldBe(120f * Dt, 1e-3f);
    }

    [Fact]
    public void A_tick_that_respawns_says_so_and_gives_the_feet_it_fell_from()
    {
        SceneManager manager = Hosted([]);
        var character = new CharacterSimulation(manager.ActiveScene.ShouldNotBeNull())
        {
            // Past the plate's edge at x = 32: nothing to land on.
            SpawnPosition = new Vector3(40f, 1f, 0f),
            FallOutHeight = -5f,
        };
        var session = new PlaySession(manager, character);
        session.Enter();

        PlayTickResult result = default;
        Vector3 before = default;
        for (int i = 0; i < 300 && !result.Respawned; i++)
        {
            before = character.State.Position;
            result = session.Tick(Dt, default);
        }

        result.Respawned.ShouldBeTrue();
        result.PreviousPosition.ShouldBe(before);
        before.Y.ShouldBeLessThan(0f);
        character.State.Position.ShouldBe(character.SpawnPosition);
        character.Respawns.ShouldBe(1);
    }

    [Fact]
    public void The_session_names_no_rendering_or_input_type_in_its_surface()
    {
        // Matched by name, so the test need not reference graphics or input.
        string[] forbidden = ["Camera", "InputManager", "ICursorLock", "Renderer", "DebugDraw", "RenderView"];
        Type type = typeof(PlaySession);

        string[] named = type.GetConstructors()
            .SelectMany(c => c.GetParameters().Select(p => p.ParameterType))
            .Concat(type.GetProperties().Select(p => p.PropertyType))
            .Concat(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
            .Select(t => t.Name.TrimEnd('&'))
            .Distinct()
            .ToArray();

        named.Intersect(forbidden).ShouldBeEmpty();
    }

    // Own catalogue: EntityCatalog.Shared freezes on first read, which would
    // make the tests order-dependent.
    private static SceneManager Hosted(List<string> log, FakeScenePhysics? physics = null)
    {
        EntityCatalog catalog = EntityRuntime.Catalog(log);
        catalog.Add(new EntitySchema("thinker"), () => new ThinkingEntity(log));

        var manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = catalog,
        };

        if (physics is not null)
            manager.PhysicsFactory = _ => physics;

        manager.LoadStartupScene(new FakeRenderer(), new AssetManager(NullLogger<AssetManager>.Instance));
        return manager;
    }

    private static void Place(SceneManager manager, string name, string className) =>
        EntityRuntime.Place(manager.ActiveScene.ShouldNotBeNull().Root, name, className);

    private static CharacterSimulation Walker(SceneManager manager) =>
        new(manager.ActiveScene.ShouldNotBeNull())
        {
            SpawnPosition = manager.PlayerSpawn,
            FallOutHeight = manager.PlayerFallOutHeight,
        };

    // Logs the first entity tick: its think is due as soon as time moves.
    private sealed class ThinkingEntity : Entity
    {
        private readonly List<string> _log;

        public ThinkingEntity(List<string> log) => _log = log;

        protected internal override void OnSpawn() => SetNextThink(0f);

        protected internal override void Think() => _log.Add("entities");
    }

    private sealed class RecordingMover : ICharacterMover
    {
        private readonly List<string> _log;

        public RecordingMover(List<string> log) => _log = log;

        public void Tick(
            ref CharacterState state,
            in CharacterCommand command,
            ICharacterCollisionSource source,
            CharacterTuning tuning,
            float deltaTime) =>
            _log.Add("character");
    }
}
