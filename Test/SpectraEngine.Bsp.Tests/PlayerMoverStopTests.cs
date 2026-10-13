using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The player's mover outlives a play session, as its tuning does. One a
/// running level put in goes with the level, whichever way the level ends,
/// and the mover the level started with is back.
/// </summary>
public sealed class PlayerMoverStopTests
{
    private readonly PlayRig _rig = new();
    private readonly List<string> _log = [];

    [Fact]
    public void A_mover_set_while_playing_moves_the_player_from_the_next_tick()
    {
        _rig.Session.Enter();
        _rig.Tick(default, 5);

        new PlayerWrites(_rig.Entities).TrySetMover(new RecordingMover(_log), out string? refusal).ShouldBeTrue(refusal);
        _rig.Tick(default, 3);

        _log.ShouldBe(["character", "character", "character"]);
    }

    [Fact]
    public void Leaving_play_puts_the_built_in_mover_back()
    {
        _rig.Session.Enter();
        new PlayerWrites(_rig.Entities).TrySetMover(new RecordingMover(_log), out _).ShouldBeTrue();
        _rig.Tick(default, 2);

        _rig.Session.Exit();

        _rig.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);
    }

    [Fact]
    public void Leaving_play_puts_back_the_mover_a_host_had_installed_before_it()
    {
        var hosts = new RecordingMover(_log);
        _rig.Character.Mover = hosts;
        _rig.Session.Enter();
        var door = new PlayerWrites(_rig.Entities);

        door.TrySetMover(new RecordingMover([]), out _).ShouldBeTrue();
        door.TrySetMover(new RecordingMover([]), out _).ShouldBeTrue();
        _rig.Session.Exit();

        _rig.Character.Mover.ShouldBeSameAs(hosts);
    }

    [Fact]
    public void No_mover_puts_the_built_in_one_in_while_the_level_plays()
    {
        _rig.Character.Mover = new RecordingMover(_log);
        _rig.Session.Enter();

        new PlayerWrites(_rig.Entities).TrySetMover(null, out string? refusal).ShouldBeTrue(refusal);

        _rig.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);
    }

    [Fact]
    public void A_map_loaded_during_play_puts_it_back_while_the_player_walks_on()
    {
        _rig.Session.Enter();
        new PlayerWrites(_rig.Entities).TrySetMover(new RecordingMover(_log), out _).ShouldBeTrue();

        _rig.Manager.OnSceneReplaced();
        _rig.Tick(default, 2);

        _rig.Session.IsActive.ShouldBeTrue();
        _rig.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);
        _log.ShouldBeEmpty();
    }

    [Fact]
    public void Taking_the_authored_map_during_play_puts_it_back()
    {
        _rig.Session.Enter();
        new PlayerWrites(_rig.Entities).TrySetMover(new RecordingMover(_log), out _).ShouldBeTrue();

        _rig.Manager.TakeAuthoredMap().ShouldNotBeNull();

        _rig.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);
    }

    [Fact]
    public void Shutting_the_scene_manager_down_during_play_puts_it_back()
    {
        _rig.Session.Enter();
        new PlayerWrites(_rig.Entities).TrySetMover(new RecordingMover(_log), out _).ShouldBeTrue();

        _rig.Manager.Shutdown();

        _rig.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);
    }

    [Fact]
    public void A_second_play_starts_with_the_mover_the_first_started_with()
    {
        _rig.Session.Enter();
        new PlayerWrites(_rig.Entities).TrySetMover(new RecordingMover(_log), out _).ShouldBeTrue();
        _rig.Session.Exit();

        _rig.Session.Enter();
        _rig.Tick(default, 3);

        _rig.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);
        _log.ShouldBeEmpty();
    }

    [Fact]
    public void A_player_whose_mover_no_level_can_reach_refuses_and_says_so()
    {
        var scene = new Scene("Level");
        var world = new EntityWorld(scene, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, new EntityCatalog());
        world.Activate();
        world.Player = new UntunedPlayerPresence();

        bool isSet = new PlayerWrites(world).TrySetMover(new RecordingMover(_log), out string? refusal);

        isSet.ShouldBeFalse();
        refusal.ShouldBe("How this player moves cannot be replaced.");
    }

    [Fact]
    public void A_level_that_has_stopped_takes_no_mover()
    {
        _rig.Session.Enter();
        EntityWorld stopped = _rig.Entities;
        _rig.Session.Exit();

        bool isSet = new PlayerWrites(stopped).TrySetMover(new RecordingMover(_log), out string? refusal);

        isSet.ShouldBeFalse();
        refusal.ShouldBe("The player cannot be changed: the level has stopped.");
        _rig.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);
    }
}
