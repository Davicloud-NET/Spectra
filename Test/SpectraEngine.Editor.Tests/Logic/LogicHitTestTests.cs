using Avalonia;
using SpectraEngine.Editor.Shell.Logic;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>What a point in the laid out vault level is on.</summary>
public sealed class LogicHitTestTests
{
    private const double Tolerance = 5;

    private static readonly LogicScene Scene = Arrange(Vault());

    [Fact]
    public void A_point_on_a_cards_header_hits_the_card()
    {
        LogicSceneCard relay = Scene.Card("OpenVault");

        LogicHit hit = Scene.HitTest(relay.Header.Center, Tolerance);

        hit.Kind.ShouldBe(LogicHitKind.Card);
        hit.Card.ShouldBeSameAs(relay);
        hit.Port.ShouldBeNull();
        hit.Edge.ShouldBeNull();
    }

    [Fact]
    public void A_point_on_a_cards_note_hits_the_card()
    {
        LogicSceneCard door = Scene.Card("VaultDoor");

        Scene.HitTest(door.NoteRow.ShouldNotBeNull().Center, Tolerance).Kind.ShouldBe(LogicHitKind.Card);
    }

    [Fact]
    public void A_point_on_a_port_row_hits_the_port()
    {
        LogicSceneCard lift = Scene.Card("Lift");
        LogicScenePort close = lift.PortNamed("Close", isOutput: false).ShouldNotBeNull();

        LogicHit hit = Scene.HitTest(close.Row.Center, Tolerance);

        hit.Kind.ShouldBe(LogicHitKind.Port);
        hit.Card.ShouldBeSameAs(lift);
        hit.Port.ShouldNotBeNull().Name.ShouldBe("Close");
    }

    [Fact]
    public void A_point_just_outside_the_card_but_near_a_ports_dot_hits_the_port()
    {
        LogicSceneCard button = Scene.Card("LiftButton");
        LogicScenePort pressed = button.PortNamed("OnPressed", isOutput: true).ShouldNotBeNull();

        LogicHit near = Scene.HitTest(pressed.Anchor + new Vector(4, 0), Tolerance);
        near.Kind.ShouldBe(LogicHitKind.Port);
        near.Port.ShouldNotBeNull().IsOutput.ShouldBeTrue();

        // Further out the dot is out of reach, and what is there is the wire.
        Scene.HitTest(pressed.Anchor + new Vector(8, 0), Tolerance).Kind.ShouldBe(LogicHitKind.Edge);
    }

    [Fact]
    public void Of_two_dots_in_reach_the_nearer_one_is_hit()
    {
        LogicSceneCard lift = Scene.Card("Lift");
        LogicScenePort open = lift.PortNamed("Open", isOutput: false).ShouldNotBeNull();
        LogicScenePort close = lift.PortNamed("Close", isOutput: false).ShouldNotBeNull();

        var nearerClose = new Point(open.Anchor.X - 2, (open.Anchor.Y + close.Anchor.Y) / 2 + 1);

        Scene.HitTest(nearerClose, 20).Port.ShouldNotBeNull().Name.ShouldBe("Close");
    }

    [Fact]
    public void A_point_on_a_label_hits_the_label_and_names_its_wire()
    {
        LogicSceneEdge wire = Scene.Edge("OpenVault", "Lift");
        Rect label = wire.LabelBounds.ShouldNotBeNull();

        LogicHit hit = Scene.HitTest(new Point(label.X + 3, label.Y + 3), Tolerance);

        hit.Kind.ShouldBe(LogicHitKind.Label);
        hit.Edge.ShouldBeSameAs(wire);
        hit.Edge.ShouldNotBeNull().Wire.ShouldBe(new LogicWireKey(OpenVault, 1));
    }

    [Fact]
    public void A_point_near_a_wire_hits_the_wire()
    {
        LogicSceneEdge wire = Scene.Edge("Presses", "OpenVault");
        Point onWire = wire.Segments[0].At(0.5);

        LogicHit hit = Scene.HitTest(onWire + new Vector(0, 3), Tolerance);

        hit.Kind.ShouldBe(LogicHitKind.Edge);
        hit.Edge.ShouldBeSameAs(wire);
    }

    [Fact]
    public void A_point_further_from_a_wire_than_the_tolerance_hits_nothing()
    {
        LogicSceneEdge wire = Scene.Edge("StartZone", "StartDoor");
        Point onWire = wire.Segments[0].At(0.5);

        Scene.HitTest(onWire + new Vector(0, 9), Tolerance).ShouldBe(LogicHit.None);
        Scene.HitTest(new Point(1, 1), Tolerance).Kind.ShouldBe(LogicHitKind.None);
    }

    [Fact]
    public void Of_two_wires_in_reach_the_nearer_one_is_hit()
    {
        // Both leave the relay's one output, so near the port they run side by side.
        LogicSceneEdge toDoor = Scene.Edge("OpenVault", "VaultDoor");
        LogicSceneEdge toLift = Scene.Edge("OpenVault", "Lift");

        Point onDoorWire = toDoor.Segments[0].At(0.1);
        Point onLiftWire = toLift.Segments[0].At(0.1);

        toLift.DistanceTo(onDoorWire).ShouldBeLessThan(Tolerance);
        toDoor.DistanceTo(onLiftWire).ShouldBeLessThan(Tolerance);

        Scene.HitTest(onDoorWire, Tolerance).Edge.ShouldBeSameAs(toDoor);
        Scene.HitTest(onLiftWire, Tolerance).Edge.ShouldBeSameAs(toLift);
    }

    [Fact]
    public void The_wire_under_the_lift_can_be_hit_where_it_loops()
    {
        LogicSceneEdge loop = Scene.Edge("Lift", "Lift");

        LogicHit hit = Scene.HitTest(loop.Segments[1].At(0.5), Tolerance);

        hit.Kind.ShouldBe(LogicHitKind.Edge);
        hit.Edge.ShouldBeSameAs(loop);
    }
}
