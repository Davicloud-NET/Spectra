using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Viewport;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// The two gizmo styles. Studio: six per-face handles on the selection's box,
/// face-anchored resize. Classic: three handles at a fixed distance from the
/// pivot, symmetric resize.
/// </summary>
public sealed class GizmoStyleTests
{
    private const float Tolerance = 1e-3f;

    [Fact]
    public void Studio_offers_a_handle_on_every_face_and_classic_offers_three()
    {
        foreach (GizmoMode mode in new[] { GizmoMode.Translate, GizmoMode.Scale })
        {
            GizmoStyle.Studio.LastAxisHandle.ShouldBe(GizmoHandle.AxisNegZ);
            GizmoStyle.Classic.LastAxisHandle.ShouldBe(GizmoHandle.AxisZ);

            GizmoStyle.Studio.Offers(GizmoHandle.AxisNegX, mode).ShouldBeTrue();
            GizmoStyle.Classic.Offers(GizmoHandle.AxisNegX, mode).ShouldBeFalse();
        }
    }

    [Fact]
    public void A_rotation_has_three_rings_in_both_styles_because_a_negative_ring_is_the_same_ring()
    {
        GizmoStyle.Studio.Offers(GizmoHandle.AxisNegX, GizmoMode.Rotate).ShouldBeFalse();
        GizmoStyle.Classic.Offers(GizmoHandle.AxisNegX, GizmoMode.Rotate).ShouldBeFalse();
    }

    [Fact]
    public void Studio_drops_the_plane_quads_the_centre_disc_and_the_view_ring()
    {
        GizmoStyle.Studio.Offers(GizmoHandle.PlaneXY, GizmoMode.Translate).ShouldBeFalse();
        GizmoStyle.Studio.Offers(GizmoHandle.Screen, GizmoMode.Translate).ShouldBeFalse();
        GizmoStyle.Studio.Offers(GizmoHandle.Screen, GizmoMode.Rotate).ShouldBeFalse();

        GizmoStyle.Classic.Offers(GizmoHandle.PlaneXY, GizmoMode.Translate).ShouldBeTrue();
        GizmoStyle.Classic.Offers(GizmoHandle.Screen, GizmoMode.Translate).ShouldBeTrue();
        GizmoStyle.Classic.Offers(GizmoHandle.Screen, GizmoMode.Rotate).ShouldBeTrue();

        // The centre cube is the only uniform resize, so both styles keep it.
        GizmoStyle.Studio.Offers(GizmoHandle.Screen, GizmoMode.Scale).ShouldBeTrue();
        GizmoStyle.Classic.Offers(GizmoHandle.Screen, GizmoMode.Scale).ShouldBeTrue();
    }

    [Fact]
    public void What_a_style_offers_is_what_it_draws()
    {
        // An arrow is nine lines, a plane quad four, plus Classic's centre circle.
        DrawnLines(GizmoStyle.Studio, GizmoMode.Translate).ShouldBe(6 * 9);
        DrawnLines(GizmoStyle.Classic, GizmoMode.Translate)
            .ShouldBe((3 * 9) + (3 * 4) + GizmoHitTesting.RingSegments);

        // A shaft and a twelve-edge cube per handle, plus the centre cube.
        DrawnLines(GizmoStyle.Studio, GizmoMode.Scale).ShouldBe((6 * 13) + 12);
        DrawnLines(GizmoStyle.Classic, GizmoMode.Scale).ShouldBe((3 * 13) + 12);

        DrawnLines(GizmoStyle.Studio, GizmoMode.Rotate).ShouldBe(3 * GizmoHitTesting.RingSegments);
        DrawnLines(GizmoStyle.Classic, GizmoMode.Rotate).ShouldBe(4 * GizmoHitTesting.RingSegments);
    }

    [Fact]
    public void A_negative_face_handle_grows_the_negative_face_and_plants_the_positive_one()
    {
        var harness = GizmoHarness.ThreeQuarterView(GizmoStyle.Studio);
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        ScaleGizmoDragTests.Scale(harness).Snap.Enabled = false;

        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisNegX, 2f);

        node.Brush!.LocalBounds.Size.X.ShouldBe(4f, Tolerance);
        (node.LocalPosition.X + node.Brush.LocalBounds.Max.X).ShouldBe(1f, Tolerance);   // planted
        (node.LocalPosition.X + node.Brush.LocalBounds.Min.X).ShouldBe(-3f, Tolerance);  // out by two
        node.LocalScale.ShouldBe(Vector3.One);
    }

    [Fact]
    public void A_negative_handle_resizes_its_own_axis_and_nothing_else()
    {
        // The mask comes from the handle's axis. Looked up by raw handle, the
        // negative values hit the mask table's uniform default.
        var harness = GizmoHarness.ThreeQuarterView(GizmoStyle.Studio);
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        ScaleGizmoDragTests.Scale(harness).Snap.Enabled = false;

        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisNegY, 1.5f);

        Vector3 size = node.Brush!.LocalBounds.Size;
        size.Y.ShouldBe(3.5f, Tolerance);
        size.X.ShouldBe(2f, Tolerance);
        size.Z.ShouldBe(2f, Tolerance);
    }

    [Fact]
    public void Both_ends_of_an_axis_are_pickable_and_they_are_different_handles()
    {
        var harness = GizmoHarness.ThreeQuarterView(GizmoStyle.Studio);
        harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        ScaleGizmoDragTests.Scale(harness);

        harness.Hover(harness.GrabPointFor(GizmoHandle.AxisX));
        harness.Gizmo.HoveredHandle.ShouldBe(GizmoHandle.AxisX);

        harness.Hover(harness.GrabPointFor(GizmoHandle.AxisNegX));
        harness.Gizmo.HoveredHandle.ShouldBe(GizmoHandle.AxisNegX);
    }

    [Fact]
    public void A_negative_arrow_moves_the_selection_the_same_way_its_positive_twin_does()
    {
        // Dragged toward +x from the -x arrow: a move has no anchored face.
        var harness = GizmoHarness.ThreeQuarterView(GizmoStyle.Studio);
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        harness.Use(GizmoMode.Translate);
        harness.Translate.Snap.Enabled = false;

        harness.Grab(harness.GrabPointFor(GizmoHandle.AxisNegX)).ShouldBe(GizmoUpdateResult.DragBegan);
        harness.Gizmo.ActiveHandle.ShouldBe(GizmoHandle.AxisNegX);
        harness.DragBy(Vector3.UnitX * 3f);
        harness.Release().ShouldBe(GizmoUpdateResult.DragCommitted);

        node.LocalPosition.X.ShouldBe(3f, Tolerance);
    }

    [Fact]
    public void Every_member_of_a_selection_grows_the_way_the_handle_points()
    {
        // The handle's sign is in the gizmo's frame, the anchor in the node's.
        // One node is turned half a turn so its local +x points the other way.
        var harness = GizmoHarness.ThreeQuarterView(GizmoStyle.Studio);
        SceneNode flipped = harness.AddSelectedBrushNode(new Vector3(-4f, 0f, 0f), 1f, "Flipped");
        flipped.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        SceneNode plain = harness.AddSelectedBrushNode(new Vector3(4f, 0f, 0f), 1f, "Plain");
        ScaleGizmoDragTests.Scale(harness).Snap.Enabled = false;

        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisX, 2f);

        // Both went from two units to four, growing toward world +x.
        (float Min, float Max) flippedSpan = WorldSpanX(flipped);
        flippedSpan.Min.ShouldBe(-5f, Tolerance);
        flippedSpan.Max.ShouldBe(-1f, Tolerance);

        (float Min, float Max) plainSpan = WorldSpanX(plain);
        plainSpan.Min.ShouldBe(3f, Tolerance);
        plainSpan.Max.ShouldBe(7f, Tolerance);
    }

    [Fact]
    public void A_symmetric_resize_holds_the_object_s_centre_not_its_origin()
    {
        // The mesh spans 0..2 in x, so its centre is a unit from the node origin.
        var harness = GizmoHarness.ThreeQuarterView(GizmoStyle.Classic);
        SceneNode node = harness.AddNode(Vector3.Zero, "Offset");
        node.MeshRenderer = new MeshRenderer(
            BoxMesh.Spanning(new Vector3(0f, -0.5f, -0.5f), new Vector3(2f, 0.5f, 0.5f)),
            new Material(null));
        harness.Scene.Selection.Add(node);
        ScaleGizmoDragTests.Scale(harness).Snap.Enabled = false;

        // Symmetric: one unit of travel adds a unit on each side.
        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisX, 1f);

        node.LocalScale.X.ShouldBe(2f, Tolerance);
        // Centre still at x = 1; the object now spans -1..3.
        (node.LocalPosition.X + 0f * node.LocalScale.X).ShouldBe(-1f, Tolerance);
        (node.LocalPosition.X + 2f * node.LocalScale.X).ShouldBe(3f, Tolerance);
    }

    [Fact]
    public void A_classic_resize_moves_both_faces_and_leaves_the_node_where_it_is()
    {
        var harness = GizmoHarness.ThreeQuarterView(GizmoStyle.Classic);
        SceneNode node = harness.AddSelectedBrushNode(new Vector3(3f, 0f, 0f), halfExtent: 1f);
        ScaleGizmoDragTests.Scale(harness).Snap.Enabled = false;

        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisX, 1f);

        node.Brush!.LocalBounds.Size.X.ShouldBe(4f, Tolerance);
        node.LocalPosition.ShouldBe(new Vector3(3f, 0f, 0f));
    }

    [Theory]
    [InlineData(GizmoStyleKind.Studio)]
    [InlineData(GizmoStyleKind.Classic)]
    public void The_modifier_asks_for_the_anchoring_the_style_is_not_doing(GizmoStyleKind kind)
    {
        GizmoStyle style = StyleFor(kind);
        var harness = GizmoHarness.ThreeQuarterView(style);
        SceneNode node = harness.AddSelectedBrushNode(new Vector3(3f, 0f, 0f), halfExtent: 1f);
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Enabled = false;
        scale.SymmetricModifier.ShouldBe(KeyModifiers.Shift);

        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisX, 1f, KeyModifiers.Shift);

        if (style.FaceAnchoredResize)
        {
            // Studio with Shift is symmetric: travel is half the size change.
            node.Brush!.LocalBounds.Size.X.ShouldBe(4f, Tolerance);
            node.LocalPosition.ShouldBe(new Vector3(3f, 0f, 0f));
        }
        else
        {
            // Classic with Shift is face-anchored: travel is the size change
            // and the node moves by half of it.
            node.Brush!.LocalBounds.Size.X.ShouldBe(3f, Tolerance);
            node.LocalPosition.X.ShouldBe(3.5f, Tolerance);
        }
    }

    [Fact]
    public void Studio_handles_stand_clear_of_the_selection_box_and_classic_handles_do_not_move()
    {
        var studio = GizmoHarness.ThreeQuarterView(GizmoStyle.Studio);
        studio.AddSelectedBrushNode(Vector3.Zero, halfExtent: 2f);
        studio.Use(GizmoMode.Scale);
        GizmoGeometry studioGeometry = studio.LiveGeometry();

        // Just outside the face, by less than a whole gizmo length.
        studioGeometry.AxisReach(GizmoHandle.AxisX).ShouldBeGreaterThan(2f);
        studioGeometry.AxisReach(GizmoHandle.AxisX).ShouldBeLessThan(2f + studioGeometry.AxisLength);
        studioGeometry.AxisReach(GizmoHandle.AxisNegX)
            .ShouldBe(studioGeometry.AxisReach(GizmoHandle.AxisX), Tolerance);

        // The whole cube clears the face, not only its centre. Same for the
        // arrow's tail below.
        studioGeometry.TryGetHandleBox(GizmoHandle.AxisX, out Vector3 cube, out float radius).ShouldBeTrue();
        (Vector3.Dot(cube - studioGeometry.Pivot, Vector3.UnitX) - radius).ShouldBeGreaterThan(2f);

        studio.Use(GizmoMode.Translate);
        GizmoGeometry arrows = studio.LiveGeometry();
        arrows.TryGetAxisSegment(GizmoHandle.AxisNegZ, out Vector3 tail, out _).ShouldBeTrue();
        Vector3.Dot(tail - arrows.Pivot, -Vector3.UnitZ).ShouldBeGreaterThan(2f);

        var classic = GizmoHarness.ThreeQuarterView(GizmoStyle.Classic);
        classic.AddSelectedBrushNode(Vector3.Zero, halfExtent: 2f);
        classic.Use(GizmoMode.Scale);
        GizmoGeometry classicGeometry = classic.LiveGeometry();

        classicGeometry.AxisReach(GizmoHandle.AxisX).ShouldBe(classicGeometry.AxisLength);
    }

    [Fact]
    public void A_tiny_object_still_gets_handles_far_enough_apart_to_aim_at()
    {
        // Without a minimum reach every handle collapses onto the pivot.
        var harness = GizmoHarness.ThreeQuarterView(GizmoStyle.Studio);
        harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 0.001f);
        harness.Use(GizmoMode.Scale);

        GizmoGeometry geometry = harness.LiveGeometry();
        geometry.AxisReach(GizmoHandle.AxisX)
            .ShouldBeGreaterThanOrEqualTo(geometry.AxisLength * GizmoStyle.Studio.MinimumReachFactor);

        harness.Hover(harness.GrabPointFor(GizmoHandle.AxisNegZ));
        harness.Gizmo.HoveredHandle.ShouldBe(GizmoHandle.AxisNegZ);
    }

    [Fact]
    public void The_studio_pivot_is_the_centre_of_the_selection_box_and_the_classic_one_is_the_average_origin()
    {
        // Different sizes, so box centre (6) and average origin (5) differ.
        foreach (GizmoStyle style in new[] { GizmoStyle.Studio, GizmoStyle.Classic })
        {
            var harness = new GizmoHarness(new Vector3(20f, 15f, 25f), Vector3.Zero, style: style);
            harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f, name: "Small");
            harness.AddSelectedBrushNode(new Vector3(10f, 0f, 0f), halfExtent: 3f, name: "Big");
            harness.Use(GizmoMode.Scale);

            float expected = style.PivotMode == GizmoPivotMode.BoundsCentre ? 6f : 5f;
            harness.LiveGeometry().Pivot.X.ShouldBe(expected, Tolerance);
        }
    }

    [Fact]
    public void The_toggle_verb_flips_the_style_and_reaches_all_three_tools()
    {
        var harness = GizmoHarness.ThreeQuarterView(GizmoStyle.Studio);
        GizmoStyle? announced = null;
        harness.Gizmos.StyleChanged += style => announced = style;

        harness.Gizmos.Apply(GizmoCommand.ToggleStyle).ShouldBeTrue();

        harness.Gizmos.Style.ShouldBeSameAs(GizmoStyle.Classic);
        announced.ShouldBeSameAs(GizmoStyle.Classic);
        harness.Translate.Style.ShouldBeSameAs(GizmoStyle.Classic);
        harness.Rotate.Style.ShouldBeSameAs(GizmoStyle.Classic);
        harness.Scale.Style.ShouldBeSameAs(GizmoStyle.Classic);

        harness.Gizmos.Apply(GizmoCommand.ToggleStyle).ShouldBeTrue();
        harness.Gizmos.Style.ShouldBeSameAs(GizmoStyle.Studio);
    }

    [Fact]
    public void Y_is_the_default_binding_for_the_style_toggle()
    {
        GizmoShortcuts.TryResolve("Y", out GizmoCommand command).ShouldBeTrue();
        command.ShouldBe(GizmoCommand.ToggleStyle);
    }

    [Fact]
    public void Switching_style_mid_drag_rolls_the_gesture_back()
    {
        var harness = GizmoHarness.ThreeQuarterView(GizmoStyle.Studio);
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        harness.Use(GizmoMode.Translate);

        harness.Grab(harness.GrabPointFor(GizmoHandle.AxisX)).ShouldBe(GizmoUpdateResult.DragBegan);
        harness.DragBy(Vector3.UnitX * 4f);
        node.LocalPosition.X.ShouldNotBe(0f);

        harness.Gizmos.Style = GizmoStyle.Classic;

        node.LocalPosition.ShouldBe(Vector3.Zero);
        harness.Undo.Count.ShouldBe(0);
        harness.Gizmo.State.ShouldBe(GizmoInteractionState.Idle);
    }

    [Fact]
    public void Setting_the_same_style_changes_nothing_and_does_not_disturb_a_drag()
    {
        var harness = GizmoHarness.ThreeQuarterView(GizmoStyle.Studio);
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        harness.Use(GizmoMode.Translate);

        harness.Grab(harness.GrabPointFor(GizmoHandle.AxisX)).ShouldBe(GizmoUpdateResult.DragBegan);
        harness.DragBy(Vector3.UnitX * 4f);

        harness.Gizmos.Style = GizmoStyle.Studio;

        harness.Gizmo.State.ShouldBe(GizmoInteractionState.Dragging);
        harness.Release().ShouldBe(GizmoUpdateResult.DragCommitted);
        node.LocalPosition.X.ShouldBe(4f, Tolerance);
    }

    [Fact]
    public void A_press_on_an_object_still_picks_it_up_in_a_style_with_no_centre_disc()
    {
        // Studio has no centre disc to pick, but select-and-move still routes
        // through the free-move constraint behind it.
        var harness = new ViewportHarness(gizmoStyle: GizmoStyle.Studio);
        harness.Orbit(Vector3.Zero, 24f, 0.9f, -0.4f);
        SceneNode node = harness.AddBrush(new Vector3(3f, 0f, 0f), 1f);
        Vector2 press = harness.WorldToScreen(node.WorldPosition);

        harness.Gizmos.Translate.FreeMoveHandle.ShouldBe(GizmoHandle.Screen);
        harness.Press(press).ShouldBe(ViewportDragMode.SelectAndMove);
        harness.Scene.Selection.Items.ShouldBe(new[] { node });

        harness.Drag(press + new Vector2(60f, -30f));
        node.LocalPosition.ShouldNotBe(new Vector3(3f, 0f, 0f));

        harness.Release(press + new Vector2(60f, -30f));
        harness.Undo.UndoCount.ShouldBe(1);
        harness.Undo.Undo().ShouldBeTrue();
        node.LocalPosition.ShouldBe(new Vector3(3f, 0f, 0f));
    }

    [Theory]
    [InlineData(GizmoStyleKind.Studio)]
    [InlineData(GizmoStyleKind.Classic)]
    public void A_cancelled_resize_restores_the_transform_bit_for_bit(GizmoStyleKind kind)
    {
        var harness = GizmoHarness.ThreeQuarterView(StyleFor(kind));
        SceneNode node = harness.AddSelectedBrushNode(new Vector3(1.5f, 0f, 0f), halfExtent: 1f);
        Brush original = node.Brush!;
        Transform before = node.LocalTransform;
        ScaleGizmoDragTests.Scale(harness).Snap.Enabled = false;

        ScaleGizmoDragTests.GrabAxis(
            harness, GizmoHandle.AxisX, out Vector3 pivot, out Vector3 axis, out float reach);
        harness.DragTo(pivot + axis * (reach + 2.6f));
        node.Brush.ShouldNotBeSameAs(original);

        harness.PressEscape().ShouldBe(GizmoUpdateResult.DragCancelled);

        node.Brush.ShouldBeSameAs(original);
        node.LocalTransform.Position.ShouldBe(before.Position);
        node.LocalTransform.Scale.ShouldBe(before.Scale);
        harness.Undo.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(GizmoStyleKind.Studio)]
    [InlineData(GizmoStyleKind.Classic)]
    public void A_grab_that_never_moved_commits_nothing(GizmoStyleKind kind)
    {
        var harness = GizmoHarness.ThreeQuarterView(StyleFor(kind));
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        Brush original = node.Brush!;
        ScaleGizmoDragTests.Scale(harness);

        ScaleGizmoDragTests.GrabAxis(harness, GizmoHandle.AxisX, out _, out _, out _);
        harness.Release().ShouldBe(GizmoUpdateResult.DragCancelled);

        node.Brush.ShouldBeSameAs(original);
        harness.Undo.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(GizmoStyleKind.Studio)]
    [InlineData(GizmoStyleKind.Classic)]
    public void One_notch_is_one_increment_in_either_style(GizmoStyleKind kind)
    {
        GizmoStyle style = StyleFor(kind);
        var harness = GizmoHarness.ThreeQuarterView(style);
        SceneNode node = harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        ScaleGizmo scale = ScaleGizmoDragTests.Scale(harness);
        scale.Snap.Enabled = true;
        scale.Snap.Increment = 1f;

        // Past half a notch under either style's travel mapping.
        float travel = style.FaceAnchoredResize ? 0.6f : 0.3f;
        ScaleGizmoDragTests.DragAxisBy(harness, GizmoHandle.AxisX, travel);

        node.Brush!.LocalBounds.Size.X.ShouldBe(3f, Tolerance);
    }

    // Extent along world x, through the world matrix so rotation counts.
    private static (float Min, float Max) WorldSpanX(SceneNode node)
    {
        Aabb bounds = node.Brush!.LocalBounds;
        Matrix4x4 world = node.WorldMatrix;
        float min = float.MaxValue;
        float max = float.MinValue;

        for (int corner = 0; corner < 8; corner++)
        {
            var local = new Vector3(
                (corner & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                (corner & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                (corner & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);

            float x = Vector3.Transform(local, world).X;
            min = MathF.Min(min, x);
            max = MathF.Max(max, x);
        }

        return (min, max);
    }

    private static GizmoStyle StyleFor(GizmoStyleKind kind) =>
        kind == GizmoStyleKind.Studio ? GizmoStyle.Studio : GizmoStyle.Classic;

    private static int DrawnLines(GizmoStyle style, GizmoMode mode)
    {
        var harness = GizmoHarness.ThreeQuarterView(style);
        harness.AddSelectedBrushNode(Vector3.Zero, halfExtent: 1f);
        harness.Use(mode);
        harness.Hover(new Vector3(0f, 0f, 100f)); // aimed away, so nothing highlights

        var output = new DebugDraw();
        harness.Gizmo.Draw(output);

        // Two vertices per line.
        return output.VertexCount / 2;
    }
}
