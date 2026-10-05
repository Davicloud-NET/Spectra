using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Sound as <see cref="SceneManager"/> hosts it: every level it starts has a
/// catalog before its entities spawn, and stopping one leaves nothing playing.
/// </summary>
public sealed class SoundHostingTests
{
    [Fact]
    public void A_hosted_level_has_the_managers_catalog_before_its_entities_spawn()
    {
        var catalog = new FakeSoundCatalog();
        catalog.Add(SpawnSoundEntity.Path, new SoundDescription(48_000, 48_000));
        SceneManager manager = Hosted();
        manager.SoundCatalog = catalog;
        SceneNode node = Place(manager, "speaker");

        manager.StartEntityWorld();

        EntityWorld world = manager.EntityWorld.ShouldNotBeNull();
        world.SoundCatalog.ShouldBeSameAs(catalog);

        var speaker = EntityRuntime.Live(world, node).ShouldBeOfType<SpawnSoundEntity>();
        speaker.SawCatalog.ShouldBeTrue();
        catalog.Asked.ShouldBe([SpawnSoundEntity.Path]);
        world.Sounds.TryGet(speaker.Emitter, out SoundEmitter emitter).ShouldBeTrue();
        emitter.Node.ShouldBeSameAs(node);
        emitter.StartTick.ShouldBe(0L);
    }

    [Fact]
    public void A_hosted_level_with_no_catalog_set_plays_what_the_asset_manager_can_open()
    {
        string root = Path.Combine(Path.GetTempPath(), "SpectraSoundHostingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Sounds"));
        File.WriteAllBytes(
            Path.Combine(root, "Sounds", "spawn.saudio"), HandBuiltSaudio.Resident(frames: 480, sampleRate: 44_100));
        var assets = new AssetManager(NullLogger<AssetManager>.Instance, root, hotReloadEnabled: false);

        try
        {
            SceneManager manager = Hosted(assets);
            SceneNode node = Place(manager, "speaker");

            manager.StartEntityWorld();

            EntityWorld world = manager.EntityWorld.ShouldNotBeNull();
            world.SoundCatalog.ShouldBeOfType<AssetSoundCatalog>();

            var speaker = EntityRuntime.Live(world, node).ShouldBeOfType<SpawnSoundEntity>();
            world.Sounds.TryGet(speaker.Emitter, out SoundEmitter emitter).ShouldBeTrue();
            emitter.FrameCount.ShouldBe(480L);
            emitter.SampleRate.ShouldBe(44_100);
            assets.AudioCount.ShouldBe(1);
        }
        finally
        {
            // First, or the open sound still holds its file.
            assets.Shutdown();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_hosted_level_with_no_catalog_set_plays_nothing_the_asset_manager_cannot_open()
    {
        SceneManager manager = Hosted();
        SceneNode node = Place(manager, "speaker");

        manager.StartEntityWorld();

        // The repo ships no cooked sound by that name.
        EntityWorld world = manager.EntityWorld.ShouldNotBeNull();
        var speaker = EntityRuntime.Live(world, node).ShouldBeOfType<SpawnSoundEntity>();
        speaker.SawCatalog.ShouldBeTrue();
        world.Sounds.Count.ShouldBe(0);
    }

    [Fact]
    public void Every_play_session_has_a_registry_of_its_own_that_counts_from_the_start()
    {
        EntityWorld first = PlayingOneSound(out SceneManager manager);
        int id = first.Sounds.Playing[0].Id;
        long version = first.Sounds.Version;

        manager.StopEntityWorld();
        manager.StartEntityWorld();

        // Same id and same version as the run before: only the registry
        // tells the two apart.
        EntityWorld second = manager.EntityWorld.ShouldNotBeNull();
        second.Sounds.ShouldNotBeSameAs(first.Sounds);
        second.Sounds.Playing[0].Id.ShouldBe(id);
        second.Sounds.Version.ShouldBe(version);
    }

    [Fact]
    public void The_catalog_is_kept_from_one_play_session_to_the_next()
    {
        var catalog = new FakeSoundCatalog();
        SceneManager manager = Hosted();
        manager.SoundCatalog = catalog;

        manager.StartEntityWorld();
        manager.StopEntityWorld();
        manager.StartEntityWorld();

        manager.EntityWorld.ShouldNotBeNull().SoundCatalog.ShouldBeSameAs(catalog);
    }

    [Fact]
    public void Stopping_a_hosted_level_leaves_no_sound_playing()
    {
        EntityWorld world = PlayingOneSound(out SceneManager manager);
        world.Sounds.Count.ShouldBe(1);

        manager.StopEntityWorld();

        world.Sounds.Count.ShouldBe(0);
    }

    [Fact]
    public void Replacing_the_scene_while_a_level_plays_leaves_no_sound_playing()
    {
        EntityWorld world = PlayingOneSound(out SceneManager manager);

        manager.OnSceneReplaced();

        world.Sounds.Count.ShouldBe(0);
        manager.EntityWorld.ShouldBeNull();
    }

    [Fact]
    public void Taking_the_authored_map_while_a_level_plays_leaves_no_sound_playing()
    {
        EntityWorld world = PlayingOneSound(out SceneManager manager);

        manager.TakeAuthoredMap().ShouldNotBeNull();

        world.Sounds.Count.ShouldBe(0);
    }

    private static EntityWorld PlayingOneSound(out SceneManager manager)
    {
        var catalog = new FakeSoundCatalog();
        catalog.Add(SpawnSoundEntity.Path, new SoundDescription(48_000, 48_000));
        manager = Hosted();
        manager.SoundCatalog = catalog;
        Place(manager, "speaker");

        manager.StartEntityWorld();
        return manager.EntityWorld.ShouldNotBeNull();
    }

    private static SceneNode Place(SceneManager manager, string name) =>
        EntityRuntime.Place(manager.ActiveScene.ShouldNotBeNull().Root, name, "spawn_sound");

    // Own catalogue: EntityCatalog.Shared freezes on first read.
    private static SceneManager Hosted(AssetManager? assets = null)
    {
        EntityCatalog catalog = EntityRuntime.Catalog([]);
        catalog.Add(new EntitySchema("spawn_sound"), () => new SpawnSoundEntity());

        var manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = catalog,
        };

        manager.LoadStartupScene(
            new FakeRenderer(), assets ?? new AssetManager(NullLogger<AssetManager>.Instance));
        return manager;
    }
}
