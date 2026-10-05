using Avalonia;
using SpectraEngine.Editor.Shell.Logic;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>
/// The drag that makes a wire, step by step on the laid out vault level. The
/// view sits at its own size here, so a point of the scene is one of the view.
/// </summary>
public sealed class LogicWireGestureTests
{
    private static readonly LogicScene Scene = Arrange(Vault());
    private static readonly Point Ground = new(2, 2);

    private static LogicHit On(Point point) => Scene.HitTest(point, 5);

    private static Point Middle(string card) => Scene.Card(card).Header.Center;

    private static LogicWireGesture Pressed(string card)
    {
        var gesture = new LogicWireGesture();
        gesture.Press(Middle(card), On(Middle(card)), canEdit: true).ShouldBeTrue();
        return gesture;
    }

    private static LogicWireGesture Dragged(string from, string over)
    {
        LogicWireGesture gesture = Pressed(from);
        gesture.Move(Ground, null);
        gesture.Move(Middle(over), Scene.Card(over));
        return gesture;
    }

    private static LogicWireGesture In(LogicWirePhase phase)
    {
        switch (phase)
        {
            case LogicWirePhase.Pressed:
                return Pressed("StartZone");

            case LogicWirePhase.Dragging:
                return Dragged("StartZone", "Lift");

            default:
                LogicWireGesture dropped = Dragged("StartZone", "Lift");
                dropped.Release().ShouldBeTrue();
                return dropped;
        }
    }

    [Fact]
    public void A_gesture_starts_with_nothing_pressed()
    {
        var gesture = new LogicWireGesture();

        gesture.Phase.ShouldBe(LogicWirePhase.Idle);
        gesture.IsActive.ShouldBeFalse();
        gesture.ShowsWire.ShouldBeFalse();
        gesture.From.ShouldBeNull();
    }

    [Fact]
    public void A_press_on_a_card_remembers_the_card_and_leaves_from_its_right_edge()
    {
        LogicSceneCard zone = Scene.Card("StartZone");

        LogicWireGesture gesture = Pressed("StartZone");

        gesture.Phase.ShouldBe(LogicWirePhase.Pressed);
        gesture.From.ShouldBeSameAs(zone);
        gesture.Output.ShouldBeNull();
        gesture.Start.ShouldBe(new Point(zone.Bounds.Right, zone.Header.Center.Y));
        gesture.ShowsWire.ShouldBeFalse();
    }

    [Fact]
    public void A_press_on_an_output_remembers_the_output_and_leaves_from_its_dot()
    {
        LogicScenePort pressed = Scene.Card("LiftButton").PortNamed("OnPressed", isOutput: true).ShouldNotBeNull();
        var gesture = new LogicWireGesture();

        gesture.Press(pressed.Row.Center, On(pressed.Row.Center), canEdit: true).ShouldBeTrue();

        gesture.Output.ShouldBe("OnPressed");
        gesture.Start.ShouldBe(pressed.Anchor);
    }

    [Fact]
    public void A_press_on_an_input_starts_from_the_card_as_a_press_elsewhere_on_it_does()
    {
        LogicSceneCard lift = Scene.Card("Lift");
        LogicScenePort close = lift.PortNamed("Close", isOutput: false).ShouldNotBeNull();
        var gesture = new LogicWireGesture();

        gesture.Press(close.Row.Center, On(close.Row.Center), canEdit: true).ShouldBeTrue();

        gesture.Output.ShouldBeNull();
        gesture.Start.X.ShouldBe(lift.Bounds.Right);
    }

    [Fact]
    public void A_press_on_empty_ground_on_a_wire_or_on_the_card_of_a_missing_name_starts_nothing()
    {
        Point wire = Scene.Edge("StartZone", "StartDoor").Segments[0].At(0.5);
        Point stub = Middle("VaultDor");

        foreach (Point at in new[] { Ground, wire, stub })
        {
            var gesture = new LogicWireGesture();

            gesture.Press(at, On(at), canEdit: true).ShouldBeFalse();
            gesture.Phase.ShouldBe(LogicWirePhase.Idle);
        }
    }

    [Fact]
    public void Nothing_starts_while_a_level_plays()
    {
        var gesture = new LogicWireGesture();

        gesture.Press(Middle("StartZone"), On(Middle("StartZone")), canEdit: false).ShouldBeFalse();

        gesture.Phase.ShouldBe(LogicWirePhase.Idle);
        gesture.Move(Ground, null).ShouldBeFalse();
        gesture.Release().ShouldBeFalse();
    }

    [Fact]
    public void A_press_that_moves_four_pixels_is_still_a_click()
    {
        LogicWireGesture gesture = Pressed("StartZone");

        gesture.Move(Middle("StartZone") + new Vector(4, -4), Scene.Card("StartZone")).ShouldBeFalse();
        gesture.Phase.ShouldBe(LogicWirePhase.Pressed);

        gesture.Release().ShouldBeFalse();
        gesture.Phase.ShouldBe(LogicWirePhase.Idle);
    }

    [Fact]
    public void A_press_that_moves_further_drags_a_wire_to_the_pointer()
    {
        LogicWireGesture gesture = Pressed("StartZone");
        Point at = Middle("StartZone") + new Vector(0, 5);

        gesture.Move(at, Scene.Card("StartZone")).ShouldBeTrue();

        gesture.Phase.ShouldBe(LogicWirePhase.Dragging);
        gesture.ShowsWire.ShouldBeTrue();
        gesture.Pointer.ShouldBe(at);
    }

    [Fact]
    public void A_drag_lights_the_entity_card_under_the_pointer_and_nothing_else()
    {
        LogicWireGesture gesture = Pressed("StartZone");

        gesture.Move(Ground, null);
        gesture.Target.ShouldBeNull();

        gesture.Move(Middle("Lift"), Scene.Card("Lift"));
        gesture.Target.ShouldBeSameAs(Scene.Card("Lift"));

        gesture.Move(Middle("VaultDor"), Scene.Card("VaultDor"));
        gesture.Target.ShouldBeNull();
    }

    [Fact]
    public void The_sender_takes_its_own_wire_once_the_pointer_has_been_off_its_card()
    {
        LogicSceneCard lift = Scene.Card("Lift");
        LogicWireGesture gesture = Pressed("Lift");

        gesture.Move(Middle("Lift") + new Vector(12, 0), lift);
        gesture.Target.ShouldBeNull();

        gesture.Move(Ground, null);
        gesture.Move(Middle("Lift"), lift);
        gesture.Target.ShouldBeSameAs(lift);
    }

    [Fact]
    public void A_wire_let_go_on_a_card_waits_for_its_menu_with_both_cards()
    {
        LogicWireGesture gesture = Dragged("StartZone", "Lift");

        gesture.Release().ShouldBeTrue();

        gesture.Phase.ShouldBe(LogicWirePhase.Dropped);
        gesture.From.ShouldBeSameAs(Scene.Card("StartZone"));
        gesture.Target.ShouldBeSameAs(Scene.Card("Lift"));
        gesture.ShowsWire.ShouldBeTrue();
    }

    [Fact]
    public void A_wire_let_go_on_empty_ground_is_given_up()
    {
        LogicWireGesture gesture = Pressed("StartZone");
        gesture.Move(Ground, null);

        gesture.Release().ShouldBeFalse();

        gesture.Phase.ShouldBe(LogicWirePhase.Cancelled);
        gesture.IsActive.ShouldBeFalse();
        gesture.From.ShouldBeNull();
    }

    [Fact]
    public void A_pick_in_the_menu_ends_the_gesture_as_done()
    {
        LogicWireGesture gesture = In(LogicWirePhase.Dropped);

        gesture.Finish();

        gesture.Phase.ShouldBe(LogicWirePhase.Done);
        gesture.IsActive.ShouldBeFalse();
        gesture.ShowsWire.ShouldBeFalse();
    }

    [Fact]
    public void Only_a_wire_that_waits_for_its_menu_can_be_finished()
    {
        LogicWireGesture gesture = In(LogicWirePhase.Dragging);

        gesture.Finish();

        gesture.Phase.ShouldBe(LogicWirePhase.Dragging);
    }

    [Theory]
    [InlineData(LogicWirePhase.Pressed, false)]
    [InlineData(LogicWirePhase.Dragging, true)]
    [InlineData(LogicWirePhase.Dropped, true)]
    public void A_gesture_is_given_up_from_wherever_it_is(LogicWirePhase phase, bool showedWire)
    {
        LogicWireGesture gesture = In(phase);

        gesture.Cancel().ShouldBe(showedWire);

        gesture.Phase.ShouldBe(LogicWirePhase.Cancelled);
        gesture.From.ShouldBeNull();
        gesture.Target.ShouldBeNull();
        gesture.Cancel().ShouldBeFalse();
    }

    [Fact]
    public void Giving_up_with_nothing_pressed_changes_nothing()
    {
        var gesture = new LogicWireGesture();

        gesture.Cancel().ShouldBeFalse();

        gesture.Phase.ShouldBe(LogicWirePhase.Idle);
    }

    [Fact]
    public void A_lost_pointer_gives_up_a_press_and_a_drag_but_not_a_wire_that_waits_for_its_menu()
    {
        LogicWireGesture pressed = In(LogicWirePhase.Pressed);
        LogicWireGesture dragging = In(LogicWirePhase.Dragging);
        LogicWireGesture dropped = In(LogicWirePhase.Dropped);

        pressed.LoseCapture();
        dragging.LoseCapture().ShouldBeTrue();
        dropped.LoseCapture().ShouldBeFalse();

        pressed.Phase.ShouldBe(LogicWirePhase.Cancelled);
        dragging.Phase.ShouldBe(LogicWirePhase.Cancelled);
        dropped.Phase.ShouldBe(LogicWirePhase.Dropped);
    }

    [Fact]
    public void A_press_during_a_gesture_is_refused_and_one_after_it_starts_anew()
    {
        LogicWireGesture gesture = In(LogicWirePhase.Dropped);

        gesture.Press(Middle("Lift"), On(Middle("Lift")), canEdit: true).ShouldBeFalse();
        gesture.From.ShouldBeSameAs(Scene.Card("StartZone"));

        gesture.Cancel();
        gesture.Press(Middle("Lift"), On(Middle("Lift")), canEdit: true).ShouldBeTrue();

        gesture.Phase.ShouldBe(LogicWirePhase.Pressed);
        gesture.From.ShouldBeSameAs(Scene.Card("Lift"));
        gesture.Target.ShouldBeNull();
    }

    [Fact]
    public void A_move_with_nothing_pressed_and_one_after_the_drop_do_nothing()
    {
        var idle = new LogicWireGesture();
        LogicWireGesture dropped = In(LogicWirePhase.Dropped);
        Point droppedAt = dropped.Pointer;

        idle.Move(Middle("Lift"), Scene.Card("Lift")).ShouldBeFalse();
        dropped.Move(Ground, null).ShouldBeFalse();

        idle.Phase.ShouldBe(LogicWirePhase.Idle);
        dropped.Pointer.ShouldBe(droppedAt);
        dropped.Target.ShouldBeSameAs(Scene.Card("Lift"));
    }
}
