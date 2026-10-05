using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// A mover stops for a player it squeezes: one its last move left deep inside
/// a brush, when the next move goes deeper still.
/// </summary>
// The player here never moves, so every tick of pressing counts.
public sealed class LinearMoverBlockingTests
{
    // The slab's underside starts 0.95 above the head and comes down a
    // thirtieth of a unit a tick: 0.117 into the head after 32 ticks, which is
    // the first time the player is left deeper than the block depth.
    private const int TicksToBlock = 32;

    [Fact]
    public void A_move_that_squeezes_the_player_is_taken_back()
    {
        Rig rig = Start();
        rig.Mover.MoveTo(rig.Mover.TravelTicks);

        for (int tick = 1; tick <= TicksToBlock; tick++)
            rig.Mover.Advance().ShouldBe(LinearMoverStep.Moving, $"tick {tick}");

        byte[] pose = Movers.Bits(rig.Node.LocalTransform);

        rig.Mover.Advance().ShouldBe(LinearMoverStep.Blocked);

        rig.Mover.TicksTravelled.ShouldBe(TicksToBlock);
        Movers.Bits(rig.Node.LocalTransform).ShouldBe(pose);
        rig.Mover.IsMoving.ShouldBeTrue();
    }

    [Fact]
    public void A_blocked_mover_stays_blocked_while_the_player_stays()
    {
        Rig rig = Start();
        rig.Mover.MoveTo(rig.Mover.TravelTicks);
        Advance(rig.Mover, TicksToBlock);

        for (int tick = 0; tick < 100; tick++)
            rig.Mover.Advance().ShouldBe(LinearMoverStep.Blocked);

        rig.Mover.TicksTravelled.ShouldBe(TicksToBlock);
    }

    [Fact]
    public void A_blocked_mover_goes_on_once_the_player_is_out_of_the_way()
    {
        Rig rig = Start();
        rig.Mover.MoveTo(rig.Mover.TravelTicks);
        Advance(rig.Mover, TicksToBlock + 5);

        rig.Player.Feet = new Vector3(5f, 0f, 0f);

        for (int tick = TicksToBlock + 1; tick < rig.Mover.TravelTicks; tick++)
            rig.Mover.Advance().ShouldBe(LinearMoverStep.Moving, $"tick {tick}");

        rig.Mover.Advance().ShouldBe(LinearMoverStep.ArrivedOpen);
    }

    [Fact]
    public void A_blocked_mover_may_turn_round_and_leave()
    {
        Rig rig = Start();
        rig.Mover.MoveTo(rig.Mover.TravelTicks);
        Advance(rig.Mover, TicksToBlock + 1);

        rig.Mover.MoveTo(0);

        for (int tick = TicksToBlock - 1; tick > 0; tick--)
            rig.Mover.Advance().ShouldBe(LinearMoverStep.Moving, $"tick {tick}");

        rig.Mover.Advance().ShouldBe(LinearMoverStep.ArrivedClosed);
        Movers.Bits(rig.Node.LocalTransform).ShouldBe(rig.Authored);
    }

    [Fact]
    public void A_move_away_from_a_player_deep_inside_is_not_blocked()
    {
        Rig rig = Start();
        rig.Mover.PlaceAt(20);

        // The head is 0.3 into the slab, which then lifts off it.
        float underside = rig.Node.LocalPosition.Y - 0.25f;
        rig.Player.Feet = new Vector3(0f, underside + 0.3f - PinnedPlayer.Height, 0f);

        rig.Mover.MoveTo(0);
        rig.Mover.Advance().ShouldBe(LinearMoverStep.Moving);

        rig.Mover.MoveTo(rig.Mover.TravelTicks);
        rig.Mover.Advance().ShouldBe(LinearMoverStep.Blocked);
    }

    [Fact]
    public void A_mover_told_of_no_brushes_is_never_blocked()
    {
        Rig rig = Start(solid: false);
        rig.Mover.MoveTo(rig.Mover.TravelTicks);

        Advance(rig.Mover, rig.Mover.TravelTicks);

        rig.Mover.IsFullyOpen.ShouldBeTrue();
    }

    [Fact]
    public void A_player_who_is_not_in_the_level_blocks_nothing()
    {
        Rig rig = Start();
        rig.Player.IsPresent = false;
        rig.Mover.MoveTo(rig.Mover.TravelTicks);

        Advance(rig.Mover, rig.Mover.TravelTicks);

        rig.Mover.IsFullyOpen.ShouldBeTrue();
    }

    [Fact]
    public void A_brush_the_player_walks_through_does_not_stop_for_it()
    {
        Rig rig = Start(collides: false);
        rig.Mover.MoveTo(rig.Mover.TravelTicks);

        Advance(rig.Mover, rig.Mover.TravelTicks);

        rig.Mover.IsFullyOpen.ShouldBeTrue();
    }

    [Fact]
    public void A_brush_below_the_node_stops_for_the_player_too()
    {
        // The node's own brush is far to the side. The one that comes down on
        // the player is a child.
        var scene = new Scene("Mover");
        SceneNode node = Slab(scene, new Vector3(40f, 3f, 0f));
        Movers.Part(node, "foot", new Vector3(2f, 0.5f, 2f), new Vector3(-40f, 0f, 0f));
        Rig rig = Run(scene, node);
        rig.Mover.MoveTo(rig.Mover.TravelTicks);

        Advance(rig.Mover, TicksToBlock);

        rig.Mover.Advance().ShouldBe(LinearMoverStep.Blocked);
    }

    private sealed record Rig(SceneNode Node, LinearMover Mover, PinnedPlayer Player, byte[] Authored);

    // A slab over a player who stands at the origin.
    private static Rig Start(bool solid = true, bool collides = true)
    {
        var scene = new Scene("Mover");
        SceneNode node = Slab(scene, new Vector3(0f, 3f, 0f), solid);
        node.CanCollide = collides;
        return Run(scene, node);
    }

    // 2 by 0.5 by 2, coming straight down 2 units at 2 a second.
    private static SceneNode Slab(Scene scene, Vector3 position, bool solid = true)
    {
        SceneNode node = Movers.Part(scene.Root, "slab", new Vector3(2f, 0.5f, 2f), position);
        node.Entity = new EntityData(MoverProbe.WireName);
        node.Entity.SetValue("direction", "0 -1 0");
        node.Entity.SetValue("solid", solid ? "1" : "0");
        return node;
    }

    private static Rig Run(Scene scene, SceneNode node)
    {
        byte[] authored = Movers.Bits(node.LocalTransform);
        var player = new PinnedPlayer(Vector3.Zero);
        var world = new EntityWorld(scene, new CapturingLogger(), Movers.Catalog([]));
        world.Activate();
        world.Player = player;
        return new Rig(node, EntityRuntime.Live<MoverProbe>(world, node).Mover, player, authored);
    }

    private static void Advance(LinearMover mover, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            mover.Advance();
    }
}
