using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The registry of playing sounds on an entity world: what starts, stops and
/// changes one, and how a reader tells that nothing did.
/// </summary>
public sealed class SoundEmittersTests
{
    private const float Tick = 1f / 60f;
    private const string Hum = "Sounds/hum.wav";

    private static readonly SoundDescription OneSecond = new(48_000, 48_000);
    private static readonly SoundEmitterSettings Plain = new(1f, 1f, 2f, 30f, IsLooped: false);

    private readonly Scene _scene = new("Sounds");

    [Fact]
    public void A_played_sound_is_listed_with_its_node_its_path_and_its_settings()
    {
        EntityWorld world = Started();
        SceneNode speaker = _scene.Root.CreateChild("speaker");
        world.Tick(Tick);
        world.Tick(Tick);

        int id = world.Sounds.Play(speaker, Hum, in OneSecond, new SoundEmitterSettings(0.5f, 2f, 3f, 40f, true));

        world.Sounds.Count.ShouldBe(1);
        SoundEmitter emitter = world.Sounds.Playing[0];
        emitter.Id.ShouldBe(id);
        emitter.Node.ShouldBeSameAs(speaker);
        emitter.Path.ShouldBe(Hum);
        emitter.Gain.ShouldBe(0.5f);
        emitter.Pitch.ShouldBe(2f);
        emitter.MinDistance.ShouldBe(3f);
        emitter.MaxDistance.ShouldBe(40f);
        emitter.IsLooped.ShouldBeTrue();
        emitter.StartTick.ShouldBe(2L);
        emitter.FrameCount.ShouldBe(48_000L);
        emitter.SampleRate.ShouldBe(48_000);
    }

    [Fact]
    public void A_sound_played_while_the_level_spawns_starts_on_tick_zero()
    {
        EntityWorld world = Started();

        int id = world.Sounds.Play(_scene.Root, Hum, in OneSecond, in Plain);

        world.Sounds.TryGet(id, out SoundEmitter emitter).ShouldBeTrue();
        emitter.StartTick.ShouldBe(0L);
    }

    [Fact]
    public void Stopping_a_sound_takes_it_off_the_list()
    {
        EntityWorld world = Started();
        int id = world.Sounds.Play(_scene.Root, Hum, in OneSecond, in Plain);

        world.Sounds.Stop(id).ShouldBeTrue();

        world.Sounds.Count.ShouldBe(0);
        world.Sounds.TryGet(id, out _).ShouldBeFalse();
    }

    [Fact]
    public void Stopping_a_sound_that_is_not_playing_does_nothing()
    {
        EntityWorld world = Started();
        int id = world.Sounds.Play(_scene.Root, Hum, in OneSecond, in Plain);
        world.Sounds.Stop(id);
        long version = world.Sounds.Version;

        world.Sounds.Stop(id).ShouldBeFalse();
        world.Sounds.Stop(SoundEmitters.None).ShouldBeFalse();

        world.Sounds.Version.ShouldBe(version);
    }

    [Fact]
    public void An_id_is_never_given_out_twice_and_the_list_stays_in_starting_order()
    {
        EntityWorld world = Started();
        int first = world.Sounds.Play(_scene.Root, "a.wav", in OneSecond, in Plain);
        int second = world.Sounds.Play(_scene.Root, "b.wav", in OneSecond, in Plain);
        int third = world.Sounds.Play(_scene.Root, "c.wav", in OneSecond, in Plain);

        world.Sounds.Stop(second);
        int fourth = world.Sounds.Play(_scene.Root, "d.wav", in OneSecond, in Plain);

        new[] { first, second, third, fourth }.Distinct().Count().ShouldBe(4);
        first.ShouldNotBe(SoundEmitters.None);
        Paths(world).ShouldBe(["a.wav", "c.wav", "d.wav"]);
        world.Sounds.TryGet(third, out SoundEmitter found).ShouldBeTrue();
        found.Path.ShouldBe("c.wav");
    }

    [Fact]
    public void The_version_moves_when_a_sound_starts_stops_or_changes_and_at_no_other_time()
    {
        EntityWorld world = Started();
        long empty = world.Sounds.Version;

        int id = world.Sounds.Play(_scene.Root, Hum, in OneSecond, in Plain);
        long started = world.Sounds.Version;
        started.ShouldNotBe(empty);

        for (int i = 0; i < 10; i++)
            world.Tick(Tick);
        world.Sounds.TryGet(id, out _);
        world.Sounds.Version.ShouldBe(started);

        world.Sounds.SetGain(id, 0.25f).ShouldBeTrue();
        long quieter = world.Sounds.Version;
        quieter.ShouldNotBe(started);

        world.Sounds.SetPitch(id, 2f).ShouldBeTrue();
        long faster = world.Sounds.Version;
        faster.ShouldNotBe(quieter);

        world.Sounds.Stop(id);
        world.Sounds.Version.ShouldNotBe(faster);
    }

    [Fact]
    public void A_change_reaches_the_listed_sound()
    {
        EntityWorld world = Started();
        int id = world.Sounds.Play(_scene.Root, Hum, in OneSecond, in Plain);

        world.Sounds.SetGain(id, 0.25f);
        world.Sounds.SetPitch(id, 1.5f);

        world.Sounds.Playing[0].Gain.ShouldBe(0.25f);
        world.Sounds.Playing[0].Pitch.ShouldBe(1.5f);
    }

    [Fact]
    public void Changing_a_sound_that_is_not_playing_does_nothing()
    {
        EntityWorld world = Started();
        long version = world.Sounds.Version;

        world.Sounds.SetGain(SoundEmitters.None, 0.5f).ShouldBeFalse();
        world.Sounds.SetPitch(41, 2f).ShouldBeFalse();

        world.Sounds.Version.ShouldBe(version);
    }

    [Fact]
    public void Deactivating_the_world_empties_the_registry_and_moves_the_version()
    {
        EntityWorld world = Started();
        world.Sounds.Play(_scene.Root, "a.wav", in OneSecond, in Plain);
        world.Sounds.Play(_scene.Root, "b.wav", in OneSecond, in Plain);
        long version = world.Sounds.Version;

        world.Deactivate();

        world.Sounds.Count.ShouldBe(0);
        world.Sounds.Playing.IsEmpty.ShouldBeTrue();
        world.Sounds.Version.ShouldNotBe(version);
    }

    [Fact]
    public void A_world_started_again_does_not_reuse_the_ids_of_its_last_run()
    {
        EntityWorld world = Started();
        int before = world.Sounds.Play(_scene.Root, Hum, in OneSecond, in Plain);
        world.Deactivate();
        world.Activate();

        int after = world.Sounds.Play(_scene.Root, Hum, in OneSecond, in Plain);

        after.ShouldNotBe(before);
        world.Sounds.TryGet(before, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_sound_cannot_start_on_a_world_that_is_not_running()
    {
        var world = new EntityWorld(_scene, new CapturingLogger(), new EntityCatalog());

        Should.Throw<InvalidOperationException>(
            () => world.Sounds.Play(_scene.Root, Hum, in OneSecond, in Plain));
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void A_gain_that_is_not_a_number_from_zero_up_is_refused(float gain)
    {
        EntityWorld world = Started();
        int id = world.Sounds.Play(_scene.Root, Hum, in OneSecond, in Plain);

        Should.Throw<ArgumentOutOfRangeException>(() => world.Sounds.SetGain(id, gain));
        Should.Throw<ArgumentOutOfRangeException>(
            () => world.Sounds.Play(_scene.Root, Hum, in OneSecond, Plain with { Gain = gain }));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void A_pitch_that_is_not_a_number_above_zero_is_refused(float pitch)
    {
        EntityWorld world = Started();
        int id = world.Sounds.Play(_scene.Root, Hum, in OneSecond, in Plain);

        Should.Throw<ArgumentOutOfRangeException>(() => world.Sounds.SetPitch(id, pitch));
        Should.Throw<ArgumentOutOfRangeException>(
            () => world.Sounds.Play(_scene.Root, Hum, in OneSecond, Plain with { Pitch = pitch }));
    }

    [Fact]
    public void Reading_the_registry_every_tick_allocates_nothing()
    {
        EntityWorld world = Started();
        for (int i = 0; i < 8; i++)
            world.Sounds.Play(_scene.Root.CreateChild($"speaker{i}"), Hum, in OneSecond, Plain with { IsLooped = true });

        ReadEverySound(world);

        // The least of several rounds: a one-off from the runtime is not a
        // cost per read.
        long least = long.MaxValue;
        long frames = 0;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                world.Tick(Tick);
                frames += ReadEverySound(world);
            }

            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        least.ShouldBe(0L);
        frames.ShouldBeGreaterThan(0L);
    }

    // What a reader does each frame: the version, then every sound's place.
    private static long ReadEverySound(EntityWorld world)
    {
        long frames = world.Sounds.Version;
        foreach (ref readonly SoundEmitter emitter in world.Sounds.Playing)
            frames += emitter.PositionAt(world.TickNumber).Frame + (long)emitter.Node.WorldPosition.X;

        return frames;
    }

    private static string[] Paths(EntityWorld world)
    {
        var paths = new List<string>();
        foreach (ref readonly SoundEmitter emitter in world.Sounds.Playing)
            paths.Add(emitter.Path);

        return [.. paths];
    }

    private EntityWorld Started()
    {
        var world = new EntityWorld(_scene, new CapturingLogger(), new EntityCatalog());
        world.Activate();
        return world;
    }
}
