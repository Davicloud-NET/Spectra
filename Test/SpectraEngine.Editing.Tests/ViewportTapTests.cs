using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Viewport;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// A tap: the press and its release reach the engine inside one frame, as a
/// touchpad tap or a slow frame gives them. No later frame carries the release.
/// </summary>
public sealed class ViewportTapTests
{
    private static ViewportHarness Fixture()
    {
        var harness = new ViewportHarness();
        harness.Orbit(Vector3.Zero, 24f, 0.9f, -0.4f);
        return harness;
    }

    private static ViewportDragMode Tap(ViewportHarness harness, Vector2 cursor) =>
        harness.Viewport.Update(
            harness.Frame(cursor, pressed: PointerButtons.Left, released: PointerButtons.Left));

    [Fact]
    public void A_tap_on_an_object_selects_it_and_the_cursor_does_not_carry_it_off()
    {
        var harness = Fixture();
        var start = new Vector3(3f, 0f, 0f);
        SceneNode node = harness.AddBrush(start, 1f);
        Vector2 pixel = harness.WorldToScreen(node.WorldPosition);

        Tap(harness, pixel);
        harness.Move(pixel + new Vector2(60f, -30f)).ShouldBe(ViewportDragMode.None);
        harness.Move(pixel + new Vector2(120f, -60f));

        harness.Scene.Selection.Items.ShouldBe(new[] { node });
        node.LocalPosition.ShouldBe(start);
        harness.Undo.IsTransactionOpen.ShouldBeFalse();
        harness.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void A_tap_on_a_gizmo_handle_moves_nothing()
    {
        var harness = Fixture();
        var start = new Vector3(3.7f, 2.2f, -0.4f);
        SceneNode node = harness.AddSelectedBrush(start, 1f);

        // With one node selected the centre disc covers its pixel.
        Vector2 pixel = harness.WorldToScreen(node.WorldPosition);
        Tap(harness, pixel).ShouldBe(ViewportDragMode.Manipulate);
        harness.Move(pixel + new Vector2(80f, 20f)).ShouldBe(ViewportDragMode.None);

        node.LocalPosition.ShouldBe(start);
        harness.Undo.IsTransactionOpen.ShouldBeFalse();
        harness.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void A_tap_on_empty_space_clears_the_selection_and_leaves_no_marquee()
    {
        var harness = Fixture();
        harness.AddSelectedBrush(Vector3.Zero, 1f);
        var corner = new Vector2(8f, 8f);

        Tap(harness, corner).ShouldBe(ViewportDragMode.BoxSelect);

        // A marquee left open would take the brush on the way across.
        harness.Move(harness.ViewportSize - corner).ShouldBe(ViewportDragMode.None);

        harness.Viewport.BoxSelect.IsActive.ShouldBeFalse();
        harness.Scene.Selection.Items.ShouldBeEmpty();
    }

    [Fact]
    public void A_tap_on_one_member_of_a_multi_selection_isolates_it()
    {
        var harness = Fixture();
        var startA = new Vector3(-3.4f, 0.2f, 0f);
        var startB = new Vector3(3.1f, 0.2f, 0f);
        SceneNode a = harness.AddSelectedBrush(startA, 1f, "A");
        SceneNode b = harness.AddSelectedBrush(startB, 1f, "B");
        Vector2 pixel = harness.WorldToScreen(a.WorldPosition);

        Tap(harness, pixel);
        harness.Move(pixel + new Vector2(70f, -40f));

        harness.Scene.Selection.Items.ShouldBe(new[] { a });
        a.LocalPosition.ShouldBe(startA);
        b.LocalPosition.ShouldBe(startB);
        harness.Undo.UndoCount.ShouldBe(0);
    }
}
