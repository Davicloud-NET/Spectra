using System.Numerics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// The handles of a selected entity stand on its node, in the middle of its
/// pile's row. A click there that drags nothing picks the icon drawn under it.
/// </summary>
public sealed class IconPileHandleTests
{
    [Fact]
    public void With_one_of_a_pile_selected_a_click_on_each_other_icon_selects_it()
    {
        var rig = new IconPileRig();
        SceneNode[] lift = rig.AddLift(new Vector3(2f, 1f, 0f));
        SceneIcon[] markers = rig.Place().Markers.ToArray();
        int onHandles = 0;

        for (int from = 0; from < lift.Length; from++)
        {
            for (int to = 0; to < lift.Length; to++)
            {
                if (to == from)
                    continue;

                rig.Scene.Selection.Select(lift[from]);
                Vector2 at = rig.Harness.WorldToScreen(markers[to].At);

                rig.Harness.Move(at);
                if (rig.Harness.Press(at) == ViewportDragMode.Manipulate)
                    onHandles++;

                rig.Harness.Release(at);

                rig.Scene.Selection.Items.ShouldBe(new[] { lift[to] }, $"from icon {from} to icon {to}");
            }
        }

        // Or no click here went through a handle and the test says nothing.
        onHandles.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void The_middle_icon_of_three_stands_on_the_node_and_is_picked_through_the_handles()
    {
        var rig = new IconPileRig();
        SceneNode first = rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        SceneNode middle = rig.AddEntity(IconPileRig.Relay, Vector3.Zero);
        rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        rig.Place().Markers[1].IsMoved.ShouldBeFalse();

        rig.Scene.Selection.Select(first);
        Vector2 at = rig.Harness.CenterPixel;
        rig.Harness.Move(at);
        rig.Harness.Press(at).ShouldBe(ViewportDragMode.Manipulate);
        rig.Harness.Release(at);

        rig.Scene.Selection.Items.ShouldBe(new[] { middle });
    }

    [Fact]
    public void A_drag_from_a_handle_over_an_icon_moves_the_selection_and_keeps_it()
    {
        var rig = new IconPileRig();
        SceneNode[] lift = rig.AddLift(Vector3.Zero);
        rig.Scene.Selection.Select(lift[0]);
        Vector2 at = HandleOverAnIcon(rig, lift[0]);

        rig.Harness.Press(at).ShouldBe(ViewportDragMode.Manipulate);
        rig.Harness.Drag(at + new Vector2(90f, -50f));
        rig.Harness.Release(at + new Vector2(90f, -50f));

        rig.Scene.Selection.Items.ShouldBe(new[] { lift[0] });
        lift[0].LocalPosition.ShouldNotBe(Vector3.Zero);
        rig.Harness.Undo.UndoCount.ShouldBe(1);
    }

    [Fact]
    public void A_handle_drag_cancelled_with_the_other_button_picks_nothing()
    {
        var rig = new IconPileRig();
        SceneNode[] lift = rig.AddLift(Vector3.Zero);
        rig.Scene.Selection.Select(lift[0]);
        Vector2 at = HandleOverAnIcon(rig, lift[0]);

        rig.Harness.Press(at).ShouldBe(ViewportDragMode.Manipulate);
        rig.Harness.Viewport.Update(rig.Harness.Frame(
            at, down: PointerButtons.Left | PointerButtons.Right, pressed: PointerButtons.Right));

        rig.Harness.Viewport.DragMode.ShouldBe(ViewportDragMode.None);
        rig.Scene.Selection.Items.ShouldBe(new[] { lift[0] });
    }

    [Fact]
    public void A_shift_click_on_a_handle_over_an_icon_adds_it()
    {
        var rig = new IconPileRig();
        SceneNode[] lift = rig.AddLift(Vector3.Zero);
        rig.Scene.Selection.Select(lift[0]);
        Vector2 at = HandleOverAnIcon(rig, lift[0]);
        SceneNode under = rig.Pick(rig.Camera.ScreenPointToRay(at, rig.ViewportSize).PointAt(12f))!;

        rig.Harness.Press(at, KeyModifiers.Shift).ShouldBe(ViewportDragMode.Manipulate);
        rig.Harness.Release(at, KeyModifiers.Shift);

        rig.Scene.Selection.Items.ShouldBe(new[] { lift[0], under });
    }

    [Fact]
    public void A_click_on_a_handle_with_no_icon_under_it_changes_nothing()
    {
        var rig = new IconPileRig();
        SceneNode brush = rig.Harness.AddSelectedBrush(new Vector3(-3f, 0f, 0f), 1f);
        SceneNode[] lift = rig.AddLift(new Vector3(3f, 0f, 0f));

        // With one node selected the centre disc covers its pixel.
        Vector2 at = rig.Harness.WorldToScreen(brush.WorldPosition);
        rig.Harness.Move(at);
        rig.Harness.Press(at).ShouldBe(ViewportDragMode.Manipulate);
        rig.Harness.Release(at);

        rig.Scene.Selection.Items.ShouldBe(new[] { brush });
        lift[0].LocalPosition.ShouldBe(new Vector3(3f, 0f, 0f));
    }

    // A pixel on another icon of the pile where a press grabs a handle of
    // the selected one.
    private static Vector2 HandleOverAnIcon(IconPileRig rig, SceneNode selected)
    {
        foreach (SceneIcon marker in rig.Place().Markers)
        {
            Vector2 at = rig.Harness.WorldToScreen(marker.At);
            rig.Harness.Move(at);

            bool isOther = !ReferenceEquals(rig.Pick(marker.At), selected);
            if (isOther && rig.Harness.Viewport.ClassifyPress(rig.Harness.Frame(at)) == ViewportDragMode.Manipulate)
                return at;
        }

        throw new Xunit.Sdk.XunitException("No handle of the selected icon stands over another icon of its pile.");
    }
}
